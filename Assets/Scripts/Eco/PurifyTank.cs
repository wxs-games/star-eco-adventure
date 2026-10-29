using UnityEngine;
using StarEco.Framework;
using StarEco.Eco.Events;

namespace StarEco.Eco
{
    /// <summary>
    /// 玩家净化剂资源组件：容量/当前值/消耗/回复，变更经事件中心广播（供 M7 HUD）。
    /// 值始终夹取在 0 ~ 容量之间，耗尽不产生负值。
    /// </summary>
    public class PurifyTank : MonoBehaviour
    {
        [SerializeField] private EcoConfig _config;

        private SprayController _spray;
        private float _rechargeDelayTimer;
        private float _rechargeProgress;

        public int Capacity => _config != null ? _config.TankCapacity : 100;

        /// <summary>当前净化剂。</summary>
        public int Current { get; private set; }

        /// <summary>是否已耗尽（喷洒/蓄力的门条件）。</summary>
        public bool IsEmpty => Current <= 0;

        private void Awake()
        {
            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }

            _spray = GetComponent<SprayController>();
            Current = Mathf.Clamp(_config.InitialTank, 0, _config.TankCapacity);
        }

        private void Start()
        {
            Broadcast();   // 初始基线广播，供 HUD/氛围获得起点
        }

        private void Update()
        {
            if (_config == null || Current >= Capacity)
            {
                _rechargeDelayTimer = 0f;
                _rechargeProgress = 0f;
                return;
            }

            bool spraying = _spray != null && _spray.IsSpraying;
            if (spraying)
            {
                _rechargeDelayTimer = 0f;
                _rechargeProgress = 0f;
                return;
            }

            _rechargeDelayTimer += Time.deltaTime;
            if (_rechargeDelayTimer < _config.TankRechargeDelay)
            {
                return;
            }

            _rechargeProgress += Mathf.Max(0f, _config.TankRechargePerSecond) * Time.deltaTime;
            int restore = Mathf.FloorToInt(_rechargeProgress);
            if (restore <= 0)
            {
                return;
            }

            _rechargeProgress -= restore;
            Restore(restore);
        }

        /// <summary>消耗净化剂，返回实际扣除值（不足时扣到 0 为止，不产生负值）。</summary>
        public int Consume(int amount)
        {
            int applied = Mathf.Min(Mathf.Max(0, amount), Current);
            if (applied > 0)
            {
                Current -= applied;
                Broadcast();
            }

            return applied;
        }

        /// <summary>回复净化剂，返回实际回复值（超出容量部分丢弃）。</summary>
        public int Restore(int amount)
        {
            int applied = Mathf.Min(Mathf.Max(0, amount), Capacity - Current);
            if (applied > 0)
            {
                Current += applied;
                Broadcast();
            }

            return applied;
        }

        private void Broadcast()
        {
            EventCenter.Publish(new PurifyTankChangedEvent { Current = Current, Capacity = Capacity });
        }
    }
}
