using UnityEngine;
using StarEco.Combat;
using StarEco.Framework;
using StarEco.Player;

namespace StarEco.Eco
{
    /// <summary>
    /// 净化喷壶主组件：K/L 输入读取、喷洒周期结算（IPurifiable / IHittable 双通道分流）、
    /// 蓄力净化波同帧结算、fx_purify 池化播放、判定盒镜像。
    /// 进入转移由玩家 FSM 的 Idle/Run 状态消费 TryConsumeSprayStart / TryConsumeChargeStart 驱动
    /// （与 M3 PlayerCombat.ConsumeAttackPressed 同构）。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class SprayController : MonoBehaviour
    {
        [SerializeField] private EcoConfig _config;
        [SerializeField] private FxOneShot _fxPurifyPrefab;
        [SerializeField] private SprayHitbox _hitbox;
        // M10：喷雾音效改走命名音效库（AudioManager.PlaySfx(SfxId)），
        // 保留序列化字段供个别关卡覆写；为空时用库内默认音。
        [SerializeField] private AudioClip _sprayStartSfx;
        [SerializeField] private AudioClip _heavyReleaseSfx;

        // 净化波复用静态数组（零每帧 GC）
        private static readonly Collider2D[] WaveOverlaps = new Collider2D[24];

        private PlayerController _player;
        private PurifyTank _tank;
        private SpriteRenderer _sprite;

        private Vector3 _hitboxLocalOffset;

        // 输入驱动属性（状态机读取）
        public bool SprayHeld { get; private set; }
        public bool ChargeHeld { get; private set; }
        private bool _sprayStartRequested;
        private bool _chargeStartRequested;

        // 喷洒运行时
        private float _tickTimer;
        private int _fxTickCounter;

        public EcoConfig Config => _config;
        public PurifyTank Tank => _tank;
        public bool IsSpraying { get; private set; }
        public float SpraySpeedFactor => _config != null ? _config.SprayMoveSpeedFactor : 1f;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _tank = GetComponent<PurifyTank>();
            _sprite = GetComponent<SpriteRenderer>();

            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }

            if (_hitbox == null)
            {
                _hitbox = GetComponentInChildren<SprayHitbox>(true);
            }

            if (_hitbox != null)
            {
                _hitboxLocalOffset = _hitbox.transform.localPosition;
                _hitboxLocalOffset.x = Mathf.Abs(_hitboxLocalOffset.x);
                _hitbox.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            ReadInput();
            MirrorHitbox();

            if (IsSpraying)
            {
                UpdateSprayTicks();
            }
        }

        // ===== 输入 =====

        private void ReadInput()
        {
            bool inputAllowed = _player != null && !_player.IsDead && !_player.InputLocked;
            SprayHeld = inputAllowed && Input.GetKey(KeyCode.K);
            ChargeHeld = inputAllowed && Input.GetKey(KeyCode.L);

            // 进入门条件（R2：喷洒需有余量；R3：蓄力需 ≥ HeavyCost）
            bool sprayAllowed = inputAllowed && _tank != null && !_tank.IsEmpty;
            bool chargeAllowed = inputAllowed && _tank != null
                && _tank.Current >= (_config != null ? _config.HeavyCost : 25);

            if (sprayAllowed && Input.GetKeyDown(KeyCode.K))
            {
                _sprayStartRequested = true;
            }

            if (chargeAllowed && Input.GetKeyDown(KeyCode.L))
            {
                _chargeStartRequested = true;
            }
        }

        /// <summary>状态机消费喷洒进入请求：仅当帧有效，消费即清。</summary>
        public bool TryConsumeSprayStart()
        {
            if (!_sprayStartRequested)
            {
                return false;
            }

            _sprayStartRequested = false;
            return true;
        }

        /// <summary>状态机消费蓄力进入请求：仅当帧有效，消费即清。</summary>
        public bool TryConsumeChargeStart()
        {
            if (!_chargeStartRequested)
            {
                return false;
            }

            _chargeStartRequested = false;
            return true;
        }

        // ===== 判定盒镜像（M4 FirePoint 同款：不依赖 transform 缩放）=====

        private void MirrorHitbox()
        {
            if (_hitbox == null)
            {
                return;
            }

            float facing = _sprite != null && _sprite.flipX ? -1f : 1f;
            var pos = _hitbox.transform.localPosition;
            pos.x = facing * _hitboxLocalOffset.x;
            _hitbox.transform.localPosition = pos;
        }

        // ===== 喷洒周期结算 =====

        private void UpdateSprayTicks()
        {
            float tick = Mathf.Max(0.01f, _config != null ? _config.SprayTickSeconds : 0.25f);
            _tickTimer += Time.deltaTime;
            while (_tickTimer >= tick)
            {
                _tickTimer -= tick;
                SettleSprayTick(tick);
            }
        }

        private void SettleSprayTick(float tick)
        {
            if (_hitbox == null || _tank == null)
            {
                return;
            }

            _hitbox.PruneInvalid();
            var targets = _hitbox.Targets;

            int purifyAmount = Mathf.CeilToInt((_config != null ? _config.SprayDPS : 2) * tick);

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];

                // 通道一：净化（垃圾/塔适配器）
                if (target.Purifiable != null)
                {
                    target.Purifiable.ReceivePurify(purifyAmount, gameObject);
                    continue;
                }

                // 喷壶只负责净化场景目标，不再直接击杀敌人。
            }

            // 净化剂消耗：按基础速率持续扣除。
            float rate = (_config != null ? _config.TankConsumePerSecond : 8) * tick;
            _tank.Consume(Mathf.CeilToInt(rate));

            // fx_purify 节流播放（每 2 tick 一次）
            _fxTickCounter++;
            if (_fxTickCounter >= 2)
            {
                _fxTickCounter = 0;
                PlayFxPurify();
            }
        }

        private bool IsEnemyLayer(GameObject go)
        {
            return _config != null && ((1 << go.layer) & _config.EnemyLayer) != 0;
        }

        private void PlayFxPurify()
        {
            FxOneShot.Play(_fxPurifyPrefab, _hitbox != null ? _hitbox.transform.position : transform.position);
        }

        // ===== 喷洒开/关（由玩家状态调用）=====

        public void BeginSpray()
        {
            IsSpraying = true;
            _tickTimer = 0f;
            _fxTickCounter = 1;   // 首 tick 即播一次特效

            if (_hitbox != null)
            {
                _hitbox.Clear();
                _hitbox.gameObject.SetActive(true);
            }

            if (_sprayStartSfx != null)
            {
                AudioManager.Instance?.PlaySfx(_sprayStartSfx);
            }
            else
            {
                AudioManager.Instance?.PlaySfx(SfxId.SprayStart);   // M10：库默认音
            }
        }

        public void EndSpray()
        {
            IsSpraying = false;

            if (_hitbox != null)
            {
                _hitbox.gameObject.SetActive(false);
                _hitbox.Clear();
            }
        }

        // ===== 蓄力净化波（同帧结算，R3：不等动画事件帧）=====

        /// <summary>释放净化波：范围内仅净化 IPurifiable，不再对敌方直接造成伤害。</summary>
        public void ReleasePurifyWave()
        {
            _tank?.Consume(_config != null ? _config.HeavyCost : 25);

            if (_hitbox == null)
            {
                return;
            }

            Vector2 center = _hitbox.transform.position;
            Vector2 size = Vector2.one;
            var box = _hitbox.GetComponent<BoxCollider2D>();
            if (box != null)
            {
                size = box.size * (_config != null ? _config.HeavyWaveScale : 2f);
            }

            int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, WaveOverlaps);

            for (int i = 0; i < count; i++)
            {
                var other = WaveOverlaps[i];
                if (other == null)
                {
                    continue;
                }

                var purifiable = other.GetComponentInParent<IPurifiable>();
                if (purifiable != null)
                {
                    purifiable.ReceivePurify(_config != null ? _config.HeavyPurifyAmount : 40, gameObject);
                    continue;
                }

                // 蓄力波同样不再直接伤害敌人，敌人需靠近战攻击处理。
            }

            if (_heavyReleaseSfx != null)
            {
                AudioManager.Instance?.PlaySfx(_heavyReleaseSfx);
            }
            else
            {
                AudioManager.Instance?.PlaySfx(SfxId.HeavyRelease);   // M10：库默认音
            }
        }
    }
}
