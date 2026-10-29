using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarEco.Framework;

namespace StarEco.UI
{
    /// <summary>
    /// 对话面板（M7 R9）：NPC 对话逐字显示 + 头像位 + E 推进/快进。
    /// 播放完成后关闭面板并广播该对话的 EventId（如有）。
    /// </summary>
    public class DialoguePanel : UIPanel
    {
        public const string PanelId = "Dialogue";

        public override string PanelName => PanelId;

        /// <summary>
        /// 是否有对话正在进行（全局只读开关）：对话期间敌人只移动不攻击。
        /// 由本面板 Open/Close 生命周期维护，敌人状态机与伤害结算只读，不反向写入。
        /// </summary>
        public static bool IsDialogueActive { get; private set; }

        private const float TypeInterval = 0.03f;

        private Text _speakerText;
        private Text _bodyText;
        private Text _hintText;
        private Image _avatarImage;

        private DialogueData _data;
        private int _entryIndex;
        private int _charCount;
        private float _typeTimer;
        private bool _lineComplete;

        // 避免逐帧 Substring 产生的 GC：对每行文本只构建一次“0..N”的前缀缓存。
        private string[] _typedLines;

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
            // 场景卸载时兜底复位，避免开关残留导致新场景敌人永不攻击
            IsDialogueActive = false;

            if (UIManager.HasInstance)
            {
                UIManager.Instance.Unregister(this);
            }
        }

        protected override void OnOpen()
        {
            IsDialogueActive = true;
        }

        protected override void OnClose()
        {
            IsDialogueActive = false;
        }

        /// <summary>开始播放一组对话（由 DialogueTrigger 调用）。</summary>
        public void Play(DialogueData data)
        {
            _data = data;
            _entryIndex = 0;
            _charCount = 0;
            _typeTimer = 0f;
            _lineComplete = false;

            if (_data == null || _data.Entries == null || _data.Entries.Count == 0)
            {
                Debug.LogWarning("[DialoguePanel] 对话数据为空，直接关闭。");
                UIManager.SafeClose(PanelId);
                return;
            }

            ShowCurrentLine();
        }

        protected override void OnUpdate(float dt)
        {
            if (_data == null)
            {
                return;
            }

            // 交互键：E（复用 M2 交互输入语义）
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (!_lineComplete)
                {
                    CompleteLineInstant();
                }
                else
                {
                    Advance();
                }
            }

            if (_lineComplete)
            {
                return;
            }

            var entry = _data.Entries[_entryIndex];
            if (entry == null || string.IsNullOrEmpty(entry.Text))
            {
                CompleteLineInstant();
                return;
            }

            _typeTimer += dt;
            while (_typeTimer >= TypeInterval && _charCount < entry.Text.Length)
            {
                _typeTimer -= TypeInterval;
                _charCount++;
            }

            if (_bodyText != null)
            {
                if (_typedLines != null && _charCount >= 0 && _charCount < _typedLines.Length)
                {
                    _bodyText.text = _typedLines[_charCount];
                }
                else
                {
                    _bodyText.text = entry.Text != null ? entry.Text : string.Empty;
                }
            }

            if (_charCount >= entry.Text.Length)
            {
                _lineComplete = true;
                UpdateHint();
            }
        }

        // ===== 内部 =====

        private void ShowCurrentLine()
        {
            var entry = _data.Entries[_entryIndex];

            if (_speakerText != null)
            {
                _speakerText.text = string.IsNullOrEmpty(entry.Speaker) ? string.Empty : entry.Speaker;
                _speakerText.gameObject.SetActive(!string.IsNullOrEmpty(entry.Speaker));
            }

            if (_avatarImage != null)
            {
                _avatarImage.color = ParseTint(entry.AvatarTint);
            }

            _charCount = 0;
            _typeTimer = 0f;
            _lineComplete = false;

            _typedLines = null;
            if (_bodyText != null)
            {
                _bodyText.text = string.Empty;
            }

            if (entry != null && !string.IsNullOrEmpty(entry.Text) && _bodyText != null)
            {
                BuildTypedLines(entry.Text);
            }

            UpdateHint();
        }

        private void CompleteLineInstant()
        {
            var entry = _data.Entries[_entryIndex];
            _charCount = entry != null && entry.Text != null ? entry.Text.Length : 0;

            if (_bodyText != null && entry != null)
            {
                _bodyText.text = entry.Text ?? string.Empty;
            }

            _lineComplete = true;
            UpdateHint();
        }

        private void Advance()
        {
            var entry = _data.Entries[_entryIndex];
            if (entry != null && !string.IsNullOrEmpty(entry.EventId))
            {
                EventCenter.Publish(new Events.DialogueEventIdEvent
                {
                    DialogueId = _data.Id,
                    EventId = entry.EventId
                });
            }

            _entryIndex++;
            if (_entryIndex >= _data.Entries.Count)
            {
                Finish();
                return;
            }

            ShowCurrentLine();
        }

        private void Finish()
        {
            _data = null;
            UIManager.SafeClose(PanelId);
        }

        private void UpdateHint()
        {
            if (_hintText == null)
            {
                return;
            }

            _hintText.text = _lineComplete
                ? (_entryIndex < _data.Entries.Count - 1 ? "按 E 继续" : "按 E 结束")
                : "按 E 快进";
        }

        private static Color ParseTint(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color))
            {
                return color;
            }

            return UIUtility.AccentGreen;
        }

        private void BuildTypedLines(string fullText)
        {
            if (string.IsNullOrEmpty(fullText))
            {
                _typedLines = null;
                return;
            }

            int len = fullText.Length;
            if (len <= 0)
            {
                _typedLines = null;
                return;
            }

            // index=0..len，分别对应“0..N字符”的前缀（只在开启一行时构建，避免逐帧 Substring）。
            var lines = new string[len + 1];
            lines[0] = string.Empty;
            for (int i = 1; i <= len; i++)
            {
                lines[i] = fullText.Substring(0, i);
            }
            _typedLines = lines;
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("DialogueCanvas", transform, 70);
            SetRoot(canvas.gameObject);

            var root = canvas.transform;

            // 底部对话框
            var box = UIUtility.CreateObject("Box", root);
            UIUtility.BottomCenterAnchor(box, new Vector2(1280f, 260f), new Vector2(0f, 40f));

            var boxBg = UIUtility.CreateImage("Background", box.transform, UIUtility.PanelBg);
            UIUtility.Stretch(boxBg.GetComponent<RectTransform>());

            // 头像位
            var avatarGo = UIUtility.CreateObject("Avatar", box.transform);
            var avatarRt = avatarGo.GetComponent<RectTransform>();
            avatarRt.anchorMin = new Vector2(0f, 0.5f);
            avatarRt.anchorMax = new Vector2(0f, 0.5f);
            avatarRt.pivot = new Vector2(0f, 0.5f);
            avatarRt.sizeDelta = new Vector2(160f, 160f);
            avatarRt.anchoredPosition = new Vector2(24f, 0f);

            _avatarImage = avatarGo.AddComponent<Image>();
            _avatarImage.color = UIUtility.AccentGreen;

            // 说话人名
            _speakerText = UIUtility.CreateText("Speaker", box.transform, string.Empty, 30,
                TextAnchor.UpperLeft, UIUtility.AccentGreen);
            var speakerRt = _speakerText.GetComponent<RectTransform>();
            speakerRt.anchorMin = new Vector2(0f, 1f);
            speakerRt.anchorMax = new Vector2(1f, 1f);
            speakerRt.pivot = new Vector2(0f, 1f);
            speakerRt.offsetMin = new Vector2(210f, -64f);
            speakerRt.offsetMax = new Vector2(-24f, -12f);

            // 台词正文
            _bodyText = UIUtility.CreateText("Body", box.transform, string.Empty, 30,
                TextAnchor.UpperLeft, UIUtility.TextBody);
            var bodyRt = _bodyText.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.pivot = new Vector2(0f, 0.5f);
            bodyRt.offsetMin = new Vector2(210f, 16f);
            bodyRt.offsetMax = new Vector2(-24f, -70f);

            // 操作提示
            _hintText = UIUtility.CreateText("Hint", box.transform, "按 E 继续", 22,
                TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.6f));
            var hintRt = _hintText.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(1f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(1f, 0f);
            hintRt.sizeDelta = new Vector2(220f, 32f);
            hintRt.anchoredPosition = new Vector2(-12f, 6f);

            Root.SetActive(false);
        }
    }
}
