using UnityEngine;
using StarEco.EnemyAI;
using StarEco.Framework;

namespace StarEco.Boss
{
    /// <summary>
    /// Boss 状态公共基类（M8 R2）：在 M4 `EnemyStateBase` 之上补 Boss 强类型访问器。
    /// </summary>
    public abstract class BossStateBase : EnemyStateBase
    {
        protected readonly BossController Boss;
        protected new BossConfig Config => Boss != null ? Boss.BossCfg : null;

        protected BossStateBase(BossController boss, EnemyStateMachine machine)
            : base(boss, machine)
        {
            Boss = boss;
        }

        /// <summary>阶段过场优先级最高（高于死亡判定之后的受击检查）。</summary>
        protected bool InTransition => Boss != null && Boss.IsPhaseTransition;
    }

    /// <summary>待机：有目标则按距离与冷却决定追击 / 技能 A / 技能 B / 特殊招。</summary>
    public class BossIdleState : BossStateBase
    {
        public BossIdleState(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        public override void Enter()
        {
            Boss.Anim?.SetBool(BossAnimParams.IsMove, false);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;
            if (InTransition) return;

            if (!Boss.HasTarget)
            {
                return;
            }

            // 观望期（刚收完远程技能）站定不动：玩家躲开也不会被立刻追上来。
            // 观望结束后若已决定逼近，追击优先于远程技能，避免"冷却一到就原地开火"变成炮台。
            if (!Boss.InMeleeRange && !Boss.IsPursueDelayed
                && (Boss.PreferMelee || !Boss.IsAttackBReady))
            {
                Machine.SwitchState<BossChaseState>();
                return;
            }

            // 对话期间不发动技能（移动/追击照常）
            if (!DialogueActive && TryBeginSkill()) return;
        }

        /// <summary>技能优先级：Special（P3）→ A（近身）→ B（远程）。返回是否已起手。</summary>
        protected bool TryBeginSkill()
        {
            if (Boss.IsSpecialReady)
            {
                Machine.SwitchState<BossSpecialState>();
                return true;
            }

            if (Boss.InMeleeRange && Boss.AttackCooldownTimer <= 0f)
            {
                Machine.SwitchState<BossAttackAState>();
                return true;
            }

            // 技能 B：P1 起可用，仅在超出近战范围时用来拉扯。
            // 观望期内不放：远程冷却一好就再打一轮，Boss 会退化成原地炮台，永远没机会逼近。
            if (Boss.IsAttackBReady && !Boss.InMeleeRange && !Boss.IsPursueDelayed)
            {
                Machine.SwitchState<BossAttackBState>();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 追击：横向移动靠近目标（移速受狂暴倍率影响），但逼近是"分段"的：
    /// 每次进入追击至少走满 ChaseBurstSeconds，走完若还没贴身就停下来放一轮远程 / 站定喘息，
    /// 而不是一路贴脸推着玩家走。这样 Boss 的节奏是 走一段 → 停 → 出招 → 再走。
    /// </summary>
    public class BossChaseState : BossStateBase
    {
        /// <summary>本次追击已经走了多久（秒）。</summary>
        private float _chaseTimer;

        public BossChaseState(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        public override void Enter()
        {
            _chaseTimer = 0f;
            Boss.Anim?.SetBool(BossAnimParams.IsMove, true);
        }

        public override void Exit()
        {
            Boss.Anim?.SetBool(BossAnimParams.IsMove, false);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;
            if (InTransition) return;

            if (!Boss.HasTarget)
            {
                Machine.SwitchState<BossIdleState>();
                return;
            }

            // 对话期间不发动技能：保持追击（移动在 FixedTick）
            if (DialogueActive) return;

            if (Boss.IsSpecialReady)
            {
                Machine.SwitchState<BossSpecialState>();
                return;
            }

            if (Boss.InMeleeRange)
            {
                if (Boss.AttackCooldownTimer <= 0f)
                {
                    Machine.SwitchState<BossAttackAState>();
                    return;
                }

                // 已经贴身但近战还在冷却：站定不推，别把玩家一路顶到墙角
                Machine.SwitchState<BossIdleState>();
                return;
            }

            // 逼近要走完一段才允许停：否则一进追击就原地开火，看起来像不会移动的炮台
            float burst = Config != null ? Mathf.Max(0.1f, Config.ChaseBurstSeconds) : 1.1f;
            if (_chaseTimer < burst)
            {
                return;
            }

            // 走满一段仍没贴身：放一轮弹幕收尾，然后回 Idle 观望（形成走走停停）
            if (Boss.IsAttackBReady)
            {
                Machine.SwitchState<BossAttackBState>();
                return;
            }

            // 远程还在冷却：停下喘息，观望结束再继续逼近，避免每帧 Idle↔追击 来回切换
            Boss.PreferMelee = true;
            Boss.BeginPursueDelay();
            Machine.SwitchState<BossIdleState>();
        }

        public override void FixedTick()
        {
            if (InTransition)
            {
                Boss.StopMoving();
                return;
            }

            _chaseTimer += Time.fixedDeltaTime;
            MoveTowards();
        }

        /// <summary>按 Boss 阶段移速移动（狂暴倍率手写，不依赖基类 EnemyConfig.MoveSpeed 缩放）。</summary>
        private void MoveTowards()
        {
            var body = Boss.Body;
            if (body == null || !Boss.HasTarget || Config == null || Config.IsStatic)
            {
                return;
            }

            Boss.FaceTarget();
            float dx = Boss.Target.position.x - Boss.transform.position.x;
            float dirX = Mathf.Sign(dx);

            // Boss 体型极宽（世界约 4.4 单位），贴到近战距离再往前走就会把玩家顶飞；
            // 这里提前一点收脚：落点要落在近战判定（AttackARange）之内、又和玩家留出一道缝。
            if (Mathf.Abs(dx) <= Mathf.Max(0.1f, Config.AttackARange - 0.2f))
            {
                return;
            }

            if (Mathf.Abs(dx) < 0.05f)
            {
                return;
            }

            float speed = Config.MoveSpeed
                * (Boss.CurrentPhase == BossPhase.Phase1 ? 1f : Config.EnrageSpeedMultiplier);

            Vector2 next = body.position + new Vector2(dirX, 0f) * speed * Time.fixedDeltaTime;
            body.MovePosition(next);
        }
    }

    /// <summary>
    /// 攻击状态公共骨架（前摇 → 命中 → 后摇）：
    /// 前摇从动画第一帧开始播，到点才结算伤害；前摇/命中阶段霸体且站定，
    /// 后摇阶段站定但恢复正常受击，玩家可在此期间反击。
    /// </summary>
    public abstract class BossAttackStateBase : BossStateBase
    {
        private enum AttackPhase { Windup, Active, Recovery }

        private AttackPhase _phase;
        private float _timer;
        private float _activeSeconds;
        private float _recoverSeconds;

        protected BossAttackStateBase(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        /// <summary>前摇时长（秒）。</summary>
        protected abstract float WindupSeconds { get; }

        /// <summary>命中阶段时长（秒），覆盖攻击动画剩余帧。</summary>
        protected abstract float ActiveSeconds { get; }

        /// <summary>后摇时长（秒）。</summary>
        protected abstract float RecoverSeconds { get; }

        /// <summary>攻击动画 Trigger。</summary>
        protected abstract int AnimTrigger { get; }

        /// <summary>
        /// 起手动画 Trigger：默认直接播攻击动作（近战 A）；
        /// 远程技能覆写为蓄力动作，前摇期间先播可见的蓄力，到点再切攻击动作。
        /// </summary>
        protected virtual int EnterTrigger => AnimTrigger;

        /// <summary>进入状态时重置本次技能冷却（各技能分开计时）。</summary>
        protected virtual void ResetCooldown() { }

        /// <summary>前摇结束时的结算入口（近战 A 由动画事件开命中窗，保持空实现）。</summary>
        protected virtual void OnWindupEnd() { }

        /// <summary>
        /// 收尾后是否转为"优先逼近"：远程技能（B / Special）为 true —— 打一轮远程就压上去；
        /// 近战 A 覆写为 false —— 打完近战又回到"远程拉扯"的节奏，形成 远程 ↔ 近战 循环。
        /// </summary>
        protected virtual bool PreferMeleeAfter => true;

        public override void Enter()
        {
            Boss.ConsumePendingHurt();
            ResetCooldown();
            Boss.StopMoving();
            Boss.FaceTarget();
            Boss.IsArmored = true;

            _activeSeconds = Mathf.Max(0f, ActiveSeconds);
            _recoverSeconds = Mathf.Max(0f, RecoverSeconds);
            _phase = AttackPhase.Windup;
            _timer = Mathf.Max(0f, WindupSeconds);

            // 有前摇就播起手动作（远程技能 = 蓄力），无前摇则直接播攻击动作。
            // 攻击动作在 OnWindupEnd 时才切，玩家能清晰看到"蓄力 → 喷出"两段。
            Boss.TryFireAnim(_timer > 0f ? EnterTrigger : AnimTrigger);

            if (_timer <= 0f)
            {
                EnterActive();
            }
        }

        public override void Exit()
        {
            Boss.IsArmored = false;
        }

        public override void Tick()
        {
            // 死亡永远优先
            if (TryHandleDieTransition())
            {
                return;
            }

            if (InTransition)
            {
                Boss.StopMoving();
                return;
            }

            // 后摇可被打断；前摇 / 命中阶段霸体，不响应受击
            if (_phase == AttackPhase.Recovery && TryHandleHurtTransition())
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            switch (_phase)
            {
                case AttackPhase.Windup:
                    EnterActive();
                    break;
                case AttackPhase.Active:
                    EnterRecovery();
                    break;
                default:
                    FinishAttack();
                    break;
            }
        }

        private void EnterActive()
        {
            _phase = AttackPhase.Active;
            _timer = _activeSeconds;
            OnWindupEnd();

            if (_timer <= 0f)
            {
                EnterRecovery();
            }
        }

        private void EnterRecovery()
        {
            _phase = AttackPhase.Recovery;
            Boss.IsArmored = false;
            _timer = _recoverSeconds;

            if (_timer <= 0f)
            {
                FinishAttack();
            }
        }

        /// <summary>
        /// 技能收尾：回待机站定观望，而不是立刻扑上去。
        /// 主流 Boss 的节奏是"远程先打 → 玩家躲开也不马上追 → 观望结束再逼近"，
        /// 所以这里统一回 Idle 并挂一段追击延迟，由 Idle 决定何时进入追击。
        /// </summary>
        private void FinishAttack()
        {
            Boss.PreferMelee = PreferMeleeAfter;
            Machine.SwitchState<BossIdleState>();

            if (Boss.HasTarget)
            {
                Boss.BeginPursueDelay();
            }
        }
    }

    /// <summary>技能 A · 近战横扫：前摇 0.20s 后由动画事件开命中窗，后摇 0.30s。</summary>
    public class BossAttackAState : BossAttackStateBase
    {
        public BossAttackAState(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        protected override float WindupSeconds => Config != null ? Config.AttackAWindupSeconds : 0.20f;
        protected override float ActiveSeconds => Config != null ? Config.AttackAActiveSeconds : 0.62f;
        protected override float RecoverSeconds => Config != null ? Config.AttackARecoverSeconds : 0.30f;
        protected override int AnimTrigger => BossAnimParams.AttackA;

        /// <summary>打完近战回到远程为主的节奏（否则会一直贴着玩家打）。</summary>
        protected override bool PreferMeleeAfter => false;

        protected override void ResetCooldown() => Boss.ResetAttackACooldown();
    }

    /// <summary>技能 B · 远程弹幕：1.10s 蓄力前摇（可见动作）→ 切攻击动作并扇形发射，后摇 0.30s。</summary>
    public class BossAttackBState : BossAttackStateBase
    {
        public BossAttackBState(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        protected override float WindupSeconds => Config != null ? Config.AttackBWindupSeconds : 1.10f;
        protected override float ActiveSeconds => Config != null ? Config.AttackBActiveSeconds : 0.60f;
        protected override float RecoverSeconds => Config != null ? Config.AttackBRecoverSeconds : 0.30f;
        protected override int AnimTrigger => BossAnimParams.AttackB;

        /// <summary>前摇播蓄力动作，而不是直接播喷吐动作。</summary>
        protected override int EnterTrigger => BossAnimParams.Charge;

        protected override void ResetCooldown() => Boss.ResetAttackBCooldown();

        protected override void OnWindupEnd()
        {
            // 蓄力到位 → 切到攻击动作（绿雾喷出），同时结算弹幕
            Boss.TryFireAnim(AnimTrigger);
            DoFire();
        }

        /// <summary>实际弹幕内容（Special 覆写它，前摇与冷却由各自 Enter 决定）。</summary>
        protected virtual void DoFire()
        {
            if (Config == null || Boss.ProjectilePrefab == null)
            {
                Debug.LogWarning("[BossAttackB] 未配置弹体预制体，技能 B 空放。");
                return;
            }

            int count = Mathf.Max(1, Config.ProjectileCount + (Boss.CurrentPhase == BossPhase.Phase1 ? 0 : 1));
            float spread = Config.ProjectileSpreadDegrees;
            Vector2 baseDir = Boss.FacingDirection;

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) - 0.5f : 0f;
                SpawnProjectile(Rotate(baseDir, t * spread));
            }
        }

        protected void SpawnProjectile(Vector2 dir)
        {
            var prefab = Boss.ProjectilePrefab;
            var projectile = PoolManager.Instance.Get(prefab, Config.ProjectilePrewarm);
            projectile.transform.position = (Vector2)Boss.transform.position + dir * Boss.FirePointOffset;

            projectile.Launch(new EnemyProjectile.ProjectileParams
            {
                Prefab = prefab,
                Direction = dir.normalized,
                Speed = Config.ProjectileSpeed,
                MaxRange = Config.ProjectileMaxRange,
                Damage = Config.AttackDamage,
                Knockback = Config.AttackKnockback,
                Source = Boss.gameObject,
                OwnerLayer = Boss.gameObject.layer
            });
        }

        protected static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }

    /// <summary>特殊招 · 环绕扇形弹幕（仅 P3）：1.40s 蓄力前摇后发射，后摇 0.35s。</summary>
    public class BossSpecialState : BossAttackBState
    {
        public BossSpecialState(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        protected override float WindupSeconds => Config != null ? Config.SpecialWindupSeconds : 1.40f;
        protected override float ActiveSeconds => Config != null ? Config.SpecialActiveSeconds : 0.62f;
        protected override float RecoverSeconds => Config != null ? Config.SpecialRecoverSeconds : 0.35f;
        protected override int AnimTrigger => BossAnimParams.Special;
        protected override void ResetCooldown() => Boss.ResetSpecialCooldown();

        protected override void DoFire()
        {
            if (Config == null || Boss.ProjectilePrefab == null)
            {
                return;
            }

            int count = Mathf.Max(1, Config.SpecialProjectileCount);
            float spread = Config.SpecialSpreadDegrees;
            Vector2 baseDir = Boss.FacingDirection;

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) - 0.5f : 0f;
                SpawnProjectile(Rotate(baseDir, t * spread));
            }

            Debug.Log($"[BossSpecial] 释放特殊招：{count} 发 / 张角 {spread}°");
        }
    }

    /// <summary>阶段狂暴过场：站定播 Enrage，等 BossController 的过场计时结束。</summary>
    public class BossEnrageState : BossStateBase
    {
        public BossEnrageState(BossController boss, EnemyStateMachine machine) : base(boss, machine) { }

        public override void Enter()
        {
            Boss.StopMoving();
            Boss.Anim?.SetBool(BossAnimParams.IsMove, false);
        }

        public override void Tick()
        {
            // 过场结束由 BossController.TickTransition 统一切回 BossIdleState
            if (TryHandleDieTransition()) return;
        }
    }
}
