using StarEco.EnemyAI;

namespace StarEco.EnemyAI.Events
{
    /// <summary>
    /// 敌人受伤事件：EventCenter 广播，供血条等表现层订阅。
    /// struct 零 GC；Enemy 为受击实例，表现层据此跟随与读取剩余 HP。
    /// </summary>
    public struct EnemyDamagedEvent
    {
        /// <summary>受击的敌人实例。</summary>
        public EnemyControllerBase Enemy;

        /// <summary>本次结算后的剩余 HP。</summary>
        public int CurrentHP;

        /// <summary>该单位的最大 HP。</summary>
        public int MaxHP;

        /// <summary>本次伤害值（飘字用）。</summary>
        public int Amount;
    }
}
