using System;

namespace StarEco.Level.Events
{
    /// <summary>
    /// 关卡通关事件：LevelManager 在 Completed 时 one-shot 广播（R2）。
    /// struct 承载引用类型字段，零 GC。
    /// </summary>
    public struct LevelCompletedEvent
    {
        /// <summary>结算数据（同一实例同时用于写档与 UI）。</summary>
        public LevelResultData Result;
    }
}
