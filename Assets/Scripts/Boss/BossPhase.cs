namespace StarEco.Boss
{
    /// <summary>
    /// Boss 阶段（M8 R2）：三阶段，只升不降。
    /// 阈值：HP% ≤ 60 → Phase2；≤ 30 → Phase3。
    /// </summary>
    public enum BossPhase
    {
        Phase1 = 1,
        Phase2 = 2,
        Phase3 = 3
    }

    /// <summary>阶段辅助。</summary>
    public static class BossPhaseExtensions
    {
        /// <summary>是否最终阶段。</summary>
        public static bool IsFinal(this BossPhase phase) => phase == BossPhase.Phase3;

        /// <summary>阶段显示名（血条提示用）。</summary>
        public static string DisplayName(this BossPhase phase)
        {
            switch (phase)
            {
                case BossPhase.Phase2: return "狂暴阶段";
                case BossPhase.Phase3: return "最终阶段";
                default: return "第一阶段";
            }
        }
    }
}
