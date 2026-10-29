using UnityEngine;

namespace StarEco.Combat
{
    /// <summary>
    /// 战斗数值配置（ScriptableObject 数据驱动，GDD 数值的落地处）。
    /// 创建：Assets → Create → StarEco → Combat Config
    /// </summary>
    [CreateAssetMenu(fileName = "CombatConfig", menuName = "StarEco/Combat Config", order = 1)]
    public class CombatConfig : ScriptableObject
    {
        [Header("生命")]
        [Tooltip("玩家基础 HP（整数定点，单位半心点：2 点 = 1 颗心，10 点 = 5 颗心）")]
        public int MaxHP = 10;

        [Header("攻击")]
        [Tooltip("每段攻击伤害（半心点：2 点 = 1 颗心）")]
        public int AttackDamage = 2;

        [Tooltip("连段输入窗口：本段动画开始后可缓冲下一段输入的时长 (s)")]
        public float ComboWindowSeconds = 0.15f;

        [Tooltip("第三段命中附带的击退速度 (u/s)，0 = 不击退；仅在第一/二段近身判定时用到")]
        public float ThirdHitKnockback = 6f;

        [Header("第三段：钳子投掷（回旋）")]
        [Tooltip("投掷命中伤害（半心点：2 点 = 1 颗心）")]
        public int ThrowDamage = 3;

        [Tooltip("投掷命中附带的击退速度 (u/s)")]
        public float ThrowKnockback = 11f;

        [Tooltip("出手飞行速度 (u/s)")]
        public float ThrowSpeed = 17f;

        [Tooltip("出手最远飞行距离 (u)，飞满后自动折返")]
        public float ThrowRange = 5.5f;

        [Tooltip("回程归位速度 (u/s)")]
        public float ThrowReturnSpeed = 22f;

        [Tooltip("命中判定半径 (u)：沿飞行路径做圆形扫描，避免高速穿过敌人")]
        public float ThrowHitRadius = 0.26f;

        [Tooltip("飞行中自转速度 (度/秒)，0 = 不自转")]
        public float ThrowSpinSpeed = 900f;

        [Tooltip("保险时限 (s)：超时强制折返，避免钳子卡在半空")]
        public float ThrowMaxFlightTime = 4f;

        [Tooltip("飞行中钳子的视觉放大倍数（纯视觉，不影响命中判定半径）")]
        public float ThrowVisualScale = 1.25f;

        [Tooltip("飞行拖尾时长 (s)，0 = 关闭拖尾")]
        public float ThrowTrailTime = 0.14f;

        [Tooltip("各段动画时长兜底 (s)，索引 = 段号（0=空中攻击 1=Attack01 2=Attack02 3=Attack03）。T7 接入 Animation Event 后以事件为准")]
        public float[] SegmentEndTimes = { 0.25f, 0.3333f, 0.3333f, 0.375f };

        [Header("受击")]
        [Tooltip("受击硬直时长 (s)，锁输入（GDD 初值 0.6）。无敌与击退参数归 PlayerConfig（M2 已实现，单一事实来源）")]
        public float StunDuration = 0.6f;

        /// <summary>配置资产缺失时的代码兜底。</summary>
        public static CombatConfig CreateDefault()
        {
            var config = CreateInstance<CombatConfig>();
            Debug.LogWarning("[CombatConfig] 未找到配置资产，已使用代码默认值。");
            return config;
        }
    }
}
