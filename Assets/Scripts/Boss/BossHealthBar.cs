using UnityEngine;
using UnityEngine.UI;
using StarEco.Framework;

namespace StarEco.Boss
{
    /// <summary>
    /// Boss 血条（M8 R5）：屏幕上方固定位，程序化 uGUI（复用 M7 UIUtility）。
    /// - Boss 名 + HP 数值 + 血条填充（平滑插值）
    /// - 60% / 30% 两条阶段分隔刻度竖线
    /// - 阶段切换时显示"狂暴阶段"/"最终阶段"提示（1.5s 后隐藏）
    /// 非阻断面板，不锁玩家输入。
    /// </summary>
    public class BossHealthBar : MonoBehaviour
    {
        [Tooltip("绑定的 Boss（为空则运行时在场景里找 BossController）")]
        [SerializeField] private BossController _boss;

        [Tooltip("Boss 显示名")]
        [SerializeField] private string _bossName = "Boss01 · 工厂污染核心";

        [Tooltip("血条平滑插值速度")]
        [SerializeField] private float _fillLerpSpeed = 6f;

        [Tooltip("阶段提示显示时长（秒）")]
        [SerializeField] private float _phaseHintSeconds = 1.5f;

        private Canvas _canvas;
        private Image _fill;
        private Image _fillGhost;
        private Text _nameText;
        private Text _hpText;
        private Text _phaseText;
        private float _targetFill = 1f;
        private float _phaseHintTimer;

        // 仅在 HP 数值变化时重生成字符串，避免每帧 ToString 分配（GC 优化）
        private int _lastHp = -1;
        private int _lastMaxHp = -1;

        private static readonly Color BarBg = new Color(0.05f, 0.05f, 0.08f, 0.85f);
        private static readonly Color BarFill = new Color(0.85f, 0.25f, 0.30f);
        private static readonly Color BarGhost = new Color(1f, 0.85f, 0.4f, 0.55f);
        private static readonly Color TickColor = new Color(1f, 0.95f, 0.6f, 0.9f);

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            EventCenter.Subscribe<Events.BossPhaseChangedEvent>(OnPhaseChanged);
            EventCenter.Subscribe<Events.BossDefeatedEvent>(OnBossDefeated);
        }

        private void OnDisable()
        {
            EventCenter.Unsubscribe<Events.BossPhaseChangedEvent>(OnPhaseChanged);
            EventCenter.Unsubscribe<Events.BossDefeatedEvent>(OnBossDefeated);
        }

        private void Start()
        {
            TryBindBoss();
        }

        private void Update()
        {
            // Boss 由 BossGate 控制登场：未登场时血条保持隐藏，登场后再绑定显示
            if (_boss == null)
            {
                TryBindBoss();
                return;
            }

            if (_canvas == null || !_canvas.gameObject.activeSelf)
            {
                return;
            }

            if (_boss.IsDead)
            {
                Hide();
                return;
            }

            UpdateFill();

            if (_phaseHintTimer > 0f)
            {
                _phaseHintTimer -= Time.unscaledDeltaTime;
                if (_phaseHintTimer <= 0f && _phaseText != null)
                {
                    _phaseText.gameObject.SetActive(false);
                }
            }
        }

        // ===== 数据 =====

        private void UpdateFill()
        {
            float percent = Mathf.Clamp01(_boss.HpPercent / 100f);
            _targetFill = percent;

            if (_fill != null)
            {
                _fill.fillAmount = Mathf.MoveTowards(
                    _fill.fillAmount, _targetFill, _fillLerpSpeed * Time.unscaledDeltaTime);
            }

            if (_fillGhost != null)
            {
                // 残影层缓慢跟随，做出"掉血残影"效果
                _fillGhost.fillAmount = Mathf.MoveTowards(
                    _fillGhost.fillAmount, _targetFill, (_fillLerpSpeed * 0.4f) * Time.unscaledDeltaTime);
            }

            if (_hpText != null)
            {
                int hp = _boss.CurrentHP;
                int maxHp = _boss.Config != null ? _boss.Config.MaxHP : 0;
                if (hp != _lastHp || maxHp != _lastMaxHp)
                {
                    _lastHp = hp;
                    _lastMaxHp = maxHp;
                    _hpText.text = $"{hp} / {maxHp}";
                }
            }
        }

        private void OnPhaseChanged(Events.BossPhaseChangedEvent evt)
        {
            if (_phaseText == null)
            {
                return;
            }

            _phaseText.text = evt.PhaseName;
            _phaseText.gameObject.SetActive(true);
            _phaseHintTimer = _phaseHintSeconds;
        }

        private void OnBossDefeated(Events.BossDefeatedEvent evt)
        {
            Hide();
        }

        /// <summary>Boss 登场后绑定并显示血条（开场 Boss 隐藏时不报错、不显示）。</summary>
        private void TryBindBoss()
        {
            if (_boss != null)
            {
                return;
            }

            var boss = FindObjectOfType<BossController>();
            if (boss == null)
            {
                return;
            }

            _boss = boss;
            _lastHp = -1;
            _lastMaxHp = -1;
            Show();
        }

        private void Hide()
        {
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }
        }

        private void Show()
        {
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
            }
        }

        // ===== 构建 =====

        private void Build()
        {
            StarEco.UI.UIUtility.EnsureEventSystem();

            _canvas = StarEco.UI.UIUtility.CreateCanvas("BossHealthBarCanvas", transform, 40);

            var root = _canvas.transform;

            // 顶部血条容器
            var panel = StarEco.UI.UIUtility.CreateObject("Bar", root);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 1f);
            panelRt.anchorMax = new Vector2(0.5f, 1f);
            panelRt.pivot = new Vector2(0.5f, 1f);
            panelRt.sizeDelta = new Vector2(900f, 118f);
            panelRt.anchoredPosition = new Vector2(0f, -110f);

            var bg = StarEco.UI.UIUtility.CreateImage("Background", panel.transform, BarBg);
            StarEco.UI.UIUtility.Stretch(bg.GetComponent<RectTransform>());

            // 血条底槽
            var track = StarEco.UI.UIUtility.CreateObject("Track", panel.transform);
            var trackRt = track.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0.25f);
            trackRt.anchorMax = new Vector2(1f, 0.72f);
            trackRt.offsetMin = new Vector2(24f, 0f);
            trackRt.offsetMax = new Vector2(-24f, 0f);
            var trackImage = StarEco.UI.UIUtility.CreateImage("TrackBg", track.transform,
                new Color(0.15f, 0.15f, 0.18f, 0.9f));
            StarEco.UI.UIUtility.Stretch(trackImage.GetComponent<RectTransform>());

            // 残影层（在填充层之下，视觉上更宽）
            var ghostGo = StarEco.UI.UIUtility.CreateObject("Ghost", track.transform);
            StarEco.UI.UIUtility.Stretch(ghostGo.GetComponent<RectTransform>());
            _fillGhost = ghostGo.AddComponent<Image>();
            _fillGhost.sprite = StarEco.UI.UIUtility.WhiteSprite;   // Filled 必须挂 Sprite，否则 fillAmount 无效
            _fillGhost.color = BarGhost;
            _fillGhost.type = Image.Type.Filled;
            _fillGhost.fillMethod = Image.FillMethod.Horizontal;
            _fillGhost.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fillGhost.fillAmount = 1f;

            // 填充层
            var fillGo = StarEco.UI.UIUtility.CreateObject("Fill", track.transform);
            StarEco.UI.UIUtility.Stretch(fillGo.GetComponent<RectTransform>());
            _fill = fillGo.AddComponent<Image>();
            _fill.sprite = StarEco.UI.UIUtility.WhiteSprite;        // Filled 必须挂 Sprite，否则 fillAmount 无效
            _fill.color = BarFill;
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fill.fillAmount = 1f;

            // 阶段刻度：60% / 30%
            CreateTick(track.transform, 0.60f);
            CreateTick(track.transform, 0.30f);

            // Boss 名
            _nameText = StarEco.UI.UIUtility.CreateText("Name", panel.transform, _bossName, 26,
                TextAnchor.MiddleLeft, StarEco.UI.UIUtility.TextMain);
            var nameRt = _nameText.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 0.72f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.offsetMin = new Vector2(24f, 0f);
            nameRt.offsetMax = new Vector2(-24f, -4f);
            // 行高 29px 小于字号 26 的单行高度，Truncate 会把整行裁成不可见 → 容许溢出
            _nameText.verticalOverflow = VerticalWrapMode.Overflow;

            // HP 数值（右对齐）
            _hpText = StarEco.UI.UIUtility.CreateText("Hp", panel.transform, "30 / 30", 24,
                TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.85f));
            var hpRt = _hpText.GetComponent<RectTransform>();
            hpRt.anchorMin = new Vector2(0f, 0f);
            hpRt.anchorMax = new Vector2(1f, 0.25f);
            hpRt.offsetMin = new Vector2(24f, 4f);
            hpRt.offsetMax = new Vector2(-24f, 0f);
            _hpText.verticalOverflow = VerticalWrapMode.Overflow;   // 同上：行高 25.5px，字号 24

            // 阶段提示（居中偏上，默认隐藏）
            _phaseText = StarEco.UI.UIUtility.CreateText("PhaseHint", root, string.Empty, 44,
                TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.3f));
            var phaseRt = _phaseText.GetComponent<RectTransform>();
            phaseRt.anchorMin = new Vector2(0.5f, 1f);
            phaseRt.anchorMax = new Vector2(0.5f, 1f);
            phaseRt.pivot = new Vector2(0.5f, 1f);
            phaseRt.sizeDelta = new Vector2(600f, 70f);
            phaseRt.anchoredPosition = new Vector2(0f, -240f);
            _phaseText.gameObject.SetActive(false);

            Hide();   // 等 Boss 真正登场后再显示（见 TryBindBoss）
        }

        private static void CreateTick(Transform parent, float percent)
        {
            var tick = StarEco.UI.UIUtility.CreateObject("Tick" + Mathf.RoundToInt(percent * 100f), parent);
            var rt = tick.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(percent, 0f);
            rt.anchorMax = new Vector2(percent, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(3f, 0f);
            rt.anchoredPosition = Vector2.zero;

            var image = tick.AddComponent<Image>();
            image.color = TickColor;
        }
    }
}
