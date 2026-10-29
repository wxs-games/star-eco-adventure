using UnityEngine;

namespace StarEco.Player
{
    /// <summary>
    /// 玩家状态公共基类：持有控制器与状态机引用，提供常用上下文方法。
    /// </summary>
    public abstract class PlayerStateBase : IPlayerState
    {
        protected readonly PlayerController Player;
        protected readonly PlayerStateMachine Machine;

        protected PlayerStateBase(PlayerController player, PlayerStateMachine machine)
        {
            Player = player;
            Machine = machine;
        }

        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick() { }
        public virtual void FixedTick() { }

        // ===== 常用转移快捷方式 =====

        /// <summary>死亡优先级最高。发生转移返回 true（调用方应立即 return）。</summary>
        protected bool TryHandleDieTransition()
        {
            if (Player.IsDead)
            {
                Machine.SwitchState<PlayerDeathState>();
                return true;
            }

            return false;
        }

        /// <summary>受击优先级次高。发生转移返回 true（调用方应立即 return）。</summary>
        protected bool TryHandleHurtTransition()
        {
            if (Player.ConsumePendingHurt())
            {
                Machine.SwitchState<PlayerHurtState>();
                return true;
            }

            return false;
        }

        /// <summary>死亡与受击统一检查：命中任一即转移。</summary>
        protected bool TryHandleCombatTransitions()
        {
            return TryHandleDieTransition() || TryHandleHurtTransition();
        }

        /// <summary>
        /// 闪避（翻滚）触发检查：可闪避且本帧按下左 Shift → 切入 Dodge 状态。
        /// 发生转移返回 true（调用方应立即 return）。
        /// </summary>
        protected bool TryStartDodge()
        {
            if (Player.CanStartDodge && Player.ConsumeDodgeInput())
            {
                Machine.SwitchState<PlayerDodgeState>();
                return true;
            }

            return false;
        }

        /// <summary>地面移动通用转移：Idle ⇄ Run。</summary>
        protected void SwitchGroundLocomotion()
        {
            if (Mathf.Abs(Player.HorizontalInput) > 0.01f)
            {
                Machine.SwitchState<PlayerRunState>();
            }
            else
            {
                Machine.SwitchState<PlayerIdleState>();
            }
        }

        /// <summary>
        /// 原地转身：站定时按反方向 → 播 Turn 帧并立即翻转朝向。
        /// 不锁定移动（转身只有 1 帧，加定身反而拖手感），后续 Run 状态会自然接走。
        /// </summary>
        protected void TryPlayTurn()
        {
            if (!Player.IsGrounded || Mathf.Abs(Player.HorizontalInput) < 0.3f)
            {
                return;
            }

            if (Mathf.Abs(Player.Body.velocity.x) > 0.6f)
            {
                return;
            }

            int want = (int)Mathf.Sign(Player.HorizontalInput);
            if (want == Player.FacingDir)
            {
                return;
            }

            Player.Sprite.flipX = want < 0;
            Player.PlayOneShot("Turn");
        }

        /// <summary>冲刺触发检查：可冲刺且本帧按下左 Ctrl → 切入 Dash 状态。</summary>
        protected bool TryStartDash()
        {
            if (Player.CanStartDash && Player.ConsumeDashInput())
            {
                Machine.SwitchState<PlayerDashState>();
                return true;
            }

            return false;
        }

        /// <summary>蹲伏触发检查：地面按住下方向 → 切入 Crouch 状态。</summary>
        protected bool TryStartCrouch()
        {
            if (Player.IsGrounded && Player.DownHeld)
            {
                Machine.SwitchState<PlayerCrouchState>();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 空中墙面动作检查：贴墙 + 下落 + 朝墙推 → 扒边（优先）或贴墙下滑。
        /// 蹬墙跳后的推离锁定期内不再判定，否则会立刻贴回同一面墙。
        /// </summary>
        protected bool TryStartWallOrLedge()
        {
            if (Player.IsGrounded || Player.IsWallJumpLocked || Player.VerticalVelocity > -0.1f)
            {
                return false;
            }

            if (Mathf.Abs(Player.HorizontalInput) < 0.3f)
            {
                return false;
            }

            int dir = (int)Mathf.Sign(Player.HorizontalInput);
            if (!Player.IsTouchingWall(dir))
            {
                return false;
            }

            if (Player.TryFindLedge(dir, out Vector2 standPos))
            {
                var ledge = Machine.Get<PlayerLedgeState>();
                ledge?.SetStandPos(standPos);
                Machine.SwitchState<PlayerLedgeState>();
                return true;
            }

            Machine.SwitchState<PlayerWallSlideState>();
            return true;
        }
    }
}
