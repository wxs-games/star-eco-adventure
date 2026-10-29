using System;
using UnityEngine;

namespace StarEco.Level
{
    /// <summary>
    /// 目标类型：目标链每一项的达成条件（R3）。
    /// </summary>
    public enum LevelObjectiveType
    {
        /// <summary>净化 N 个污染源（垃圾堆/挡路墙，按 TargetId 非塔判定）。</summary>
        PurifyTargets = 0,

        /// <summary>净化 N 座塔（TargetId 以 Tower 开头）。</summary>
        PurifyTowers = 1,

        /// <summary>关卡净化值百分比达标（如 100%）。</summary>
        ReachPurity = 2,

        /// <summary>击杀 N 个敌人。</summary>
        DefeatEnemies = 3,

        /// <summary>解救 N 个被困动物（M9：E 键互动，AnimalRescuedEvent 计数）。</summary>
        RescueTargets = 4,

        /// <summary>疏通河道（M9 水流解谜：清理 N 处堵塞点，GarbagePurifiedEvent 的 WaterBlock 前缀计数）。</summary>
        PuzzleSolved = 5
    }

    /// <summary>
    /// 关卡配置（ScriptableObject 数据驱动，GDD 关卡数据的落地处）。
    /// 一关一份资产（Assets/Settings/Levels/Level01.asset），目标链/刷怪表/星级阈值全部可在 Inspector 编辑。
    /// </summary>
    [CreateAssetMenu(fileName = "Level01", menuName = "StarEco/Level Config", order = 4)]
    public class LevelConfig : ScriptableObject
    {
        // ===== 目标链 =====

        /// <summary>单个目标（顺序解锁，只有当前目标计数）。</summary>
        [Serializable]
        public class LevelObjective
        {
            [Tooltip("达成条件类型")]
            public LevelObjectiveType Type = LevelObjectiveType.PurifyTargets;

            [Tooltip("所需数量：PurifyTargets=污染源个数 / PurifyTowers=塔数 / ReachPurity=百分比(0~100) / DefeatEnemies=敌人数 / RescueTargets=动物数 / PuzzleSolved=堵塞点数")]
            public int Required = 1;

            [Tooltip("UI 显示文本（M7 HUD 消费）")]
            public string Label = "净化污染源";

            /// <summary>构造（代码建链用）。</summary>
            public LevelObjective(LevelObjectiveType type, int required, string label)
            {
                Type = type;
                Required = required;
                Label = label;
            }
        }

        // ===== 刷怪 =====

        /// <summary>刷怪条目：某类敌人在一组锚点上的生成规则（R5）。</summary>
        [Serializable]
        public class SpawnEntry
        {
            [Tooltip("敌人预制体（须为预制体资产，走 PoolManager 对象池）")]
            public GameObject Prefab;

            [Tooltip("生成锚点（循环取用；为空则该条目报错跳过）")]
            public Transform[] Anchors = Array.Empty<Transform>();

            [Tooltip("同时存在上限（名额；敌人死亡后回收）")]
            public int MaxAlive = 2;

            [Tooltip("基础生成间隔 (s)，实际间隔 = 该值 × Lerp(1.5, 0.5, 污染度)，污染越高间隔越短")]
            public float BaseInterval = 4f;

            [Tooltip("仅在该序号目标完成后才开始生成（-1 = 一开始就刷）")]
            public int RequiredObjectiveIndex = -1;
        }

        // ===== 标识 =====

        [Header("标识")]
        [Tooltip("关卡序号（存档/解锁进度用，从 1 开始）")]
        public int LevelIndex = 1;

        [Tooltip("关卡名（结算面板显示）")]
        public string LevelName = "第一关：污染初现的星球";

        [Header("出生点")]
        [Tooltip("关卡开始时玩家的出生位置")]
        public Vector2 SpawnPoint = new Vector2(0f, 2f);

        [Header("目标链")]
        [Tooltip("有序目标链：逐个解锁激活，全完成即达成关卡目标")]
        public LevelObjective[] Objectives = Array.Empty<LevelObjective>();

        [Header("刷怪")]
        [Tooltip("刷怪表（污染度驱动：污染越高刷得越快）")]
        public SpawnEntry[] SpawnEntries = Array.Empty<SpawnEntry>();

        [Header("结算")]
        [Tooltip("三星所需净化度百分比 (0~100)")]
        public int Star3Purity = 100;

        [Tooltip("二星所需净化度百分比 (0~100)")]
        public int Star2Purity = 60;

        [Tooltip("一星所需净化度百分比 (0~100)，达标即至少一星")]
        public int Star1Purity = 30;

        [Tooltip("限时 (s)，0 = 不限时。本里程碑不消费该字段（超时失败属 M7，见 design §3）")]
        public float TimeLimit = 0f;

        [Header("开场")]
        [TextArea(2, 4)]
        [Tooltip("开场提示语（M7 提示牌/对话框消费）")]
        public string IntroText = "这颗星球已经被垃圾污染了。用喷壶把污染源清理干净，让天空重新变绿。";

        /// <summary>按净化度百分比计算星级（0~3，失败局面由调用方给 0）。</summary>
        public int EvaluateStars(float purityPercent)
        {
            if (purityPercent >= Star3Purity) return 3;
            if (purityPercent >= Star2Purity) return 2;
            if (purityPercent >= Star1Purity) return 1;
            return 0;
        }

        /// <summary>配置资产缺失时的代码兜底（保证关卡不因漏配而崩溃）。</summary>
        public static LevelConfig CreateDefault()
        {
            var config = CreateInstance<LevelConfig>();
            config.LevelIndex = 1;
            config.LevelName = "默认关卡";
            config.Objectives = new[]
            {
                new LevelObjective(LevelObjectiveType.ReachPurity, 100, "关卡净化值达到 100%")
            };
            Debug.LogWarning("[LevelConfig] 未找到关卡配置资产，已使用代码默认值。");
            return config;
        }
    }
}
