using UnityEngine;

namespace StarEco.EnemyAI.Events
{
    /// <summary>
    /// 防御塔关闭事件：EventCenter 广播，供 M5/M6 订阅。
    /// struct 零 GC。
    /// </summary>
    public struct TowerDisableEvent
    {
        /// <summary>塔类型标识（如 Tower01）。</summary>
        public string TowerTypeId;

        /// <summary>塔位置。</summary>
        public Vector2 Position;
    }
}
