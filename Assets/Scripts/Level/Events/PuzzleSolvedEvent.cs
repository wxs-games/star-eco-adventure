using UnityEngine;

namespace StarEco.Level.Events
{
    /// <summary>
    /// 水流解谜完成事件：WaterPuzzleController 收齐堵塞点后广播（M9 R3）。
    /// 仅作通知（FX/音效/未来观察者）；目标链计数走 GarbagePurifiedEvent 的
    /// WaterBlock 前缀分支，不订阅本事件，避免双源计数。
    /// struct 零 GC（M1 约定）。
    /// </summary>
    public struct PuzzleSolvedEvent
    {
        /// <summary>解谜完成位置（水域中心，FX 用）。</summary>
        public Vector2 Position;

        /// <summary>从关卡开始到解谜完成的耗时（s，展示用）。</summary>
        public float ElapsedInPuzzle;
    }
}
