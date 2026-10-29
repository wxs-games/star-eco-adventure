using UnityEngine;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// Enemy03 巡逻机器人：高速追击 + 出场。注册 Spawn/Idle/Chase/Attack/Hurt/Die，初始态 Spawn。
    /// </summary>
    public class Enemy03Controller : EnemyControllerBase
    {
        protected override System.Type InitialStateType => typeof(Enemy03SpawnState);

        protected override void BuildStateMachine()
        {
            Machine = new EnemyStateMachine();
            Machine.Register(new Enemy03SpawnState(this, Machine));
            Machine.Register(new EnemyIdleState(this, Machine));
            Machine.Register(new EnemyChaseState(this, Machine));
            Machine.Register(new EnemyMeleeAttackState(this, Machine));
            Machine.Register(new EnemyHurtState(this, Machine));
            Machine.Register(new EnemyDieState(this, Machine));
        }

        /// <summary>Animation Event: SpawnEnd — 出场结束转 Idle。</summary>
        public void OnSpawnEnd()
        {
            EnsureMachine();
            if (Machine.CurrentType == typeof(Enemy03SpawnState))
            {
                Machine.SwitchState<EnemyIdleState>();
            }
        }
    }
}
