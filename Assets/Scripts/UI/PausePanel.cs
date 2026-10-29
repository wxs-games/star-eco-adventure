using UnityEngine;
using UnityEngine.SceneManagement;
using StarEco.Framework;
using StarEco.Level.Events;

namespace StarEco.UI
{
    /// <summary>
    /// 暂停面板（M7 R4）：Esc 打开（栈空且关卡 Playing 时由 UIManager 触发）。
    /// timeScale=0 冻结游戏逻辑；按钮：继续 / 重新开始 / 设置 / 返回主菜单。
    /// </summary>
    public class PausePanel : UIPanel
    {
        public const string PanelId = "Pause";

        public override string PanelName => PanelId;

        private void Awake()
        {
            Build();
            if (UIManager.HasInstance)
            {
                UIManager.Instance.Register(this);
            }
        }

        private void OnDestroy()
        {
            if (UIManager.HasInstance)
            {
                UIManager.Instance.Unregister(this);
            }
        }

        protected override void OnOpen()
        {
            Time.timeScale = 0f;
            var settings = FindObjectOfType<SettingsPanel>();
            if (settings != null)
            {
                settings.SetReturnTarget(true);
            }
        }

        protected override void OnClose()
        {
            // 暂停面板出栈即恢复游戏时间。
            // 注意：从暂停打开设置时，暂停仍在栈中（设置压在其上），设置的返回只关设置，
            // 不会触发本方法 —— 暂停语义由 UIManager.Open(Pause) 幂等保持（timeScale 仍为 0）。
            Time.timeScale = 1f;
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("PauseCanvas", transform, 50);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            var bg = UIUtility.CreateImage("Background", root, UIUtility.Overlay);
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var panel = UIUtility.CreateObject("Panel", root);
            UIUtility.CenterAnchor(panel, new Vector2(480f, 560f), Vector2.zero);
            var panelBg = UIUtility.CreateImage("Background", panel.transform, UIUtility.PanelBg);
            UIUtility.Stretch(panelBg.GetComponent<RectTransform>());

            var title = UIUtility.CreateText("Title", panel.transform, "已暂停", 48,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(0f, 90f);
            titleRt.anchoredPosition = new Vector2(0f, -20f);

            var resumeButton = UIUtility.CreateButton("ResumeButton", panel.transform, "继续游戏",
                new Vector2(320f, 72f), new Vector2(0f, 180f));
            resumeButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeClose(PanelId);
            });

            var restartButton = UIUtility.CreateButton("RestartButton", panel.transform, "重新开始",
                new Vector2(320f, 72f), new Vector2(0f, 90f));
            restartButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                Time.timeScale = 1f;
                EventCenter.Publish(new RestartLevelEvent());
            });

            var settingsButton = UIUtility.CreateButton("SettingsButton", panel.transform, "设置",
                new Vector2(320f, 72f), new Vector2(0f, 0f));
            settingsButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeOpen(SettingsPanel.PanelId);
            });

            var titleButton = UIUtility.CreateButton("TitleButton", panel.transform, "返回主菜单",
                new Vector2(320f, 72f), new Vector2(0f, -90f));
            titleButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneNames.Title);
            });
        }
    }
}
