namespace StarEco.Level.Events
{
    /// <summary>
    /// 当前目标变化事件：解锁下一目标 / 目标计数变化时广播，供 M7 HUD 显示（R3）。
    /// struct 零 GC。
    /// </summary>
    public struct LevelObjectiveChangedEvent
    {
        /// <summary>当前目标序号（-1 = 链已全部完成）。</summary>
        public int Index;

        /// <summary>目标显示文本。</summary>
        public string Label;

        /// <summary>已完成数量。</summary>
        public int Current;

        /// <summary>所需数量。</summary>
        public int Required;
    }
}
