using UnityEngine;

namespace StarEco.Boss
{
    /// <summary>
    /// Boss 动画参数（M8 R3/R8）：Boss01 专属 clip 对应的 Trigger。
    /// 与 M4 `EnemyAnimParams` 同表不同类，避免污染小怪参数集合。
    /// </summary>
    public static class BossAnimParams
    {
        /// <summary>技能 A · 近战横扫（clip Boss01_AttackA）。</summary>
        public static readonly int AttackA = Animator.StringToHash("Attack");

        /// <summary>发射前蓄力（clip Charge：核心能量／绿雾反复鼓胀，技能 B 与特殊招共用的长前摇）。</summary>
        public static readonly int Charge = Animator.StringToHash("Charge");

        /// <summary>技能 B · 远程弹幕（clip Boss01_AttackB）。</summary>
        public static readonly int AttackB = Animator.StringToHash("AttackB");

        /// <summary>特殊招 · 环绕弹幕（clip Boss01_Special，仅 P3）。</summary>
        public static readonly int Special = Animator.StringToHash("Special");

        /// <summary>阶段狂暴过场（clip Boss01_Enrage）。</summary>
        public static readonly int Enrage = Animator.StringToHash("Enrage");

        /// <summary>移动布尔（复用 M4 同名参数）。</summary>
        public static readonly int IsMove = Animator.StringToHash("IsMove");
    }
}
