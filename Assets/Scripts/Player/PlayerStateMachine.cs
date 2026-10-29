using UnityEngine;

namespace StarEco.Player
{
    /// <summary>玩家状态接口：所有玩家状态的统一契约。</summary>
    public interface IPlayerState
    {
        void Enter();
        void Exit();
        /// <summary>每帧逻辑（输入判断、转移检查）。</summary>
        void Tick();
        /// <summary>每物理帧逻辑（施力/位移）。</summary>
        void FixedTick();
    }

    /// <summary>
    /// 通用状态机：字典注册 + 当前态轮询。切换时保证 Exit → Enter 顺序。
    /// </summary>
    public class PlayerStateMachine
    {
        private readonly System.Collections.Generic.Dictionary<System.Type, IPlayerState> _states =
            new System.Collections.Generic.Dictionary<System.Type, IPlayerState>();

        public IPlayerState Current { get; private set; }
        public System.Type CurrentType => Current?.GetType();

        /// <summary>注册状态（重复注册同名状态会覆盖）。</summary>
        public void Register(IPlayerState state)
        {
            _states[state.GetType()] = state;
        }

        /// <summary>设置初始状态（不走 Exit）。</summary>
        public void SetInitial<T>() where T : class, IPlayerState
        {
            Current = _states[typeof(T)];
            Current.Enter();
        }

        /// <summary>取出已注册的状态实例（用于转移前向目标状态传参）。</summary>
        public T Get<T>() where T : class, IPlayerState
        {
            return _states.TryGetValue(typeof(T), out var state) ? state as T : null;
        }

        /// <summary>切换状态：先 Exit 当前，再 Enter 新态。</summary>
        public void SwitchState<T>() where T : class, IPlayerState
        {
            var next = _states[typeof(T)];
            if (next == Current)
            {
                return;
            }

            Current?.Exit();
            Current = next;
            Current.Enter();
        }

        public void Tick()
        {
            Current?.Tick();
        }

        public void FixedTick()
        {
            Current?.FixedTick();
        }
    }
}
