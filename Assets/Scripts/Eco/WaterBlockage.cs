using UnityEngine;
using StarEco.Framework;
using StarEco.Eco.Events;

namespace StarEco.Eco
{
    /// <summary>
    /// 河道堵塞点（M9 R3）：实现 IPurifiable 的静态可净化障碍，语义 = 拦住水流的污染物。
    /// 与 GarbagePile 同构（喷雾净化 → 广播 GarbagePurifiedEvent），差异只有两点：
    /// ① TargetId 固定前缀 WaterBlock（LevelObjectiveTracker 据此单独计 PuzzleSolved 进度）；
    /// ② 净化完成后隐藏自身（清走堵塞物），不像挡路墙那样只关 Collider。
    /// 由 WaterPuzzleController 统一收齐并驱动水位上升。
    /// </summary>
    public class WaterBlockage : MonoBehaviour, IResettablePurifiable
    {
        [SerializeField] private EcoConfig _config;

        [Tooltip("净化阈值：-1 = 跟随 EcoConfig.GarbageWallPurifyRequired，≥0 显式覆盖")]
        [SerializeField] private int _purifyRequired = -1;

        [Tooltip("净化值贡献：-1 = 跟随 EcoConfig.GarbageWallPurityValue，≥0 显式覆盖")]
        [SerializeField] private int _purityValue = -1;

        [Tooltip("目标标识（须以 WaterBlock 开头，Tracker 据此计数）")]
        [SerializeField] private string _targetId = "WaterBlock";

        private SpriteRenderer _sprite;
        private BoxCollider2D _collider;
        private int _progress;
        private bool _purified;

        public bool IsPurified => _purified;

        public int PurityValue => _purityValue >= 0
            ? _purityValue
            : (_config != null ? _config.GarbageWallPurityValue : 10);

        private void Awake()
        {
            _sprite = GetComponentInChildren<SpriteRenderer>();
            _collider = GetComponent<BoxCollider2D>();

            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }

            if (_sprite != null)
            {
                // 堵塞物用垃圾墙贴图（本身有完整色彩），不再整体染色 —— 染色会把画面压成黑块。
                // 污染态改为「压暗 + 去饱和」，净化过程中逐渐恢复原色。
                _sprite.color = new Color(0.62f, 0.60f, 0.58f, 1f);
            }
        }

        // ===== IPurifiable =====

        public void ReceivePurify(int amount, GameObject source)
        {
            if (_purified || amount <= 0)
            {
                return;   // 幂等
            }

            _progress += amount;

            int required = _purifyRequired >= 0
                ? _purifyRequired
                : (_config != null ? _config.GarbageWallPurifyRequired : 30);

            if (_progress < required)
            {
                // 进度即时反馈：从压暗去饱和逐渐恢复到原色（同 GarbagePile 的插值思路，
                // 但基准色改为白色而非污染色，避免贴图被整体染色）
                if (_sprite != null)
                {
                    float t = Mathf.Clamp01((float)_progress / Mathf.Max(1, required));
                    _sprite.color = Color.Lerp(new Color(0.62f, 0.60f, 0.58f, 1f), Color.white, t);
                }
                return;
            }

            CompletePurify();
        }

        /// <summary>恢复未净化初始态（关卡重载场景即可，这里留接口备用）。</summary>
        public void ResetPurify()
        {
            _progress = 0;
            _purified = false;
            gameObject.SetActive(true);
            if (_sprite != null)
            {
                _sprite.color = new Color(0.62f, 0.60f, 0.58f, 1f);
            }
        }

        private void CompletePurify()
        {
            _purified = true;

            EventCenter.Publish(new GarbagePurifiedEvent
            {
                PurityValue = PurityValue,
                Position = transform.position,
                TargetId = _targetId
            });

            // 清走堵塞物：整体隐藏（水流通了）
            gameObject.SetActive(false);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, Vector3.one);
        }
    }
}
