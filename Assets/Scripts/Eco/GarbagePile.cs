using UnityEngine;
using StarEco.Framework;
using StarEco.Eco.Events;

namespace StarEco.Eco
{
    /// <summary>
    /// 垃圾堆/挡路重垃圾（静态可净化目标）：实现 IPurifiable。
    /// 净化进度驱动占位 Sprite 颜色插值（污染色 → 净化色，即时反馈）；
    /// 完成后播 fx_purify、广播事件；挡路型关闭阻挡 Collider 打开通路。
    /// 进度中断不衰减（design §2.1）。美术后补仅换 Sprite，逻辑不变。
    /// </summary>
    public class GarbagePile : MonoBehaviour, IResettablePurifiable
    {
        [SerializeField] private EcoConfig _config;
        [SerializeField] private FxOneShot _fxPurifyPrefab;

        [Tooltip("净化完成后的贴图（绿油油草地/灌木）；未配置则退回纯色插值")]
        [SerializeField] private Sprite _cleanSprite;

        [Tooltip("净化阈值：-1 = 跟随 EcoConfig（按 BlocksPath 分流），≥0 显式覆盖")]
        [SerializeField] private int _purifyRequired = -1;

        [Tooltip("净化值贡献：-1 = 跟随 EcoConfig（按 BlocksPath 分流），≥0 显式覆盖")]
        [SerializeField] private int _purityValue = -1;

        [Tooltip("是否挡路型（净化完成后关闭阻挡 Collider 打开通路）")]
        [SerializeField] private bool _blocksPath = false;

        [Tooltip("目标标识（净化完成事件携带）")]
        [SerializeField] private string _targetId = "GarbagePile";

        private SpriteRenderer _sprite;
        private Sprite _originalSprite;
        private Collider2D _collider;
        private int _progress;
        private bool _purified;

        public bool IsPurified => _purified;

        public int PurityValue => _purityValue >= 0
            ? _purityValue
            : (_blocksPath ? ConfigValue(c => c.GarbageWallPurityValue, 10) : ConfigValue(c => c.GarbagePurityValue, 5));

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            _collider = GetComponent<Collider2D>();

            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }

            // 初始态：污染色；记住原始贴图，重置时还原
            if (_sprite != null)
            {
                _originalSprite = _sprite.sprite;
                _sprite.color = _config.GarbagePollutedTint;
            }
        }

        // ===== IPurifiable =====

        public void ReceivePurify(int amount, GameObject source)
        {
            if (_purified || amount <= 0)
            {
                return;   // 幂等（R1）
            }

            _progress += amount;

            int required = ResolveRequired();
            float t = required > 0 ? Mathf.Clamp01((float)_progress / required) : 1f;

            // 净化中：颜色随进度插值（R5 即时反馈）
            if (_sprite != null)
            {
                _sprite.color = Color.Lerp(_config.GarbagePollutedTint, _config.GarbageCleanTint, t);
            }

            if (t >= 1f)
            {
                CompletePurify();
            }
        }

        private void CompletePurify()
        {
            _purified = true;

            if (_sprite != null)
            {
                if (_cleanSprite != null)
                {
                    // 净化完成：换成绿油油的草地/灌木贴图（与污染垃圾形成强对比）
                    _sprite.sprite = _cleanSprite;
                    _sprite.color = Color.white;
                }
                else
                {
                    _sprite.color = _config.GarbageCleanTint;
                }
            }

            FxOneShot.Play(_fxPurifyPrefab, transform.position);

            EventCenter.Publish(new GarbagePurifiedEvent
            {
                PurityValue = PurityValue,
                Position = transform.position,
                TargetId = _targetId
            });

            // 净化完成：一律关闭阻挡碰撞体，净化后的目标不再挡路（R5）
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            // 挡路型额外视觉变淡，提示通路已打开
            if (_blocksPath && _sprite != null)
            {
                var c = _sprite.color;
                c.a = 0.3f;
                _sprite.color = c;
            }
        }

        /// <summary>恢复未净化初始态（M6 关卡重开）。</summary>
        public void ResetPurify()
        {
            _progress = 0;
            _purified = false;

            if (_collider != null)
            {
                _collider.enabled = true;
            }

            if (_sprite != null)
            {
                if (_originalSprite != null)
                {
                    _sprite.sprite = _originalSprite;   // 还原污染贴图
                }
                _sprite.color = _config != null ? _config.GarbagePollutedTint : Color.gray;
            }
        }

        private int ResolveRequired()        {
            if (_purifyRequired >= 0)
            {
                return _purifyRequired;
            }

            return _blocksPath
                ? ConfigValue(c => c.GarbageWallPurifyRequired, 60)
                : ConfigValue(c => c.GarbagePurifyRequired, 4);
        }

        private int ConfigValue(System.Func<EcoConfig, int> selector, int fallback)
        {
            return _config != null ? selector(_config) : fallback;
        }
    }
}
