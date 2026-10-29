namespace StarEco.Boss.Events
{
    /// <summary>
    /// Boss 被击败事件（M8 R4）：Boss 死亡时随 EnemyDeathEvent 之后广播。
    /// 关卡完成判定走 M6 既有的 DefeatEnemies 目标链（订阅 EnemyDeathEvent），
    /// 本事件供血条隐藏、Boss 战结算与将来的成就系统使用。struct 零 GC。
    /// </summary>
    public struct BossDefeatedEvent
    {
        /// <summary>Boss 配置里的单位 Id（如 Boss01）。</summary>
        public string BossId;

        /// <summary>所在关卡序号。</summary>
        public int LevelIndex;

        /// <summary>战斗耗时（秒，由 LevelStats 提供；无统计时为 0）。</summary>
        public float ElapsedSeconds;
    }
}
