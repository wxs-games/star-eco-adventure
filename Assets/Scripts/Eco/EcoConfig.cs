using UnityEngine;

namespace StarEco.Eco
{
    /// <summary>
    /// 环保玩法数值配置（ScriptableObject 数据驱动，GDD §8 数值的落地处）。
    /// 创建：Assets → Create → StarEco → Eco Config
    /// 全里程碑唯一一份资产（Assets/Settings/EcoConfig.asset），所有 Eco 组件共用同一引用。
    /// </summary>
    [CreateAssetMenu(fileName = "EcoConfig", menuName = "StarEco/Eco Config", order = 3)]
    public class EcoConfig : ScriptableObject
    {
        [Header("喷洒")]
        [Tooltip("喷洒净化量/秒（仅作用于可净化目标；喷壶不对敌造成伤害，GDD §8 = 2）")]
        public int SprayDPS = 2;

        [Tooltip("喷洒结算周期 (s)，4 次/秒，净化量按周期向上取整")]
        public float SprayTickSeconds = 0.25f;

        [Tooltip("喷洒中移速因子（1 = 不减速；GDD 横版手感：减速不锁死）")]
        public float SprayMoveSpeedFactor = 0.3f;

        [Tooltip("预留：命中敌方目标时的净化剂消耗倍率（当前喷壶不对敌，未启用）")]
        public int EnemySprayCostMultiplier = 2;

        [Tooltip("喷洒基础净化剂消耗/秒")]
        public int TankConsumePerSecond = 8;

        [Header("蓄力净化波")]
        [Tooltip("蓄力到满所需时长 (s)")]
        public float HeavyChargeSeconds = 0.8f;

        [Tooltip("净化波单个可净化目标收到的净化量")]
        public int HeavyPurifyAmount = 40;

        [Tooltip("预留：净化波对敌伤害（当前净化波只净化，未启用）")]
        public int HeavyDamage = 3;

        [Tooltip("净化波对敌击退速度 (u/s)")]
        public float HeavyKnockback = 8f;

        [Tooltip("净化波净化剂消耗（进入蓄力的门条件）")]
        public int HeavyCost = 25;

        [Tooltip("净化波释放后的恢复硬直 (s)")]
        public float HeavyRecoverSeconds = 0.5f;

        [Tooltip("净化波判定范围 = 喷洒判定盒尺寸 × 该系数")]
        public float HeavyWaveScale = 2f;

        [Header("净化剂资源")]
        [Tooltip("净化剂容量（GDD §8 = 100）")]
        public int TankCapacity = 100;

        [Tooltip("开局净化剂（当前关卡体验：默认满槽开局）")]
        public int InitialTank = 100;

        [Tooltip("停止喷洒后，开始自动回复前的等待时间（秒）")]
        public float TankRechargeDelay = 1.5f;

        [Tooltip("净化剂自动回复速度（点/秒）")]
        public float TankRechargePerSecond = 12f;

        [Tooltip("拾取物单次回复量")]
        public int PickupTankAmount = 20;

        [Header("垃圾堆")]
        [Tooltip("普通垃圾净化阈值（= 2 秒喷洒，GDD'长按 2 秒'）")]
        public int GarbagePurifyRequired = 4;

        [Tooltip("挡路重垃圾净化阈值（蓄力波 ×1.5 次，普通喷洒不现实）")]
        public int GarbageWallPurifyRequired = 60;

        [Tooltip("普通垃圾净化值贡献（%，GDD 走读 +5%）")]
        public int GarbagePurityValue = 5;

        [Tooltip("挡路重垃圾净化值贡献")]
        public int GarbageWallPurityValue = 10;

        [Tooltip("垃圾堆污染态颜色（占位表现，净化进度插值起点）")]
        public Color GarbagePollutedTint = new Color(0.45f, 0.38f, 0.32f, 1f);

        [Tooltip("垃圾堆净化态颜色（占位表现，净化进度插值终点）")]
        public Color GarbageCleanTint = new Color(0.62f, 0.78f, 0.55f, 1f);

        [Header("塔")]
        [Tooltip("Tower01 净化阈值（3.75 秒持续喷洒或蓄力波 ×1）")]
        public int TowerPurifyRequired = 30;

        [Tooltip("塔净化值贡献")]
        public int TowerPurityValue = 10;

        [Header("氛围")]
        [Tooltip("污染态背景色（灰紫，GDD 主色板）")]
        public Color PollutedColor = new Color(0.45f, 0.40f, 0.52f, 1f);

        [Tooltip("清新态背景色（自然绿，GDD 主色板）")]
        public Color CleanColor = new Color(0.56f, 0.74f, 0.50f, 1f);

        [Tooltip("背景色平滑速率 (1/s)，越大过渡越快")]
        public float AtmosphereLerpSpeed = 2f;

        [Header("碰撞层")]
        [Tooltip("敌方层掩码（喷洒/净化波伤害分流判定用）")]
        public LayerMask EnemyLayer = ~0;

        [Tooltip("玩家层掩码（拾取物 Trigger 判定用）")]
        public LayerMask PlayerLayer = ~0;

        /// <summary>配置资产缺失时的代码兜底（保证游戏不因漏配而崩溃）。</summary>
        public static EcoConfig CreateDefault()
        {
            var config = CreateInstance<EcoConfig>();
            Debug.LogWarning("[EcoConfig] 未找到配置资产，已使用代码默认值。");
            return config;
        }
    }
}
