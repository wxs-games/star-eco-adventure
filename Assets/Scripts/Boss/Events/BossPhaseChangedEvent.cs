using UnityEngine;

namespace StarEco.Boss.Events
{
    /// <summary>
    /// Boss 阶段变化事件（M8 R2）：进入新阶段时广播，供血条 UI 与将来的音频/成就订阅。
    /// struct 零 GC。
    /// </summary>
    public struct BossPhaseChangedEvent
    {
        /// <summary>新阶段。</summary>
        public int Phase;

        /// <summary>阶段显示名。</summary>
        public string PhaseName;

        /// <summary>触发时的 HP 百分比 [0,100]。</summary>
        public float HpPercent;
    }
}
