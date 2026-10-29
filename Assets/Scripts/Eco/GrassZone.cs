using System.Collections.Generic;
using UnityEngine;
using StarEco.Player;

namespace StarEco.Eco
{
    /// <summary>
    /// 草丛感应区：挂在可破坏水草上，复用其 Trigger 碰撞体。
    /// 玩家进入时给 <see cref="PlayerController"/> 记一次草丛接触，离开时撤销；
    /// 由玩家侧在迈步时改播草丛沙沙声，而不是普通脚步。
    /// 水草被击碎时本组件随对象销毁，OnDisable 会补一次撤销，避免计数器残留。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class GrassZone : MonoBehaviour
    {
        private readonly HashSet<PlayerController> _inside = new HashSet<PlayerController>();

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 只认玩家本体（非 Trigger）；攻击判定盒等子触发器不计入，避免计数重复
            if (other.isTrigger)
            {
                return;
            }

            var player = other.GetComponentInParent<PlayerController>();
            if (player == null || !_inside.Add(player))
            {
                return;
            }

            player.AddGrassContact();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.isTrigger)
            {
                return;
            }

            var player = other.GetComponentInParent<PlayerController>();
            if (player == null || !_inside.Remove(player))
            {
                return;
            }

            player.RemoveGrassContact();
        }

        private void OnDisable()
        {
            foreach (var player in _inside)
            {
                if (player != null)
                {
                    player.RemoveGrassContact();
                }
            }

            _inside.Clear();
        }
    }
}
