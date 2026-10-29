using UnityEngine;

namespace StarEco.EnemyAI
{
    /// <summary>Tower01 待机：等玩家进入射程。</summary>
    public class Tower01IdleState : EnemyStateBase
    {
        public Tower01IdleState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;
            if (Controller.HasTarget)
            {
                Controller.FaceTarget();   // 持续朝目标转向，不等进入瞄准
            }
            if (!DialogueActive && Controller.HasTarget && Controller.AttackCooldownTimer <= 0f)
            {
                Machine.SwitchState<Tower01AimState>();
            }
        }
    }

    /// <summary>Tower01 瞄准：flipX 朝玩家，等 AimEnd 事件帧或 Config 兜底时长。</summary>
    public class Tower01AimState : EnemyStateBase
    {
        private float _timer;

        public Tower01AimState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.Anim?.SetBool(EnemyAnimParams.IsAiming, true);
            _timer = Config.AimSeconds;
        }

        public override void Exit()
        {
            Controller.Anim?.SetBool(EnemyAnimParams.IsAiming, false);
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;

            if (!Controller.HasTarget)
            {
                Machine.SwitchState<Tower01IdleState>();
                return;
            }

            Controller.FaceTarget();

            if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    // 对话期间不发射：作废这次瞄准，冷却结束后由 Idle 的门控重新决定
                    Machine.SwitchState(DialogueActive
                        ? typeof(Tower01IdleState)
                        : typeof(Tower01FireState));
                }
            }
        }
    }

    /// <summary>Tower01 开火：Fire Trigger，等 FireEnd 事件帧或 Config 兜底时长。</summary>
    public class Tower01FireState : EnemyStateBase
    {
        private float _timer;

        public Tower01FireState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.AttackCooldownTimer = Config.AttackInterval;
            Controller.TryFireAnim(EnemyAnimParams.Fire);
            _timer = Config.FireSeconds;
        }

        public override void Tick()
        {
            if (TryHandleCombatTransitions()) return;

            if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    // 回到 Idle 等攻击冷却走完，再由 Idle 的门控决定是否重新瞄准
                    Machine.SwitchState<Tower01IdleState>();
                }
            }
        }
    }

    /// <summary>Tower01 关闭：终态，停感知/判定/开火，广播 TowerDisableEvent 仅一次。</summary>
    public class Tower01DisableState : EnemyStateBase
    {
        public Tower01DisableState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.StopMoving();
            var col = Controller.GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            var hitbox = Controller.GetComponentInChildren<EnemyAttackHitbox>();
            if (hitbox != null) hitbox.gameObject.SetActive(false);
            Controller.TryFireAnim(EnemyAnimParams.Die);

            if (Controller is Tower01Controller tower)
            {
                tower.BroadcastDisableOnce();
            }
        }

        public override void Tick()
        {
        }
    }
}
