using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StarEco.Level;

namespace StarEco.UI
{
    /// <summary>
    /// 主菜单面板（M7 R3）：Title 场景根面板。
    /// 按钮：开始新游戏 / 继续游戏 / 关卡选择 / 图鉴 / 设置 / 退出游戏。
    /// </summary>
    public class MainMenuPanel : UIPanel
    {
        public const string PanelId = "MainMenu";

        public override string PanelName => PanelId;

        private Button _continueButton;
        private Text _versionText;

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

        private void Start()
        {
            // 与 HUD 同款时序兜底：Awake 早于 UIManager.Awake 时注册会丢；
            // Start 也可能早于 UIManager.Start 的兜底扫描，故这里先幂等注册再打开。
            if (UIManager.HasInstance)
            {
                UIManager.Instance.Register(this);
                UIManager.Instance.Open(PanelId);
            }

            PlayTitleBgm();
        }

        protected override void OnOpen()
        {
            RefreshContinueState();
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("MainMenuCanvas", transform, 20);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            // 背景：AI 生成的主菜单插画（素材缺失时退回纯色底，保证可运行）
            var bg = UIUtility.CreateImage("Background", root, new Color(0.06f, 0.10f, 0.09f, 1f));
            UIUtility.Stretch(bg.GetComponent<RectTransform>());
            var bgSprite = MenuArt.Background;
            if (bgSprite != null)
            {
                var bgImage = bg.GetComponent<Image>();
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Simple;
                bgImage.color = Color.white;
                bgImage.preserveAspect = false;
            }

            // 压暗层：保证标题与按钮在任何底图上都清晰可读
            var dim = UIUtility.CreateImage("ReadabilityDim", root, new Color(0f, 0f, 0f, 0.28f));
            UIUtility.Stretch(dim.GetComponent<RectTransform>());

            // 标题牌匾：AI 生成的装饰边框，垫在标题文字之下
            var plateSprite = MenuArt.TitlePlate;
            if (plateSprite != null)
            {
                var plate = UIUtility.CreateImage("TitlePlate", root, Color.white);
                var plateImage = plate.GetComponent<Image>();
                plateImage.sprite = plateSprite;
                plateImage.type = Image.Type.Simple;

                var plateRt = plate.GetComponent<RectTransform>();
                plateRt.anchorMin = new Vector2(0.5f, 1f);
                plateRt.anchorMax = new Vector2(0.5f, 1f);
                plateRt.pivot = new Vector2(0.5f, 0.5f);
                plateRt.sizeDelta = new Vector2(700f, 255f);
                plateRt.anchoredPosition = new Vector2(0f, -130f);
            }

            var title = UIUtility.CreateText("Title", root, "星际环保", 96,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            // 注意：Text 的 verticalOverflow 为 Truncate，96 号字整行高约 136px，
            // 行框必须留够高度，否则整行被丢弃、标题完全不显示（曾踩）。
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(900f, 170f);
            titleRt.anchoredPosition = new Vector2(0f, -45f);

            var subtitle = UIUtility.CreateText("Subtitle", root, "—— 动作冒险 · 环保教育 ——", 32,
                TextAnchor.MiddleCenter, UIUtility.AccentGreen);
            var subtitleRt = subtitle.GetComponent<RectTransform>();
            subtitleRt.anchorMin = new Vector2(0.5f, 1f);
            subtitleRt.anchorMax = new Vector2(0.5f, 1f);
            subtitleRt.pivot = new Vector2(0.5f, 1f);
            subtitleRt.sizeDelta = new Vector2(900f, 50f);
            subtitleRt.anchoredPosition = new Vector2(0f, -265f);

            // 按钮列
            var column = UIUtility.CreateObject("Buttons", root);
            var columnRt = column.GetComponent<RectTransform>();
            columnRt.anchorMin = new Vector2(0.5f, 0.5f);
            columnRt.anchorMax = new Vector2(0.5f, 0.5f);
            columnRt.pivot = new Vector2(0.5f, 0.5f);
            columnRt.sizeDelta = new Vector2(360f, 480f);
            columnRt.anchoredPosition = new Vector2(0f, -40f);

            var startButton = UIUtility.CreateButton("StartButton", column.transform, "开始新游戏",
                new Vector2(340f, 74f), new Vector2(0f, 200f));
            startButton.onClick.AddListener(OnStartClicked);

            _continueButton = UIUtility.CreateButton("ContinueButton", column.transform, "继续游戏",
                new Vector2(340f, 74f), new Vector2(0f, 110f));
            _continueButton.onClick.AddListener(OnContinueClicked);

            var levelSelectButton = UIUtility.CreateButton("LevelSelectButton", column.transform, "关卡选择",
                new Vector2(340f, 74f), new Vector2(0f, 20f));
            levelSelectButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeOpen(LevelSelectPanel.PanelId);
            });

            var codexButton = UIUtility.CreateButton("CodexButton", column.transform, "环保图鉴",
                new Vector2(340f, 74f), new Vector2(0f, -70f));
            codexButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeOpen(CodexPanel.PanelId);
            });

            var settingsButton = UIUtility.CreateButton("SettingsButton", column.transform, "设置",
                new Vector2(340f, 74f), new Vector2(0f, -160f));
            settingsButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeOpen(SettingsPanel.PanelId);
            });

            var quitButton = UIUtility.CreateButton("QuitButton", column.transform, "退出游戏",
                new Vector2(340f, 74f), new Vector2(0f, -250f));
            quitButton.onClick.AddListener(OnQuitClicked);

            // 按钮皮肤：换成 AI 生成的玻璃绿贴图（素材缺失时保留原纯色按钮）
            foreach (var button in column.GetComponentsInChildren<Button>(true))
            {
                MenuArt.ApplyButtonSkin(button);
            }

            _versionText = UIUtility.CreateText("Version", root,
                "v1.0 · Unity 2022.3", 22, TextAnchor.LowerRight,
                new Color(1f, 1f, 1f, 0.35f));
            var versionRt = _versionText.GetComponent<RectTransform>();
            versionRt.anchorMin = new Vector2(1f, 0f);
            versionRt.anchorMax = new Vector2(1f, 0f);
            versionRt.pivot = new Vector2(1f, 0f);
            versionRt.sizeDelta = new Vector2(420f, 34f);
            versionRt.anchoredPosition = new Vector2(-16f, 10f);
        }

        // ===== 交互 =====

        private void OnStartClicked()
        {
            UIUtility.PlayClickSfx();

            // 新游戏：清进度与图鉴收集、清结算，回到第一关
            LevelSaveService.ClearProgress();
            CodexService.ResetProgress();
            LevelSaveService.ClearResult();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneNames.Level01);
        }

        private void OnContinueClicked()
        {
            UIUtility.PlayClickSfx();

            // M9：三关均建成，继续游戏 = 进入已解锁的最高关卡（通关即解锁下一关）
            var progress = LevelSaveService.LoadProgress();
            int target = Mathf.Clamp(progress.UnlockedLevelIndex, 1, 3);
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneNames.ForLevel(target));
            Debug.Log($"[MainMenu] 继续游戏：进入第 {target} 关（{SceneNames.ForLevel(target)}）。");
        }

        private void OnQuitClicked()
        {
            UIUtility.PlayClickSfx();
#if UNITY_EDITOR
            Debug.Log("[MainMenu] 退出游戏（编辑器模式下不真正退出）。");
#else
            Application.Quit();
#endif
        }

        private void RefreshContinueState()
        {
            if (_continueButton == null)
            {
                return;
            }

            bool hasProgress = LevelSaveService.HasProgress();
            _continueButton.interactable = hasProgress;
        }

        private void PlayTitleBgm()
        {
            // BGM 素材缺失时 AudioManager 内部静默跳过（M10）
            if (StarEco.Framework.AudioManager.HasInstance)
            {
                StarEco.Framework.AudioManager.Instance.PlayTitleBgm();
            }
        }
    }

    /// <summary>场景名常量（避免散落字符串）。</summary>
    public static class SceneNames
    {
        public const string Title = "Title";
        public const string Level01 = "Level01";
        public const string Level02 = "Level02";
        public const string Level03 = "Level03";

        /// <summary>关卡序号（1 起）→ 场景名。LevelIndex 与 Build Settings 索引一致（Title=0）。</summary>
        public static string ForLevel(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 1, 3))
            {
                case 2: return Level02;
                case 3: return Level03;
                default: return Level01;
            }
        }
    }

    /// <summary>
    /// 主菜单美术资源入口（M11）：统一从 Resources 加载 AI 生成的贴图并缓存。
    /// 素材缺失时返回 null，调用方回退到原有纯色表现，保证界面始终可运行。
    /// </summary>
    public static class MenuArt
    {
        private const string Root = "UI/Menu/";

        private static Sprite _background;
        private static Sprite _titlePlate;
        private static Sprite _button;
        private static Sprite _panelFrame;
        private static Sprite _closeButton;
        private static bool _loaded;

        /// <summary>主菜单全屏背景插画。</summary>
        public static Sprite Background
        {
            get { EnsureLoaded(); return _background; }
        }

        /// <summary>标题装饰牌匾。</summary>
        public static Sprite TitlePlate
        {
            get { EnsureLoaded(); return _titlePlate; }
        }

        /// <summary>按钮玻璃底板。</summary>
        public static Sprite Button
        {
            get { EnsureLoaded(); return _button; }
        }

        /// <summary>弹窗面板九宫格底板（内腔已烘焙玻璃底色）。</summary>
        public static Sprite PanelFrame
        {
            get { EnsureLoaded(); return _panelFrame; }
        }

        /// <summary>右上角圆形 X 关闭按钮图标。</summary>
        public static Sprite CloseIcon
        {
            get { EnsureLoaded(); return _closeButton; }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _background = Resources.Load<Sprite>(Root + "menu_bg");
            _titlePlate = Resources.Load<Sprite>(Root + "menu_title_plate");
            _button = Resources.Load<Sprite>(Root + "menu_button");
            _panelFrame = Resources.Load<Sprite>(Root + "menu_panel");
            _closeButton = Resources.Load<Sprite>(Root + "menu_close");
        }

        /// <summary>
        /// 把按钮底图换成贴图，并重设颜色渐变（贴图正常显示为原色，
        /// 悬停提亮、按下压暗、禁用转灰）。素材缺失时保持原纯色按钮不动。
        /// </summary>
        public static void ApplyButtonSkin(Button button)
        {
            if (button == null)
            {
                return;
            }

            // 运行时 AddComponent<Button> 不会自动填 targetGraphic（Reset 仅编辑器执行），
            // 这里补上并顺带修好悬停/按下的颜色渐变。
            var image = button.targetGraphic as Image;
            if (image == null)
            {
                image = button.GetComponent<Image>();
                button.targetGraphic = image;
            }

            var sprite = Button;
            if (sprite == null || image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.disabledColor = new Color(0.42f, 0.42f, 0.45f, 1f);
            button.colors = colors;
        }

        /// <summary>
        /// 创建一块带边框皮肤的弹窗面板，返回的 RectTransform 直接作为内容父节点。
        /// 底板贴图已烘焙玻璃内腔，调用方不必再叠纯色背景；素材缺失时回退 PanelBg。
        /// </summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var panel = UIUtility.CreateObject(name, parent);
            UIUtility.CenterAnchor(panel, size, position);

            var skin = UIUtility.CreateImage("Skin", panel.transform, Color.white);
            UIUtility.Stretch(skin.GetComponent<RectTransform>());

            var sprite = PanelFrame;
            if (sprite != null)
            {
                skin.sprite = sprite;
                skin.type = Image.Type.Sliced;
            }
            else
            {
                skin.color = UIUtility.PanelBg;
            }

            return panel.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 在面板右上角挂一个圆形 X 关闭按钮。点击先播 UI 音效，再执行回调。
        /// 素材缺失时回退为纯色圆角块 + 「×」文字，保证始终可关闭。
        /// </summary>
        public static Button CreateCloseButton(RectTransform panel, UnityEngine.Events.UnityAction onClose)
        {
            var go = UIUtility.CreateObject("CloseButton", panel);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(56f, 56f);
            rt.anchoredPosition = new Vector2(-64f, -64f);

            var image = go.AddComponent<Image>();
            image.raycastTarget = true;

            var sprite = CloseIcon;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
            }
            else
            {
                image.color = UIUtility.ButtonNormal;
                var fallback = UIUtility.CreateText("Fallback", rt, "×", 34,
                    TextAnchor.MiddleCenter, UIUtility.TextMain);
                UIUtility.Stretch(fallback.GetComponent<RectTransform>());
            }

            // 运行时 AddComponent<Button> 不会自动填 targetGraphic，必须手动指定，
            // 否则悬停/按下的颜色渐变不生效。
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.20f, 1.20f, 1.20f, 1f);
            colors.pressedColor = new Color(0.70f, 0.70f, 0.70f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            button.colors = colors;

            button.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                onClose?.Invoke();
            });

            return button;
        }
    }
}
