using System.Collections.Generic;
using UnityEngine;
using StarEco.Player;

namespace StarEco.Combat
{
    /// <summary>
    /// 攻击命中盒（T6）：挂在 Player 根（Animation Event 直接调用本组件方法）。
    /// 开窗期启用 trigger 碰撞体，OnTriggerEnter2D 对 IHittable 去重命中；
    /// 同一目标在单次开窗内只结算一次（R3）。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class AttackHitbox : MonoBehaviour
    {
        [Tooltip("攻击 trigger 碰撞体（允许挂在 Player 子物体上）")]
        [SerializeField] private Collider2D _hitbox;

        private readonly HashSet<IHittable> _hitThisWindow = new HashSet<IHittable>();
        private PlayerController _player;
        private PlayerCombat _combat;
        private bool _windowOpen;
        private Vector3 _hitboxLocalPosition;

        /// <summary>当前攻击段号：由 PlayerAttackState 在 Enter/衔接/退出时写入（第三段命中附击退）。</summary>
        public int CurrentSegment { get; set; }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _combat = GetComponent<PlayerCombat>();
            if (_hitbox == null)
            {
                foreach (var col in GetComponentsInChildren<Collider2D>(true))
                {
                    if (col != null && col.isTrigger)
                    {
                        _hitbox = col;
                        break;
                    }
                }
            }

            if (_hitbox != null)
            {
                _hitboxLocalPosition = _hitbox.transform.localPosition;
                _hitboxLocalPosition.x = Mathf.Abs(_hitboxLocalPosition.x);
                _hitbox.enabled = false;
                MirrorHitbox();
            }
        }

        private void LateUpdate()
        {
            MirrorHitbox();
        }

        // ===== Animation Event 入口（帧号见排期表「动画事件帧」列）=====

        /// <summary>开窗：HitOn 事件帧调用。</summary>
        public void OnAttackWindowOpen()
        {
            // 第三段是投掷：近身判定整段不开窗，伤害全部由飞出去的钳子（ToolProjectile）结算。
            // 动画事件在进入第三段时仍会照常触发，这里按段号拦掉。
            if (CurrentSegment == 3)
            {
                _windowOpen = false;
                _hitThisWindow.Clear();
                if (_hitbox != null)
                {
                    _hitbox.enabled = false;
                }
                return;
            }

            _hitThisWindow.Clear();
            _windowOpen = true;
            if (_hitbox != null)
            {
                _hitbox.enabled = true;
            }
        }

        /// <summary>关窗：HitOff 事件帧调用。</summary>
        public void OnAttackWindowClose()
        {
            _windowOpen = false;
            _hitThisWindow.Clear();
            if (_hitbox != null)
            {
                _hitbox.enabled = false;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_windowOpen)
            {
                return;
            }

            // 同一目标本窗只命中一次
            IHittable target = other.GetComponentInParent<IHittable>();
            if (target == null || _hitThisWindow.Contains(target))
            {
                return;
            }

            _hitThisWindow.Add(target);

            int dmg = _combat != null && _combat.Config != null ? _combat.Config.AttackDamage : 1;
            float thirdKnock = _combat != null && _combat.Config != null ? _combat.Config.ThirdHitKnockback : 0f;

            var info = new DamageInfo(dmg, FacingDirection(), gameObject)
            {
                KnockbackPower = CurrentSegment == 3 ? thirdKnock : 0f
            };
            target.TakeDamage(info);

            // 命中手感：短顿帧 + 轻震屏（第三段收尾更重一点）
            bool heavy = CurrentSegment == 3;
            HitFeel.Play(heavy ? 0.075f : 0.05f, heavy ? 0.10f : 0.055f);
        }

        private void MirrorHitbox()
        {
            if (_hitbox == null || _player == null || _player.Sprite == null)
            {
                return;
            }

            float facing = _player.Sprite.flipX ? -1f : 1f;
            var pos = _hitbox.transform.localPosition;
            pos.x = facing * _hitboxLocalPosition.x;
            pos.y = _hitboxLocalPosition.y;
            _hitbox.transform.localPosition = pos;
        }

        /// <summary>伤害来源方向（受击者 → 来源）：与攻击者面朝方向相反，击退按其反方向施加即远离攻击者。</summary>
        private Vector2 FacingDirection()
        {
            return _player != null && _player.Sprite.flipX ? Vector2.right : Vector2.left;
        }
    }
}
