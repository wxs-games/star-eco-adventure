using UnityEngine;
using UnityEngine.UI;
using StarEco.Framework;
using StarEco.Level.Events;
using StarEco.Player;
using StarEco.UI;

namespace StarEco.Level
{
    /// <summary>
    /// 被困动物陷阱笼（M9 R2）：E 键解救互动。
    /// 与 M7 DialogueTrigger 同构（半径检测 + 世界空间气泡 + KeyCode.E），差异在动作：
    /// 触发后播玩家 Interact 动画、0.3s 开笼（笼体与动物隐藏）、广播 AnimalRescuedEvent。
    /// 幂等：解救后本组件停用，不可重复触发。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RescueTarget : MonoBehaviour
    {
        [Tooltip("目标标识（事件携带，如 Animal01）")]
        [SerializeField] private string _targetId = "Animal01";

        [Tooltip("交互键，默认 E（与 M7 对话一致）")]
        [SerializeField] private KeyCode _interactKey = KeyCode.E;

        [Tooltip("交互半径（u）")]
        [SerializeField] private float _radius = 2.2f;

        [Tooltip("气泡相对笼子高度")]
        [SerializeField] private float _bubbleHeight = 1.8f;

        [Tooltip("开笼延迟（s）：等 Interact 动画起播")]
        [SerializeField] private float _openDelay = 0.3f;

        [Tooltip("开笼后隐藏的对象（笼体 + 动物），为空则只隐藏自身") ]
        [SerializeField] private GameObject _cageVisual;

        private Transform _player;
        private bool _playerInRange;
        private GameObject _bubble;
        private bool _done;
        private float _openTimer;
        private bool _opening;

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
            if (_done)
            {
                return;
            }

            if (_opening)
            {
                _openTimer -= Time.deltaTime;
                if (_openTimer <= 0f)
                {
                    CompleteRescue();
                }
                return;
            }

            if (_player == null)
            {
                var pc = FindObjectOfType<PlayerController>();
                if (pc == null)
                {
                    return;
                }
                _player = pc.transform;
            }

            // 暂停/面板打开时不可互动（UIManager 输入冻结收口，M7 语义）
            if (UIManager.HasInstance && UIManager.Instance.IsBlocking)
            {
                return;
            }

            bool inRange = Vector2.Distance(transform.position, _player.position) <= _radius;
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

            if (Input.GetKeyDown(_interactKey))
            {
                StartRescue();
            }
        }

        private void StartRescue()
        {
            _opening = true;
            _openTimer = _openDelay;

            if (_bubble != null)
            {
                _bubble.SetActive(false);
            }

            // 玩家 Interact 动画（空值保护：无 Animator 或缺动画不阻断流程）
            var pc = _player != null ? _player.GetComponent<PlayerController>() : null;
            pc?.TryFireAnim(StarEco.Player.PlayerAnimParams.Interact);

            Debug.Log($"[RescueTarget] 开始解救 {_targetId}");
        }

        private void CompleteRescue()
        {
            _opening = false;
            _done = true;

            var visual = _cageVisual != null ? _cageVisual : gameObject;
            visual.SetActive(false);

            AudioManager.Instance?.PlaySfx(SfxId.Rescue);   // M10

            EventCenter.Publish(new AnimalRescuedEvent
            {
                TargetId = _targetId,
                Position = transform.position
            });

            Debug.Log($"[RescueTarget] 已解救 {_targetId}，广播 AnimalRescuedEvent");
        }

        // ===== 气泡（复刻 DialogueTrigger 的世界空间 Canvas 模式）=====

        private void BuildBubble()
        {
            var canvasGo = new GameObject("RescueBubbleCanvas",
                typeof(RectTransform), typeof(Canvas));
            UIUtility.CreateWorldCanvas(canvasGo, transform, new Vector3(0f, _bubbleHeight, 0f));

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(400f, 120f);   // 像素；已由 localScale 换算为 4×1.2 世界单位

            _bubble = UIUtility.CreateObject("Bubble", canvasGo.transform);
            UIUtility.CenterAnchor(_bubble, new Vector2(360f, 90f), Vector2.zero);
            var bubbleBg = UIUtility.CreateImage("Background", _bubble.transform, UIUtility.PanelBg);
            UIUtility.Stretch(bubbleBg.GetComponent<RectTransform>());

            var label = UIUtility.CreateText("Label", _bubble.transform, "按 E 解救", 34,
                TextAnchor.MiddleCenter, UIUtility.TextMain);
            UIUtility.Stretch(label.GetComponent<RectTransform>());

            _bubble.SetActive(false);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 1f, 0.7f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
