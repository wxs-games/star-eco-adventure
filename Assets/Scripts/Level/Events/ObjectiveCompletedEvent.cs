namespace StarEco.Level.Events
{
    /// <summary>
    /// 单个目标完成事件：LevelObjectiveTracker 广播（R3）。
    /// struct 零 GC。
    /// </summary>
    public struct ObjectiveCompletedEvent
    {
        /// <summary>目标在链中的序号（0 起）。</summary>
        public int Index;

        /// <summary>目标显示文本。</summary>
        public string Label;
    }
}
