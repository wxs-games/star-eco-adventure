using UnityEngine;
using StarEco.Combat;
using StarEco.Framework;

namespace StarEco.Player
{
    /// <summary>
    /// 攻击状态（T4）：地面三段连击（Attack01→02→03）与空中单段（AttackAir）。
    /// 连段时序以 CombatConfig.SegmentEndTimes 为兜底；T7 接入 Animation Event 后以事件为准。
    ///
    /// 手感要点（2026-10-28 改）：
    /// 1) 边跑边打：攻击不再钉住水平速度，地面全速、空中沿用 AirControlRatio，
    ///    移动完全交给 MoveHorizontally（空洞骑士式）。也不再"新按方向就取消攻击"，
    ///    否则跑动中起手会被自己立刻打断。
    /// 2) 第三段改为投掷：动画播到挥臂最前处（ThrowReleaseTime）把手里的钳子甩出去，
    ///    命中判定全交给飞出去的钳子（AttackHitbox 在第三段不开窗）。
    /// </summary>
    public class PlayerAttackState : PlayerStateBase
    {
        /// <summary>第三段出手时刻 (s)：24fps 的 Attack03 第 5 帧附近，手臂挥到最前。</summary>
        private const float ThrowReleaseTime = 0.19f;

        private PlayerCombat _combat;
        private AttackHitbox _hitbox;

        private int _segment;             // 0=空中攻击 1/2/3=地面三段
        private float _segmentTimer;      // 本段动画剩余时长（兜底计时）
        private float _comboWindowTimer;  // 连段输入窗口剩余
        private bool _nextQueued;         // 窗口内缓冲的下一段输入
        private float _throwCountdown;    // 第三段的手部释放倒计时，>0 表示还没出手

        public PlayerAttackState(PlayerController player, PlayerStateMachine machine)
            : base(player, machine) { }

        public override void Enter()
        {
            _combat = Player.Combat;
            _nextQueued = false;
            _comboWindowTimer = 0f;
            _throwCountdown = 0f;
            Player.Anim?.ResetOneShot(PlayerAnimParams.AttackCancel);   // 清掉上次中断残留的取消触发器
            AudioManager.Instance?.PlaySfx(SfxId.Attack, 0.05f);   // M10：挥击音，轻微变调避免连击机械感

            if (!Player.IsGrounded)
            {
                _segment = 0;
                _segmentTimer = EndTime(0);
                Player.PlayOneShot("AttackAir");
            }
            else
            {
                _segment = 1;
                _segmentTimer = EndTime(1);
                _comboWindowTimer = ComboWindow;
                Player.PlayOneShot("Attack");
            }

            _hitbox = Player.GetComponent<AttackHitbox>();
            if (_hitbox != null)
            {
                _hitbox.CurrentSegment = _segment;
            }
        }

        public override void Exit()
        {
            // 攻击被打断（受击 / 死亡 / 跑出平台）时立即关命中窗，避免"人已走开、判定还在"
            if (_hitbox != null)
            {
                _hitbox.OnAttackWindowClose();
            }

            _throwCountdown = 0f;
        }

        public override void FixedTick()
        {
            // 边跑边打：不再 StopHorizontal。地面按正常移速、空中按 AirControlRatio，
            // 攻击只负责播动画与判定，位移交给移动逻辑（起手时已有的速度也自然延续）。
            Player.MoveHorizontally(!Player.IsGrounded);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions())
            {
                return;   // 受击/死亡打断攻击（R4，优先级高于攻击）
            }

            _segmentTimer -= Time.deltaTime;

            // ===== 空中单段 =====
            if (_segment == 0)
            {
                // 触地优先 → Land（R2）
                if (Player.IsGrounded && Player.VerticalVelocity <= 0.01f)
                {
                    Machine.SwitchState<PlayerLandState>();
                    return;
                }

                if (_segmentTimer <= 0f)
                {
                    Machine.SwitchState<PlayerAirState>();
                }

                return;
            }

            // 地面段跑到平台外：交给空中状态，避免"人在天上、播着地面攻击"
            if (!Player.IsGrounded && Player.VerticalVelocity < -0.1f)
            {
                ExitToLocomotion();
                return;
            }

            // 第三段出手：到点把钳子甩出去（不在手上/已在空中时 TryThrow 返回 false，本下空挥）
            if (_throwCountdown > 0f)
            {
                _throwCountdown -= Time.deltaTime;
                if (_throwCountdown <= 0f)
                {
                    Player.ToolThrow?.TryThrow();
                }
            }

            // ===== 地面三段链 =====
            if (_comboWindowTimer > 0f)
            {
                _comboWindowTimer -= Time.deltaTime;
                if (_combat != null && _combat.ConsumeAttackPressed())
                {
                    _nextQueued = true;
                }
            }

            if (_segmentTimer > 0f)
            {
                return;   // 本段动画未播完
            }

            if (_nextQueued && _segment < 3)
            {
                AdvanceCombo();   // 窗口内有输入 → 播完即衔接下一段（R1）
            }
            else
            {
                ExitToLocomotion();
            }
        }

        private void AdvanceCombo()
        {
            _segment++;
            _nextQueued = false;
            _segmentTimer = EndTime(_segment);
            _comboWindowTimer = _segment < 3 ? ComboWindow : 0f;   // 第三段后不再缓冲

            if (_hitbox != null)
            {
                _hitbox.CurrentSegment = _segment;
            }

            Player.PlayOneShot(_segment == 2 ? "Attack02" : "Attack03");

            // 第三段：记下出手倒计时（动画播到挥臂最前时由 Tick 释放投掷）
            if (_segment == 3)
            {
                _throwCountdown = ThrowReleaseTime;
            }
        }

        private void ExitToLocomotion()
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

        private float EndTime(int segment)
        {
            if (_combat == null || _combat.Config == null || _combat.Config.SegmentEndTimes == null)
            {
                return 0.3f;
            }

            int idx = Mathf.Clamp(segment, 0, _combat.Config.SegmentEndTimes.Length - 1);
            return Mathf.Max(0.05f, _combat.Config.SegmentEndTimes[idx]);
        }

        private float ComboWindow =>
            _combat != null && _combat.Config != null ? _combat.Config.ComboWindowSeconds : 0.15f;
    }
}
