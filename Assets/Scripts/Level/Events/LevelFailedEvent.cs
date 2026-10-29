namespace StarEco.Level.Events
{
    /// <summary>
    /// 关卡失败事件：LevelManager 在 Failed（玩家死亡/依赖缺失）时 one-shot 广播（R2）。
    /// struct 零 GC。
    /// </summary>
    public struct LevelFailedEvent
    {
        /// <summary>失败原因（供结算面板与日志展示）。</summary>
        public string Reason;
    }
}
