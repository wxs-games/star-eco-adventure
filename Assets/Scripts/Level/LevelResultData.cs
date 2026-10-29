using System;

namespace StarEco.Level
{
    /// <summary>
    /// 关卡结算数据（R6）：JsonUtility 支持的字段（基本类型 + string），可安全写档。
    /// </summary>
    [Serializable]
    public class LevelResultData
    {
        /// <summary>关卡序号。</summary>
        public int LevelIndex = 1;

        /// <summary>是否通关（失败局面为 false，星级恒 0）。</summary>
        public bool Cleared;

        /// <summary>结算时净化度百分比 [0, 100]。</summary>
        public float PurityPercent;

        /// <summary>结算时已耗时（秒）。</summary>
        public float ElapsedSeconds;

        /// <summary>击杀数。</summary>
        public int KillCount;

        /// <summary>拾取数。</summary>
        public int PickupCount;

        /// <summary>星级 0~3。</summary>
        public int Stars;

        /// <summary>达成时间戳（UTC ticks，便于日志与排序）。</summary>
        public long TimestampUtcTicks;
    }

    /// <summary>
    /// 进度数据（R6）：已解锁关卡序号 + 是否完成当前关。
    /// 平铺字段（JsonUtility 不支持字典与嵌套泛型）。
    /// </summary>
    [Serializable]
    public class ProgressData
    {
        /// <summary>已解锁到的最高关卡序号（1 起）。</summary>
        public int UnlockedLevelIndex = 1;

        /// <summary>当前关是否已通关（有可续关进度）。</summary>
        public bool LevelCleared;

        /// <summary>当前关历史最佳星级。</summary>
        public int BestStars;
    }
}
