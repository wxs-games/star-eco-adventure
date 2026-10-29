using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StarEco.Framework;
using StarEco.Level.Events;
using StarEco.Player;

namespace StarEco.Level
{
    /// <summary>
    /// 关卡结算面板（R7）：完全程序化 uGUI，无美术资源依赖。
    /// 胜负标题 + 关卡名 + 净化度/耗时/击杀/拾取 + 星级文本 + 两个按钮。
    /// 缺 EventSystem 时自动创建（否则按钮无响应）。
    /// </summary>
    public class LevelResultUI : MonoBehaviour
    {
        private Canvas _canvas;
        private Text _bodyText;
        private Text _titleText;
        private Button _restartButton;
        private Button _titleButton;
        private bool _built;

        private void Start()
        {
            BuildIfNeeded();

            EventCenter.Subscribe<LevelCompletedEvent>(OnCompleted);
            EventCenter.Subscribe<LevelFailedEvent>(OnFailed);
        }

        private void OnDestroy()
        {
            EventCenter.Unsubscribe<LevelCompletedEvent>(OnCompleted);
            EventCenter.Unsubscribe<LevelFailedEvent>(OnFailed);
        }

        private void BuildIfNeeded()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            EnsureEventSystem();

            // Canvas
            var canvasGo = new GameObject("LevelResultCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // 统一走 UIUtility.BuiltinFont（中文字体 + 内置字体回退），避免重复硬编码
            var font = StarEco.UI.UIUtility.BuiltinFont;
            if (font == null)
            {
                Debug.LogError("[LevelResultUI] 未取得任何字体，结算面板文字将不可见。");
            }

            // 遮罩
            var bg = CreateUIObject("Background", canvasGo.transform);
            Stretch(bg.GetComponent<RectTransform>());
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.72f);

            // 标题
            var title = CreateUIObject("Title", canvasGo.transform);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 0.78f);
            titleRt.anchorMax = new Vector2(0.5f, 0.78f);
            titleRt.pivot = new Vector2(0.5f, 0.5f);
            titleRt.sizeDelta = new Vector2(900f, 90f);
            titleRt.anchoredPosition = Vector2.zero;

            _titleText = title.AddComponent<Text>();
            _titleText.font = font;
            _titleText.fontSize = 56;
            _titleText.alignment = TextAnchor.MiddleCenter;
            _titleText.color = Color.white;
            _titleText.text = string.Empty;

            // 统计正文
            var body = CreateUIObject("Body", canvasGo.transform);
            var bodyRt = body.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.pivot = new Vector2(0.5f, 0.5f);
            bodyRt.sizeDelta = new Vector2(900f, 320f);
            bodyRt.anchoredPosition = new Vector2(0f, -40f);

            _bodyText = body.AddComponent<Text>();
            _bodyText.font = font;
            _bodyText.fontSize = 34;
            _bodyText.alignment = TextAnchor.MiddleCenter;
            _bodyText.lineSpacing = 1.5f;
            _bodyText.color = new Color(0.9f, 0.95f, 0.9f);
            _bodyText.text = string.Empty;

            // 按钮
            _restartButton = CreateButton("RestartButton", canvasGo.transform, "重新开始", font,
                new Vector2(-160f, -260f));
            _restartButton.onClick.AddListener(() =>
            {
                Hide();
                EventCenter.Publish(new RestartLevelEvent());
            });

            _titleButton = CreateButton("TitleButton", canvasGo.transform, "返回主菜单", font,
                new Vector2(160f, -260f));
            _titleButton.onClick.AddListener(() =>
            {
                Time.timeScale = 1f;
                UnityEngine.SceneManagement.SceneManager.LoadScene(StarEco.UI.SceneNames.Title);
            });

            Hide();
        }

        // ===== 事件 =====

        private void OnCompleted(LevelCompletedEvent evt)
        {
            Show(true, evt.Result);
        }

        private void OnFailed(LevelFailedEvent evt)
        {
            Show(false, null);
        }

        // ===== 显示 =====

        private void Show(bool success, LevelResultData result)
        {
            if (!_built)
            {
                return;
            }

            if (_titleText != null)
            {
                _titleText.text = success ? "关卡完成" : "关卡失败";
                _titleText.color = success ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.5f, 0.5f);
            }

            if (_bodyText != null)
            {
                if (result == null)
                {
                    _bodyText.text = "你倒下了……\n净化行动未能完成。";
                }
                else
                {
                    int minutes = Mathf.FloorToInt(result.ElapsedSeconds / 60f);
                    int seconds = Mathf.FloorToInt(result.ElapsedSeconds % 60f);

                    var cfg = LevelManager.HasInstance ? LevelManager.Instance.Config : null;
                    string levelName = cfg != null ? cfg.LevelName : "第一关";

                    _bodyText.text =
                        $"关卡：{levelName}\n" +
                        $"净化度：{result.PurityPercent:F0}%\n" +
                        $"用时：{minutes:00}:{seconds:00}\n" +
                        $"击杀：{result.KillCount}    拾取：{result.PickupCount}\n" +
                        $"星级：{StarsText(result.Stars)}";
                }
            }

            // 失败局面只保留"重新开始"（R7）
            if (_titleButton != null)
            {
                _titleButton.gameObject.SetActive(success);
            }

            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
            }

            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                player.InputLocked = true;
            }
        }

        private void Hide()
        {
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }
        }

        private static string StarsText(int stars)
        {
            switch (Mathf.Clamp(stars, 0, 3))
            {
                case 3: return "★★★";
                case 2: return "★★☆";
                case 1: return "★☆☆";
                default: return "☆☆☆";
            }
        }

        // ===== 构建辅助 =====

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Debug.Log("[LevelResultUI] 场景缺少 EventSystem，已自动创建（否则按钮无法点击）。");
            DontDestroyOnLoad(go);
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Button CreateButton(string name, Transform parent, string label, Font font, Vector2 anchoredPos)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 70f);
            rt.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.22f, 0.35f, 0.28f, 0.95f);

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.32f, 0.5f, 0.38f, 1f);
            colors.pressedColor = new Color(0.16f, 0.26f, 0.2f, 1f);
            button.colors = colors;

            var textGo = CreateUIObject("Label", go.transform);
            Stretch(textGo.GetComponent<RectTransform>());

            var text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;

            return button;
        }
    }
}
