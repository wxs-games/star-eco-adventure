using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StarEco.Combat;
using StarEco.Eco;
using StarEco.EnemyAI;
using StarEco.Framework;
using StarEco.Level;
using StarEco.Player;
using StarEco.UI;
using StarEco.EnemyAI.Events;

namespace StarEco.Boss
{
    /// <summary>
    /// Boss01 控制器（M8 R2/R4）：继承 M4 `EnemyControllerBase`，复用 HP/受击硬直/死亡广播/感知/击退，
    /// 只新增"阶段判定 + 三套技能状态 + 净化倍率"三块差异逻辑。
    /// 配置槽位接受 `BossConfig`（配置继承 EnemyConfig，基类字段可直接读）。
    /// </summary>
    public class BossController : EnemyControllerBase
    {
        /// <summary>当前阶段（只升不降）。</summary>
        public BossPhase CurrentPhase { get; private set; } = BossPhase.Phase1;

        /// <summary>是否处于阶段切换过场（期间无敌、不响应伤害）。</summary>
        public bool IsPhaseTransition => _transitionTimer > 0f;

        /// <summary>技能前摇/命中阶段霸体：只吃伤害，不被硬直打断。</summary>
        public bool IsArmored { get; set; }

        protected override bool CanBeHurtInterrupted => !IsArmored;

        /// <summary>当前使用的 Boss 配置（缺失时回落默认）。</summary>
        public BossConfig BossCfg { get; private set; }

        /// <summary>弹体预制体（技能 B / 特殊招共用，复用 M4 EnemyProjectile）。</summary>
        public StarEco.EnemyAI.EnemyProjectile ProjectilePrefab => _projectilePrefab;

        /// <summary>弹体出生点相对 Boss 的偏移距离（沿发射方向）。</summary>
        public float FirePointOffset => _firePointOffset;

        [Tooltip("弹体预制体（技能 B / 特殊招）")]
        [SerializeField] private StarEco.EnemyAI.EnemyProjectile _projectilePrefab;

        [Tooltip("弹体出生点相对 Boss 中心的前向偏移")]
        [SerializeField] private float _firePointOffset = 1.2f;

        [Header("击败演出")]
        [Tooltip("净化波特效（击败瞬间在核心周围炸开）")]
        [SerializeField] private FxOneShot _fxPurifyPrefab;

        [Tooltip("击败演出收尾字幕（剧情依据：第三关“能源工厂的危机”，Boss01 是驱动整座工厂的污染核心）")]
        [SerializeField, TextArea(2, 3)]
        private string _epilogueText = "污染核心停转了——工厂的烟囱，第一次透出天光。\n这颗星球，重新开始呼吸了。";

        /// <summary>最终 Boss 演出总时长（近似值，用于演出期间给玩家无敌）。</summary>
        private const float DefeatSequenceSeconds = 5.5f;

        private bool _defeatSequenceStarted;
        private bool _deathBodyFrozen;

        /// <summary>当前 HP 百分比 [0,100]。</summary>
        public float HpPercent => Config.MaxHP > 0 ? (float)CurrentHP / Config.MaxHP * 100f : 0f;

        /// <summary>攻击间隔阶段系数（P2 更快、P3 最快）。</summary>
        public float IntervalScale
        {
            get
            {
                if (BossCfg == null)
                {
                    return 1f;
                }

                switch (CurrentPhase)
                {
                    case BossPhase.Phase2: return BossCfg.Phase2IntervalScale;
                    case BossPhase.Phase3: return BossCfg.Phase3IntervalScale;
                    default: return 1f;
                }
            }
        }

        private float _transitionTimer;
        private bool _triggeredPhase2;
        private bool _triggeredPhase3;
        private float _attackBCooldown;
        private float _specialCooldown;
        private float _pursueDelayTimer;
        private int _levelIndex = 3;

        protected override void Awake()
        {
            base.Awake();

            BossCfg = _config as BossConfig;
            if (BossCfg == null)
            {
                Debug.LogWarning("[BossController] 配置不是 BossConfig，已回落到默认 Boss 配置（HP 80 半心点 = 40 心）。");
                BossCfg = BossConfig.CreateDefault();
                _config = BossCfg;
            }

            CurrentPhase = BossPhase.Phase1;
            _triggeredPhase2 = false;
            _triggeredPhase3 = false;
            IsArmored = false;
        }

        protected override void BuildStateMachine()
        {
            Machine = new EnemyStateMachine();
            Machine.Register(new BossIdleState(this, Machine));
            Machine.Register(new BossChaseState(this, Machine));
            Machine.Register(new BossAttackAState(this, Machine));
            Machine.Register(new BossAttackBState(this, Machine));
            Machine.Register(new BossSpecialState(this, Machine));
            Machine.Register(new BossEnrageState(this, Machine));
            Machine.Register(new EnemyHurtState(this, Machine));
            Machine.Register(new EnemyDieState(this, Machine));
        }

        protected override System.Type InitialStateType => typeof(BossIdleState);

        protected override void Update()
        {
            base.Update();

            if (IsDead)
            {
                // 死亡态会关掉 Collider2D，若仍是动态刚体，Boss 会在 2s 死亡动画里直接穿过地面掉出画面，
                // 玩家只看到"凭空消失"。这里在死亡首帧钉住刚体，让 Die 动画与随后的击败演出留在原地。
                FreezeBodyOnDeath();
                return;
            }

            TickTransition();
            TickCooldowns();

            // 阶段判定放最后：本帧受击掉血后立刻反映到阶段
            EvaluatePhase();
        }

        private void FreezeBodyOnDeath()
        {
            if (_deathBodyFrozen || Body == null)
            {
                return;
            }

            _deathBodyFrozen = true;
            Body.velocity = Vector2.zero;
            Body.bodyType = RigidbodyType2D.Kinematic;
        }

        // ===== 阶段 =====

        private void EvaluatePhase()
        {
            if (BossCfg == null || IsPhaseTransition)
            {
                return;
            }

            float percent = HpPercent;

            if (!_triggeredPhase3 && percent <= BossCfg.Phase3HpPercent)
            {
                _triggeredPhase3 = true;
                EnterPhase(BossPhase.Phase3, percent);
                return;
            }

            if (!_triggeredPhase2 && percent <= BossCfg.Phase2HpPercent)
            {
                _triggeredPhase2 = true;
                EnterPhase(BossPhase.Phase2, percent);
            }
        }

        private void EnterPhase(BossPhase next, float percent)
        {
            CurrentPhase = next;
            _transitionTimer = BossCfg != null ? BossCfg.PhaseTransitionSeconds : 0.8f;

            // 过场期间不进入受击态，避免打断狂暴动画
            _pendingHurt = false;
            _hurtStunTimer = 0f;
            IsArmored = false;

            Machine.SwitchState<BossEnrageState>();
            TryFireAnim(BossAnimParams.Enrage);

            EventCenter.Publish(new Events.BossPhaseChangedEvent
            {
                Phase = (int)next,
                PhaseName = next.DisplayName(),
                HpPercent = percent
            });

            Debug.Log($"[BossController] 进入 {next.DisplayName()}（HP {percent:F0}%，过场无敌 {_transitionTimer:F2}s）");
        }

        private void TickTransition()
        {
            if (_transitionTimer <= 0f)
            {
                return;
            }

            _transitionTimer -= Time.deltaTime;
            if (_transitionTimer > 0f)
            {
                return;
            }

            _transitionTimer = 0f;
            Debug.Log($"[BossController] {CurrentPhase.DisplayName()} 过场结束，恢复战斗。");
            Machine.SwitchState<BossIdleState>();
        }

        // ===== 冷却（B / Special 独立计时，A 复用基类 AttackCooldownTimer） =====

        private void TickCooldowns()
        {
            if (_attackBCooldown > 0f)
            {
                _attackBCooldown -= Time.deltaTime;
            }

            if (_specialCooldown > 0f)
            {
                _specialCooldown -= Time.deltaTime;
            }

            if (_pursueDelayTimer > 0f)
            {
                _pursueDelayTimer -= Time.deltaTime;
            }
        }

        /// <summary>观望中：刚收完远程技能，暂不追击（玩家躲开也不会被立刻贴脸）。</summary>
        public bool IsPursueDelayed => _pursueDelayTimer > 0f;

        /// <summary>
        /// 下一次待机时是否优先逼近（远程技能收尾后置 true，近战收尾后置 false）。
        /// 没有这个开关，弹幕冷却一到就会原地再开火，Boss 会变成不会移动的炮台。
        /// </summary>
        public bool PreferMelee { get; set; }

        /// <summary>技能收尾时启动观望：站定一段时间再逼近，时长按阶段系数缩短（P2/P3 更凶）。</summary>
        public void BeginPursueDelay()
        {
            float seconds = BossCfg != null ? BossCfg.PursueDelaySeconds : 1.4f;
            _pursueDelayTimer = Mathf.Max(0f, seconds * IntervalScale);
        }

        /// <summary>技能 B 是否冷却就绪。</summary>
        public bool IsAttackBReady => _attackBCooldown <= 0f;

        /// <summary>设置技能 B 冷却。</summary>
        public void ResetAttackBCooldown()
        {
            _attackBCooldown = BossCfg != null
                ? BossCfg.AttackBInterval * IntervalScale
                : 3f;
        }

        /// <summary>特殊招是否冷却就绪（仅 P3）。</summary>
        public bool IsSpecialReady => CurrentPhase == BossPhase.Phase3 && _specialCooldown <= 0f;

        /// <summary>设置特殊招冷却。</summary>
        public void ResetSpecialCooldown()
        {
            _specialCooldown = BossCfg != null
                ? BossCfg.SpecialInterval * IntervalScale
                : 6f;
        }

        /// <summary>设置技能 A 冷却（走基类 AttackCooldownTimer）。</summary>
        public void ResetAttackACooldown()
        {
            AttackCooldownTimer = BossCfg != null
                ? BossCfg.AttackAInterval * IntervalScale
                : 2f;
        }

        /// <summary>近战范围判定（用 BossConfig.AttackARange）。</summary>
        public bool InMeleeRange =>
            HasTarget && BossCfg != null
            && Mathf.Abs(Target.position.x - transform.position.x) <= BossCfg.AttackARange;

        // ===== 伤害（R4：净化 ×1.5，过场无敌） =====

        /// <summary>M10：Boss 受击音效（基类默认 EnemyHit）。</summary>
        protected override SfxId HitSfxId => StarEco.Framework.SfxId.BossHit;

        public override void TakeDamage(DamageInfo info)
        {
            if (IsDead)
            {
                return;
            }

            // 阶段过场：忽略一切伤害（不扣血、不进受击态、不播 Hurt）
            if (IsPhaseTransition)
            {
                Debug.Log("[BossController] 阶段过场无敌，伤害被忽略。");
                return;
            }

            if (info.IsPurify && BossCfg != null && BossCfg.PurifyDamageMultiplier > 0f)
            {
                int boosted = Mathf.Max(1, Mathf.FloorToInt(info.Amount * BossCfg.PurifyDamageMultiplier));
                Debug.Log($"[BossController] 净化伤害 {info.Amount} ×{BossCfg.PurifyDamageMultiplier} → {boosted}");
                info.Amount = boosted;
            }

            base.TakeDamage(info);
        }

        // ===== 死亡：额外广播 BossDefeatedEvent =====

        public override void Recycle()
        {
            bool alreadyDead = IsDead;

            // Boss 是场景实例（非池借出），走基类的 SetActive(false) 分支
            base.Recycle();

            if (!alreadyDead)
            {
                return;
            }

            var stats = FindObjectOfType<StarEco.Level.LevelStats>();
            float elapsed = stats != null ? stats.ElapsedSeconds : 0f;

            EventCenter.Publish(new Events.BossDefeatedEvent
            {
                BossId = Config != null ? Config.EnemyTypeId : "Boss01",
                LevelIndex = _levelIndex,
                ElapsedSeconds = elapsed
            });

            Debug.Log($"[BossController] Boss 已击败（耗时 {elapsed:F1}s），已广播 BossDefeatedEvent。");
        }

        /// <summary>设置所属关卡序号（BossDefeatedEvent 携带；默认第三关）。</summary>
        public void SetLevelIndex(int index)
        {
            _levelIndex = index;
        }

        // ===== 击败演出（剧情收尾） =====

        /// <summary>
        /// Animation Event: DieEnd。Boss 不立刻广播死亡事件——先播一段"污染核心被净化"的收尾演出，
        /// 结束后再走基类流程。否则 LevelResultUI 会瞬间弹出结算面板，把演出整段盖住。
        /// </summary>
        public override void OnDieEnd()
        {
            if (_defeatSequenceStarted)
            {
                return;
            }

            _defeatSequenceStarted = true;
            StartCoroutine(DefeatSequence());
        }

        /// <summary>
        /// 击败演出：第三关"能源工厂的危机"里，Boss01 是抽干整颗星球的工厂污染核心。
        /// 核心被击穿后，净化波连续炸开 → 污染色褪成净化绿 → 核心上浮消散 → 收尾字幕，
        /// 最后才广播死亡事件进入结算，让玩家先看到"星球重新呼吸"这一幕。
        /// </summary>
        private IEnumerator DefeatSequence()
        {
            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                player.InputLocked = true;
                player.GrantInvincibility(DefeatSequenceSeconds + 1f);
            }

            // 演出期间残余小怪停手（第三关仍有 Enemy03 巡逻，不能打断收尾）
            foreach (var enemy in FindObjectsOfType<EnemyControllerBase>())
            {
                if (enemy != this)
                {
                    enemy.enabled = false;
                }
            }

            var cam = FindObjectOfType<CameraFollow2D>();
            var sprite = Sprite;
            Color startColor = sprite != null ? sprite.color : Color.white;
            Vector3 startPosition = transform.position;

            if (Body != null)
            {
                Body.velocity = Vector2.zero;
                Body.bodyType = RigidbodyType2D.Kinematic;   // 上浮阶段不再受重力拉扯
            }

            // 1) 净化波连炸：核心被自身的污染能量反噬
            AudioManager.Instance?.PlaySfx(SfxId.PurifyComplete);
            cam?.Shake(0.5f, 0.12f);
            for (int i = 0; i < 3; i++)
            {
                FxOneShot.Play(_fxPurifyPrefab, startPosition + (Vector3)(Random.insideUnitCircle * 1.1f));
                yield return new WaitForSeconds(0.25f);
            }

            // 2) 污染色褪去 → 转为净化绿（表现"核心熄灭、污染被抽离"）
            Color cleanColor = new Color(0.55f, 1f, 0.68f, 1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.9f)
            {
                if (sprite != null)
                {
                    Color c = Color.Lerp(startColor, cleanColor, Mathf.Clamp01(t));
                    c.a = 1f;
                    sprite.color = c;
                }

                cam?.Shake(0.06f, 0.03f);
                yield return null;
            }

            // 3) 核心上浮并淡出，象征污染源彻底消散
            for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
            {
                transform.position = startPosition + Vector3.up * (1.4f * t);
                if (sprite != null)
                {
                    Color c = cleanColor;
                    c.a = 1f - Mathf.Clamp01(t);
                    sprite.color = c;
                }

                yield return null;
            }

            // 4) 玩家胜利动作 + 收尾字幕
            if (player != null)
            {
                player.Anim?.FireOneShot(PlayerAnimParams.Victory);
            }

            yield return ShowEpilogue();

            base.OnDieEnd();
        }

        /// <summary>收尾字幕：底部黑底条 + 两行浅绿文字，淡入 0.45s / 停留 1.7s / 淡出 0.45s。</summary>
        private IEnumerator ShowEpilogue()
        {
            var canvas = UIUtility.CreateCanvas("BossDefeatEpilogueCanvas", null, 90);
            var canvasGo = canvas.gameObject;

            var strip = UIUtility.CreateImage("Strip", canvas.transform, new Color(0f, 0f, 0f, 0f));
            UIUtility.BottomCenterAnchor(strip.gameObject, new Vector2(1920f, 200f), new Vector2(0f, 80f));
            strip.raycastTarget = false;

            var text = UIUtility.CreateText("EpilogueText", canvas.transform, _epilogueText, 42,
                TextAnchor.MiddleCenter, new Color(0.85f, 1f, 0.90f, 0f));
            UIUtility.BottomCenterAnchor(text.gameObject, new Vector2(1680f, 180f), new Vector2(0f, 90f));
            text.raycastTarget = false;

            const float fadeIn = 0.45f;
            const float hold = 1.7f;
            const float fadeOut = 0.45f;

            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                SetEpilogueAlpha(strip, text, t / fadeIn);
                yield return null;
            }

            SetEpilogueAlpha(strip, text, 1f);
            yield return new WaitForSeconds(hold);

            for (float t = 0f; t < fadeOut; t += Time.deltaTime)
            {
                SetEpilogueAlpha(strip, text, 1f - t / fadeOut);
                yield return null;
            }

            Object.Destroy(canvasGo);
        }

        private static void SetEpilogueAlpha(Image strip, Text text, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);

            if (strip != null)
            {
                Color c = strip.color;
                c.a = 0.62f * alpha;
                strip.color = c;
            }

            if (text != null)
            {
                Color c = text.color;
                c.a = alpha;
                text.color = c;
            }
        }

        /// <summary>受击硬直后回落状态（Boss 优先追击）。</summary>
        public override System.Type DecidePostHurtState()
        {
            return HasTarget ? typeof(BossChaseState) : typeof(BossIdleState);
        }
    }
}
