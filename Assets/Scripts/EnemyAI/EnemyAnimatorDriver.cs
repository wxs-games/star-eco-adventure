using UnityEngine;

namespace StarEco.EnemyAI
{
    /// <summary>Animator 参数名常量：代码与动画资源之间唯一的事实来源。</summary>
    public static class EnemyAnimParams
    {
        public static readonly int Speed = Animator.StringToHash("Speed");
        public static readonly int IsMove = Animator.StringToHash("IsMove");
        public static readonly int IsAiming = Animator.StringToHash("IsAiming");
        public static readonly int Attack = Animator.StringToHash("Attack");
        public static readonly int Fire = Animator.StringToHash("Fire");
        public static readonly int Hurt = Animator.StringToHash("Hurt");
        public static readonly int Die = Animator.StringToHash("Die");
        public static readonly int Spawn = Animator.StringToHash("Spawn");

        // M8 Boss 专属：技能 B / 特殊招 / 阶段狂暴（三个 Trigger，其余沿用上面通用参数）
        public static readonly int AttackB = Animator.StringToHash("AttackB");
        public static readonly int Special = Animator.StringToHash("Special");
        public static readonly int Enrage = Animator.StringToHash("Enrage");
    }

    /// <summary>
    /// 敌人动画驱动：每帧将控制器事实写入 Animator 参数。
    /// 状态机逻辑与动画资源完全解耦（仅通过参数通信）。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class EnemyAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private EnemyControllerBase _controller;

        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_controller == null)
            {
                _controller = GetComponentInParent<EnemyControllerBase>();
            }
        }

        private void Update()
        {
            if (_controller == null || _animator == null)
            {
                return;
            }

            float speed = 0f;
            if (_controller.Body != null)
            {
                speed = Mathf.Abs(_controller.Body.velocity.x);
            }
            _animator.SetFloat(EnemyAnimParams.Speed, speed);
        }

        /// <summary>触发一次性动画（攻击/受击/死亡/出场/开火）。供状态 Enter 调用。</summary>
        public void FireOneShot(int triggerHash)
        {
            _animator?.SetTrigger(triggerHash);
        }

        public void SetBool(int paramHash, bool value)
        {
            _animator?.SetBool(paramHash, value);
        }

        public void SetFloat(int paramHash, float value)
        {
            _animator?.SetFloat(paramHash, value);
        }

        /// <summary>复位所有参数（池复用时调用）。</summary>
        public void ResetParams()
        {
            if (_animator == null)
            {
                return;
            }
            _animator.SetFloat(EnemyAnimParams.Speed, 0f);
            _animator.SetBool(EnemyAnimParams.IsMove, false);
            _animator.SetBool(EnemyAnimParams.IsAiming, false);
        }
    }
}
