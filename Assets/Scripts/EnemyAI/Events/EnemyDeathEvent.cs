using UnityEngine;

namespace StarEco.EnemyAI.Events
{
    /// <summary>
    /// 敌人死亡事件：EventCenter 广播，供 M5 掉落/M6 任务订阅。
    /// struct 零 GC。
    /// </summary>
    public struct EnemyDeathEvent
    {
        /// <summary>单位类型标识（如 Enemy01）。</summary>
        public string EnemyTypeId;

        /// <summary>死亡位置。</summary>
        public Vector2 Position;

        /// <summary>掉落表引用（M5 落地前可为 null）。</summary>
        public ScriptableObject DropTable;
    }
}
