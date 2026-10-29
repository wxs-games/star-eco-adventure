// ============================================================================
// 【模板】伤害公式三族：减法 / 乘法 / 混合。
// 复制到 <工程根>/Assets/Scripts/Core/ 后由 Unity 编译。
// 纯 System 依赖，不引用 UnityEngine，可脱离 Unity 做单元测试。
//
// 关键约定：
//   1. 乘法式的减伤率 = 防御 / (防御 + K)，永远 < 1，天然抗数值膨胀。
//   2. 任何公式都要有 minDamage，否则「防御 >= 攻击」会导致 0 伤害死锁。
//   3. 至少保留 MaxMitigation 的伤害穿透，避免后期无敌坦克。
// ============================================================================
using System;

namespace GameFramework
{
    public enum DamageFormula
    {
        /// <summary>减法：伤害 = 攻击 - 防御。前期直观，后期需要靠数值堆叠，否则一刀 0 血。</summary>
        Subtractive = 0,

        /// <summary>乘法：伤害 = 攻击 × K / (K + 防御)。曲线平滑，长线项目首选。</summary>
        Multiplicative = 1,

        /// <summary>混合：先扣一半防御，再吃乘法减伤。兼顾「破防感」与曲线稳定性。</summary>
        Hybrid = 2
    }

    public static class DamageMath
    {
        /// <summary>减伤常数：K=100 时，防御 100 = 减伤 50%。通常取「1 级角色攻击力」量级。</summary>
        public const float DefaultArmorK = 100f;

        /// <summary>最低伤害。设为 0 会让高防目标变成无敌。</summary>
        public const float DefaultMinDamage = 1f;

        /// <summary>减伤上限：任何情况下至少吃 10% 伤害。</summary>
        public const float MaxMitigation = 0.9f;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>减伤率，0~0.9。</summary>
        public static float Mitigation(float armor, float armorK)
        {
            if (armor <= 0f || armorK <= 0f) return 0f;
            return Clamp(armor / (armor + armorK), 0f, MaxMitigation);
        }

        /// <summary>单次伤害结算。</summary>
        public static float Mitigate(float damage, float armor, DamageFormula formula,
                                     float armorK, float minDamage)
        {
            if (damage <= 0f) return 0f;

            float result;
            switch (formula)
            {
                case DamageFormula.Subtractive:
                    result = damage - armor;
                    break;
                case DamageFormula.Hybrid:
                    result = (damage - armor * 0.5f) * (1f - Mitigation(armor, armorK));
                    break;
                default:
                    result = damage * (1f - Mitigation(armor, armorK));
                    break;
            }
            return Math.Max(minDamage, result);
        }

        /// <summary>暴击的数学期望：不改变单次伤害数字的观感，只用于 DPS 核算。</summary>
        public static float CritExpected(float damage, float critRate, float critMultiplier)
        {
            float rate = Clamp(critRate, 0f, 1f);
            float multiplier = Math.Max(1f, critMultiplier);
            return damage * (1f + rate * (multiplier - 1f));
        }

        public static float Dps(float damagePerHit, float attacksPerSecond,
                                float critRate, float critMultiplier)
        {
            if (attacksPerSecond <= 0f) return 0f;
            return CritExpected(damagePerHit, critRate, critMultiplier) * attacksPerSecond;
        }

        /// <summary>等效血量：EHP = HP / (1 - 减伤率)。用于横向比较不同防御单位的硬度。</summary>
        public static float EffectiveHp(float hp, float armor, float armorK)
        {
            float mitigation = Mitigation(armor, armorK);
            if (mitigation >= 1f) return float.MaxValue;
            return hp / (1f - mitigation);
        }

        /// <summary>按「原始 DPS」估算 TTK（乘法式口径）。</summary>
        public static float TimeToKillByDps(float targetHp, float targetArmor, float rawDps, float armorK)
        {
            float effectiveDps = rawDps * (1f - Mitigation(targetArmor, armorK));
            if (effectiveDps <= 0f) return float.PositiveInfinity;
            return targetHp / effectiveDps;
        }

        /// <summary>按「每击伤害 × 攻速」估算 TTK：先取整到命中次数，更贴近逐击结算。</summary>
        public static float TimeToKillByHits(float targetHp, float damagePerHit, float attacksPerSecond,
                                             float armor, DamageFormula formula,
                                             float armorK, float minDamage)
        {
            if (targetHp <= 0f) return 0f;
            if (attacksPerSecond <= 0f) return float.PositiveInfinity;

            float perHit = Mitigate(damagePerHit, armor, formula, armorK, minDamage);
            if (perHit <= 0f) return float.PositiveInfinity;

            float hits = (float)Math.Ceiling(targetHp / perHit);
            return hits / attacksPerSecond;
        }
    }
}
