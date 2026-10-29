using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StarEco.Framework;
using StarEco.Level;
using StarEco.Level.Events;
using StarEco.Player;

namespace StarEco.UI
{
    /// <summary>
    /// 结算面板（M7 R7）：订阅 M6 的 LevelCompletedEvent/LevelFailedEvent。
    /// 完成：标题/关卡名/净化度/用时/击杀/拾取 + 评级（S/A/B/C 与 ★ 并存）。
    /// 失败：失败原因 + 重新开始/返回主菜单。
    /// 下一关按钮：result.LevelIndex < UnlockedLevelIndex 时显示，加载下一关场景（M9 起三关均建成）。
    /// </summary>
    public class ResultPanel : UIPanel
    {
        public const string PanelId = "Result";

        public override string PanelName => PanelId;

        private Text _titleText;
        private Text _bodyText;
        private Button _restartButton;
        private Button _nextButton;
        private Button _titleButton;
        private int _resultLevelIndex = 1;

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
            EventCenter.Unsubscribe<LevelCompletedEvent>(OnCompleted);
            EventCenter.Unsubscribe<LevelFailedEvent>(OnFailed);

            if (UIManager.HasInstance)
            {
                UIManager.Instance.Unregister(this);
            }
        }

        private void Start()
        {
            EventCenter.Subscribe<LevelCompletedEvent>(OnCompleted);
            EventCenter.Subscribe<LevelFailedEvent>(OnFailed);
        }

        // ===== 事件 =====

        private void OnCompleted(LevelCompletedEvent evt)
        {
            var result = evt.Result;
            _resultLevelIndex = result != null ? result.LevelIndex : 1;

            if (_titleText != null)
            {
                _titleText.text = "关卡完成";
                _titleText.color = UIUtility.AccentGreen;
            }

            if (_bodyText != null && result != null)
            {
                var cfg = LevelManager.HasInstance ? LevelManager.Instance.Config : null;
                string levelName = cfg != null ? cfg.LevelName : "关卡";

                _bodyText.text =
                    $"关卡：{levelName}\n" +
                    $"净化度：{result.PurityPercent:F0}%\n" +
                    $"用时：{UIUtility.FormatTime(result.ElapsedSeconds)}\n" +
                    $"击杀：{result.KillCount}    拾取：{result.PickupCount}\n" +
                    $"评级：{GradeText(result.PurityPercent)}    {StarsText(result.Stars)}";
            }

            if (_restartButton != null)
            {
                _restartButton.gameObject.SetActive(true);
            }

            if (_nextButton != null)
            {
                bool hasNext = LevelSaveService.LoadProgress().UnlockedLevelIndex > _resultLevelIndex;
                _nextButton.gameObject.SetActive(hasNext);
            }

            if (_titleButton != null)
            {
                _titleButton.gameObject.SetActive(true);
            }

            UIManager.SafeOpen(PanelId);
        }

        private void OnFailed(LevelFailedEvent evt)
        {
            if (_titleText != null)
            {
                _titleText.text = "关卡失败";
                _titleText.color = UIUtility.WarnRed;
            }

            if (_bodyText != null)
            {
                _bodyText.text =
                    "你倒下了……\n" +
                    $"原因：{evt.Reason}\n" +
                    "净化行动未能完成，再试一次吧。";
            }

            // 失败局面：只保留「重新开始（重开本关）」与「返回主菜单」两个出口
            if (_restartButton != null)
            {
                _restartButton.gameObject.SetActive(true);
            }

            if (_nextButton != null)
            {
                _nextButton.gameObject.SetActive(false);
            }

            if (_titleButton != null)
            {
                _titleButton.gameObject.SetActive(true);
            }

            UIManager.SafeOpen(PanelId);
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("ResultCanvas", transform, 100);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            var bg = UIUtility.CreateImage("Background", root, UIUtility.Overlay);
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            _titleText = UIUtility.CreateText("Title", root, string.Empty, 56,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            var titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(900f, 90f);
            titleRt.anchoredPosition = new Vector2(0f, -60f);

            _bodyText = UIUtility.CreateText("Body", root, string.Empty, 34,
                TextAnchor.MiddleCenter, UIUtility.TextBody);
            var bodyRt = _bodyText.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.pivot = new Vector2(0.5f, 0.5f);
            bodyRt.sizeDelta = new Vector2(900f, 300f);
            bodyRt.anchoredPosition = new Vector2(0f, -20f);
            _bodyText.lineSpacing = 1.5f;

            _restartButton = UIUtility.CreateButton("RestartButton", root, "重新开始",
                new Vector2(260f, 70f), new Vector2(-200f, -330f));
            _restartButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeClose(PanelId);
                EventCenter.Publish(new RestartLevelEvent());
            });

            _nextButton = UIUtility.CreateButton("NextButton", root, "下一关",
                new Vector2(260f, 70f), new Vector2(80f, -330f));
            _nextButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                // M9：三关均建成，LevelIndex 与 Build Settings 索引一致（Title=0）
                int next = _resultLevelIndex + 1;
                int nextBuild = next <= 3 ? next : -1;
                if (nextBuild < 0)
                {
                    Debug.Log($"[ResultPanel] 第 {next} 关尚未建成，敬请期待。");
                    return;
                }

                SceneManager.LoadScene(nextBuild);
            });

            _titleButton = UIUtility.CreateButton("TitleButton", root, "返回主菜单",
                new Vector2(260f, 70f), new Vector2(360f, -330f));
            _titleButton.onClick.AddListener(() =>
            {
                UIUtility.PlayClickSfx();
                UIManager.SafeClose(PanelId);
                SceneManager.LoadScene(SceneNames.Title);
            });

            Root.SetActive(false);
        }

        // ===== 评级 =====

        /// <summary>字母评级：100%→S / ≥60%→A / ≥30%→B / 其他→C。</summary>
        public static string GradeText(float purityPercent)
        {
            if (purityPercent >= 99.9f)
            {
                return "S";
            }

            if (purityPercent >= 60f)
            {
                return "A";
            }

            if (purityPercent >= 30f)
            {
                return "B";
            }

            return "C";
        }

        /// <summary>星级文本：3→★★★ / 2→★★☆ / 1→★☆☆ / 0→☆☆☆。</summary>
        public static string StarsText(int stars)
        {
            switch (Mathf.Clamp(stars, 0, 3))
            {
                case 3: return "★★★";
                case 2: return "★★☆";
                case 1: return "★☆☆";
                default: return "☆☆☆";
            }
        }
    }
}
