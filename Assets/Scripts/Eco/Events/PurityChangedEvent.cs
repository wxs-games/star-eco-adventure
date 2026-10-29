namespace StarEco.Eco.Events
{
    /// <summary>
    /// 净化值变化事件：PuritySystem 广播，供 AtmosphereController / M7 HUD 订阅。
    /// struct 零 GC。
    /// </summary>
    public struct PurityChangedEvent
    {
        /// <summary>已净化值。</summary>
        public int Current;

        /// <summary>场景全部可净化目标净化值总量。</summary>
        public int Total;

        /// <summary>净化进度百分比 [0, 1]。</summary>
        public float Percent;

        /// <summary>污染度 = 1 - 百分比（氛围插值与 M6 刷怪频率依据）。</summary>
        public float Pollution;
    }
}
