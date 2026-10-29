using System.Collections.Generic;
using UnityEngine;
using StarEco.Combat;
using StarEco.UI;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// 敌人攻击命中盒：Animation Event 驱动开窗，OnTriggerEnter2D 对 IHittable 去重结算。
    /// 与玩家 AttackHitbox 同构同契约，互不干扰（碰撞矩阵隔离）。
    /// 挂在敌人根的 AttackHitbox 子物体上，控制器经 GetComponentInParent 获取。
    /// </summary>
    public class EnemyAttackHitbox : MonoBehaviour
    {
        [SerializeField] private BoxCollider2D _hitbox;
        [SerializeField] private Vector2 _localOffset = new Vector2(0.5f, 0f);

        private EnemyControllerBase _controller;
        private bool _windowOpen;
        private readonly HashSet<IHittable> _hitThisWindow = new HashSet<IHittable>();

        private void Awake()
        {
            _controller = GetComponentInParent<EnemyControllerBase>();
            if (_hitbox == null)
            {
                _hitbox = GetComponent<BoxCollider2D>();
            }
            if (_hitbox != null)
            {
                _hitbox.isTrigger = true;
                _hitbox.enabled = false;
            }
        }

        /// <summary>Animation Event: HitOn — 开命中窗。</summary>
        public void OnAttackWindowOpen()
        {
            _windowOpen = true;
            _hitThisWindow.Clear();
            if (_hitbox != null)
            {
                _hitbox.enabled = true;
                UpdateHitboxPosition();
            }
        }

        /// <summary>Animation Event: HitOff — 关命中窗。</summary>
        public void OnAttackWindowClose()
        {
            _windowOpen = false;
            _hitThisWindow.Clear();
            if (_hitbox != null)
            {
                _hitbox.enabled = false;
            }
        }

        private void UpdateHitboxPosition()
        {
            if (_hitbox == null || _controller == null)
            {
                return;
            }
            float sign = _controller.IsFacingLeft ? -1f : 1f;
            _hitbox.transform.localPosition = new Vector3(sign * _localOffset.x, _localOffset.y, 0f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_windowOpen)
            {
                return;
            }

            // 对话期间敌人暂停攻击：判定盒不结算伤害（也不登记，避免对话结束后补刀）
            if (DialoguePanel.IsDialogueActive)
            {
                return;
            }

            int layerBit = 1 << other.gameObject.layer;
            if ((_controller.Config.PlayerLayer.value & layerBit) == 0)
            {
                return;
            }

            var hittable = other.GetComponentInParent<IHittable>();
            if (hittable == null || _hitThisWindow.Contains(hittable))
            {
                return;
            }

            _hitThisWindow.Add(hittable);

            var dmg = new DamageInfo(
                _controller.Config.AttackDamage,
                -_controller.FacingDirection,
                gameObject)
            {
                KnockbackPower = _controller.Config.AttackKnockback
            };
            hittable.TakeDamage(dmg);
        }
    }
}
