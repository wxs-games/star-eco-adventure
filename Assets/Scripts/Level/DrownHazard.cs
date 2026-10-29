using UnityEngine;
using StarEco.Combat;
using StarEco.Framework;

namespace StarEco.Level
{
    /// <summary>
    /// 溺水伤害区（M9 R5）：玩家掉入河道深水区即重伤（走既有 IHittable 伤害流水线，
    /// HP 归零 → IsDead → LevelManager 判定失败并弹出结算面板，与 M6 死亡处理完全一致）。
    /// 用 Stay 持续结算：受伤无敌期（1s）内的掉落也能在无敌结束后立即致死，不会卡在河床。
    /// TakeDamage 自带 死亡/无敌 守卫，重复调用安全。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class DrownHazard : MonoBehaviour
    {
        [Tooltip("每次结算伤害（远大于玩家最大 HP，保证一击致命）")]
        [SerializeField] private int _damage = 999;

        private void OnTriggerEnter2D(Collider2D other)
        {
            // M10：入水瞬间播放 Splash（Stay 持续伤害不重复播）
            if (other.GetComponentInParent<IHittable>() != null)
            {
                AudioManager.Instance?.PlaySfx(SfxId.Splash);
            }
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
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.35f);
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                Gizmos.DrawCube(transform.position, col.bounds.size);
            }
        }
    }
}
