using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarEco.Level;

namespace StarEco.UI
{
    /// <summary>
    /// 图鉴面板（M7 R8）：网格展示全部知识卡片。
    /// 已收集：标题 + 正文；未收集："???" 占位 + 剪影。
    /// </summary>
    public class CodexPanel : UIPanel
    {
        public const string PanelId = "Codex";

        public override string PanelName => PanelId;

        private readonly List<Text> _titleTexts = new List<Text>();
        private readonly List<Text> _bodyTexts = new List<Text>();
        private Text _progressText;
        private float _scroll;

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
            Refresh();
        }

        protected override void OnUpdate(float dt)
        {
            if (_scroll == 0f)
            {
                return;
            }

            // 简易滚轮翻页（ScrollRect 手动驱动）
            var scrollRect = Root != null ? Root.GetComponentInChildren<ScrollRect>(true) : null;
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                    scrollRect.verticalNormalizedPosition + _scroll * dt * 2f);
            }
        }

        /// <summary>HUD/触发器的滚轮输入入口（由 UIManager 之外不便取，暂留扩展位）。</summary>
        public void AddScroll(float delta)
        {
            _scroll = delta;
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("CodexCanvas", transform, 60);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            var bg = UIUtility.CreateImage("Background", root, UIUtility.Overlay);
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var panel = MenuArt.CreatePanel(root, "Panel", new Vector2(1240f, 920f), Vector2.zero);

            var title = UIUtility.CreateText("Title", panel, "环保图鉴", 48,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(800f, 96f);
            titleRt.anchoredPosition = new Vector2(0f, -28f);

            _progressText = UIUtility.CreateText("Progress", panel, "已收集 0/8", 26,
                TextAnchor.MiddleCenter, UIUtility.AccentGreen);
            var progressRt = _progressText.GetComponent<RectTransform>();
            progressRt.anchorMin = new Vector2(0.5f, 1f);
            progressRt.anchorMax = new Vector2(0.5f, 1f);
            progressRt.pivot = new Vector2(0.5f, 1f);
            progressRt.sizeDelta = new Vector2(500f, 44f);
            progressRt.anchoredPosition = new Vector2(0f, -118f);

            // 滚动视图
            var scrollGo = UIUtility.CreateObject("Scroll", panel);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRt.pivot = new Vector2(0.5f, 0.5f);
            scrollRt.sizeDelta = new Vector2(1080f, 580f);
            scrollRt.anchoredPosition = new Vector2(0f, -35f);

            var scrollBg = UIUtility.CreateImage("Background", scrollGo.transform,
                new Color(0f, 0f, 0f, 0.38f));
            UIUtility.Stretch(scrollBg.GetComponent<RectTransform>());

            var viewport = UIUtility.CreateObject("Viewport", scrollGo.transform);
            UIUtility.Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();

            var content = UIUtility.CreateObject("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var cards = CodexService.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                CreateCardRow(content.transform, i, cards[i]);
            }

            var sizeFitter = content.AddComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            var backButton = UIUtility.CreateButton("BackButton", panel, "返回",
                new Vector2(240f, 70f), new Vector2(0f, -375f));
            backButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeClose(PanelId);
            });

            MenuArt.CreateCloseButton(panel, () => UIManager.SafeClose(PanelId));
        }

        private void CreateCardRow(Transform parent, int index, KnowledgeCardData card)
        {
            var row = UIUtility.CreateObject("CardRow_" + index, parent);
            row.AddComponent<LayoutElement>().preferredHeight = 110f;

            var rowBg = UIUtility.CreateImage("Background", row.transform,
                new Color(1f, 1f, 1f, 0.05f));
            UIUtility.Stretch(rowBg.GetComponent<RectTransform>());

            var levelTag = UIUtility.CreateText("LevelTag", row.transform, $"第{card.LevelIndex}关", 22,
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.45f));
            var levelTagRt = levelTag.GetComponent<RectTransform>();
            levelTagRt.anchorMin = new Vector2(0f, 0.5f);
            levelTagRt.anchorMax = new Vector2(0f, 0.5f);
            levelTagRt.pivot = new Vector2(0f, 0.5f);
            levelTagRt.sizeDelta = new Vector2(110f, 90f);
            levelTagRt.anchoredPosition = new Vector2(10f, 0f);

            var titleText = UIUtility.CreateText("Title", row.transform, card.Title, 30,
                TextAnchor.UpperLeft, UIUtility.TextMain);
            var titleRt = titleText.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.sizeDelta = new Vector2(-150f, 44f);
            titleRt.anchoredPosition = new Vector2(130f, -6f);

            var bodyText = UIUtility.CreateText("Body", row.transform, card.Body, 20,
                TextAnchor.UpperLeft, UIUtility.TextBody);
            var bodyRt = bodyText.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.pivot = new Vector2(0f, 0.5f);
            bodyRt.offsetMin = new Vector2(130f, 8f);
            bodyRt.offsetMax = new Vector2(-16f, -40f);

            _titleTexts.Add(titleText);
            _bodyTexts.Add(bodyText);
        }

        private void Refresh()
        {
            var cards = CodexService.Cards;
            int collected = 0;

            for (int i = 0; i < cards.Count && i < _titleTexts.Count; i++)
            {
                bool has = CodexService.IsCollected(cards[i].Id);
                if (has)
                {
                    collected++;
                    _titleTexts[i].text = cards[i].Title;
                    _bodyTexts[i].text = cards[i].Body;
                    _titleTexts[i].color = UIUtility.AccentGreen;
                    _bodyTexts[i].color = UIUtility.TextBody;
                }
                else
                {
                    _titleTexts[i].text = "???";
                    _bodyTexts[i].text = "尚未收集：在关卡中寻找发光的知识卡片。";
                    _titleTexts[i].color = UIUtility.DisabledGray;
                    _bodyTexts[i].color = UIUtility.DisabledGray;
                }
            }

            if (_progressText != null)
            {
                _progressText.text = $"已收集 {collected}/{cards.Count}";
            }
        }
    }
}
