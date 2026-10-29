using UnityEngine;
using UnityEngine.UI;
using StarEco.Framework;
using StarEco.Eco;
using StarEco.Eco.Events;
using StarEco.Level;
using StarEco.Level.Events;
using StarEco.Player;

namespace StarEco.UI
{
    /// <summary>
    /// HUD（M7 R1）：常驻关卡界面。
    /// - HP 心形×5（实心=剩余 / 空心=损失）
    /// - 净化剂条 + 数值
    /// - 净化值进度条 + 百分比
    /// - 当前目标提示（Label + 进度 x/y）
    /// - 蓄力进度条（蓄力中显示）
    /// 非阻断面板（BlocksInput=false）；关卡终态自动隐藏。
    /// </summary>
    public class HUDController : UIPanel
    {
        public const string PanelId = "HUD";

        public override string PanelName => PanelId;
        public override bool BlocksInput => false;

        private Image[] _hearts;
        private Image _tankFill;
        private Text _tankText;
        private Image _purityFill;
        private Text _purityText;
        private Text _objectiveText;
        private Image _chargeFill;
        private GameObject _chargeRoot;

        private PlayerController _player;
        private PuritySystem _purity;
        private LevelObjectiveTracker _tracker;
        private LevelManager _level;
        private float _tankBlinkTimer;
        private bool _tankWarned;
        // GC 优化：缓存上一帧文本，仅在数值变化时才重新生成字符串，避免每帧 ToString 分配
        private int _lastTankCurrent = -1;
        private int _lastTankCapacity = -1;
        private int _lastPurityPercent = -1;

        private static readonly Color TankFillColor = new Color(0.35f, 0.75f, 1f);
        private static readonly Color PurityFillColor = new Color(0.49f, 0.99f, 0.60f);
        private static readonly Color ChargeFillColor = new Color(1f, 0.85f, 0.35f);

        private Sprite _heartFullSprite;
        private Sprite _heartEmptySprite;
        private Sprite _tankFrameSprite;
        private Sprite _tankFillSprite;
        private Sprite _purityFrameSprite;
        private Sprite _purityFillSprite;
        private Sprite _chargeFrameSprite;
        private Sprite _chargeFillSprite;

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
            EventCenter.Unsubscribe<PurityChangedEvent>(OnPurityChanged);
            EventCenter.Unsubscribe<LevelObjectiveChangedEvent>(OnObjectiveChanged);
            EventCenter.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);

            if (UIManager.HasInstance)
            {
                UIManager.Instance.Unregister(this);
            }
        }

        private void Start()
        {
            ResolveDependencies();

            EventCenter.Subscribe<PurityChangedEvent>(OnPurityChanged);
            EventCenter.Subscribe<LevelObjectiveChangedEvent>(OnObjectiveChanged);
            EventCenter.Subscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);

            // 主动拉取一次：首个 LevelObjectiveChangedEvent 由 LevelManager.Start 触发，
            // 可能早于本组件的 Start（同场景 Start 顺序不定），不补拉会停在"目标：—"。
            RefreshObjectiveFromTracker();
            RefreshPurity();

            // 注册可能因 Awake 时序丢失（HUD 的 Awake 早于 UIManager 的 Awake 时
            // HasInstance 为 false）。Start 阶段 UIManager 必已就绪，这里幂等补注册。
            if (UIManager.HasInstance)
            {
                UIManager.Instance.Register(this);
                UIManager.Instance.Open(PanelId);
            }
        }

        private void ResolveDependencies()
        {
            _player = FindObjectOfType<PlayerController>();
            _purity = FindObjectOfType<PuritySystem>();
            _tracker = FindObjectOfType<LevelObjectiveTracker>();
            _level = LevelManager.HasInstance ? LevelManager.Instance : null;

            // 无玩家数据源（如误挂主菜单场景）→ HUD 不显示
            if (_player == null)
            {
                Debug.LogWarning("[HUD] 未找到 PlayerController，HUD 停用。");
                if (Root != null)
                {
                    Root.SetActive(false);
                }
            }
        }

        // ===== 构建 =====

        private void Build()
        {
            UIUtility.EnsureEventSystem();

            var canvas = UIUtility.CreateCanvas("HUDCanvas", transform, 10);
            SetRoot(canvas.gameObject, visibleByDefault: true);   // HUD 常驻

            var font = UIUtility.BuiltinFont;
            var root = canvas.transform;

            LoadHudSprites();

            // 左上：HP 心形 + 净化剂条
            var statusGo = UIUtility.CreateObject("Status", root);
            UIUtility.TopLeftAnchor(statusGo, new Vector2(390f, 112f), new Vector2(26f, -22f));

            var statusBg = UIUtility.CreateImage("PanelBg", statusGo.transform,
                new Color(0.04f, 0.08f, 0.12f, 0.72f));
            var statusBgRt = statusBg.GetComponent<RectTransform>();
            statusBgRt.anchorMin = new Vector2(0f, 0f);
            statusBgRt.anchorMax = new Vector2(1f, 1f);
            statusBgRt.offsetMin = new Vector2(-10f, -8f);
            statusBgRt.offsetMax = new Vector2(10f, 8f);

            var heartsGo = UIUtility.CreateObject("Hearts", statusGo.transform);
            var heartsRt = heartsGo.GetComponent<RectTransform>();
            heartsRt.anchorMin = new Vector2(0f, 1f);
            heartsRt.anchorMax = new Vector2(0f, 1f);
            heartsRt.pivot = new Vector2(0f, 1f);
            heartsRt.sizeDelta = new Vector2(240f, 40f);
            heartsRt.anchoredPosition = Vector2.zero;

            _hearts = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var heartGo = UIUtility.CreateObject("Heart" + i, heartsGo.transform);
                var rt = heartGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(38f, 34f);
                rt.anchoredPosition = new Vector2(22f + i * 46f, 0f);

                // 双层结构：底层空心常显，上层满心按 fillAmount 裁切（0/0.5/1 = 空/半/满）
                var empty = UIUtility.CreateImage("Empty", heartGo.transform, Color.white);
                UIUtility.Stretch(empty.GetComponent<RectTransform>());
                empty.sprite = _heartEmptySprite;
                empty.preserveAspect = true;

                var full = UIUtility.CreateImage("Full", heartGo.transform, Color.white);
                UIUtility.Stretch(full.GetComponent<RectTransform>());
                full.sprite = _heartFullSprite;
                full.preserveAspect = true;
                full.type = Image.Type.Filled;
                full.fillMethod = Image.FillMethod.Horizontal;
                full.fillOrigin = (int)Image.OriginHorizontal.Left;
                full.fillAmount = 1f;

                _hearts[i] = full;
            }

            var tankBar = UIUtility.CreateBar("TankBar", statusGo.transform,
                new Vector2(320f, 28f), new Vector2(0f, -18f),
                new Color(0f, 0f, 0f, 0.55f), TankFillColor, out var tankBarGo);
            _tankFill = tankBar;
            ApplyHudBarStyle(tankBarGo, _tankFrameSprite, _tankFillSprite, 8f);

            var tankTextGo = UIUtility.CreateObject("TankText", statusGo.transform);
            var tankTextRt = tankTextGo.GetComponent<RectTransform>();
            tankTextRt.anchorMin = new Vector2(0f, 0f);
            tankTextRt.anchorMax = new Vector2(0f, 0f);
            tankTextRt.pivot = new Vector2(0f, 0f);
            tankTextRt.sizeDelta = new Vector2(320f, 22f);
            tankTextRt.anchoredPosition = new Vector2(0f, -88f);
            _tankText = tankTextGo.AddComponent<Text>();
            _tankText.font = font;
            _tankText.fontSize = 18;
            _tankText.alignment = TextAnchor.MiddleCenter;
            _tankText.color = TankFillColor;
            _tankText.text = "净化剂 0/0";

            // 蓄力条（净化剂条上方，默认隐藏）
            var chargeGo = UIUtility.CreateObject("ChargeBar", statusGo.transform);
            UIUtility.TopLeftAnchor(chargeGo, new Vector2(380f, 18f), new Vector2(0f, -112f));
            _chargeRoot = chargeGo;

            var chargeBg = UIUtility.CreateImage("Background", chargeGo.transform, Color.white);
            UIUtility.Stretch(chargeBg.GetComponent<RectTransform>());
            if (_chargeFrameSprite != null)
            {
                chargeBg.sprite = _chargeFrameSprite;
                chargeBg.type = Image.Type.Sliced;
            }
            else
            {
                chargeBg.color = new Color(0f, 0f, 0f, 0.55f);
            }

            var chargeFillGo = UIUtility.CreateObject("Fill", chargeGo.transform);
            var chargeFillRt = chargeFillGo.GetComponent<RectTransform>();
            chargeFillRt.anchorMin = Vector2.zero;
            chargeFillRt.anchorMax = Vector2.one;
            chargeFillRt.offsetMin = Vector2.zero;
            chargeFillRt.offsetMax = Vector2.zero;
            _chargeFill = chargeFillGo.AddComponent<Image>();
            _chargeFill.color = _chargeFillSprite != null ? Color.white : ChargeFillColor;
            _chargeFill.type = Image.Type.Filled;
            _chargeFill.fillMethod = Image.FillMethod.Horizontal;
            _chargeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            if (_chargeFillSprite != null)
            {
                _chargeFill.sprite = _chargeFillSprite;
                chargeFillRt.offsetMin = new Vector2(7f, 7f);
                chargeFillRt.offsetMax = new Vector2(-7f, -7f);
            }

            chargeGo.SetActive(false);

            // 右上：净化值进度条（向中央收拢，减少左右割裂）
            var purityGo = UIUtility.CreateObject("PurityPanel", root);
            var purityRt = purityGo.GetComponent<RectTransform>();
            purityRt.anchorMin = new Vector2(0.5f, 1f);
            purityRt.anchorMax = new Vector2(0.5f, 1f);
            purityRt.pivot = new Vector2(0.5f, 1f);
            purityRt.sizeDelta = new Vector2(320f, 82f);
            purityRt.anchoredPosition = new Vector2(470f, -22f);

            var purityLabel = UIUtility.CreateText("Label", purityGo.transform, "星球净化值", 22,
                TextAnchor.MiddleLeft, PurityFillColor);
            var purityLabelRt = purityLabel.GetComponent<RectTransform>();
            purityLabelRt.anchorMin = new Vector2(0f, 1f);
            purityLabelRt.anchorMax = new Vector2(1f, 1f);
            purityLabelRt.pivot = new Vector2(0f, 1f);
            purityLabelRt.sizeDelta = new Vector2(0f, 34f);
            purityLabelRt.anchoredPosition = Vector2.zero;

            _purityFill = UIUtility.CreateBar("Bar", purityGo.transform,
                new Vector2(320f, 30f), new Vector2(0f, -46f),
                new Color(0f, 0f, 0f, 0.55f), PurityFillColor, out var purityBarGo);
            _purityFill.fillAmount = 0f;
            ApplyHudBarStyle(purityBarGo, _purityFrameSprite, _purityFillSprite, 8f);

            _purityText = UIUtility.CreateText("Percent", purityGo.transform, "0%", 22,
                TextAnchor.MiddleRight, TextMainColor());
            var purityTextRt = _purityText.GetComponent<RectTransform>();
            purityTextRt.anchorMin = new Vector2(0f, 1f);
            purityTextRt.anchorMax = new Vector2(1f, 1f);
            purityTextRt.pivot = new Vector2(1f, 1f);
            purityTextRt.sizeDelta = new Vector2(0f, 34f);
            purityTextRt.anchoredPosition = Vector2.zero;

            // 顶部居中：当前目标
            var objGo = UIUtility.CreateObject("Objective", root);
            var objRt = objGo.GetComponent<RectTransform>();
            objRt.anchorMin = new Vector2(0.5f, 1f);
            objRt.anchorMax = new Vector2(0.5f, 1f);
            objRt.pivot = new Vector2(0.5f, 1f);
            objRt.sizeDelta = new Vector2(760f, 60f);
            objRt.anchoredPosition = new Vector2(0f, -24f);

            var objBg = UIUtility.CreateImage("Background", objGo.transform,
                new Color(0.05f, 0.08f, 0.12f, 0.65f));
            UIUtility.Stretch(objBg.GetComponent<RectTransform>());

            _objectiveText = UIUtility.CreateText("Label", objGo.transform, "目标：—", 28,
                TextAnchor.MiddleCenter, UIUtility.TextBody);
            UIUtility.Stretch(_objectiveText.GetComponent<RectTransform>());
        }

        private static Color TextMainColor() => UIUtility.TextMain;

        // ===== 事件 =====

        private void OnPurityChanged(PurityChangedEvent evt)
        {
            RefreshPurity();
        }

        private void OnObjectiveChanged(LevelObjectiveChangedEvent evt)
        {
            if (_objectiveText == null)
            {
                return;
            }

            if (evt.Index < 0 || string.IsNullOrEmpty(evt.Label))
            {
                _objectiveText.text = "目标：关卡目标已完成";
            }
            else if (evt.Required > 0)
            {
                _objectiveText.text = $"目标：{evt.Label}（{evt.Current}/{evt.Required}）";
            }
            else
            {
                _objectiveText.text = $"目标：{evt.Label}";
            }
        }

        /// <summary>从目标链直接读当前目标文本（初始化与事件丢失时的兜底）。</summary>
        private void RefreshObjectiveFromTracker()
        {
            if (_objectiveText == null || _tracker == null)
            {
                return;
            }

            var objective = _tracker.CurrentObjective;
            int required = _tracker.CurrentRequired;

            if (objective == null)
            {
                _objectiveText.text = "目标：关卡目标已完成";
            }
            else if (required > 0)
            {
                _objectiveText.text = $"目标：{objective.Label}（{_tracker.CurrentProgress}/{required}）";
            }
            else
            {
                _objectiveText.text = $"目标：{objective.Label}";
            }
        }

        private void OnObjectiveCompleted(ObjectiveCompletedEvent evt)
        {
            // 计数刷新由 LevelObjectiveChangedEvent 统一驱动；这里只做日志
            Debug.Log($"[HUD] 目标完成：{evt.Label}");
        }

        // ===== 刷新 =====

        protected override void OnUpdate(float dt)
        {
            if (Root == null || !Root.activeSelf)
            {
                return;
            }

            // 终态隐藏
            if (_level != null && _level.State != LevelState.Playing)
            {
                Root.SetActive(false);
                return;
            }

            if (_player == null || _player.Combat == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null)
                {
                    return;
                }
            }

            RefreshHearts();
            RefreshTank(dt);
            RefreshCharge();
        }

        private void LoadHudSprites()
        {
            _heartFullSprite = Resources.Load<Sprite>("UI/HUD/hud_heart_full");
            _heartEmptySprite = Resources.Load<Sprite>("UI/HUD/hud_heart_empty");
            _tankFrameSprite = Resources.Load<Sprite>("UI/HUD/hud_tank_frame");
            _tankFillSprite = Resources.Load<Sprite>("UI/HUD/hud_tank_fill");
            _purityFrameSprite = Resources.Load<Sprite>("UI/HUD/hud_purity_frame");
            _purityFillSprite = Resources.Load<Sprite>("UI/HUD/hud_purity_fill");
            _chargeFrameSprite = Resources.Load<Sprite>("UI/HUD/hud_charge_frame");
            _chargeFillSprite = Resources.Load<Sprite>("UI/HUD/hud_charge_fill");
        }

        private static void ApplyHudBarStyle(GameObject barGo, Sprite frameSprite, Sprite fillSprite, float inset)
        {
            if (barGo == null)
            {
                return;
            }

            var bg = barGo.transform.Find("Background")?.GetComponent<Image>();
            var fill = barGo.transform.Find("Fill")?.GetComponent<Image>();
            if (bg == null || fill == null)
            {
                return;
            }

            if (frameSprite != null)
            {
                bg.sprite = frameSprite;
                bg.type = Image.Type.Sliced;
                bg.color = Color.white;
                bg.preserveAspect = false;
            }

            if (fillSprite != null)
            {
                fill.sprite = fillSprite;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.color = Color.white;
                fill.preserveAspect = false;
            }

            var fillRt = fill.rectTransform;
            fillRt.offsetMin = new Vector2(inset, inset);
            fillRt.offsetMax = new Vector2(-inset, -inset);
        }

        private void RefreshHearts()
        {
            // HP 为整数定点「半心点」（2 点 = 1 颗心）；每颗心占 2 个半心位
            int hpPoints = _player.Combat.CurrentHP;
            int maxPoints = Mathf.Max(2, _player.Combat.MaxHP);
            int maxHearts = Mathf.CeilToInt(maxPoints / 2f);
            int halfUnits = Mathf.Clamp(hpPoints, 0, maxHearts * 2);

            for (int i = 0; i < _hearts.Length; i++)
            {
                if (_hearts[i] == null)
                {
                    continue;
                }

                // 2→满心，1→半心，0→空心
                _hearts[i].fillAmount = Mathf.Clamp01((halfUnits - i * 2) / 2f);
                // HP 上限超过 5 时超出部分隐藏（M3 MaxHP=5）；连同底层空心一起隐藏
                var heartRoot = _hearts[i].transform.parent;
                if (heartRoot != null)
                {
                    heartRoot.gameObject.SetActive(i < maxHearts);
                }
            }
        }

        private void RefreshTank(float dt)
        {
            var tank = _player.Spray != null ? _player.Spray.Tank : null;
            if (tank == null || _tankFill == null)
            {
                return;
            }

            float ratio = tank.Capacity > 0 ? Mathf.Clamp01((float)tank.Current / tank.Capacity) : 0f;
            _tankFill.fillAmount = ratio;

            if (_tankText != null)
            {
                // 仅在数值变化时重新生成字符串，避免每帧 ToString 分配（GC 优化项 1）
                if (tank.Current != _lastTankCurrent || tank.Capacity != _lastTankCapacity)
                {
                    _lastTankCurrent = tank.Current;
                    _lastTankCapacity = tank.Capacity;
                    _tankText.text = $"净化剂 {tank.Current}/{tank.Capacity}";
                }
            }

            // 不足提示：低于蓄力消耗时条体闪烁
            var spray = _player.Spray;
            int heavyCost = spray != null && spray.Config != null ? spray.Config.HeavyCost : 0;
            bool low = tank.Current < heavyCost;

            if (low)
            {
                _tankBlinkTimer += dt;
                float blink = Mathf.PingPong(_tankBlinkTimer * 4f, 1f);
                _tankFill.color = Color.Lerp(TankFillColor, UIUtility.WarnRed, blink);

                if (!_tankWarned)
                {
                    _tankWarned = true;
                    Debug.Log("[HUD] 净化剂不足，无法蓄力。");
                }
            }
            else
            {
                _tankBlinkTimer = 0f;
                _tankWarned = false;
                _tankFill.color = TankFillColor;
            }
        }

        private void RefreshCharge()
        {
            if (_chargeRoot == null)
            {
                return;
            }

            float ratio = _player.ChargeRatio;
            bool charging = ratio > 0.001f;

            if (_chargeRoot.activeSelf != charging)
            {
                _chargeRoot.SetActive(charging);
            }

            if (charging && _chargeFill != null)
            {
                _chargeFill.fillAmount = Mathf.Clamp01(ratio);
            }
        }

        private void RefreshPurity()
        {
            if (_purity == null || _purityFill == null)
            {
                return;
            }

            float percent = Mathf.Clamp01(_purity.Percent);
            _purityFill.fillAmount = percent;

            if (_purityText != null)
            {
                // 仅在百分比变化时重新生成字符串（GC 优化项 1）
                int percentRounded = Mathf.RoundToInt(percent * 100f);
                if (percentRounded != _lastPurityPercent)
                {
                    _lastPurityPercent = percentRounded;
                    _purityText.text = $"{percentRounded}%";
                }
            }
        }
    }
}
