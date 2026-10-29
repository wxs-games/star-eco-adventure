namespace StarEco.Level
{
    /// <summary>
    /// 关卡状态（四态，design §2.1）。
    /// NotStarted → Playing → Completed / Failed（终态不回退，仅 RestartLevel 重置）。
    /// </summary>
    public enum LevelState
    {
        NotStarted = 0,
        Playing = 1,
        Completed = 2,
        Failed = 3
    }

    public static class LevelStateUtil
    {
        /// <summary>是否为终态（终态停止计时与刷怪，只等结算/重开）。</summary>
        public static bool IsFinal(this LevelState state)
        {
            return state == LevelState.Completed || state == LevelState.Failed;
        }
    }
}
