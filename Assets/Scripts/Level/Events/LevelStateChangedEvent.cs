namespace StarEco.Level.Events
{
    /// <summary>
    /// 关卡状态迁移事件：LevelManager 广播（R2）。
    /// struct 零 GC。
    /// </summary>
    public struct LevelStateChangedEvent
    {
        /// <summary>迁移前状态。</summary>
        public LevelState OldState;

        /// <summary>迁移后状态。</summary>
        public LevelState NewState;
    }
}
