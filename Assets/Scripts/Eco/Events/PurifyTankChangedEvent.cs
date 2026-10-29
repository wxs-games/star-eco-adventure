namespace StarEco.Eco.Events
{
    /// <summary>
    /// 净化剂变化事件：PurifyTank 广播，供 M7 HUD 订阅。
    /// struct 零 GC。
    /// </summary>
    public struct PurifyTankChangedEvent
    {
        /// <summary>当前净化剂。</summary>
        public int Current;

        /// <summary>净化剂容量。</summary>
        public int Capacity;
    }
}
