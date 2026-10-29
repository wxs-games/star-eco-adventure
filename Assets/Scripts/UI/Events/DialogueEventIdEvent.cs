using UnityEngine;
using StarEco.Framework;

namespace StarEco.UI.Events
{
    /// <summary>
    /// 对话事件 Id 广播（M7 R9）：对话/提示牌阅读结束时携带数据里的 EventId 广播。
    /// 关卡逻辑（如解锁通路）可在 M8/M9 订阅此事件。struct 零 GC。
    /// </summary>
    public struct DialogueEventIdEvent
    {
        /// <summary>对话数据 Id。</summary>
        public string DialogueId;

        /// <summary>数据里配置的事件 Id。</summary>
        public string EventId;
    }
}
