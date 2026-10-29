// ============================================================================
// 【模板】数值安全：货币溢出、float 精度、玩家可读的紧凑数字。
// 复制到 <工程根>/Assets/Scripts/Core/ 后由 Unity 编译。
// 纯 System 依赖，不引用 UnityEngine，可脱离 Unity 做单元测试。
//
// 为什么不用 int 存货币：
//   int 上限 2,147,483,647（约 21 亿）。一件装备卖 500 万、玩家有 500 件，
//   再乘上活动倍率就会溢出成负数，玩家瞬间破产。货币一律 long。
// ============================================================================
using System;

namespace GameFramework
{
    public static class NumericSafety
    {
        /// <summary>单货币上限：1 万亿。超过这个量级，UI 已经无法展示，应该换货币单位。</summary>
        public const long DefaultCurrencyCap = 999999999999L;

        /// <summary>float 能精确表示的最大整数（2^24）。超过它，+1 可能不再变化。</summary>
        public const long FloatExactIntMax = 16777216L;

        /// <summary>安全加法：溢出时返回 false，并把 result 钳到边界。</summary>
        public static bool TryAdd(long left, long right, out long result)
        {
            if (right > 0L && left > long.MaxValue - right)
            {
                result = long.MaxValue;
                return false;
            }
            if (right < 0L && left < long.MinValue - right)
            {
                result = long.MinValue;
                return false;
            }
            result = left + right;
            return true;
        }

        /// <summary>加法 + 上下限钳制：货币增减都走这里，外部永远不会拿到负数或超上限。</summary>
        public static long AddClamped(long current, long delta, long cap)
        {
            if (cap < 0L) cap = 0L;

            long sum;
            if (!TryAdd(current, delta, out sum))
            {
                return delta > 0L ? cap : 0L;
            }
            if (sum > cap) return cap;
            if (sum < 0L) return 0L;
            return sum;
        }

        /// <summary>这个整数用 float 存会不会丢精度。</summary>
        public static bool IsFloatExact(long value)
        {
            return value >= -FloatExactIntMax && value <= FloatExactIntMax;
        }

        /// <summary>1.2K / 3.4M / 5.6B / 7.8T。仅用于显示，不参与结算。</summary>
        public static string FormatCompact(long value)
        {
            if (value == long.MinValue) return "-9.2E";
            if (value < 0L) return "-" + FormatCompact(-value);

            if (value < 1000L) return value.ToString();
            if (value < 1000000L) return Trim((double)value / 1000.0) + "K";
            if (value < 1000000000L) return Trim((double)value / 1000000.0) + "M";
            if (value < 1000000000000L) return Trim((double)value / 1000000000.0) + "B";
            return Trim((double)value / 1000000000000.0) + "T";
        }

        private static string Trim(double value)
        {
            // 一位小数；整数值不显示 .0，避免 "1.0K" 这种噪音
            double rounded = Math.Round(value, 1);
            if (Math.Abs(rounded - Math.Round(rounded)) < 0.0001)
            {
                return ((long)Math.Round(rounded)).ToString();
            }
            return rounded.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>百分比：part / total。total 为 0 时返回 0，不抛异常、不出 NaN。</summary>
        public static float SafePercent(long part, long total)
        {
            if (total == 0L) return 0f;
            return (float)part / (float)total;
        }
    }
}
