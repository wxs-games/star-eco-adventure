using UnityEngine;
using StarEco.Eco;

namespace StarEco.Player
{
    /// <summary>
    /// 喷洒状态（M5）：按住 K 期间持续结算判定盒内目标，减速不锁死；
    /// 松开/净化剂耗尽/离地/受击/死亡时退出。动画复用 Player_Interact。
    /// </summary>
    public class PlayerSprayState : PlayerStateBase
    {
        private static readonly int InteractStateHash = Animator.StringToHash("Interact");

        private SprayController _spray;
        private PurifyTank _tank;
        private Animator _animator;

        public PlayerSprayState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            _spray = Player.GetComponent<SprayController>();
            _tank = Player.GetComponent<PurifyTank>();
            _animator = Player.GetComponent<Animator>();
            Player.TryFireAnim(PlayerAnimParams.Interact);
            _spray?.BeginSpray();
        }

        public override void Exit()
        {
            _spray?.EndSpray();
        }

        public override void FixedTick()
        {
            // 减速不锁死（R2）：移速因子由 SprayController 提供
            Player.MoveHorizontally(false, _spray != null ? _spray.SpraySpeedFactor : 1f);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            // 喷洒仅地面：走下平台立即转空中
            if (!Player.IsGrounded)
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            // 长按持续喷雾：若 Interact 片段播完又退回 locomotion，就在仍按住 K 时再次触发，
            // 这样当前占位喷雾动画会持续循环表现，直到松键或净化剂耗尽。
            if (_animator != null && !_animator.IsInTransition(0))
            {
                var state = _animator.GetCurrentAnimatorStateInfo(0);
                if (state.shortNameHash != InteractStateHash)
                {
                    Player.TryFireAnim(PlayerAnimParams.Interact);
                }
            }

            // 松开 / 净化剂耗尽 → 自动退出（R2，不报错不产生负值）
            bool held = _spray != null && _spray.SprayHeld;
            bool hasTank = _tank == null || !_tank.IsEmpty;
            if (!held || !hasTank)
            {
                SwitchGroundLocomotion();
            }
        }
    }
}
