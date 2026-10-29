using UnityEngine;

namespace StarEco.Level.Events
{
    /// <summary>
    /// 被困动物已解救事件：RescueTarget 广播，LevelObjectiveTracker 订阅累加（M9 R2）。
    /// struct 零 GC（M1 约定）。
    /// </summary>
    public struct AnimalRescuedEvent
    {
        /// <summary>目标标识（如 Animal01 / Animal02）。</summary>
        public string TargetId;

        /// <summary>解救位置（FX/音效观察者用）。</summary>
        public Vector2 Position;
    }
}
