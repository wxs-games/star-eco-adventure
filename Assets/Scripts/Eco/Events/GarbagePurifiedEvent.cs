using UnityEngine;

namespace StarEco.Eco.Events
{
    /// <summary>
    /// 垃圾/污染源已净化事件：GarbagePile / TowerPurifiable 广播，PuritySystem 订阅累加。
    /// struct 零 GC。
    /// </summary>
    public struct GarbagePurifiedEvent
    {
        /// <summary>该目标贡献的净化值。</summary>
        public int PurityValue;

        /// <summary>目标位置。</summary>
        public Vector2 Position;

        /// <summary>目标标识（如 GarbagePile / GarbageWall / Tower01）。</summary>
        public string TargetId;
    }
}
