namespace StarEco.Eco.Events
{
    /// <summary>
    /// 拾取物收集事件：PurifyPickup 广播，供 M7 HUD 订阅。
    /// struct 零 GC。
    /// </summary>
    public struct PickupCollectedEvent
    {
        /// <summary>拾取物类型（初期仅 PurifyBottle 净化瓶）。</summary>
        public PickupKind Kind;

        /// <summary>回复数量。</summary>
        public int Amount;
    }
}
