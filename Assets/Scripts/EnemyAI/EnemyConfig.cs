using UnityEngine;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// 敌人/炮台战斗数值配置（ScriptableObject 数据驱动，GDD 数值的落地处）。
    /// 创建：Assets → Create → StarEco → Enemy Config
    /// 每单位一份资产（Enemy01Config/Enemy02Config/Enemy03Config/Tower01Config），共用同一字段结构。
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "StarEco/Enemy Config", order = 2)]
    public class EnemyConfig : ScriptableObject
    {
        public enum AttackKindEnum { Melee, Ranged }

        [Header("标识")]
        [Tooltip("单位类型标识，死亡事件携带（如 Enemy01/Enemy02/Enemy03/Tower01）")]
        public string EnemyTypeId = "Enemy01";

        [Tooltip("是否固定单位（Tower01 = true，不位移、无 Rigidbody2D）")]
        public bool IsStatic = false;

        [Tooltip("攻击类型：近战或远程（决定注册哪种攻击状态）")]
        public AttackKindEnum AttackKind = AttackKindEnum.Melee;

        [Header("生命")]
        [Tooltip("最大血量（半心点：2 点 = 1 颗心；Enemy01/02/03 = 6/8/10 = 3/4/5 颗心）")]
        public int MaxHP = 6;

        [Tooltip("受击击退系数（Tower01 固定 0，受击不位移）")]
        public float KnockbackResponse = 1f;

        [Header("移动")]
        [Tooltip("水平移速 (u/s)，Enemy03 显著大于 Enemy01")]
        public float MoveSpeed = 1.5f;

        [Header("攻击")]
        [Tooltip("对玩家伤害值（半心点：2 点 = 1 颗心；Enemy02 远程毒弹为 1 点 = 半颗心）")]
        public int AttackDamage = 2;

        [Tooltip("攻击距离 (u)，Enemy02 远程大于 Enemy01")]
        public float AttackRange = 1.0f;

        [Tooltip("攻击冷却时长 (s)")]
        public float AttackInterval = 1.2f;

        [Tooltip("命中玩家附带的击退力 (u/s)，写入 DamageInfo.KnockbackPower")]
        public float AttackKnockback = 0f;

        [Header("感知")]
        [Tooltip("感知半径 (u)")]
        public float PerceptionRadius = 6f;

        [Tooltip("脱战时长 (s)，目标丢失后保持追击的宽限时间")]
        public float LoseTargetSeconds = 1.5f;

        [Tooltip("已锁定目标后的脱战缓冲半径 (u)，防抖")]
        public float LoseTargetBuffer = 1.0f;

        [Tooltip("同层判定垂直容差 (u)，超过则视为不同地面层")]
        public float SameGroundToleranceY = 1.0f;

        [Header("受击")]
        [Tooltip("受击硬直时长 (s)")]
        public float HurtStunSeconds = 0.3f;

        [Header("弹体")]
        [Tooltip("弹体速度 (u/s)")]
        public float ProjectileSpeed = 6f;

        [Tooltip("弹体最大射程 (u)")]
        public float ProjectileMaxRange = 8f;

        [Tooltip("弹体池预热数量")]
        public int ProjectilePrewarm = 4;

        [Header("塔")]
        [Tooltip("Tower01 Aim 兜底时长 (s)，事件帧为准")]
        public float AimSeconds = 0.4f;

        [Tooltip("Tower01 Fire 兜底时长 (s)，事件帧为准")]
        public float FireSeconds = 0.3f;

        [Header("出场")]
        [Tooltip("出场时长兜底 (s)，0 = 无出场（Enemy03 > 0）")]
        public float SpawnSeconds = 0f;

        [Header("掉落")]
        [Tooltip("掉落表引用（具体类型由 M5 落地，M4 只透传）")]
        public ScriptableObject DropTable = null;

        [Header("动画")]
        [Tooltip("动画帧率 (fps)，与手绘 12fps 一致")]
        public float AnimFrameRate = 12f;

        [Header("碰撞层")]
        [Tooltip("玩家层掩码（感知与攻击判定用）")]
        public LayerMask PlayerLayer = ~0;

        [Tooltip("地面层掩码（弹体触地用）")]
        public LayerMask GroundLayer = ~0;

        /// <summary>配置资产缺失时的代码兜底（保证游戏不因漏配而崩溃）。</summary>
        public static EnemyConfig CreateDefault()
        {
            var config = CreateInstance<EnemyConfig>();
            Debug.LogWarning("[EnemyConfig] 未找到配置资产，已使用代码默认值。");
            return config;
        }
    }
}
