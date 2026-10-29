using UnityEngine;
using UnityEngine.UI;
using StarEco.Player;

namespace StarEco.UI
{
    /// <summary>
    /// NPC 对话触发器（M7 R9）：玩家进入范围显示"按 E 交谈"气泡，按 E 打开对话面板。
    /// 与 DialoguePanel 的生命周期：触发器只负责"发起"，播放/推进全在面板内。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class DialogueTrigger : MonoBehaviour
    {
        [Tooltip("对话数据 Id（Resources/Data/dialogues.json）")]
        [SerializeField] private string _dialogueId = "ava_intro";

        [Tooltip("后续重复对话 Id（为空则按 _dialogueId + _repeat 规则查找；找不到则继续使用首段对话）")]
        [SerializeField] private string _repeatDialogueId = string.Empty;

        [Tooltip("交互键，默认 E")]
        [SerializeField] private KeyCode _interactKey = KeyCode.E;

        [Tooltip("触发区半径（单位 u）")]
        [SerializeField] private float _radius = 2.5f;

        [Tooltip("气泡相对触发器高度")]
        [SerializeField] private float _bubbleHeight = 1.8f;

        private Transform _player;
        private bool _playerInRange;
        private GameObject _bubble;
        private Text _bubbleText;
        private bool _playing;
        private bool _hasTalkedOnce;
        private DialoguePanel _panel;   // 缓存面板引用，避免每帧 FindObjectOfType 内部数组分配（GC 优化）
        private SpriteRenderer _sprite; // NPC 统一朝右作画，对话时靠 flipX 转向玩家
        private bool _defaultFlipX;

        private void OnEnable()
        {
            _sprite = GetComponent<SpriteRenderer>();
            if (_sprite != null)
            {
                _defaultFlipX = _sprite.flipX;
            }

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

            if (!inRange)
            {
                return;
            }

            // 对话播放中不重复触发（面板自己处理推进）。
            // 关键：最后一句也是用 E 关闭面板，如果这里仅靠 panelOpen 判断，
            // 同一帧里面板刚关闭时会再次被 GetKeyDown(E) 立即重开，形成“无限循环”。
            if (_panel == null)
            {
                _panel = FindObjectOfType<DialoguePanel>();
            }

            bool panelOpen = _panel != null && _panel.IsOpen;

            if (_playing)
            {
                if (panelOpen)
                {
                    return;
                }

                // 等交互键完全抬起后，才允许下一次重新触发。
                if (Input.GetKey(_interactKey))
                {
                    return;
                }

                _playing = false;
                RestoreFacing();
                if (_bubble != null)
                {
                    _bubble.SetActive(_playerInRange);
                }
            }

            if (!_playing && Input.GetKeyDown(_interactKey))
            {
                var data = ResolveDialogue();
                if (data == null)
                {
                    Debug.LogError($"[DialogueTrigger] 未找到对话：{_dialogueId}");
                    return;
                }

                if (_bubble != null)
                {
                    _bubble.SetActive(false);
                }

                UIManager.SafeOpen(DialoguePanel.PanelId);
                _panel = FindObjectOfType<DialoguePanel>();
                _panel?.Play(data);
                _playing = true;
                _hasTalkedOnce = true;
                FacePlayer();
            }
        }

        /// <summary>对话时转向玩家：素材统一朝右，玩家站到左侧就水平翻转，站右侧保持原朝向。</summary>
        private void FacePlayer()
        {
            if (_sprite == null || _player == null)
            {
                return;
            }

            _sprite.flipX = _player.position.x < transform.position.x;
        }

        /// <summary>对话结束恢复默认朝向（不把"面向玩家"的临时状态留在场景里）。</summary>
        private void RestoreFacing()
        {
            if (_sprite != null)
            {
                _sprite.flipX = _defaultFlipX;
            }
        }

        private DialogueData ResolveDialogue()
        {
            if (!_hasTalkedOnce)
            {
                return DialogueRepository.Find(_dialogueId);
            }

            string repeatId = string.IsNullOrEmpty(_repeatDialogueId)
                ? _dialogueId + "_repeat"
                : _repeatDialogueId;

            var repeatData = DialogueRepository.Find(repeatId);
            if (repeatData != null)
            {
                return repeatData;
            }

            return DialogueRepository.Find(_dialogueId);
        }

        private void BuildBubble()
        {
            UIUtility.EnsureEventSystem();

            var canvasGo = new GameObject("DialogueBubbleCanvas",
                typeof(RectTransform), typeof(Canvas));
            UIUtility.CreateWorldCanvas(canvasGo, transform, new Vector3(0f, _bubbleHeight, 0f));

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(400f, 120f);   // 像素；已由 localScale 换算为 4×1.2 世界单位

            _bubble = UIUtility.CreateObject("Bubble", canvasGo.transform);
            UIUtility.CenterAnchor(_bubble, new Vector2(360f, 90f), Vector2.zero);
            var bubbleBg = UIUtility.CreateImage("Background", _bubble.transform, UIUtility.PanelBg);
            UIUtility.Stretch(bubbleBg.GetComponent<RectTransform>());

            _bubbleText = UIUtility.CreateText("Label", _bubble.transform, "按 E 交谈", 34,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            UIUtility.Stretch(_bubbleText.GetComponent<RectTransform>());

            _bubble.SetActive(false);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
