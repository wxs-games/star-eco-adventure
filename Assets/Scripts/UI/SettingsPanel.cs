using UnityEngine;
using UnityEngine.UI;
using StarEco.Framework;

namespace StarEco.UI
{
    /// <summary>
    /// 设置面板（M7 R5）：BGM/SFX 音量滑条 + 画质三档 + 返回。
    /// 音量实时调 AudioManager；关闭时写 settings 槽位（GameSettingsData）。
    /// </summary>
    public class SettingsPanel : UIPanel
    {
        public const string PanelId = "Settings";

        public override string PanelName => PanelId;

        private Slider _bgmSlider;
        private Slider _sfxSlider;
        private Text _qualityLabel;
        private int _qualityIndex = 1;

        // 从暂停菜单打开时返回暂停而非直接关闭
        private bool _returnToPause;

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

        /// <summary>由调用方设置：返回时是否重新打开暂停菜单。</summary>
        public void SetReturnTarget(bool toPause)
        {
            _returnToPause = toPause;
        }

        protected override void OnOpen()
        {
            LoadAndFill();
        }

        protected override void OnClose()
        {
            Save();
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("SettingsCanvas", transform, 60);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            var bg = UIUtility.CreateImage("Background", root, UIUtility.Overlay);
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var panel = MenuArt.CreatePanel(root, "Panel", new Vector2(720f, 600f), Vector2.zero);

            var title = UIUtility.CreateText("Title", panel, "设置", 44,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(600f, 84f);
            titleRt.anchoredPosition = new Vector2(0f, -18f);

            // BGM 音量
            CreateSliderRow(panel, "BgmRow", "音乐音量", 140f, out _bgmSlider, value =>
            {
                if (AudioManager.HasInstance)
                {
                    AudioManager.Instance.SetBgmVolume(value);
                }
            });

            // SFX 音量
            CreateSliderRow(panel, "SfxRow", "音效音量", 45f, out _sfxSlider, value =>
            {
                if (AudioManager.HasInstance)
                {
                    AudioManager.Instance.SetSfxVolume(value);
                }
            });

            // 画质
            var qualityRow = UIUtility.CreateObject("QualityRow", panel);
            var qualityRt = qualityRow.GetComponent<RectTransform>();
            qualityRt.anchorMin = new Vector2(0.5f, 0.5f);
            qualityRt.anchorMax = new Vector2(0.5f, 0.5f);
            qualityRt.pivot = new Vector2(0.5f, 0.5f);
            qualityRt.sizeDelta = new Vector2(560f, 70f);
            qualityRt.anchoredPosition = new Vector2(0f, -50f);

            var qualityLabel = UIUtility.CreateText("Label", qualityRow.transform, "画质", 28,
                TextAnchor.MiddleLeft, UIUtility.TextBody);
            var qualityLabelRt = qualityLabel.GetComponent<RectTransform>();
            qualityLabelRt.anchorMin = new Vector2(0f, 0.5f);
            qualityLabelRt.anchorMax = new Vector2(0f, 0.5f);
            qualityLabelRt.pivot = new Vector2(0f, 0.5f);
            qualityLabelRt.sizeDelta = new Vector2(120f, 60f);
            qualityLabelRt.anchoredPosition = Vector2.zero;

            var qualityButton = UIUtility.CreateButton("QualityButton", qualityRow.transform,
                "中", new Vector2(220f, 60f), new Vector2(240f, 0f));
            _qualityLabel = qualityButton.GetComponentInChildren<Text>();
            qualityButton.onClick.AddListener(() =>
            {
                _qualityIndex = (_qualityIndex + 1) % 3;
                ApplyQuality();
            });

            // 返回
            var backButton = UIUtility.CreateButton("BackButton", panel, "返回",
                new Vector2(240f, 70f), new Vector2(0f, -195f));
            backButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeClose(PanelId);

                if (_returnToPause)
                {
                    UIManager.SafeOpen(PausePanel.PanelId);
                }
            });

            MenuArt.CreateCloseButton(panel, () =>
            {
                UIManager.SafeClose(PanelId);

                if (_returnToPause)
                {
                    UIManager.SafeOpen(PausePanel.PanelId);
                }
            });
        }

        private void CreateSliderRow(Transform parent, string name, string label, float y,
            out Slider slider, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var row = UIUtility.CreateObject(name, parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(560f, 70f);
            rt.anchoredPosition = new Vector2(0f, y);

            var labelText = UIUtility.CreateText("Label", row.transform, label, 28,
                TextAnchor.MiddleLeft, UIUtility.TextBody);
            var labelRt = labelText.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0f, 0.5f);
            labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.sizeDelta = new Vector2(160f, 60f);
            labelRt.anchoredPosition = Vector2.zero;

            var sliderGo = UIUtility.CreateObject("Slider", row.transform);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0f, 0.5f);
            sliderRt.anchorMax = new Vector2(0f, 0.5f);
            sliderRt.pivot = new Vector2(0f, 0.5f);
            sliderRt.sizeDelta = new Vector2(360f, 40f);
            sliderRt.anchoredPosition = new Vector2(170f, 0f);

            var bg = UIUtility.CreateImage("Background", sliderGo.transform,
                new Color(0f, 0f, 0f, 0.6f));
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var fillArea = UIUtility.CreateObject("Fill Area", sliderGo.transform);
            UIUtility.Stretch(fillArea.GetComponent<RectTransform>());

            var fill = UIUtility.CreateImage("Fill", fillArea.transform, UIUtility.AccentGreen);
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            var handleArea = UIUtility.CreateObject("Handle Slide Area", sliderGo.transform);
            UIUtility.Stretch(handleArea.GetComponent<RectTransform>());

            var handle = UIUtility.CreateImage("Handle", handleArea.transform, Color.white);
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(28f, 44f);

            slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.onValueChanged.AddListener(onChanged);
        }

        // ===== 数据 =====

        private void LoadAndFill()
        {
            var data = SaveSystem.Load<GameSettingsData>(SettingsSlotName);

            if (_bgmSlider != null)
            {
                _bgmSlider.value = Mathf.Clamp01(data.bgmVolume);
            }

            if (_sfxSlider != null)
            {
                _sfxSlider.value = Mathf.Clamp01(data.sfxVolume);
            }

            _qualityIndex = Mathf.Clamp(data.qualityLevel, 0, 2);
            ApplyQualityLabel();

            _returnToPause = false;
        }

        private void Save()
        {
            var data = new GameSettingsData
            {
                bgmVolume = _bgmSlider != null ? _bgmSlider.value : 0.8f,
                sfxVolume = _sfxSlider != null ? _sfxSlider.value : 1f,
                qualityLevel = _qualityIndex
            };

            SaveSystem.Save(SettingsSlotName, data);
        }

        private void ApplyQuality()
        {
            ApplyQualityLabel();

            // 画质三档映射：0=低 1=中 2=高（Unity 默认 Fast/Simple/Good/Beautiful/Ultra 中取前三档近似）
            QualitySettings.SetQualityLevel(Mathf.Clamp(_qualityIndex, 0, QualitySettings.names.Length - 1), true);
        }

        private void ApplyQualityLabel()
        {
            if (_qualityLabel != null)
            {
                _qualityLabel.text = _qualityIndex switch
                {
                    0 => "低",
                    2 => "高",
                    _ => "中"
                };
            }
        }

        private const string SettingsSlotName = "settings";
    }
}
