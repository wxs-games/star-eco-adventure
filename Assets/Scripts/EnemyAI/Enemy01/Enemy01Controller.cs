namespace StarEco.EnemyAI
{
    /// <summary>
    /// Enemy01 污染污泥怪：慢速近战。注册共享状态 Idle/Chase/Attack/Hurt/Die。
    /// 无专属状态文件，全复用 EnemyStates.cs。
    /// </summary>
    public class Enemy01Controller : EnemyControllerBase
    {
        protected override void BuildStateMachine()
        {
            Machine = new EnemyStateMachine();
            Machine.Register(new EnemyIdleState(this, Machine));
            Machine.Register(new EnemyChaseState(this, Machine));
            Machine.Register(new EnemyMeleeAttackState(this, Machine));
            Machine.Register(new EnemyHurtState(this, Machine));
            Machine.Register(new EnemyDieState(this, Machine));
        }
    }
}
