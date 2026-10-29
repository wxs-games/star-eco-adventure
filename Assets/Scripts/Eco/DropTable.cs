using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarEco.Eco
{
    /// <summary>拾取物类型（初期仅净化瓶，M6+ 可扩展）。</summary>
    public enum PickupKind
    {
        PurifyBottle
    }

    /// <summary>
    /// 掉落表（ScriptableObject）：敌人死亡后按条目概率生成拾取物。
    /// 允许空表 = 不掉落；继承 ScriptableObject，与 M4 EnemyConfig.DropTable 占位字段直接兼容。
    /// 创建：Assets → Create → StarEco → Drop Table
    /// </summary>
    [CreateAssetMenu(fileName = "DropTable", menuName = "StarEco/Drop Table", order = 4)]
    public class DropTable : ScriptableObject
    {
        /// <summary>单条掉落条目。</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("拾取物类型")]
            public PickupKind Kind;

            [Tooltip("掉落数量（同条目独立判定）")]
            public int Count;

            [Tooltip("掉落概率 [0,1]，1 = 必掉")]
            [Range(0f, 1f)]
            public float Probability;
        }

        [Tooltip("掉落条目列表（空表 = 不掉落）")]
        public List<Entry> Entries = new List<Entry>();
    }
}
