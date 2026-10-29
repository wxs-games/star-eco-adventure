using UnityEngine;
using StarEco.EnemyAI;
using StarEco.Framework;
using StarEco.Eco.Events;

namespace StarEco.Eco
{
    /// <summary>
    /// 塔净化适配器：让 Tower01 实现 IPurifiable，零改 M4 已验收源码。
    /// 净化完成后只做两件事：调用 M4 预留的 RequestDisable()（幂等由 M4 保证）
    /// 与广播 GarbagePurifiedEvent；关闭表现复用 M4 Tower01_Disable 动画。
    /// </summary>
    [RequireComponent(typeof(Tower01Controller))]
    public class TowerPurifiable : MonoBehaviour, IResettablePurifiable
    {
        [SerializeField] private EcoConfig _config;

        [Tooltip("净化阈值：-1 = 跟随 EcoConfig.TowerPurifyRequired，≥0 显式覆盖")]
        [SerializeField] private int _purifyRequired = -1;

        [Tooltip("净化值贡献：-1 = 跟随 EcoConfig.TowerPurityValue，≥0 显式覆盖")]
        [SerializeField] private int _purityValue = -1;

        [SerializeField] private string _targetId = "Tower01";

        private Tower01Controller _tower;
        private int _progress;
        private bool _purified;

        public bool IsPurified => _purified;

        public int PurityValue => _purityValue >= 0
            ? _purityValue
            : (_config != null ? _config.TowerPurityValue : 10);

        private void Awake()
        {
            _tower = GetComponent<Tower01Controller>();

            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }

            if (_tower != null)
            {
                _tower.Disabled += OnTowerDisabled;
            }
        }

        private void OnDestroy()
        {
            if (_tower != null)
            {
                _tower.Disabled -= OnTowerDisabled;
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

            int required = _purifyRequired >= 0
                ? _purifyRequired
                : (_config != null ? _config.TowerPurifyRequired : 30);

            if (_progress >= required)
            {
                CompletePurify();
            }
        }

        /// <summary>
        /// 恢复未净化初始态（M6 关卡重开）。
        /// 注意：塔已 RequestDisable（关闭）后无法复活 —— 这是 M4 的单向开关语义，
        /// 因此重开只重置净化进度，塔保持关闭（重开后该目标不再贡献净化值）。
        /// </summary>
        public void ResetPurify()
        {
            _progress = 0;
            _purified = false;
        }

        /// <summary>
        /// 攻击击毁（非喷雾净化）时补记净化事件：塔的 10% 已计入 PuritySystem 分母，
        /// 若击毁不补发事件，净化值会永远封顶在“垃圾总分 / 含塔总分”。
        /// </summary>
        private void OnTowerDisabled()
        {
            CompletePurify(true);
        }

        /// <param name="alreadyDisabled">塔已处于关闭态（攻击击毁路径），不再回调 RequestDisable。</param>
        private void CompletePurify(bool alreadyDisabled = false)
        {
            if (_purified)
            {
                return;   // 幂等：喷雾净化与击毁只记账一次
            }

            _purified = true;

            // 关塔走 M4 既有入口（幂等），净化语义 = 塔关闭（R8）
            if (!alreadyDisabled && _tower != null)
            {
                _tower.RequestDisable();
            }

            EventCenter.Publish(new GarbagePurifiedEvent
            {
                PurityValue = PurityValue,
                Position = transform.position,
                TargetId = _targetId
            });
        }
    }
}
