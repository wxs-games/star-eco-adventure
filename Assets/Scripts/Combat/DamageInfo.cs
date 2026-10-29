using UnityEngine;

namespace StarEco.Combat
{
    /// <summary>
    /// 统一伤害数据：任何攻击（玩家夹钳、M5 净化喷雾、M4 敌人反击）命中时携带的信息。
    /// </summary>
    public struct DamageInfo
    {
        /// <summary>伤害值（整数定点，单位「半心点」：2 点 = 1 颗心；Enemy02 远程毒弹 1 点 = 半颗心）。</summary>
        public int Amount;

        /// <summary>指向伤害来源的方向（受击者 → 来源），击退按其反方向施加。</summary>
        public Vector2 SourceDirection;

        /// <summary>伤害来源对象（用于计算击退方向与归属，可为 null）。</summary>
        public GameObject Source;

        /// <summary>附带击退速度（0 = 不附带，受击方自行决定击退）。</summary>
        public float KnockbackPower;

        /// <summary>
        /// 是否为净化伤害（M5 喷雾/净化波置 true）。
        /// M8 Boss 据此套用净化倍率；普通敌人忽略该字段，行为不变。
        /// </summary>
        public bool IsPurify;

        public DamageInfo(int amount, Vector2 sourceDirection, GameObject source)
        {
            Amount = amount;
            SourceDirection = sourceDirection;
            Source = source;
            KnockbackPower = 0f;
            IsPurify = false;
        }
    }
}
