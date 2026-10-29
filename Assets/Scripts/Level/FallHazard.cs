using UnityEngine;
using StarEco.Combat;

namespace StarEco.Level
{
    /// <summary>
    /// 坠落死亡区：玩家掉出关卡地图（落到地面以外的虚空）即判定死亡。
    /// 与溺水区同构，走既有 IHittable 伤害流水线：HP 归零 → IsDead →
    /// LevelManager 判定失败并弹出结算面板，不另开一套死亡逻辑。
    /// 与溺水区差异：不播放入水音效，仅作为「掉出地图」的兜底判定。
    /// 用 Stay 持续结算：受击无敌期（1s）内的穿透也能在无敌结束后立即致死。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class FallHazard : MonoBehaviour
    {
        [Tooltip("每次结算伤害（远大于玩家最大 HP，保证一击致命）")]
        [SerializeField] private int _damage = 999;

        private void OnTriggerEnter2D(Collider2D other)
        {
            Apply(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            Apply(other);
        }

        private void Apply(Collider2D other)
        {
            var hittable = other.GetComponentInParent<IHittable>();
            if (hittable == null)
            {
                return;
            }

            hittable.TakeDamage(new DamageInfo(_damage, Vector2.up, gameObject));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.35f);
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                Gizmos.DrawCube(transform.position, col.bounds.size);
            }
        }
    }
}
