using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StarEco.Level;

namespace StarEco.UI
{
    /// <summary>
    /// 关卡选择面板（M7 R6）：3 关卡片（名/简介/锁定态/最佳星级）。
    /// 解锁依据 LevelSaveService.LoadProgress().UnlockedLevelIndex。
    /// </summary>
    public class LevelSelectPanel : UIPanel
    {
        public const string PanelId = "LevelSelect";

        public override string PanelName => PanelId;

        private struct LevelCardInfo
        {
            public string SceneName;
            public string DisplayName;
            public string Brief;
            public int BuildIndex;
        }

        private static readonly LevelCardInfo[] Levels =
        {
            new LevelCardInfo { SceneName = "Level01", DisplayName = "第一关 · 污染初现的星球", Brief = "教学与轻度战斗：清理垃圾堆，学会净化喷雾", BuildIndex = 1 },
            // M9：第二关已建成（E 键解救 + 水流解谜），Build Settings index 2
            new LevelCardInfo { SceneName = "Level02", DisplayName = "第二关 · 生态失衡的森林", Brief = "E 键解救动物、喷雾清堵疏通河道、木筏渡河", BuildIndex = 2 },
            // M8：第三关已建成（Boss01 三阶段战），Build Settings index 3
            new LevelCardInfo { SceneName = "Level03", DisplayName = "第三关 · 能源工厂的危机", Brief = "高强度战斗：清理污染源，击败 Boss01", BuildIndex = 3 }
        };

        private readonly List<Text> _starTexts = new List<Text>();
        private readonly List<Button> _cardButtons = new List<Button>();
        private int _unlocked = 1;

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
            var progress = LevelSaveService.LoadProgress();
            _unlocked = Mathf.Max(1, progress.UnlockedLevelIndex);

            for (int i = 0; i < _cardButtons.Count && i < Levels.Length; i++)
            {
                bool playable = i < _unlocked;
                _cardButtons[i].interactable = playable;
            }
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("LevelSelectCanvas", transform, 60);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            var bg = UIUtility.CreateImage("Background", root, UIUtility.Overlay);
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var panel = MenuArt.CreatePanel(root, "Panel", new Vector2(1240f, 860f), Vector2.zero);

            var title = UIUtility.CreateText("Title", panel, "选择关卡", 48,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(800f, 96f);
            titleRt.anchoredPosition = new Vector2(0f, -30f);

            for (int i = 0; i < Levels.Length; i++)
            {
                CreateCard(panel, i, Levels[i]);
            }

            var backButton = UIUtility.CreateButton("BackButton", panel, "返回",
                new Vector2(240f, 70f), new Vector2(0f, -330f));
            backButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeClose(PanelId);
            });

            MenuArt.CreateCloseButton(panel, () => UIManager.SafeClose(PanelId));
        }

        private void CreateCard(Transform parent, int index, LevelCardInfo info)
        {
            var card = UIUtility.CreateObject("Card" + index, parent);
            UIUtility.CenterAnchor(card, new Vector2(760f, 150f), new Vector2(0f, 180f - index * 180f));

            var cardBg = UIUtility.CreateImage("Background", card.transform, UIUtility.PanelBg);
            UIUtility.Stretch(cardBg.GetComponent<RectTransform>());

            var nameText = UIUtility.CreateText("Name", card.transform, info.DisplayName, 32,
                TextAnchor.MiddleLeft, UIUtility.TextMain);
            var nameRt = nameText.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0f, 1f);
            nameRt.sizeDelta = new Vector2(-40f, 48f);
            nameRt.anchoredPosition = new Vector2(20f, -10f);

            var briefText = UIUtility.CreateText("Brief", card.transform, info.Brief, 24,
                TextAnchor.MiddleLeft, UIUtility.TextBody);
            var briefRt = briefText.GetComponent<RectTransform>();
            briefRt.anchorMin = new Vector2(0f, 0f);
            briefRt.anchorMax = new Vector2(1f, 0f);
            briefRt.pivot = new Vector2(0f, 0f);
            briefRt.sizeDelta = new Vector2(-40f, 40f);
            briefRt.anchoredPosition = new Vector2(20f, 12f);

            var starText = UIUtility.CreateText("Stars", card.transform, "最佳：—", 26,
                TextAnchor.MiddleRight, UIUtility.AccentGreen);
            var starRt = starText.GetComponent<RectTransform>();
            starRt.anchorMin = new Vector2(1f, 0f);
            starRt.anchorMax = new Vector2(1f, 0f);
            starRt.pivot = new Vector2(1f, 0f);
            starRt.sizeDelta = new Vector2(200f, 40f);
            starRt.anchoredPosition = new Vector2(-20f, 12f);
            _starTexts.Add(starText);

            var button = card.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.001f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.10f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.001f);
            button.colors = colors;

            int captured = index;
            button.onClick.AddListener(() => OnCardClicked(captured));
            _cardButtons.Add(button);
        }

        private void OnCardClicked(int index)
        {
            var info = Levels[index];

            if (index >= _unlocked)
            {
                UIUtility.PlayClickSfx();
                Debug.Log("[LevelSelect] 关卡未解锁。");
                return;
            }

            if (info.BuildIndex < 0 || info.BuildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                UIUtility.PlayClickSfx();
                Debug.Log($"[LevelSelect] {info.DisplayName} 尚未建成，敬请期待。");
                return;
            }

            UIUtility.PlayClickSfx();
            Time.timeScale = 1f;   // 防止从暂停菜单打开时带回暂停
            SceneManager.LoadScene(info.BuildIndex);
        }
    }
}
