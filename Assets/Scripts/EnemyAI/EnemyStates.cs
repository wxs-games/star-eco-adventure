using UnityEngine;

namespace StarEco.EnemyAI
{
    /// <summary>待机状态：感知目标，有目标则转移动/追击或攻击。</summary>
    public class EnemyIdleState : EnemyStateBase
    {
        public EnemyIdleState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;

            if (!Controller.HasTarget || Controller.IsStunned) return;

            if (Controller.InAttackRange && Controller.AttackCooldownTimer <= 0f && !DialogueActive)
            {
                if (Config.AttackKind == EnemyConfig.AttackKindEnum.Melee)
                    Machine.SwitchState<EnemyMeleeAttackState>();
                else
                    Machine.SwitchState<EnemyRangedAttackState>();
            }
            else if (!Config.IsStatic && !Controller.InAttackRange)
            {
                Machine.SwitchState<EnemyChaseState>();
            }
        }
    }

    /// <summary>追击状态：水平移动追击目标。</summary>
    public class EnemyChaseState : EnemyStateBase
    {
        public EnemyChaseState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.Anim?.SetBool(EnemyAnimParams.IsMove, true);
        }

        public override void Exit()
        {
            Controller.Anim?.SetBool(EnemyAnimParams.IsMove, false);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;

            if (!Controller.HasTarget)
            {
                Machine.SwitchState<EnemyIdleState>();
                return;
            }

            if (Controller.InAttackRange && Controller.AttackCooldownTimer <= 0f && !DialogueActive)
            {
                if (Config.AttackKind == EnemyConfig.AttackKindEnum.Melee)
                    Machine.SwitchState<EnemyMeleeAttackState>();
                else
                    Machine.SwitchState<EnemyRangedAttackState>();
            }
        }

        public override void FixedTick()
        {
            Controller.MoveTowardsTarget();
        }
    }

    /// <summary>近战攻击状态：播放 Attack Trigger，冷却后回落。</summary>
    public class EnemyMeleeAttackState : EnemyStateBase
    {
        public EnemyMeleeAttackState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.AttackCooldownTimer = Config.AttackInterval;
            Controller.StopMoving();
            Controller.FaceTarget();
            Controller.TryFireAnim(EnemyAnimParams.Attack);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;

            if (Controller.AttackCooldownTimer <= 0f)
            {
                if (Controller.HasTarget && Controller.InAttackRange && !DialogueActive)
                {
                    Controller.AttackCooldownTimer = Config.AttackInterval;
                    Controller.TryFireAnim(EnemyAnimParams.Attack);
                }
                else if (Controller.HasTarget)
                {
                    Machine.SwitchState<EnemyChaseState>();
                }
                else
                {
                    Machine.SwitchState<EnemyIdleState>();
                }
            }
        }
    }

    /// <summary>远程攻击状态：播放 Attack Trigger，HitOn 事件帧生成弹体。</summary>
    public class EnemyRangedAttackState : EnemyStateBase
    {
        public EnemyRangedAttackState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.AttackCooldownTimer = Config.AttackInterval;
            Controller.StopMoving();
            Controller.FaceTarget();
            Controller.TryFireAnim(EnemyAnimParams.Attack);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;

            if (Controller.AttackCooldownTimer <= 0f)
            {
                if (Controller.HasTarget && Controller.InAttackRange && !DialogueActive)
                {
                    Controller.AttackCooldownTimer = Config.AttackInterval;
                    Controller.TryFireAnim(EnemyAnimParams.Attack);
                }
                else if (Controller.HasTarget)
                {
                    Machine.SwitchState<EnemyChaseState>();
                }
                else
                {
                    Machine.SwitchState<EnemyIdleState>();
                }
            }
        }
    }

    /// <summary>受击状态：硬直+击退，结束后回战斗/Idle。</summary>
    public class EnemyHurtState : EnemyStateBase
    {
        public EnemyHurtState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.StopMoving();
            Controller.TryFireAnim(EnemyAnimParams.Hurt);
            var kb = Controller.ConsumeKnockback();
            if (Controller.Body != null && kb.sqrMagnitude > 0.0001f)
            {
                Controller.Body.velocity = new Vector2(kb.x, Controller.Body.velocity.y);
            }
        }

        public override void Tick()
        {
            if (TryHandleDieTransition()) return;

            if (!Controller.IsStunned)
            {
                Machine.SwitchState(Controller.DecidePostHurtState());
            }
        }
    }

    /// <summary>死亡状态：关碰撞/判定/停移动，等 DieEnd 事件帧回收。</summary>
    public class EnemyDieState : EnemyStateBase
    {
        public EnemyDieState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.StopMoving();
            var col = Controller.GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            var hitbox = Controller.GetComponentInChildren<EnemyAttackHitbox>();
            if (hitbox != null) hitbox.gameObject.SetActive(false);
            Controller.TryFireAnim(EnemyAnimParams.Die);
        }

        public override void Tick()
        {
        }
    }
}
