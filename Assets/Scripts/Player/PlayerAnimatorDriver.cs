using UnityEngine;

namespace StarEco.Player
{
    /// <summary>Animator 参数名常量：代码与动画资源之间唯一的事实来源。</summary>
    public static class PlayerAnimParams
    {
        public static readonly int Speed = Animator.StringToHash("Speed");
        public static readonly int IsGrounded = Animator.StringToHash("IsGrounded");
        public static readonly int VelocityY = Animator.StringToHash("VelocityY");
        public static readonly int Hurt = Animator.StringToHash("Hurt");
        public static readonly int Death = Animator.StringToHash("Death");
        public static readonly int Attack = Animator.StringToHash("Attack");       // M3 使用
        public static readonly int Interact = Animator.StringToHash("Interact");   // M5 使用
        // —— 收尾补齐的 22 条动作参数（与排期表/装配工具一一对应）——
        public static readonly int Dash = Animator.StringToHash("Dash");
        public static readonly int Roll = Animator.StringToHash("Roll");
        public static readonly int Attack2 = Animator.StringToHash("Attack2");
        public static readonly int Attack3 = Animator.StringToHash("Attack3");
        public static readonly int AttackAir = Animator.StringToHash("AttackAir");
        public static readonly int AttackCancel = Animator.StringToHash("AttackCancel");   // 攻击被移动取消
        public static readonly int HeavyAttack = Animator.StringToHash("HeavyAttack");
        public static readonly int Victory = Animator.StringToHash("Victory");
        public static readonly int Turn = Animator.StringToHash("Turn");
        public static readonly int WallJump = Animator.StringToHash("WallJump");
        // 持续型状态用 bool（进入/退出即时响应），一次性动作用 trigger
        public static readonly int IsWallSliding = Animator.StringToHash("IsWallSliding");
        public static readonly int IsLedgeGrab = Animator.StringToHash("IsLedgeGrab");
        public static readonly int IsCrouching = Animator.StringToHash("IsCrouching");
        public static readonly int IsLongIdle = Animator.StringToHash("IsLongIdle");
    }

    /// <summary>
    /// 动画驱动：每帧将控制器事实写入 Animator 参数。
    /// 状态机逻辑与动画资源完全解耦（仅通过参数通信）。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private PlayerController _player;

        [Header("长待机（Idle02）")]
        [Tooltip("站定多久后切换到长待机动画。0 表示禁用。")]
        [SerializeField] private float _longIdleDelay = 10f;
        [Tooltip("判定为「站定」的速度阈值（世界单位/秒）")]
        [SerializeField] private float _idleSpeedEpsilon = 0.1f;

        private Animator _animator;
        private float _idleTimer;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_player == null)
            {
                _player = GetComponentInParent<PlayerController>();
            }
        }

        private void OnDisable()
        {
            // 切场景/重生时清零，避免下一条命一进场就顶着长待机
            _idleTimer = 0f;
        }

        private void Update()
        {
            if (_player == null || _animator == null)
            {
                return;
            }

            _animator.SetFloat(PlayerAnimParams.Speed, Mathf.Abs(_player.Body.velocity.x));
            _animator.SetBool(PlayerAnimParams.IsGrounded, _player.IsGrounded);
            _animator.SetFloat(PlayerAnimParams.VelocityY, _player.VerticalVelocity);

            // 持续型状态参数：之前这三个从未被任何代码写入，对应动画状态是死状态。
            // 现在由状态机在 Enter/Exit 里维护标志，这里每帧同步给 Animator。
            _animator.SetBool(PlayerAnimParams.IsCrouching, _player.IsCrouching);
            _animator.SetBool(PlayerAnimParams.IsWallSliding, _player.IsWallSliding);
            _animator.SetBool(PlayerAnimParams.IsLedgeGrab, _player.IsLedgeGrabbing);

            UpdateLongIdle();

            // 受击/死亡触发器：由状态机在 Enter 时调用 FireOneShot 触发
        }

        /// <summary>
        /// 长待机计时：站在地上、没有水平输入、身体几乎不动，累计够时长就切 Idle02。
        /// 之前这个参数只在状态机里接了线，没有任何代码写它，Idle02 实际上是死状态。
        /// </summary>
        private void UpdateLongIdle()
        {
            if (_longIdleDelay <= 0f)
            {
                return;
            }

            bool standingStill = _player.IsGrounded
                                 && Mathf.Abs(_player.HorizontalInput) < 0.01f
                                 && Mathf.Abs(_player.Body.velocity.x) < _idleSpeedEpsilon;

            _idleTimer = standingStill ? _idleTimer + Time.deltaTime : 0f;
            _animator.SetBool(PlayerAnimParams.IsLongIdle, _idleTimer >= _longIdleDelay);
        }

        /// <summary>触发一次性动画（受击/死亡/攻击/互动）。供状态 Enter 调用。</summary>
        public void FireOneShot(int triggerHash)
        {
            _animator?.SetTrigger(triggerHash);
        }

        /// <summary>清除未消费的一次性触发器（避免上次中断残留影响下一次动作）。</summary>
        public void ResetOneShot(int triggerHash)
        {
            _animator?.ResetTrigger(triggerHash);
        }

        /// <summary>
        /// 直接播放一次性动画状态（绕过 AnyState trigger 转移）。
        /// trigger + AnyState 在 InterruptionSource=0 时有已知评估问题，Play() 是社区标准做法。
        /// </summary>
        public void PlayOneShot(string stateName)
        {
            if (_animator != null)
            {
                _animator.Play(stateName, 0, 0f);
            }
        }
    }
}
