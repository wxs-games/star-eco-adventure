using System.Collections.Generic;
using UnityEngine;
using StarEco.Framework;
using StarEco.Eco.Events;
using StarEco.Level.Events;

namespace StarEco.Level
{
    /// <summary>
    /// 水流解谜控制器（M9 R3，用户确认方案：清堵塞→水位升→通路）。
    /// 监听 GarbagePurifiedEvent 统计 WaterBlock 前缀的堵塞点；收齐后：
    /// 水位 1.5s 平滑上升 → 激活木筏通路（RaftPath 组）→ 广播 PuzzleSolvedEvent。
    /// 通路初始禁用（不过河够不着右侧区域），解谜完成后保持激活（本关不回退）。
    /// 计数与 LevelObjectiveTracker 的 PuzzleSolved 目标同源（都走 WaterBlock 前缀事件），
    /// 但互不依赖：Tracker 管目标链，本组件管场景表现。
    /// </summary>
    public class WaterPuzzleController : MonoBehaviour
    {
        private const string BlockPrefix = "WaterBlock";

        [Tooltip("堵塞点列表（为空时启动时自动扫描场景 WaterBlockage）")]
        [SerializeField] private WaterBlockageBridge[] _blockages = new WaterBlockageBridge[0];

        [Tooltip("水位视觉（整体上移的对象，如水域色块根）")]
        [SerializeField] private Transform _waterVisual;

        [Tooltip("水位低位 Y（初始）")]
        [SerializeField] private float _lowY = 0f;

        [Tooltip("水位高位 Y（上升后）")]
        [SerializeField] private float _highY = 1.2f;

        [Tooltip("上升时长（s）")]
        [SerializeField] private float _riseDuration = 1.5f;

        [Tooltip("木筏通路（初始禁用，解谜完成后激活）")]
        [SerializeField] private GameObject _raftPath;

        private readonly HashSet<string> _cleared = new HashSet<string>();
        private StarEco.Level.LevelStats _stats;
        private float _startTime;
        private bool _solved;
        private bool _rising;
        private float _riseT;

        /// <summary>解谜是否已完成。</summary>
        public bool IsSolved => _solved;

        /// <summary>已清理堵塞点数。</summary>
        public int ClearedCount => _cleared.Count;

        /// <summary>Bridge 序列化适配：让 Inspector/Setup 工具能挂场景对象引用。</summary>
        [System.Serializable]
        public class WaterBlockageBridge
        {
            public StarEco.Eco.WaterBlockage Blockage;
        }

        private void OnEnable()
        {
            EventCenter.Subscribe<GarbagePurifiedEvent>(OnGarbagePurified);
        }

        private void OnDisable()
        {
            EventCenter.Unsubscribe<GarbagePurifiedEvent>(OnGarbagePurified);
        }

        private void Start()
        {
            _stats = FindObjectOfType<StarEco.Level.LevelStats>();
            _startTime = _stats != null ? _stats.ElapsedSeconds : Time.time;

            // 未在 Inspector 挂堵塞点则自动扫描（Setup 工具会显式回填，这里是兜底）
            if (_blockages == null || _blockages.Length == 0)
            {
                var found = FindObjectsOfType<StarEco.Eco.WaterBlockage>();
                var list = new List<WaterBlockageBridge>();
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null && !found[i].IsPurified)
                    {
                        list.Add(new WaterBlockageBridge { Blockage = found[i] });
                    }
                }
                _blockages = list.ToArray();
                if (_blockages.Length == 0)
                {
                    Debug.LogWarning("[WaterPuzzle] 场景中没有堵塞点，解谜将立即完成。");
                }
            }

            if (_waterVisual != null)
            {
                _waterVisual.position = new Vector3(_waterVisual.position.x, _lowY, _waterVisual.position.z);
            }

            if (_raftPath != null)
            {
                _raftPath.SetActive(false);
            }
        }

        private void Update()
        {
            if (!_rising)
            {
                return;
            }

            _riseT += Time.deltaTime;
            float t = _riseDuration > 0f ? Mathf.Clamp01(_riseT / _riseDuration) : 1f;

            if (_waterVisual != null)
            {
                _waterVisual.position = new Vector3(
                    _waterVisual.position.x, Mathf.Lerp(_lowY, _highY, t), _waterVisual.position.z);
            }

            if (t >= 1f)
            {
                _rising = false;
                if (_raftPath != null)
                {
                    _raftPath.SetActive(true);
                    Debug.Log("[WaterPuzzle] 水位上升完成，木筏通路已激活。");
                }
            }
        }

        // ===== 事件 =====

        private void OnGarbagePurified(GarbagePurifiedEvent evt)
        {
            if (_solved)
            {
                return;
            }

            if (string.IsNullOrEmpty(evt.TargetId) || !evt.TargetId.StartsWith(BlockPrefix, System.StringComparison.Ordinal))
            {
                return;   // 普通污染源与解谜无关
            }

            _cleared.Add(evt.TargetId);
            Debug.Log($"[WaterPuzzle] 堵塞点 {evt.TargetId} 已清理（{_cleared.Count}/{ExpectedTotal()}）");

            if (_cleared.Count >= ExpectedTotal())
            {
                Solve();
            }
        }

        private int ExpectedTotal()
        {
            if (_blockages == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _blockages.Length; i++)
            {
                if (_blockages[i] != null && _blockages[i].Blockage != null)
                {
                    count++;
                }
            }
            return count;
        }

        private void Solve()
        {
            if (_solved)
            {
                return;
            }

            _solved = true;
            _rising = true;
            _riseT = 0f;

            AudioManager.Instance?.PlaySfx(SfxId.PuzzleSolved);   // M10

            float elapsed = _stats != null
                ? _stats.ElapsedSeconds - _startTime
                : Time.time - _startTime;

            EventCenter.Publish(new PuzzleSolvedEvent
            {
                Position = _waterVisual != null ? (Vector2)_waterVisual.position : transform.position,
                ElapsedInPuzzle = elapsed
            });

            Debug.Log($"[WaterPuzzle] 解谜完成：全部堵塞点已清理，水位上升中（耗时 {elapsed:F1}s）。");
        }
    }
}
