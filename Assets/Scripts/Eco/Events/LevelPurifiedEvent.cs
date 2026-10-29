namespace StarEco.Eco.Events
{
    /// <summary>
    /// 关卡净化完成事件（净化值 100%）：PuritySystem one-shot 广播，供 M6 关卡流程判定过关。
    /// struct 零 GC，无载荷。
    /// </summary>
    public struct LevelPurifiedEvent
    {
    }
}
