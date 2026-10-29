using UnityEngine;
using StarEco.Framework;

namespace StarEco.Player
{
    /// <summary>待机：地面静止。</summary>
    public class PlayerIdleState : PlayerStateBase
    {
        public PlayerIdleState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void FixedTick()
        {
            Player.StopHorizontal();
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            if (TryStartDodge())
            {
                return;
            }

            if (TryStartDash())
            {
                return;
            }

            if (TryStartCrouch())
            {
                return;
            }

            if (Player.Combat != null && Player.Combat.ConsumeAttackPressed())
            {
                Machine.SwitchState<PlayerAttackState>();
                return;
            }

            // 喷洒/蓄力进入（M5：门条件在 SprayController，K/L 按下帧请求）
            if (Player.Spray != null)
            {
                if (Player.Spray.TryConsumeSprayStart())
                {
                    Machine.SwitchState<PlayerSprayState>();
                    return;
                }

                if (Player.Spray.TryConsumeChargeStart())
                {
                    Machine.SwitchState<PlayerChargeState>();
                    return;
                }
            }

            // 跳跃缓冲（含土狼时间内的起跳）
            if (Player.HasJumpBuffer && Player.TryConsumeJump())
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            TryPlayTurn();
            SwitchGroundLocomotion();

            // 地面静止但角色离地（走下平台）→ 空中
            if (!Player.IsGrounded && Machine.CurrentType == typeof(PlayerIdleState))
            {
                Machine.SwitchState<PlayerAirState>();
            }
        }
    }

    /// <summary>奔跑：地面水平移动。</summary>
    public class PlayerRunState : PlayerStateBase
    {
        public PlayerRunState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void FixedTick()
        {
            Player.MoveHorizontally(false);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            if (TryStartDodge())
            {
                return;
            }

            if (TryStartDash())
            {
                return;
            }

            if (TryStartCrouch())
            {
                return;
            }

            if (Player.Combat != null && Player.Combat.ConsumeAttackPressed())
            {
                Machine.SwitchState<PlayerAttackState>();
                return;
            }

            // 喷洒/蓄力进入（M5：门条件在 SprayController，K/L 按下帧请求）
            if (Player.Spray != null)
            {
                if (Player.Spray.TryConsumeSprayStart())
                {
                    Machine.SwitchState<PlayerSprayState>();
                    return;
                }

                if (Player.Spray.TryConsumeChargeStart())
                {
                    Machine.SwitchState<PlayerChargeState>();
                    return;
                }
            }

            if (Player.HasJumpBuffer && Player.TryConsumeJump())
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            SwitchGroundLocomotion();

            if (!Player.IsGrounded && Machine.CurrentType == typeof(PlayerRunState))
            {
                Machine.SwitchState<PlayerAirState>();
            }
        }
    }

    /// <summary>
    /// 空中：上升(Jump)与下落(Fall)合并为一个状态，
    /// 动画由 Animator 依据 VelocityY 参数分支（设计说明见 spec design.md）。
    /// </summary>
    public class PlayerAirState : PlayerStateBase
    {
        public PlayerAirState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void FixedTick()
        {
            Player.MoveHorizontally(true);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            if (TryStartDodge())
            {
                return;
            }

            if (TryStartDash())
            {
                return;
            }

            if (TryStartCrouch())
            {
                return;
            }

            if (Player.Combat != null && Player.Combat.ConsumeAttackPressed())
            {
                Machine.SwitchState<PlayerAttackState>();
                return;
            }

            // 空中不再有二段跳：TryConsumeJump 在离地后恒为 false，
            // 保留这次调用只是让“落地前预输入”继续由 Land 状态接力缓冲。
            if (Player.HasJumpBuffer && Player.TryConsumeJump())
            {
                return;
            }

            // 贴墙下滑 / 扒边（自动触发，放在攻击/冲刺之后：主动输入优先）
            if (TryStartWallOrLedge())
            {
                return;
            }

            // 触地 → Land
            if (Player.IsGrounded && Player.VerticalVelocity <= 0.01f)
            {
                Machine.SwitchState<PlayerLandState>();
            }
        }
    }

    /// <summary>落地：短暂锁定移动并播放 Land 动画，随后回到地面移动状态。</summary>
    public class PlayerLandState : PlayerStateBase
    {
        private float _lockTimer;

        public PlayerLandState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            _lockTimer = Player.Config.LandLockTime;
            AudioManager.Instance?.PlaySfx(SfxId.Land);   // M10
        }

        public override void FixedTick()
        {
            if (_lockTimer > 0f)
            {
                Player.StopHorizontal();
            }
            else
            {
                Player.MoveHorizontally(false);
            }
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            if (TryStartDodge())
            {
                return;
            }

            if (TryStartDash())
            {
                return;
            }

            _lockTimer -= Time.deltaTime;

            // 落地瞬间恰好有跳跃缓冲 → 立即再跳（连贯跳跃手感）
            if (_lockTimer <= 0f && Player.HasJumpBuffer && Player.TryConsumeJump())
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            if (_lockTimer <= 0f)
            {
                SwitchGroundLocomotion();

                if (!Player.IsGrounded && Machine.CurrentType == typeof(PlayerLandState))
                {
                    Machine.SwitchState<PlayerAirState>();
                }
            }
        }
    }

    /// <summary>受击：击退 + 无敌闪烁，结束后回到地面/空中状态。</summary>
    public class PlayerHurtState : PlayerStateBase
    {
        private float _recoverTimer;

        public PlayerHurtState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            // 击退：水平反向 + 小幅上抛
            var knockback = Player.TakeKnockback();
            Player.SetKnockback(knockback);
            Player.PlayOneShot("Hurt");
            AudioManager.Instance?.PlaySfx(SfxId.Hurt);   // M10
            // 硬直时长来自 CombatConfig（T5：GDD 初值 0.6s）
            _recoverTimer = Player.Combat != null && Player.Combat.Config != null
                ? Player.Combat.Config.StunDuration
                : 0.6f;
            Player.InputLocked = true;
        }

        public override void Exit()
        {
            Player.Sprite.color = Color.white;
            Player.InputLocked = false;
        }

        public override void FixedTick()
        {
            // 击退期间不接管水平速度（保留 SetKnockback 的冲量）
        }

        public override void Tick()
        {
            if (TryHandleDieTransition())
            {
                return;
            }

            _recoverTimer -= Time.deltaTime;

            // 无敌闪烁
            float alpha = Player.InvincibleTimer > 0f
                ? (Mathf.FloorToInt(Player.InvincibleTimer * 12f) % 2 == 0 ? 1f : 0.4f)
                : 1f;
            Player.Sprite.color = new Color(1f, 1f, 1f, alpha);

            if (_recoverTimer <= 0f)
            {
                if (Player.IsGrounded)
                {
                    SwitchGroundLocomotion();
                }
                else
                {
                    Machine.SwitchState<PlayerAirState>();
                }
            }
        }
    }

    /// <summary>死亡：停输入停碰撞，播放 Death 动画后由关卡流程接管重生。</summary>
    public class PlayerDeathState : PlayerStateBase
    {
        public PlayerDeathState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            Player.StopHorizontal();
            Player.PlayOneShot("Death");
            AudioManager.Instance?.PlaySfx(SfxId.Death);   // M10

            var collider = Player.GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            Player.Body.isKinematic = true;
            Player.Body.velocity = Vector2.zero;
        }

        public override void Tick()
        {
            // 死亡是终态；重生逻辑由 LevelManager（M6）在失败后重载本关实现
        }
    }

    /// <summary>
    /// 翻滚闪避（R1-R5）：时长内以配置速度位移，全程无敌；方向取当前水平输入，无输入取朝向。
    /// 时长/距离/无敌/冷却均来自 PlayerConfig；翻滚期间锁输入，并穿透怪物（不与怪物层碰撞、不被顶回）。
    /// </summary>
    public class PlayerDodgeState : PlayerStateBase
    {
        private float _timer;
        private float _dir;

        public PlayerDodgeState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            _timer = Player.Config.DodgeDuration;
            _dir = Mathf.Abs(Player.HorizontalInput) > 0.01f
                ? Mathf.Sign(Player.HorizontalInput)
                : (Player.Sprite.flipX ? -1f : 1f);

            Player.BeginDodge(_dir);                        // 起冷却 / 开无敌窗 / 记空中次数
            Player.PlayOneShot("Roll");
            Player.InputLocked = true;                      // 翻滚期间锁输入（R5）
            Player.SetDodgePassThrough(true);               // 翻滚期间穿过怪物（不与怪物层发生碰撞）
        }

        public override void Exit()
        {
            Player.SetDodgePassThrough(false);
            Player.InputLocked = false;
        }

        public override void FixedTick()
        {
            // 保留竖直分量（空中翻滚不被重力打断水平位移），水平按配置速度推进
            Player.Body.velocity = new Vector2(_dir * Player.Config.DodgeSpeed, Player.Body.velocity.y);
        }

        public override void Tick()
        {
            if (TryHandleDieTransition())
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            if (Player.IsGrounded)
            {
                SwitchGroundLocomotion();
            }
            else
            {
                Machine.SwitchState<PlayerAirState>();
            }
        }
    }

    /// <summary>
    /// 冲刺（Dash）：时长内以配置速度直线位移，无无敌帧（无敌是 Roll 的职责）。
    /// 与翻滚的区别：可空中使用（每段一次）、不受重力、方向锁定、无穿透。
    /// </summary>
    public class PlayerDashState : PlayerStateBase
    {
        private float _timer;
        private float _dir;

        public PlayerDashState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            _dir = Mathf.Abs(Player.HorizontalInput) > 0.01f
                ? Mathf.Sign(Player.HorizontalInput)
                : Player.FacingDir;
            _timer = Player.Config.DashDuration;

            Player.BeginDash(_dir);
            Player.PlayOneShot("Dash");
            Player.InputLocked = true;      // 冲刺期间方向锁定，不接受中途反向
            Player.Body.gravityScale = 0f;  // 水平位移不受重力影响
        }

        public override void Exit()
        {
            Player.InputLocked = false;
            Player.Body.gravityScale = Player.Config.GravityScale;
        }

        public override void FixedTick()
        {
            Player.Body.gravityScale = 0f;
            Player.Body.velocity = new Vector2(_dir * Player.Config.DashSpeed, 0f);
        }

        public override void Tick()
        {
            if (TryHandleDieTransition())
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            if (Player.IsGrounded)
            {
                SwitchGroundLocomotion();
            }
            else
            {
                Machine.SwitchState<PlayerAirState>();
            }
        }
    }

    /// <summary>
    /// 蹲伏（Crouch）：碰撞体压缩 + 减速移动。
    /// 起身条件：松开下方向且头顶有空间（低矮通道里保持蹲着，避免碰撞体弹进天花板）。
    /// </summary>
    public class PlayerCrouchState : PlayerStateBase
    {
        public PlayerCrouchState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            Player.SetCrouch(true);
        }

        public override void Exit()
        {
            Player.SetCrouch(false);
        }

        public override void FixedTick()
        {
            if (Mathf.Abs(Player.HorizontalInput) > 0.01f)
            {
                Player.MoveHorizontally(false, Player.Config.CrouchSpeedFactor);
            }
            else
            {
                Player.StopHorizontal();
            }
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            // 走下平台 → 空中（蹲伏状态不复位碰撞体，Exit 会还原，落地后重新判定）
            if (!Player.IsGrounded)
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            // 蹲着按跳：先确认能起身再起跳，否则会把碰撞体弹进天花板
            if (Player.HasJumpBuffer && Player.CanStandUp() && Player.TryConsumeJump())
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            if (TryStartDash())
            {
                return;
            }

            if (Player.Combat != null && Player.Combat.ConsumeAttackPressed())
            {
                Machine.SwitchState<PlayerAttackState>();
                return;
            }

            // 松开下方向且头顶有空间 → 起身回地面移动
            if (!Player.DownHeld && Player.CanStandUp())
            {
                SwitchGroundLocomotion();
            }
        }
    }

    /// <summary>
    /// 贴墙下滑（WallSlide）：下落速度被夹到配置值，贴墙期间可蹬墙跳。
    /// 离开墙面 / 反向输入 / 蹬墙跳锁定期内 → 回空中状态。
    /// </summary>
    public class PlayerWallSlideState : PlayerStateBase
    {
        private int _side;

        public PlayerWallSlideState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            _side = Mathf.Abs(Player.HorizontalInput) > 0.01f
                ? (int)Mathf.Sign(Player.HorizontalInput)
                : Player.FacingDir;

            Player.SetWallSliding(true);
            Player.Sprite.flipX = _side < 0;   // 面向墙
        }

        public override void Exit()
        {
            Player.SetWallSliding(false);
        }

        public override void FixedTick()
        {
            float vy = Player.Body.velocity.y;
            if (vy < -Player.Config.WallSlideSpeed)
            {
                Player.Body.velocity = new Vector2(Player.Body.velocity.x, -Player.Config.WallSlideSpeed);
            }

            // 贴住墙面：保留一点朝墙的推力，避免摩擦抖动把角色挤离墙面
            Player.Body.velocity = new Vector2(_side * 1f, Player.Body.velocity.y);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;
            }

            // 蹬墙跳
            if (Player.TryConsumeWallJump(_side))
            {
                Machine.SwitchState<PlayerAirState>();
                return;
            }

            if (Player.IsGrounded)
            {
                Machine.SwitchState<PlayerLandState>();
                return;
            }

            // 离开墙面 / 松开朝墙输入 → 回空中
            bool pushingIntoWall = Player.HorizontalInput * _side > 0.01f;
            if (Player.IsWallJumpLocked || !pushingIntoWall || !Player.IsTouchingWall(_side))
            {
                Machine.SwitchState<PlayerAirState>();
            }
        }
    }

    /// <summary>
    /// 扒边悬挂（LedgeGrab）：冻结刚体挂在边缘；上/跳 → 爬上去，下 → 松手。
    /// 落点由 PlayerController.TryFindLedge 在进入前算好并注入。
    /// </summary>
    public class PlayerLedgeState : PlayerStateBase
    {
        private Vector2 _standPos;
        private float _hangTimer;

        public PlayerLedgeState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        /// <summary>由空中状态在转移前写入"爬上去之后站哪"。</summary>
        public void SetStandPos(Vector2 pos)
        {
            _standPos = pos;
        }

        public override void Enter()
        {
            _hangTimer = 0f;
            Player.SetLedgeHanging(true);
        }

        public override void Exit()
        {
            Player.SetLedgeHanging(false);
        }

        public override void FixedTick()
        {
            Player.Body.velocity = Vector2.zero;
        }

        public override void Tick()
        {
            if (TryHandleDieTransition())
            {
                return;
            }

            if (Player.ConsumePendingHurt())
            {
                Machine.SwitchState<PlayerHurtState>();
                return;
            }

            _hangTimer += Time.deltaTime;

            // 爬上去：跳跃键或上方向（起手 0.05s 内忽略，避免抓取瞬间误触）
            if (_hangTimer > 0.05f && (Player.HasJumpBuffer || Player.UpHeld))
            {
                Player.ConsumeJumpBuffer();
                Player.ClimbLedge(_standPos);
                Machine.SwitchState<PlayerIdleState>();
                return;
            }

            // 松手：按住下方向
            if (_hangTimer > 0.05f && Player.DownHeld)
            {
                Player.ReleaseLedge();
                Machine.SwitchState<PlayerAirState>();
            }
        }
    }
}
