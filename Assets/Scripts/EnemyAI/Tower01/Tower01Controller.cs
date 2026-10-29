using UnityEngine;
using StarEco.Combat;
using StarEco.Framework;
using StarEco.EnemyAI.Events;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// Tower01 毒气防御塔：固定单位，Idle→Aim→Fire→Idle（攻击冷却）循环，Disable 终态。
    /// IsStatic=true，KnockbackResponse=0，无 Rigidbody2D，默认不可死。
    /// </summary>
    public class Tower01Controller : EnemyControllerBase
    {
        [SerializeField] private Transform _firePoint;
        [SerializeField] private EnemyProjectile _projectilePrefab;

        private bool _isDisabled;
        private bool _disableBroadcasted;

        /// <summary>
        /// 塔首次关闭时触发一次。攻击击毁与净化关闭都会走到这里，
        /// 供 TowerPurifiable 把“击毁”也计入净化值（否则会漏 10%）。
        /// </summary>
        public event System.Action Disabled;

        protected override System.Type InitialStateType => typeof(Tower01IdleState);

        /// <summary>美术炮口默认朝左，flipX 语义与常规（默认朝右）相反。</summary>
        protected override bool SpriteFacesLeftByDefault => true;

        protected override void BuildStateMachine()
        {
            Machine = new EnemyStateMachine();
            Machine.Register(new Tower01IdleState(this, Machine));
            Machine.Register(new Tower01AimState(this, Machine));
            Machine.Register(new Tower01FireState(this, Machine));
            Machine.Register(new Tower01DisableState(this, Machine));
        }

        public bool IsDisabled => _isDisabled;

        /// <summary>
        /// 可被攻击击杀（与怪物一致）：扣血 + 受击闪白 + 广播受伤事件；血尽即被击毁。
        /// 不走基类的受击硬直/死亡状态机 —— 塔只注册 Idle/Aim/Fire/Disable，
        /// 共用 TryHandleCombatTransitions 会切到未注册的 EnemyHurtState/EnemyDieState；
        /// 这里直接复用既有 Disable 终态作为"被击毁"表现。
        /// </summary>
        public override void TakeDamage(DamageInfo info)
        {
            if (_isDisabled || IsDead || IsSpawnImmune || info.Amount <= 0)
            {
                return;
            }

            CurrentHP = Mathf.Max(0, CurrentHP - info.Amount);
            LastHitDirection = info.SourceDirection;
            AudioManager.Instance?.PlaySfx(HitSfxId, 0.05f);

            // 与小怪共用同一受伤事件与受击闪白（血条据此刷新）
            EventCenter.Publish(new EnemyDamagedEvent
            {
                Enemy = this,
                CurrentHP = CurrentHP,
                MaxHP = Config != null ? Config.MaxHP : CurrentHP,
                Amount = info.Amount
            });

            HitFlash.Play(Sprite);

            if (CurrentHP <= 0)
            {
                IsDead = true;
                RequestDisable();   // 被击毁：Die 动画 + 关闭碰撞体 + 广播关闭事件
            }
        }

        /// <summary>外部请求关闭塔（幂等：不重播/不报错/不重复广播）。</summary>
        public void RequestDisable()
        {
            if (_isDisabled) return;
            _isDisabled = true;
            EnsureMachine();
            Machine.SwitchState<Tower01DisableState>();
            Disabled?.Invoke();
        }

        /// <summary>广播关闭事件（只广播一次）。</summary>
        public void BroadcastDisableOnce()
        {
            if (_disableBroadcasted) return;
            _disableBroadcasted = true;
            EventCenter.Publish(new TowerDisableEvent
            {
                TowerTypeId = Config.EnemyTypeId,
                Position = transform.position
            });
        }

        /// <summary>Animation Event: FireSpawn — 生成毒气弹体。</summary>
        public void OnFireSpawn()
        {
            if (_isDisabled || _projectilePrefab == null || _firePoint == null) return;

            Vector2 dir = FacingDirection;
            var projectile = PoolManager.Instance.Get(_projectilePrefab, Config.ProjectilePrewarm);
            projectile.transform.position = (Vector2)transform.position + new Vector2(
                dir.x * _firePoint.localPosition.x, _firePoint.localPosition.y);
            projectile.Launch(new EnemyProjectile.ProjectileParams
            {
                Prefab = _projectilePrefab,
                Direction = dir,
                Speed = Config.ProjectileSpeed,
                MaxRange = Config.ProjectileMaxRange,
                Damage = Config.AttackDamage,
                Knockback = Config.AttackKnockback,
                Source = gameObject,
                OwnerLayer = gameObject.layer
            });
        }

        /// <summary>Animation Event: AimEnd — Aim → Fire。</summary>
        public void OnAimEnd()
        {
            if (Machine.CurrentType == typeof(Tower01AimState))
            {
                Machine.SwitchState<Tower01FireState>();
            }
        }

        /// <summary>Animation Event: FireEnd — 开火结束，回 Idle 等冷却。</summary>
        public void OnFireEnd()
        {
            if (Machine.CurrentType == typeof(Tower01FireState))
            {
                Machine.SwitchState<Tower01IdleState>();
            }
        }

        public override void ResetForReuse()
        {
            base.ResetForReuse();
            _isDisabled = false;
            _disableBroadcasted = false;
        }
    }
}
