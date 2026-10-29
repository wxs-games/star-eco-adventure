using UnityEngine;
using UnityEngine.SceneManagement;
using StarEco.Framework;
using StarEco.Eco.Events;
using StarEco.EnemyAI;
using StarEco.EnemyAI.Events;

namespace StarEco.Level
{
    /// <summary>
    /// 污染度驱动的动态刷怪器（R5）：每条目独立计时、锚点轮询、对象池生成、名额随死亡回收。
    /// 间隔 = BaseInterval × Lerp(1.5, 0.5, 污染度)（污染越高乘数越小，刷得越快）。
    /// </summary>
    public class LevelSpawner : MonoBehaviour
    {
        [SerializeField] private LevelConfig _config;

        [Tooltip("锚点命名约定：关卡配置里 Anchors 为空时，运行时按此名前缀在场景中查找锚点（场景对象无法序列化进 asset）")]
        [SerializeField] private string _anchorNamePrefix = "SpawnAnchor";

        [Tooltip("每只敌人的重生次数上限（1 = 被打死后只重生一次，0 = 不重生）。每条目总生成数 = MaxAlive × (1 + 该值)")]
        [SerializeField] private int _respawnsPerEnemy = 1;

        private LevelObjectiveTracker _tracker;
        private Transform _player;
        private EnemyControllerBase[] _prefabComponents;
        private Transform[][] _anchors;
        private float[] _timers;
        private int[] _aliveCounts;
        private int[] _spawnedTotals;   // 每条目累计生成数（含重生），用于限制重生次数
        private int[] _anchorCursor;
        private float _pollution = 1f;
        private bool _running;

        private void OnEnable()
        {
            EventCenter.Subscribe<PurityChangedEvent>(OnPurityChanged);
            EventCenter.Subscribe<EnemyDeathEvent>(OnEnemyDied);
        }

        private void OnDisable()
        {
            EventCenter.Unsubscribe<PurityChangedEvent>(OnPurityChanged);
            EventCenter.Unsubscribe<EnemyDeathEvent>(OnEnemyDied);
        }

        /// <summary>启动刷怪（LevelManager 进入 Playing 时调用）。</summary>
        public void Begin(LevelConfig config, LevelObjectiveTracker tracker, Transform player)
        {
            _config = config;
            _tracker = tracker;
            _player = player;
            _pollution = 1f;

            int count = Entries?.Length ?? 0;
            _prefabComponents = new EnemyControllerBase[count];
            _anchors = new Transform[count][];
            _timers = new float[count];
            _aliveCounts = new int[count];
            _spawnedTotals = new int[count];
            _anchorCursor = new int[count];
            _running = count > 0;

            for (int i = 0; i < count; i++)
            {
                _prefabComponents[i] = ResolvePrefabComponent(i);
                ResolveAnchors(i);
            }

            if (count == 0)
            {
                Debug.LogWarning("[LevelSpawner] 关卡配置没有刷怪表，本关不会有敌人出现。");
            }
        }

        /// <summary>停止刷怪（关卡进入终态）。已在场的敌人不处理。</summary>
        public void Stop()
        {
            _running = false;
        }

        private LevelConfig.SpawnEntry[] Entries => _config != null ? _config.SpawnEntries : null;

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            var entries = Entries;
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                if (entry.MaxAlive <= 0)
                {
                    continue;   // 上限 0 = 本条目禁用
                }

                if (_aliveCounts[i] >= entry.MaxAlive)
                {
                    continue;
                }

                // 重生次数上限：条目总生成数 = MaxAlive × (1 + 重生次数)，用完后不再补充
                int spawnLimit = entry.MaxAlive * (1 + Mathf.Max(0, _respawnsPerEnemy));
                if (_spawnedTotals[i] >= spawnLimit)
                {
                    continue;
                }

                // 目标链解锁检查（R5）
                if (entry.RequiredObjectiveIndex >= 0 &&
                    (_tracker == null || !_tracker.IsObjectiveCompleted(entry.RequiredObjectiveIndex)))
                {
                    continue;
                }

                _timers[i] -= Time.deltaTime;
                if (_timers[i] > 0f)
                {
                    continue;
                }

                _timers[i] = CurrentInterval(entry);

                if (TrySpawn(i, entry))
                {
                    _aliveCounts[i]++;
                    _spawnedTotals[i]++;
                }
            }
        }

        private float CurrentInterval(LevelConfig.SpawnEntry entry)
        {
            // 污染度 1（脏）→ 0.5 倍间隔（刷得快）；污染度 0（干净）→ 1.5 倍间隔（刷得慢）。
            return Mathf.Max(0.2f, entry.BaseInterval * Mathf.Lerp(1.5f, 0.5f, _pollution));
        }

        private bool TrySpawn(int index, LevelConfig.SpawnEntry entry)
        {
            var anchors = AnchorsFor(index);
            if (anchors == null || anchors.Length == 0)
            {
                Debug.LogError($"[LevelSpawner] 刷怪条目 {index} 没有锚点（配置未指定且场景无 {_anchorNamePrefix}* 对象），已跳过。");
                return false;
            }

            var prefabComponent = _prefabComponents[index];
            if (prefabComponent == null)
            {
                return false;
            }

            var anchor = anchors[_anchorCursor[index] % anchors.Length];
            _anchorCursor[index]++;

            if (anchor == null)
            {
                return false;
            }

            var instance = PoolManager.Instance.Get(prefabComponent);
            instance.MarkPooled();

            // 池根节点在 DontDestroyOnLoad，下发出来的对象默认也留在该 Scene。
            // 运行时把敌人显式搬回关卡场景，避免层级混乱和跨场景残留副作用。
            var targetScene = anchor.gameObject.scene;
            if (targetScene.IsValid() && instance.gameObject.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(instance.gameObject, targetScene);
            }

            instance.transform.position = anchor.position;

            if (_player != null)
            {
                instance.SetTarget(_player);
            }

            return true;
        }

        /// <summary>
        /// 解析锚点：条目已配锚点则直接用；否则按命名约定在场景里找 SpawnAnchor*，
        /// 并按条目序号分配偶数/奇数锚点（两条条目各占一半位置，交替生成）。
        /// </summary>
        private void ResolveAnchors(int index)
        {
            var entries = Entries;
            if (entries == null || index >= entries.Length)
            {
                return;
            }

            var entry = entries[index];
            if (entry.Anchors != null && entry.Anchors.Length > 0)
            {
                return;   // 配置里已指定
            }

            var found = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            System.Collections.Generic.List<Transform> matched = null;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].name.StartsWith(_anchorNamePrefix, System.StringComparison.Ordinal))
                {
                    matched ??= new System.Collections.Generic.List<Transform>();
                    matched.Add(found[i]);
                }
            }

            if (matched == null || matched.Count == 0)
            {
                return;   // 交给 TrySpawn 报错跳过
            }

            // FindObjectsByType 返回顺序不保证稳定，先按名字排序：
            // 否则条目 0/1 每次会话可能取到不同锚点，出生位置随机漂移。
            matched.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

            // 按条目序号取模分配锚点：条目 0 用 0,2,4…；条目 1 用 1,3,5…
            // 存运行时数组，绝不回写 LevelConfig 资产（避免 Editor 下资产被标脏持久化场景对象引用）
            var subset = new System.Collections.Generic.List<Transform>();
            for (int i = index % 2; i < matched.Count; i += 2)
            {
                subset.Add(matched[i]);
            }

            _anchors[index] = subset.ToArray();
        }

        /// <summary>该条目的运行时锚点（配置优先，其次运行时解析）。</summary>
        private Transform[] AnchorsFor(int index)
        {
            if (_anchors != null && index < _anchors.Length && _anchors[index] != null && _anchors[index].Length > 0)
            {
                return _anchors[index];
            }

            var entries = Entries;
            return entries != null && index < entries.Length ? entries[index].Anchors : null;
        }

        private EnemyControllerBase ResolvePrefabComponent(int index)        {
            var entries = Entries;
            if (entries == null || index >= entries.Length)
            {
                return null;
            }

            var entry = entries[index];
            if (entry.Prefab == null)
            {
                Debug.LogError($"[LevelSpawner] 刷怪条目 {index} 未配置敌人预制体，已跳过。");
                return null;
            }

            var comp = entry.Prefab.GetComponent<EnemyControllerBase>();
            if (comp == null)
            {
                Debug.LogError($"[LevelSpawner] 预制体 {entry.Prefab.name} 上没有 EnemyControllerBase 派生组件，已跳过。");
            }

            return comp;
        }

        private void OnPurityChanged(PurityChangedEvent evt)
        {
            _pollution = evt.Pollution;
        }

        private void OnEnemyDied(EnemyDeathEvent evt)
        {
            var entries = Entries;
            if (entries == null || string.IsNullOrEmpty(evt.EnemyTypeId))
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                var comp = _prefabComponents[i];
                if (comp == null || comp.Config == null)
                {
                    continue;
                }

                if (comp.Config.EnemyTypeId == evt.EnemyTypeId)
                {
                    _aliveCounts[i] = Mathf.Max(0, _aliveCounts[i] - 1);
                }
            }
        }
    }
}
