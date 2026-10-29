namespace StarEco.Eco
{
    /// <summary>
    /// 可重置的净化目标（M6 关卡重开用）。
    /// 与 <see cref="IPurifiable"/> 分离：净化协议本身不需要"撤销"，
    /// 只有"重开关卡"这一流程才需要把目标恢复成未净化。故用可选接口表达，
    /// 不污染 IPurifiable 契约（M5 零改动）。
    /// </summary>
    public interface IResettablePurifiable : IPurifiable
    {
        /// <summary>恢复为未净化初始态（进度清零、视觉与碰撞回到初始、允许重复净化与重复结算）。</summary>
        void ResetPurify();
    }
}
