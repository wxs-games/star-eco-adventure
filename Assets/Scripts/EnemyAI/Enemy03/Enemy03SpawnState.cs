using UnityEngine;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// Enemy03 出场状态：播放 Spawn 动画，期间无敌/不移动/不攻击/不感知。
    /// 动画结束（SpawnEnd 事件帧）或 Config.SpawnSeconds 兜底超时后转 Idle。
    /// </summary>
    public class Enemy03SpawnState : EnemyStateBase
    {
        private float _timer;

        public Enemy03SpawnState(EnemyControllerBase controller, EnemyStateMachine machine)
            : base(controller, machine) { }

        public override void Enter()
        {
            Controller.IsSpawnImmune = true;
            Controller.StopMoving();
            Controller.TryFireAnim(EnemyAnimParams.Spawn);
            _timer = Config.SpawnSeconds;
        }

        public override void Tick()
        {
            if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    Machine.SwitchState<EnemyIdleState>();
                }
            }
        }

        public override void Exit()
        {
            Controller.IsSpawnImmune = false;
        }
    }
}
