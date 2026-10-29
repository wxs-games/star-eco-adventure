using System;
using UnityEngine;
using StarEco.Framework;
using StarEco.Eco;
using StarEco.Eco.Events;
using StarEco.EnemyAI;
using StarEco.EnemyAI.Events;
using StarEco.Level.Events;

namespace StarEco.Level
{
    /// <summary>
    /// 目标链追踪器（R3）：按 LevelConfig.Objectives 顺序解锁，只有当前目标参与计数。
    /// 分类计数与 M5 解耦：塔 vs 污染源按 GarbagePurifiedEvent.TargetId 前缀判定（design §2.2 已知取舍）。
    /// </summary>
    public class LevelObjectiveTracker : MonoBehaviour
    {
        private const string TowerIdPrefix = "Tower";
        private const string WaterBlockPrefix = "WaterBlock";   // M9：河道堵塞点单独计数

        [SerializeField] private LevelConfig _config;

        private PuritySystem _purity;
        private LevelStats _stats;
        private Action _onChainCompleted;

        private int _index;
        private bool _initialized;

        // 各类型累计计数（只由当前目标推进时写入）
        private int _purifiedTargets;
        private int _purifiedTowers;
        private int _defeatedEnemies;
        private int _rescuedAnimals;   // M9 R2：AnimalRescuedEvent 计数
        private int _waterCleared;     // M9 R3：WaterBlock 前缀 GarbagePurifiedEvent 计数

        // 运行时夹取后的 Required（不修改 LevelConfig 资产本身，避免 Editor 下资产被标脏持久化）
        private int[] _effectiveRequired;

        /// <summary>当前目标序号（-1 = 链已完成/未初始化）。</summary>
        public int CurrentIndex => _index;

        /// <summary>目标链是否全部完成。</summary>
        public bool ChainCompleted { get; private set; }

        /// <summary>当前目标配置（链完成时为 null）。</summary>
        public LevelConfig.LevelObjective CurrentObjective =>
            _config != null && _index >= 0 && _index < (_config.Objectives?.Length ?? 0)
                ? _config.Objectives[_index]
                : null;

        /// <summary>当前目标的运行时所需数量（已夹取）。</summary>
        public int CurrentRequired =>
            _effectiveRequired != null && _index >= 0 && _index < _effectiveRequired.Length
                ? _effectiveRequired[_index]
                : 0;

        /// <summary>
        /// 当前目标已完成的计数（M7 HUD 初始化时主动拉取用：
        /// 首帧的 LevelObjectiveChangedEvent 可能早于 HUD 订阅，需要能回头读一次）。
        /// </summary>
        public int CurrentProgress
        {
            get
            {
                var objective = CurrentObjective;
                return objective != null ? CurrentCountFor(objective.Type) : 0;
            }
        }

        private void OnEnable()
        {
            EventCenter.Subscribe<GarbagePurifiedEvent>(OnGarbagePurified);
            EventCenter.Subscribe<EnemyDeathEvent>(OnEnemyDied);
            EventCenter.Subscribe<PurityChangedEvent>(OnPurityChanged);
            EventCenter.Subscribe<AnimalRescuedEvent>(OnAnimalRescued);   // M9 R2
            EventCenter.Subscribe<TowerDisableEvent>(OnTowerDestroyed);   // 炮塔改为攻击击毁后由此计数
        }

        private void OnDisable()
        {
            EventCenter.Unsubscribe<GarbagePurifiedEvent>(OnGarbagePurified);
            EventCenter.Unsubscribe<EnemyDeathEvent>(OnEnemyDied);
            EventCenter.Unsubscribe<PurityChangedEvent>(OnPurityChanged);
            EventCenter.Unsubscribe<AnimalRescuedEvent>(OnAnimalRescued);
            EventCenter.Unsubscribe<TowerDisableEvent>(OnTowerDestroyed);
        }

        /// <summary>
        /// 初始化目标链：夹取不合理 Required、激活首个目标。
        /// </summary>
        /// <param name="config">关卡配置（null 时回退默认配置）。</param>
        /// <param name="purity">净化系统（ReachPurity 目标需要）。</param>
        /// <param name="stats">统计数据（DefeatEnemies 计数展示）。</param>
        /// <param name="onChainCompleted">目标链全部完成回调（LevelManager 传，成功时用于通关判据）。</param>
        public void Initialize(LevelConfig config, PuritySystem purity, LevelStats stats, Action onChainCompleted)
        {
            _config = config != null ? config : LevelConfig.CreateDefault();
            _purity = purity;
            _stats = stats;
            _onChainCompleted = onChainCompleted;

            _index = 0;
            _purifiedTargets = 0;
            _purifiedTowers = 0;
            _defeatedEnemies = 0;
            _rescuedAnimals = 0;
            _waterCleared = 0;
            ChainCompleted = false;

            ClampRequired();

            _initialized = true;
            BroadcastCurrent();
        }

        /// <summary>重置为未开始（关卡重启）。</summary>
        public void ResetChain()
        {
            _index = 0;
            _purifiedTargets = 0;
            _purifiedTowers = 0;
            _defeatedEnemies = 0;
            _rescuedAnimals = 0;
            _waterCleared = 0;
            ChainCompleted = false;
            _initialized = false;
        }

        /// <summary>指定序号目标是否已完成（LevelSpawner 用它判断刷怪解锁）。</summary>
        public bool IsObjectiveCompleted(int objectiveIndex)
        {
            return objectiveIndex < 0 || objectiveIndex < _index;
        }

        // ===== 事件 =====

        private void OnGarbagePurified(GarbagePurifiedEvent evt)
        {
            // M9 R3：河道堵塞点单独计数（PuzzleSolved），不算普通污染源
            if (!string.IsNullOrEmpty(evt.TargetId) &&
                evt.TargetId.StartsWith(WaterBlockPrefix, StringComparison.Ordinal))
            {
                _waterCleared++;
                Evaluate();
                return;
            }

            if (IsTower(evt.TargetId))
            {
                _purifiedTowers++;
            }
            else
            {
                _purifiedTargets++;
            }

            Evaluate();
        }

        private void OnEnemyDied(EnemyDeathEvent evt)
        {
            _defeatedEnemies++;
            Evaluate();
        }

        /// <summary>炮塔被击毁（攻击击杀）后计入"击毁塔"目标。</summary>
        private void OnTowerDestroyed(TowerDisableEvent evt)
        {
            _purifiedTowers++;
            Evaluate();
        }

        private void OnPurityChanged(PurityChangedEvent evt)
        {
            Evaluate();
        }

        /// <summary>M9 R2：解救被困动物计数。</summary>
        private void OnAnimalRescued(AnimalRescuedEvent evt)
        {
            _rescuedAnimals++;
            Evaluate();
        }

        // ===== 推进 =====

        private void Evaluate()
        {
            if (!_initialized || ChainCompleted)
            {
                return;
            }

            // M9 修复：循环推进到首个未满足目标。
            // 若新激活目标的计数已被先前事件满足（提前击杀 / 提前净化 / 提前解救），
            // 单次 if 会让链停滞到下一次同类事件才恢复，必须继续检查。
            while (!ChainCompleted)
            {
                var objective = CurrentObjective;
                if (objective == null)
                {
                    CompleteChain();
                    return;
                }

                int required = CurrentRequired;
                int current = CurrentCountFor(objective.Type);

                if (current < required)
                {
                    BroadcastCurrent();   // 计数变化即刷新目标文本
                    return;
                }

                EventCenter.Publish(new ObjectiveCompletedEvent { Index = _index, Label = objective.Label });

                _index++;
                if (_index >= (_config.Objectives?.Length ?? 0))
                {
                    CompleteChain();
                    return;
                }
            }
        }

        private void CompleteChain()
        {
            if (ChainCompleted)
            {
                return;
            }

            ChainCompleted = true;
            _index = (_config.Objectives?.Length ?? 0);
            BroadcastCurrent();
            _onChainCompleted?.Invoke();
        }

        private int CurrentCountFor(LevelObjectiveType type)
        {
            switch (type)
            {
                case LevelObjectiveType.PurifyTargets:
                    return _purifiedTargets;
                case LevelObjectiveType.PurifyTowers:
                    return _purifiedTowers;
                case LevelObjectiveType.DefeatEnemies:
                    return _defeatedEnemies;
                case LevelObjectiveType.RescueTargets:
                    return _rescuedAnimals;
                case LevelObjectiveType.PuzzleSolved:
                    return _waterCleared;
                case LevelObjectiveType.ReachPurity:
                    return _purity != null ? Mathf.FloorToInt(_purity.Percent * 100f) : 0;
                default:
                    return 0;
            }
        }

        private void BroadcastCurrent()
        {
            var objective = CurrentObjective;
            EventCenter.Publish(new LevelObjectiveChangedEvent
            {
                Index = objective != null ? _index : -1,
                Label = objective?.Label ?? "关卡目标已完成",
                Current = objective != null ? CurrentCountFor(objective.Type) : 0,
                Required = CurrentRequired
            });
        }

        /// <summary>夹取不合理的 Required：净化类目标不得超过场景实际可净化目标数（R3 防死锁）。</summary>
        private void ClampRequired()
        {
            if (_config.Objectives == null || _config.Objectives.Length == 0)
            {
                _effectiveRequired = Array.Empty<int>();
                Debug.LogWarning("[LevelObjectiveTracker] 关卡配置没有目标，关卡将立即达成。");
                return;
            }

            _effectiveRequired = new int[_config.Objectives.Length];

            int available = 0;
            int towers = 0;
            int waterBlocks = 0;   // M9 R3：场景实际堵塞点数（PuzzleSolved 夹取用）
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                // 塔改为"攻击击毁"：按场景现存的 Tower01 数量统计可击毁塔数
                if (behaviours[i] is Tower01Controller tower && !tower.IsDisabled)
                {
                    towers++;
                }

                if (behaviours[i] is IPurifiable target && !target.IsPurified)
                {
                    if (behaviours[i] is WaterBlockage)
                    {
                        waterBlocks++;   // 堵塞点单独统计，不算普通污染源
                        continue;
                    }

                    available++;
                }
            }

            for (int i = 0; i < _config.Objectives.Length; i++)
            {
                var obj = _config.Objectives[i];
                if (obj == null)
                {
                    continue;
                }

                if (obj.Type == LevelObjectiveType.PurifyTargets && obj.Required > available)
                {
                    Debug.LogWarning($"[LevelObjectiveTracker] 目标 {i} 要求净化 {obj.Required} 个污染源，场景仅有 {available} 个，已按实际夹取。");
                    _effectiveRequired[i] = available;
                }
                else if (obj.Type == LevelObjectiveType.PurifyTowers && obj.Required > towers)
                {
                    Debug.LogWarning($"[LevelObjectiveTracker] 目标 {i} 要求净化 {obj.Required} 座塔，场景仅有 {towers} 座，已按实际夹取。");
                    _effectiveRequired[i] = towers;
                }
                else if (obj.Type == LevelObjectiveType.PuzzleSolved && obj.Required > waterBlocks)
                {
                    Debug.LogWarning($"[LevelObjectiveTracker] 目标 {i} 要求清理 {obj.Required} 处堵塞，场景仅有 {waterBlocks} 处，已按实际夹取。");
                    _effectiveRequired[i] = waterBlocks;
                }
                else
                {
                    _effectiveRequired[i] = obj.Required;
                }
            }
        }

        private static bool IsTower(string targetId)
        {
            return !string.IsNullOrEmpty(targetId) && targetId.StartsWith(TowerIdPrefix, StringComparison.Ordinal);
        }
    }
}
