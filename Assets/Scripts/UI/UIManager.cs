using System.Collections.Generic;
using UnityEngine;
using StarEco.Framework;
using StarEco.Level;
using StarEco.Player;

namespace StarEco.UI
{
    /// <summary>
    /// UI 管理器（M7 R2）：面板栈单例，跨场景常驻（Title/Level 共用）。
    /// - Open/Close/Toggle/CloseTop 按 LIFO 管理面板栈
    /// - Esc 路由：栈顶优先；栈空且关卡 Playing → 打开暂停菜单
    /// - 输入冻结收口：按栈顶面板 BlocksInput 统一设置 PlayerController.InputLocked
    /// </summary>
    public class UIManager : MonoSingleton<UIManager>
    {
        private readonly List<UIPanel> _stack = new List<UIPanel>();
        private readonly Dictionary<string, UIPanel> _panels = new Dictionary<string, UIPanel>();

        /// <summary>栈顶面板名（空栈返回空串）。</summary>
        public string TopPanelName => _stack.Count > 0 ? _stack[_stack.Count - 1].PanelName : string.Empty;

        /// <summary>当前是否有阻断型面板在栈顶。</summary>
        public bool IsBlocking => _stack.Count > 0 && _stack[_stack.Count - 1].BlocksInput;

        protected override void OnSingletonAwake()
        {
            Debug.Log("[UIManager] UI 管理器就绪。");
        }

        private void Start()
        {
            // 兜底注册：Unity 保证同场景所有 Awake 先于任何 Start。
            // 各面板 Awake 里可能因 UIManager 尚未实例化而注册失败（首次启动 Title 场景时
            // Awake 顺序不确定），这里统一补注册（幂等）。
            foreach (var panel in FindObjectsOfType<UIPanel>())
            {
                Register(panel);
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;   // 暂停（timeScale=0）时 UI 动画仍可跑
            for (int i = 0; i < _stack.Count; i++)
            {
                _stack[i].UpdatePanel(dt);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                HandleEscape();
            }
        }

        /// <summary>面板注册（各面板 Awake 时调用；场景重载后由新面板重新注册）。</summary>
        public void Register(UIPanel panel)
        {
            if (panel == null || string.IsNullOrEmpty(panel.PanelName))
            {
                return;
            }

            if (!_panels.ContainsKey(panel.PanelName))
            {
                _panels.Add(panel.PanelName, panel);
            }
        }

        /// <summary>面板注销（OnDestroy 时调用，防止跨场景残留引用）。</summary>
        public void Unregister(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            _stack.Remove(panel);

            if (_panels.TryGetValue(panel.PanelName, out var registered) && registered == panel)
            {
                _panels.Remove(panel.PanelName);
            }

            ApplyInputLock();
        }

        /// <summary>打开面板（入栈）。</summary>
        public void Open(string panelName)
        {
            if (!_panels.TryGetValue(panelName, out var panel))
            {
                Debug.LogWarning($"[UIManager] 面板未注册：{panelName}");
                return;
            }

            if (_stack.Contains(panel))
            {
                panel.Open();   // 幂等
                return;
            }

            _stack.Add(panel);
            panel.Open();
            ApplyInputLock();
            UIUtility.PlayClickSfx();
        }

        /// <summary>关闭面板（出栈）。</summary>
        public void Close(string panelName)
        {
            if (!_panels.TryGetValue(panelName, out var panel))
            {
                return;
            }

            _stack.Remove(panel);
            panel.Close();
            ApplyInputLock();
        }

        /// <summary>切换面板开/关。</summary>
        public void Toggle(string panelName)
        {
            if (!_panels.TryGetValue(panelName, out var panel))
            {
                return;
            }

            if (_stack.Contains(panel))
            {
                Close(panelName);
            }
            else
            {
                Open(panelName);
            }
        }

        /// <summary>关闭栈顶面板（Esc 用）。</summary>
        public void CloseTop()
        {
            if (_stack.Count == 0)
            {
                return;
            }

            var top = _stack[_stack.Count - 1];
            Close(top.PanelName);
        }

        /// <summary>静态便捷打开（供面板按钮回调使用，无实例时静默）。</summary>
        public static void SafeOpen(string panelName)
        {
            if (HasInstance)
            {
                Instance.Open(panelName);
            }
        }

        /// <summary>静态便捷关闭（供面板按钮回调使用，无实例时静默）。</summary>
        public static void SafeClose(string panelName)
        {
            if (HasInstance)
            {
                Instance.Close(panelName);
            }
        }

        /// <summary>清空面板栈（场景切换时由外部调用）。</summary>
        public void ClearStack()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                _stack[i].Close();
            }

            _stack.Clear();
            _panels.Clear();
            ApplyInputLock();
        }

        // ===== 内部 =====

        private void HandleEscape()
        {
            if (_stack.Count > 0)
            {
                // HUD 不响应 Esc（非阻断面板不占 Esc）
                var top = _stack[_stack.Count - 1];
                if (top.BlocksInput)
                {
                    CloseTop();
                    return;
                }
            }

            // 栈空（或只剩 HUD）：关卡进行中才开暂停
            var level = LevelManager.HasInstance ? LevelManager.Instance : null;
            if (level != null && level.State == LevelState.Playing)
            {
                Open(PausePanel.PanelId);
            }
        }

        private void ApplyInputLock()
        {
            var player = FindObjectOfType<PlayerController>();
            if (player == null)
            {
                return;
            }

            player.InputLocked = IsBlocking;
        }
    }
}
