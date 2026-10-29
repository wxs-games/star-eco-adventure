using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StarEco.UI
{
    /// <summary>
    /// 程序化 uGUI 公共工具（M7 R2）：Canvas/EventSystem 保活、字体、颜色常量与控件工厂。
    /// 从 M6 LevelResultUI 的同类私有代码抽离，行为保持一致。
    /// </summary>
    public static class UIUtility
    {
        /// <summary>
        /// 世界空间 Canvas 的像素→世界换算比例。
        /// 关卡相机为 orthographicSize=4.5，视野约 16×9 世界单位；
        /// 400px 宽的气泡按 0.01 换算即4 单位，约占屏宽 1/4，符合观感。
        ///
        /// 注意：RenderMode.WorldSpace 下 CanvasScaler 不生效（scaleFactor 被忽略），
        /// 早期版本误以为 0.01 的 scaleFactor 会生效，导致 400×120 的RectTransform
        /// 被当作 400×120 **世界单位**渲染，一个气泡横跨 25 屏，表现为斜穿画面的白色带。
        /// 因此必须落在 transform.localScale 上。
        /// </summary>
        public const float WorldUiScale = 0.01f;

        /// <summary>
        /// 把新建的 Canvas 配成世界空间 UI：设置 renderMode 与像素→世界缩放。
        /// RectTransform 的 sizeDelta 等仍按「像素」填写，由本方法统一换算。
        /// </summary>
        public static Canvas CreateWorldCanvas(GameObject canvasGo, Transform parent, Vector3 localPosition)
        {
            canvasGo.transform.SetParent(parent, false);
            canvasGo.transform.localPosition = localPosition;
            canvasGo.transform.localScale = Vector3.one * WorldUiScale;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // WorldSpace 下 CanvasScaler 无效, 移除以免误导后续维护者
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                Object.DestroyImmediate(scaler);
            }

            return canvas;
        }
        /// <summary>面板底色（半透明深蓝紫）。</summary>
        public static readonly Color PanelBg = new Color(0.10f, 0.10f, 0.18f, 0.93f);

        /// <summary>全屏遮罩色。</summary>
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.72f);

        /// <summary>主文字白。</summary>
        public static readonly Color TextMain = Color.white;

        /// <summary>次要文字（浅灰绿）。</summary>
        public static readonly Color TextBody = new Color(0.90f, 0.95f, 0.90f);

        /// <summary>强调绿。</summary>
        public static readonly Color AccentGreen = new Color(0.49f, 0.99f, 0.60f);

        /// <summary>警示红。</summary>
        public static readonly Color WarnRed = new Color(1f, 0.42f, 0.42f);

        /// <summary>禁用灰。</summary>
        public static readonly Color DisabledGray = new Color(0.53f, 0.53f, 0.53f);

        /// <summary>按钮常态色。</summary>
        public static readonly Color ButtonNormal = new Color(0.22f, 0.35f, 0.28f, 0.95f);

        /// <summary>按钮高亮色。</summary>
        public static readonly Color ButtonHighlight = new Color(0.32f, 0.50f, 0.38f, 1f);

        /// <summary>按钮按下色。</summary>
        public static readonly Color ButtonPressed = new Color(0.16f, 0.26f, 0.20f, 1f);

        /// <summary>按钮禁用色。</summary>
        public static readonly Color ButtonDisabled = new Color(0.25f, 0.25f, 0.27f, 0.9f);

        /// <summary>
        /// UI 字体。优先加载项目内置的中文字体 Noto Sans SC（SIL OFL 1.1，可随项目分发）；
        /// 回退到 Unity 内置 LegacyRuntime.ttf（无 CJK 字形，中文会显示为方块）。
        /// 字体资源需放在 Assets/Resources/Fonts/ 下才能被 Resources.Load 命中。
        /// </summary>
        public static Font BuiltinFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
                    if (_font == null)
                    {
                        Debug.LogWarning(
                            "[UIUtility] 未找到 Assets/Resources/Fonts/NotoSansSC-Regular，" +
                            "回退内置 LegacyRuntime.ttf —— 中文将渲染为方块（乱码）。");
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }

                    if (_font == null)
                    {
                        Debug.LogError("[UIUtility] 未取得任何字体，UI 文字将不可见。");
                    }
                }

                return _font;
            }
        }

        private static Font _font;

        /// <summary>
        /// 纯白 1×1 Sprite。uGUI 的 Image 在 Type.Filled 下必须挂 Sprite，
        /// 否则 fillAmount 不生效、进度条永远画满（血条/能量条类 UI 的常见坑）。
        /// </summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var tex = Texture2D.whiteTexture;
                    _whiteSprite = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    _whiteSprite.name = "UIUtilityWhite";
                }

                return _whiteSprite;
            }
        }

        private static Sprite _whiteSprite;

        /// <summary>参考分辨率（16:9）。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>确保场景中存在 EventSystem（否则按钮无响应）。幂等。</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Object.DontDestroyOnLoad(go);
            Debug.Log("[UIUtility] 场景缺少 EventSystem，已自动创建。");
        }

        /// <summary>创建 ScreenSpaceOverlay Canvas（含 Scaler 与 Raycaster）。</summary>
        public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>创建 UI 空物体（RectTransform，UI 层）。</summary>
        public static GameObject CreateObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>全屏拉伸锚点。</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>居中锚点 + sizeDelta + 位置。</summary>
        public static RectTransform CenterAnchor(GameObject go, Vector2 size, Vector2 pos)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>左上锚点。</summary>
        public static RectTransform TopLeftAnchor(GameObject go, Vector2 size, Vector2 pos)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>底部居中锚点。</summary>
        public static RectTransform BottomCenterAnchor(GameObject go, Vector2 size, Vector2 pos)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>创建 Text。</summary>
        public static Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = CreateObject(name, parent);
            var text = go.AddComponent<Text>();
            text.font = BuiltinFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color ?? TextMain;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>创建 Image。</summary>
        public static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = CreateObject(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>创建水平填充条（背景 + 填充层）。返回填充 Image。</summary>
        public static Image CreateBar(string name, Transform parent, Vector2 size, Vector2 pos,
            Color bgColor, Color fillColor, out GameObject barGo)
        {
            barGo = CreateObject(name, parent);
            CenterAnchor(barGo, size, pos);

            var bg = CreateImage("Background", barGo.transform, bgColor);
            Stretch(bg.GetComponent<RectTransform>());

            var fillGo = CreateObject("Fill", barGo.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            return fill;
        }

        /// <summary>创建按钮（含文字标签），返回 Button。</summary>
        public static Button CreateButton(string name, Transform parent, string label, Vector2 size, Vector2 pos,
            int fontSize = 30, bool interactable = true)
        {
            var go = CreateObject(name, parent);
            CenterAnchor(go, size, pos);

            var image = go.AddComponent<Image>();
            image.color = ButtonNormal;

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = ButtonNormal;
            colors.highlightedColor = ButtonHighlight;
            colors.pressedColor = ButtonPressed;
            colors.disabledColor = ButtonDisabled;
            button.colors = colors;
            button.interactable = interactable;

            var text = CreateText("Label", go.transform, label, fontSize);
            Stretch(text.GetComponent<RectTransform>());

            return button;
        }

        /// <summary>把秒格式化为 mm:ss。</summary>
        public static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(Mathf.Max(0f, seconds) / 60f);
            int s = Mathf.FloorToInt(Mathf.Max(0f, seconds) % 60f);
            return string.Format("{0:00}:{1:00}", m, s);
        }

        /// <summary>播放 UI 音效（资源缺失时静默跳过，不报错）。</summary>
        public static void PlayClickSfx()
        {
            var audio = StarEco.Framework.AudioManager.HasInstance
                ? StarEco.Framework.AudioManager.Instance
                : null;
            if (audio == null)
            {
                return;
            }

            audio.PlaySfx(StarEco.Framework.SfxId.UIClick);
        }
    }
}
