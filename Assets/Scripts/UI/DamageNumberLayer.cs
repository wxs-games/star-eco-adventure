using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarEco.EnemyAI.Events;

namespace StarEco.UI
{
    /// <summary>
    /// 伤害飘字层：单 Canvas，怪物受伤时在其头顶飘出伤害数字并上浮淡出。
    /// 与 <see cref="EnemyHealthBarLayer"/> 同一套思路（WorldToScreenPoint 投影 + 对象池 + 运行时自举），
    /// 避免每只怪各挂一个世界空间 Canvas。
    /// </summary>
    public class DamageNumberLayer : MonoBehaviour
    {
        [Header("动画")]
        [Tooltip("数字上浮的总像素高度（参考分辨率下）")]
        [SerializeField] private float _riseDistance = 60f;

        [Tooltip("单条飘字存活时长（秒）")]
        [SerializeField] private float _lifetime = 0.65f;

        [Tooltip("同屏飘字上限（对象池容量）")]
        [SerializeField] private int _maxNumbers = 24;

        [Header("样式")]
        [SerializeField] private int _fontSize = 36;

        [SerializeField] private Color _textColor = new Color(1f, 0.95f, 0.85f);

        [SerializeField] private Color _outlineColor = new Color(0.1f, 0.05f, 0.05f, 0.95f);

        private Canvas _canvas;
        private Camera _camera;
        private readonly List<Popup> _popups = new List<Popup>();

        /// <summary>单条飘字的视图数据（对象池元素）。</summary>
        private sealed class Popup
        {
            public GameObject Go;
            public RectTransform Rt;
            public CanvasGroup Group;
            public Text Label;
            public Vector3 StartScreen;
            public float JitterX;
            public float Elapsed;
            public bool Active;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindObjectOfType<DamageNumberLayer>(true) != null)
            {
                return;
            }

            var go = new GameObject("DamageNumberLayer", typeof(DamageNumberLayer));
            Object.DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            _canvas = UIUtility.CreateCanvas("DamageNumberCanvas", transform, 36);
            _camera = Camera.main;
        }

        private void OnEnable()
        {
            EventCenterSubscribe();
        }

        private void OnDisable()
        {
            StarEco.Framework.EventCenter.Unsubscribe<EnemyDamagedEvent>(OnEnemyDamaged);
        }

        private void EventCenterSubscribe()
        {
            StarEco.Framework.EventCenter.Subscribe<EnemyDamagedEvent>(OnEnemyDamaged);
        }

        private void OnEnemyDamaged(EnemyDamagedEvent evt)
        {
            if (evt.Enemy == null || evt.Amount <= 0)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            // 从胸口高度起飘，避免与头顶血条重叠
            Vector3 world = evt.Enemy.transform.position + Vector3.up * 0.9f;
            Vector3 screen = _camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                return;
            }

            var popup = Acquire();
            if (popup == null)
            {
                return;
            }

            popup.Label.text = evt.Amount.ToString();
            popup.StartScreen = screen;
            popup.JitterX = Random.Range(-26f, 26f);
            popup.Elapsed = 0f;
            popup.Group.alpha = 1f;
            popup.Rt.position = screen;
            popup.Go.SetActive(true);
            popup.Go.transform.SetAsLastSibling();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                if (!p.Active)
                {
                    continue;
                }

                p.Elapsed += dt;
                float t = p.Elapsed / _lifetime;
                if (t >= 1f)
                {
                    Release(p);
                    continue;
                }

                // 先快后慢地上浮，尾段淡出
                float rise = _riseDistance * (1f - (1f - t) * (1f - t));
                p.Rt.position = p.StartScreen + new Vector3(p.JitterX * t, rise, 0f);
                p.Group.alpha = 1f - t * t;

                float scale = t < 0.15f ? Mathf.Lerp(1.35f, 1f, t / 0.15f) : 1f;
                p.Rt.localScale = Vector3.one * scale;
            }
        }

        // ===== 对象池 =====

        private Popup Acquire()
        {
            for (int i = 0; i < _popups.Count; i++)
            {
                if (!_popups[i].Active)
                {
                    _popups[i].Active = true;
                    return _popups[i];
                }
            }

            if (_popups.Count >= _maxNumbers)
            {
                return null;
            }

            var popup = CreatePopup();
            popup.Active = true;
            return popup;
        }

        private void Release(Popup popup)
        {
            popup.Active = false;
            popup.Go.SetActive(false);
        }

        private Popup CreatePopup()
        {
            var go = UIUtility.CreateObject("DamageNumber", _canvas.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 64f);

            var group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var label = go.AddComponent<Text>();
            label.font = UIUtility.BuiltinFont;
            label.fontSize = _fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = _textColor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = _outlineColor;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            go.SetActive(false);

            var popup = new Popup
            {
                Go = go,
                Rt = rt,
                Group = group,
                Label = label
            };
            _popups.Add(popup);
            return popup;
        }
    }
}
