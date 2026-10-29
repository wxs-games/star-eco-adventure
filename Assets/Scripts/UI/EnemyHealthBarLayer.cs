using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarEco.Boss;
using StarEco.EnemyAI;
using StarEco.EnemyAI.Events;
using StarEco.Framework;

namespace StarEco.UI
{
    /// <summary>
    /// 小怪血条层（M4 表现层补充）：全部血条共用一个 Screen Space - Overlay Canvas，
    /// 每帧把单位头顶世界坐标投影成屏幕坐标来定位，避免每个怪物各挂一个 Canvas。
    ///
    /// 显示规则：受伤后淡入，最后一次受伤起 <see cref="_visibleSeconds"/> 秒后淡出；
    /// 死亡或离开场景立即淡出。Boss 走 <see cref="BossHealthBar"/> 的屏幕血条，这里跳过。
    ///
    /// 自举：运行时自动创建并 Don'tDestroyOnLoad，无需往场景里手工挂组件。
    /// </summary>
    public class EnemyHealthBarLayer : MonoBehaviour
    {
        [Header("时序")]
        [Tooltip("最后一次受伤后保持可见的时长（秒），超时淡出")]
        [SerializeField] private float _visibleSeconds = 3f;

        [Tooltip("淡入速度（alpha/秒）")]
        [SerializeField] private float _fadeInSpeed = 10f;

        [Tooltip("淡出速度（alpha/秒）")]
        [SerializeField] private float _fadeOutSpeed = 3.5f;

        [Header("布局")]
        [Tooltip("血条相对单位中心的世界高度偏移（u），用于放到头顶")]
        [SerializeField] private float _headOffsetWorld = 1.4f;

        [Tooltip("血条像素尺寸（参考分辨率 1920x1080 下）")]
        [SerializeField] private Vector2 _barSize = new Vector2(110f, 14f);

        [Tooltip("同屏血条上限（对象池容量）")]
        [SerializeField] private int _maxBars = 16;

        [Header("配色")]
        [SerializeField] private Color _backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.78f);
        [SerializeField] private Color _highColor = new Color(0.36f, 0.85f, 0.38f);
        [SerializeField] private Color _midColor = new Color(0.95f, 0.78f, 0.25f);
        [SerializeField] private Color _lowColor = new Color(0.90f, 0.28f, 0.28f);

        private Canvas _canvas;
        private Camera _camera;
        private readonly List<BarView> _bars = new List<BarView>();
        private readonly Dictionary<EnemyControllerBase, BarView> _activeBars =
            new Dictionary<EnemyControllerBase, BarView>();

        /// <summary>单条血条的视图数据（对象池元素）。</summary>
        private sealed class BarView
        {
            public GameObject Go;
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Fill;
            public EnemyControllerBase Target;
            public float LastDamageTime;
        }

        /// <summary>
        /// 运行时自举：进入 Play 后场景加载完成时创建一个常驻血条层，已有则不重复创建。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindObjectOfType<EnemyHealthBarLayer>(true) != null)
            {
                return;
            }

            var go = new GameObject("EnemyHealthBarLayer", typeof(EnemyHealthBarLayer));
            Object.DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            _canvas = UIUtility.CreateCanvas("EnemyHealthBarCanvas", transform, 35);
            _camera = Camera.main;
        }

        private void OnEnable()
        {
            EventCenter.Subscribe<EnemyDamagedEvent>(OnEnemyDamaged);
        }

        private void OnDisable()
        {
            EventCenter.Unsubscribe<EnemyDamagedEvent>(OnEnemyDamaged);
        }

        private void OnDestroy()
        {
            _activeBars.Clear();
            _bars.Clear();
        }

        // ===== 事件 =====

        private void OnEnemyDamaged(EnemyDamagedEvent evt)
        {
            var enemy = evt.Enemy;
            if (enemy == null || enemy is BossController)
            {
                return;   // Boss 已有屏幕顶部的专属血条
            }

            var config = enemy.Config;
            if (config == null || config.MaxHP <= 0)
            {
                return;   // 不可击杀单位（如炮台）不显示血条
            }

            var bar = AcquireBar(enemy);
            if (bar == null)
            {
                return;
            }

            bar.LastDamageTime = Time.unscaledTime;
            bar.Go.SetActive(true);
            bar.Go.transform.SetAsLastSibling();
            ApplyFill(bar, evt.CurrentHP, evt.MaxHP > 0 ? evt.MaxHP : config.MaxHP);
        }

        // ===== 每帧定位与淡入淡出 =====

        private void Update()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            for (int i = _bars.Count - 1; i >= 0; i--)
            {
                var bar = _bars[i];
                var target = bar.Target;
                if (target == null)
                {
                    continue;
                }

                bool gone = target.IsDead || !target.gameObject.activeInHierarchy;
                bool visible = !gone && now - bar.LastDamageTime <= _visibleSeconds;
                float targetAlpha = visible ? 1f : 0f;
                float speed = visible ? _fadeInSpeed : _fadeOutSpeed;

                bar.Group.alpha = Mathf.MoveTowards(bar.Group.alpha, targetAlpha, speed * dt);

                if (!visible && bar.Group.alpha <= 0.001f)
                {
                    Release(bar);
                    continue;
                }

                ApplyFill(bar, target.CurrentHP, target.Config != null ? target.Config.MaxHP : 1);

                Vector3 world = target.transform.position + Vector3.up * _headOffsetWorld;
                Vector3 screen = _camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    bar.Group.alpha = 0f;   // 在相机背后：不显示，也不做位置更新
                    continue;
                }

                // Screen Space - Overlay 下 RectTransform.position 即屏幕像素坐标
                bar.Rt.position = screen;
            }
        }

        // ===== 填充与配色 =====

        private void ApplyFill(BarView bar, int currentHp, int maxHp)
        {
            maxHp = Mathf.Max(1, maxHp);
            float ratio = Mathf.Clamp01((float)currentHp / maxHp);

            if (bar.Fill != null)
            {
                bar.Fill.fillAmount = ratio;
                bar.Fill.color = ratio > 0.5f ? _highColor : (ratio > 0.25f ? _midColor : _lowColor);
            }
        }

        // ===== 对象池 =====

        private BarView AcquireBar(EnemyControllerBase enemy)
        {
            if (_activeBars.TryGetValue(enemy, out var existing) && existing != null)
            {
                return existing;
            }

            BarView free = null;
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i].Target == null)
                {
                    free = _bars[i];
                    break;
                }
            }

            if (free == null)
            {
                if (_bars.Count >= _maxBars)
                {
                    return null;   // 超出上限时静默跳过，避免无限膨胀
                }

                free = CreateBar();
            }

            free.Target = enemy;
            free.LastDamageTime = Time.unscaledTime;
            free.Go.SetActive(true);
            _activeBars[enemy] = free;
            return free;
        }

        private void Release(BarView bar)
        {
            if (bar.Target != null)
            {
                _activeBars.Remove(bar.Target);
            }

            bar.Target = null;
            bar.Go.SetActive(false);
        }

        private BarView CreateBar()
        {
            var go = UIUtility.CreateObject("EnemyHealthBar", _canvas.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = _barSize;

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var bg = UIUtility.CreateImage("Background", go.transform, _backgroundColor);
            bg.raycastTarget = false;
            bg.sprite = UIUtility.WhiteSprite;
            UIUtility.Stretch(bg.GetComponent<RectTransform>());

            var fillImage = UIUtility.CreateImage("Fill", go.transform, _highColor);
            fillImage.raycastTarget = false;
            fillImage.sprite = UIUtility.WhiteSprite;   // Filled 必须挂 Sprite，否则 fillAmount 无效
            var fillRt = fillImage.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1f, 1f);
            fillRt.offsetMax = new Vector2(-1f, -1f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;

            go.SetActive(false);

            var bar = new BarView
            {
                Go = go,
                Rt = rt,
                Group = group,
                Fill = fillImage,
                Target = null,
                LastDamageTime = 0f
            };
            _bars.Add(bar);
            return bar;
        }
    }
}
