using UnityEngine;

namespace StarEco.UI
{
    /// <summary>
    /// UI 面板抽象基类（M7 R2）：所有面板（HUD/主菜单/暂停/设置/关卡选择/图鉴/对话/结算）派生自它。
    /// 生命周期由 UIManager 驱动：Open → OnOpen，Close → OnClose，每帧 OnUpdate。
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        /// <summary>面板唯一标识（UIManager 栈按此名寻址）。</summary>
        public abstract string PanelName { get; }

        /// <summary>
        /// 是否阻断玩家输入。HUD = false（常驻不锁输入）；
        /// 主菜单/暂停/设置/关卡选择/图鉴/对话/结算 = true。
        /// </summary>
        public virtual bool BlocksInput => true;

        /// <summary>面板内容根节点（通常为 Canvas 或子容器）。</summary>
        protected GameObject Root;

        /// <summary>
        /// 声明面板内容根节点。默认立即隐藏，等待 <see cref="Open"/> 显示。
        ///
        /// 背景：场景里这些面板 GameObject 本身是激活的，而 <see cref="Close"/>
        /// 在 <c>_isOpen == false</c> 时直接 return —— 于是没有任何代码路径能隐藏
        /// 它们。实测表现为进入游戏后Codex / Settings / Pause 等面板全部常驻，
        /// 文字互相压在一起。显式在此隐藏，是唯一可靠入口。
        ///
        /// <paramref name="visibleByDefault"/> 仅 HUDController 这类常驻面板传 true。
        /// </summary>
        protected void SetRoot(GameObject root, bool visibleByDefault = false)
        {
            Root = root;

            if (!visibleByDefault && root != null)
            {
                root.SetActive(false);
            }
        }

        private bool _isOpen;

        /// <summary>面板当前是否打开。</summary>
        public bool IsOpen => _isOpen;

        /// <summary>UIManager 调用：激活面板并触发 OnOpen。</summary>
        public void Open()
        {
            if (_isOpen)
            {
                return;
            }

            _isOpen = true;

            if (Root != null)
            {
                Root.SetActive(true);
            }

            OnOpen();
        }

        /// <summary>UIManager 调用：触发 OnClose 并隐藏面板。</summary>
        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            _isOpen = false;
            OnClose();

            if (Root != null)
            {
                Root.SetActive(false);
            }
        }

        /// <summary>UIManager 每帧转发（deltaTime）。面板可选实现。</summary>
        public void UpdatePanel(float dt)
        {
            if (_isOpen)
            {
                OnUpdate(dt);
            }
        }

        /// <summary>打开时回调（子类填充数据/刷新状态）。</summary>
        protected virtual void OnOpen() { }

        /// <summary>关闭时回调（子类清理临时状态）。</summary>
        protected virtual void OnClose() { }

        /// <summary>每帧回调（子类刷新动态内容）。</summary>
        protected virtual void OnUpdate(float dt) { }
    }
}
