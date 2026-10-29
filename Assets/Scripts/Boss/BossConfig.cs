using UnityEngine;

namespace StarEco.Boss
{
    /// <summary>
    /// Boss 配置（M8 R1）：继承 M4 `EnemyConfig`，复用 HP/移速/攻击/感知/弹幕等通用字段，
    /// 额外提供阶段阈值、双技能、特殊招与净化倍率。
    /// 创建：Assets → Create → StarEco → Boss Config
    /// </summary>
    [CreateAssetMenu(fileName = "BossConfig", menuName = "StarEco/Boss Config", order = 3)]
    public class BossConfig : StarEco.EnemyAI.EnemyConfig
    {
        [Header("阶段阈值（HP 百分比）")]
        [Tooltip("降到该百分比以下切入第二阶段（过场 + 狂暴）")]
        [Range(1f, 99f)] public float Phase2HpPercent = 60f;

        [Tooltip("降到该百分比以下切入第三阶段（过场 + 最终形态）")]
        [Range(1f, 99f)] public float Phase3HpPercent = 30f;

        [Tooltip("阶段切换过场时长（秒），期间无敌")]
        public float PhaseTransitionSeconds = 0.8f;

        [Header("技能 A · 近战")]
        [Tooltip("近战触发距离")]
        public float AttackARange = 2.2f;

        [Tooltip("近战冷却（秒），实际间隔再乘阶段系数")]
        public float AttackAInterval = 2.0f;

        [Header("技能 B · 弹幕")]
        [Tooltip("弹幕冷却（秒）")]
        public float AttackBInterval = 3.0f;

        [Tooltip("一次发射的弹数（P1 起）")]
        public int ProjectileCount = 3;

        [Tooltip("扇形弹幕张角（度）")]
        public float ProjectileSpreadDegrees = 24f;

        [Header("特殊招（仅 P3）")]
        [Tooltip("特殊招冷却（秒）")]
        public float SpecialInterval = 6.0f;

        [Tooltip("特殊招弹数（环绕扇形）")]
        public int SpecialProjectileCount = 5;

        [Tooltip("特殊招张角（度）")]
        public float SpecialSpreadDegrees = 140f;

        [Header("技能节奏（前摇 / 命中 / 后摇，秒）")]
        [Tooltip("技能 A 前摇：起手到命中判定；期间霸体、不移动")]
        public float AttackAWindupSeconds = 0.20f;

        [Tooltip("技能 A 命中阶段：覆盖攻击动画剩余帧（动画 0.83s）")]
        public float AttackAActiveSeconds = 0.62f;

        [Tooltip("技能 A 后摇：动画结束后的站定，可被打断")]
        public float AttackARecoverSeconds = 0.30f;

        [Tooltip("技能 B 前摇：播放蓄力动作（Charge，核心能量反复鼓胀）到弹幕发射；期间霸体、不移动。需与 Charge 动画时长 1.1s 对齐")]
        public float AttackBWindupSeconds = 1.10f;

        [Tooltip("技能 B 命中阶段：覆盖攻击动画剩余帧（动画 1.00s）")]
        public float AttackBActiveSeconds = 0.60f;

        [Tooltip("技能 B 后摇：动画结束后的站定，可被打断")]
        public float AttackBRecoverSeconds = 0.30f;

        [Tooltip("特殊招前摇：播放蓄力动作（Charge）到弹幕发射；期间霸体、不移动。比技能 B 略长以示更重的招")]
        public float SpecialWindupSeconds = 1.40f;

        [Tooltip("特殊招命中阶段：覆盖攻击动画剩余帧（动画 1.17s）")]
        public float SpecialActiveSeconds = 0.62f;

        [Tooltip("特殊招后摇：动画结束后的站定，可被打断")]
        public float SpecialRecoverSeconds = 0.35f;

        [Header("追击节奏")]
        [Tooltip("远程技能收尾后的站定观望时长（秒）：先远程打，观望结束才逼近；实际时长再乘阶段系数")]
        public float PursueDelaySeconds = 1.4f;

        [Tooltip("一次逼近持续的最短时长（秒）：走满这一段才允许停下来放远程，形成走走停停的节奏")]
        public float ChaseBurstSeconds = 1.1f;

        [Header("狂暴成长")]
        [Tooltip("P2/P3 移速倍率")]
        public float EnrageSpeedMultiplier = 1.3f;

        [Tooltip("P2 攻击间隔倍率")]
        public float Phase2IntervalScale = 0.8f;

        [Tooltip("P3 攻击间隔倍率")]
        public float Phase3IntervalScale = 0.7f;

        [Header("净化伤害")]
        [Tooltip("净化喷雾对 Boss 的伤害倍率（M8 R4：两种伤害都吃，喷雾加倍）")]
        public float PurifyDamageMultiplier = 1.5f;

        /// <summary>配置缺失时的代码兜底（GDD §八 数值表初值）。</summary>
        public static new BossConfig CreateDefault()
        {
            var config = CreateInstance<BossConfig>();
            config.EnemyTypeId = "Boss01";
            config.MaxHP = 80;
            Debug.LogWarning("[BossConfig] 未找到配置资产，已使用代码默认值（HP 80 半心点 = 40 心 / 60% / 30%）。");
            return config;
        }
    }
}
