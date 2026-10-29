using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarEco.Player;

namespace StarEco.UI
{
    /// <summary>
    /// 提示牌触发器（M7 R9）：靠近按 E 打开阅读界面，整段文本分页，E 翻页，末页关闭。
    /// 与 NPC 对话的区别：不分句、无逐字、无头像；数据复用 DialogueData 单 Entry。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SignboardTrigger : MonoBehaviour
    {
        private const int PageCharCount = 80;

        [Tooltip("提示牌数据 Id（Resources/Data/dialogues.json，Entry 只有一条长文本）")]
        [SerializeField] private string _dialogueId = "signboard_plastic";

        [Tooltip("交互键，默认 E")]
        [SerializeField] private KeyCode _interactKey = KeyCode.E;

        [Tooltip("触发区半径（单位 u）")]
        [SerializeField] private float _radius = 2.5f;

        [Tooltip("气泡相对牌体高度")]
        [SerializeField] private float _bubbleHeight = 1.6f;

        private Transform _player;
        private bool _playerInRange;
        private GameObject _bubble;

        // 阅读状态
        private bool _reading;
        private List<string> _pages = new List<string>();
        private int _pageIndex;

        private void OnEnable()
        {
            BuildBubble();
        }

        private void OnDisable()
        {
            if (_bubble != null)
            {
                _bubble.SetActive(false);
            }
        }

        private void Update()
        {
            if (_player == null)
            {
                var pc = FindObjectOfType<PlayerController>();
                if (pc == null)
                {
                    return;
                }

                _player = pc.transform;
            }

            if (_reading)
            {
                HandleReadingInput();
                return;
            }

            float dist = Vector2.Distance(transform.position, _player.position);
            bool inRange = dist <= _radius;

            if (inRange != _playerInRange)
            {
                _playerInRange = inRange;
                if (_bubble != null)
                {
                    _bubble.SetActive(inRange);
                }
            }

            if (inRange && Input.GetKeyDown(_interactKey))
            {
                StartReading();
            }
        }

        private void StartReading()
        {
            var data = DialogueRepository.Find(_dialogueId);
            if (data == null || data.Entries == null || data.Entries.Count == 0)
            {
                Debug.LogError($"[SignboardTrigger] 未找到提示牌数据：{_dialogueId}");
                return;
            }

            var text = data.Entries[0].Text ?? string.Empty;
            _pages = Paginate(text, PageCharCount);
            _pageIndex = 0;
            _reading = true;

            if (_bubble != null)
            {
                _bubble.SetActive(false);
            }

            EnsureReaderUI();
            _readerRoot.SetActive(true);
            RefreshPage();
        }

        private void HandleReadingInput()
        {
            if (!Input.GetKeyDown(_interactKey))
            {
                return;
            }

            _pageIndex++;
            if (_pageIndex >= _pages.Count)
            {
                StopReading();
                return;
            }

            RefreshPage();
        }

        private void StopReading()
        {
            _reading = false;
            if (_readerRoot != null)
            {
                _readerRoot.SetActive(false);
            }
        }

        private void RefreshPage()
        {
            if (_readerText != null && _pageIndex < _pages.Count)
            {
                _readerText.text = _pages[_pageIndex];
            }

            if (_readerPageText != null)
            {
                _readerPageText.text = $"第 {_pageIndex + 1}/{_pages.Count} 页 · 按 E 翻页";
            }
        }

        private static List<string> Paginate(string text, int perPage)
        {
            var pages = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                pages.Add("（牌子上什么都没有）");
                return pages;
            }

            for (int i = 0; i < text.Length; i += perPage)
            {
                int len = Mathf.Min(perPage, text.Length - i);
                pages.Add(text.Substring(i, len));
            }

            return pages;
        }

        // ===== 阅读界面（程序化） =====

        private GameObject _readerRoot;
        private Text _readerText;
        private Text _readerPageText;

        private void EnsureReaderUI()
        {
            if (_readerRoot != null)
            {
                return;
            }

            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("SignboardCanvas", transform, 80);
            _readerRoot = canvas.gameObject;

            var bg = UIUtility.CreateImage("Background", canvas.transform, UIUtility.Overlay);
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var panel = UIUtility.CreateObject("Panel", canvas.transform);
            UIUtility.CenterAnchor(panel, new Vector2(1100f, 560f), Vector2.zero);
            var panelBg = UIUtility.CreateImage("Background", panel.transform, UIUtility.PanelBg);
            UIUtility.Stretch(panelBg.GetComponent<RectTransform>());

            var title = UIUtility.CreateText("Title", panel.transform, "提示牌", 40,
                TextAnchor.MiddleCenter, UIUtility.AccentGreen);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(0f, 70f);
            titleRt.anchoredPosition = new Vector2(0f, -14f);

            _readerText = UIUtility.CreateText("Body", panel.transform, string.Empty, 30,
                TextAnchor.UpperLeft, UIUtility.TextBody);
            var bodyRt = _readerText.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.pivot = new Vector2(0.5f, 0.5f);
            bodyRt.offsetMin = new Vector2(48f, 70f);
            bodyRt.offsetMax = new Vector2(-48f, -90f);
            _readerText.lineSpacing = 1.6f;

            _readerPageText = UIUtility.CreateText("Page", panel.transform, string.Empty, 24,
                TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.6f));
            var pageRt = _readerPageText.GetComponent<RectTransform>();
            pageRt.anchorMin = new Vector2(0.5f, 0f);
            pageRt.anchorMax = new Vector2(0.5f, 0f);
            pageRt.pivot = new Vector2(0.5f, 0f);
            pageRt.sizeDelta = new Vector2(0f, 40f);
            pageRt.anchoredPosition = new Vector2(0f, 14f);

            _readerRoot.SetActive(false);
        }

        // ===== 气泡 =====

        private void BuildBubble()
        {
            var canvasGo = new GameObject("SignBubbleCanvas",
typeof(RectTransform), typeof(Canvas));
            UIUtility.CreateWorldCanvas(canvasGo, transform, new Vector3(0f, _bubbleHeight, 0f));

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(400f, 120f);   // 像素；已由 localScale 换算为 4×1.2 世界单位

            _bubble = UIUtility.CreateObject("Bubble", canvasGo.transform);
            UIUtility.CenterAnchor(_bubble, new Vector2(360f, 90f), Vector2.zero);
            var bubbleBg = UIUtility.CreateImage("Background", _bubble.transform, UIUtility.PanelBg);
            UIUtility.Stretch(bubbleBg.GetComponent<RectTransform>());

            var text = UIUtility.CreateText("Label", _bubble.transform, "按 E 查看", 34,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            UIUtility.Stretch(text.GetComponent<RectTransform>());

            _bubble.SetActive(false);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
