using UnityEngine;
using StarEco.UI;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// 敌人状态公共基类：持有控制器与状态机引用，提供常用上下文方法。
    /// 与 M2 PlayerStateBase 同模式，命名独立。
    /// </summary>
    public abstract class EnemyStateBase : IEnemyState
    {
        protected readonly EnemyControllerBase Controller;
        protected readonly EnemyStateMachine Machine;
        protected EnemyConfig Config => Controller.Config;

        /// <summary>
        /// 对话进行中（全局只读）：对话期间敌人不发动攻击，但移动/追击照常。
        /// </summary>
        protected static bool DialogueActive => DialoguePanel.IsDialogueActive;

        protected EnemyStateBase(EnemyControllerBase controller, EnemyStateMachine machine)
        {
            Controller = controller;
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
            if (Controller.IsDead)
            {
                Machine.SwitchState<EnemyDieState>();
                return true;
            }
            return false;
        }

        /// <summary>受击优先级次高。发生转移返回 true（调用方应立即 return）。</summary>
        protected bool TryHandleHurtTransition()
        {
            if (Controller.ConsumePendingHurt())
            {
                Machine.SwitchState<EnemyHurtState>();
                return true;
            }
            return false;
        }

        /// <summary>死亡与受击统一检查：命中任一即转移。</summary>
        protected bool TryHandleCombatTransitions()
        {
            return TryHandleDieTransition() || TryHandleHurtTransition();
        }
    }
}
