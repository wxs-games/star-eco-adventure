using UnityEngine;
using StarEco.Framework;

namespace StarEco.Player
{
    /// <summary>
    /// 玩家控制器：物理与手感核心。
    /// 职责：输入采集（Update）、物理运动（FixedUpdate）、土狼时间/跳跃缓冲/可变跳高、朝向、地面检测、受击与死亡事件入口。
    /// 状态决策交给状态机，本类只提供"能力与事实"（能否跳、是否着地等）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerConfig _config;

        [Header("脚步音效")]
        [Tooltip("低于该水平速度不出声（单位/秒）")]
        [SerializeField] private float _footstepSpeedThreshold = 0.8f;
        [Tooltip("慢走时的迈步间隔")]
        [SerializeField] private float _footstepIntervalSlow = 0.42f;
        [Tooltip("全速奔跑时的迈步间隔")]
        [SerializeField] private float _footstepIntervalFast = 0.26f;

        public PlayerConfig Config => _config;
        public Rigidbody2D Body { get; private set; }
        public SpriteRenderer Sprite { get; private set; }
        public PlayerAnimatorDriver Anim { get; private set; }
        public StarEco.Combat.PlayerCombat Combat { get; private set; }
        public StarEco.Eco.SprayController Spray { get; private set; }

        /// <summary>
        /// 钳子投掷（第三段连击的收尾）。缺失时 Awake 自动补挂，避免每个场景/预制体都要手工加组件。
        /// </summary>
        public StarEco.Combat.PlayerToolThrow ToolThrow { get; private set; }

        // ===== 运行时事实（供状态机读取） =====
        public float HorizontalInput { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool JumpReleasedThisFrame { get; private set; }
        public bool DodgePressedThisFrame { get; private set; }
        public bool IsGrounded { get; private set; }
        public float VerticalVelocity => Body.velocity.y;
        public bool IsDead { get; private set; }
        public bool InputLocked { get; set; }

        // ===== 冲刺 / 蹲伏 / 墙面 / 扒边（2026-10-28 补齐 6 个死状态的驱动） =====
        /// <summary>本帧是否按下冲刺键（左 Ctrl）。</summary>
        public bool DashPressedThisFrame { get; private set; }
        /// <summary>上方向是否按住（W / ↑），扒边爬上用。</summary>
        public bool UpHeld { get; private set; }
        /// <summary>下方向是否按住（S / ↓），蹲伏进入、扒边放手用。</summary>
        public bool DownHeld { get; private set; }
        /// <summary>当前朝向：1 右 / -1 左。</summary>
        public int FacingDir => Sprite != null && Sprite.flipX ? -1 : 1;
        /// <summary>是否处于蹲伏（碰撞体已压缩）。</summary>
        public bool IsCrouching { get; private set; }
        /// <summary>是否处于贴墙下滑（写 Animator 的 IsWallSliding）。</summary>
        public bool IsWallSliding { get; private set; }
        /// <summary>是否处于扒边悬挂（写 Animator 的 IsLedgeGrab）。</summary>
        public bool IsLedgeGrabbing { get; private set; }
        /// <summary>蹬墙跳后是否还在强制推离墙面的锁定期。</summary>
        public bool IsWallJumpLocked => _wallJumpLockTimer > 0f;

        private PlayerStateMachine _machine;

        // 手感计时器
        private float _coyoteCounter;
        private float _jumpBufferCounter;
        private bool _canJump = true;   // 起跳窗口（地面/土狼时间）是否可用
        private bool _jumpCutPending;   // 按键在起跳前已松开：本次起跳要按短跳处理

        // 闪避（翻滚）
        private float _dodgeCooldownTimer;
        private int _airDodgesUsed;

        // 冲刺：冷却 + 空中次数
        private float _dashCooldownTimer;
        private int _airDashesUsed;

        // 墙面：蹬墙跳后的推离锁定期
        private float _wallJumpLockTimer;
        private float _wallJumpLockDir;

        // 扒边：松手后的再次抓取冷却
        private float _ledgeCooldownTimer;

        // 蹲伏：站立尺寸快照（蹲下压缩、起身还原）
        private Vector2 _baseColliderSize;
        private Vector2 _baseColliderOffset;
        private bool _colliderSizeCaptured;

        // 扒边悬挂期间的刚体约束快照
        private RigidbodyConstraints2D _baseConstraints;

        // 翻滚穿透：本体碰撞体在翻滚期间排除怪物层
        private Collider2D _bodyCollider;
        private int _baseExcludeLayers;

        // 受击
        private bool _pendingHurt;
        private float _invincibleTimer;
        private Vector2 _knockbackVelocity;

        // 脚步音效：迈步计时 + 当前踩在几片草丛里（可重叠，用计数而非布尔）
        private float _footstepTimer;
        private int _grassContacts;
        private bool _wasGrounded;

        // 常量输入轴（旧版 Input Manager）
        private const string AxisHorizontal = "Horizontal";
        private const string ButtonJump = "Jump";

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Sprite = GetComponent<SpriteRenderer>();
            Anim = GetComponent<PlayerAnimatorDriver>();
            Combat = GetComponent<StarEco.Combat.PlayerCombat>();
            Spray = GetComponent<StarEco.Eco.SprayController>();

            // 投掷组件：场景/预制体上已挂则复用，没有就运行时补一个（幂等，不写资产）
            ToolThrow = GetComponent<StarEco.Combat.PlayerToolThrow>();
            if (ToolThrow == null)
            {
                ToolThrow = gameObject.AddComponent<StarEco.Combat.PlayerToolThrow>();
            }

            Body.freezeRotation = true;

            _bodyCollider = GetComponent<Collider2D>();
            if (_bodyCollider != null)
            {
                _baseExcludeLayers = _bodyCollider.excludeLayers.value;
            }

            _baseConstraints = Body.constraints;
            CaptureColliderSize();

            if (_config == null)
            {
                _config = Resources.Load<PlayerConfig>("PlayerConfig")
                          ?? PlayerConfig.CreateDefault();
            }

            BuildStateMachine();
        }

        private void BuildStateMachine()
        {
            _machine = new PlayerStateMachine();
            _machine.Register(new PlayerIdleState(this, _machine));
            _machine.Register(new PlayerRunState(this, _machine));
            _machine.Register(new PlayerAirState(this, _machine));
            _machine.Register(new PlayerLandState(this, _machine));
            _machine.Register(new PlayerDodgeState(this, _machine));
            _machine.Register(new PlayerDashState(this, _machine));
            _machine.Register(new PlayerCrouchState(this, _machine));
            _machine.Register(new PlayerWallSlideState(this, _machine));
            _machine.Register(new PlayerLedgeState(this, _machine));
            _machine.Register(new PlayerAttackState(this, _machine));
            _machine.Register(new PlayerSprayState(this, _machine));
            _machine.Register(new PlayerChargeState(this, _machine));
            _machine.Register(new PlayerHurtState(this, _machine));
            _machine.Register(new PlayerDeathState(this, _machine));
            _machine.SetInitial<PlayerIdleState>();
        }

        private void Update()
        {
            EnsureMachine();
            ReadInput();
            UpdateTimers();
            UpdateInvincibility();
            UpdateStuckEscape();
            _machine.Tick();
            UpdateFootsteps();
        }

        private void FixedUpdate()
        {
            EnsureMachine();
            UpdateGrounded();
            UpdateGravity();
            _machine.FixedTick();
            ClampFallSpeed();
        }

        /// <summary>
        /// 保证状态机可用。域重载（Play 中途改脚本触发）会把实例字段清零但 Awake 不重跑，
        /// 导致 _machine 为 null 而 Update/FixedUpdate 照常被调用 → 每帧 NRE。
        /// 懒加载自愈：缺失时重建并复位初始态。BuildStateMachine 幂等，可重复调用。
        /// </summary>
        private void EnsureMachine()
        {
            if (_machine != null)
            {
                return;
            }
            BuildStateMachine();
        }

        // ===== 输入 =====

        private void ReadInput()
        {
            if (InputLocked || IsDead)
            {
                HorizontalInput = 0f;
                JumpPressedThisFrame = false;
                JumpReleasedThisFrame = false;
                DodgePressedThisFrame = false;
                DashPressedThisFrame = false;
                UpHeld = false;
                DownHeld = false;
                return;
            }

            float axis = Input.GetAxisRaw(AxisHorizontal);

            // 卡住自救期间临时接管水平输入，把玩家推离支撑物（见 UpdateStuckEscape/TryUnstick）
            if (_escapeInputTimer > 0f)
            {
                _escapeInputTimer -= Time.deltaTime;
                axis = _escapeDir;
            }

            HorizontalInput = axis;

            JumpPressedThisFrame = Input.GetButtonDown(ButtonJump);
            if (JumpPressedThisFrame)
            {
                _jumpBufferCounter = _config.JumpBufferTime;
                _jumpCutPending = false;   // 新的一次按键，重新决定本次跳高
            }

            JumpReleasedThisFrame = Input.GetButtonUp(ButtonJump);
            if (JumpReleasedThisFrame)
            {
                if (_jumpBufferCounter > 0f)
                {
                    // 还没起跳就松手（同帧点按 / 空中预输入后松手）→ 本次起跳按短跳处理
                    _jumpCutPending = true;
                }
                else if (Body.velocity.y > 0f)
                {
                    // 可变跳高：上升途中松键 = 短跳
                    Body.velocity = new Vector2(Body.velocity.x, Body.velocity.y * _config.JumpCutMultiplier);
                }
            }

            // 闪避（翻滚）：左 Shift
            DodgePressedThisFrame = Input.GetKeyDown(KeyCode.LeftShift);

            // 冲刺：左 Ctrl（与翻滚区分：冲刺无无敌、可空中一次）
            DashPressedThisFrame = Input.GetKeyDown(KeyCode.LeftControl);

            // 上下方向：W/S 与 ↑/↓（Unity 默认 Vertical 轴同键位，这里取按住状态）
            UpHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            DownHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        }

        private void UpdateTimers()
        {
            // 土狼时间
            if (IsGrounded)
            {
                _coyoteCounter = _config.CoyoteTime;
                _canJump = true;
                _airDodgesUsed = 0;   // 落地重置空中闪避次数（R1）
                _airDashesUsed = 0;   // 落地重置空中冲刺次数
                _ledgeCooldownTimer = 0f;
            }
            else
            {
                _coyoteCounter -= Time.deltaTime;
            }

            // 跳跃缓冲
            if (_jumpBufferCounter > 0f)
            {
                _jumpBufferCounter -= Time.deltaTime;
            }

            // 闪避冷却
            if (_dodgeCooldownTimer > 0f)
            {
                _dodgeCooldownTimer -= Time.deltaTime;
            }

            // 冲刺冷却 / 蹬墙推离锁 / 扒边冷却
            if (_dashCooldownTimer > 0f)
            {
                _dashCooldownTimer -= Time.deltaTime;
            }

            if (_wallJumpLockTimer > 0f)
            {
                _wallJumpLockTimer -= Time.deltaTime;
            }

            if (_ledgeCooldownTimer > 0f)
            {
                _ledgeCooldownTimer -= Time.deltaTime;
            }
        }

        // ===== 物理能力 =====

        /// <summary>
        /// 尝试起跳：只在着地（或土狼时间窗口内）生效，空中不再有二段跳。
        /// 跳高由跳跃键按住时长决定（松手截断）。
        /// </summary>
        public bool TryConsumeJump()
        {
            bool allowed = _coyoteCounter > 0f && _canJump;
            if (allowed)
            {
                _coyoteCounter = 0f;
                _canJump = false;

                _jumpBufferCounter = 0f;
                Body.velocity = new Vector2(Body.velocity.x, _config.JumpVelocity);

                // 按键在起跳前就已松开（同帧点按，或空中预输入后松手）→ 本次起跳截断为短跳。
                // ReadInput 的截断发生在起跳之前，那一帧速度还是地面值，必须在这里补一次。
                if (_jumpCutPending)
                {
                    _jumpCutPending = false;
                    Body.velocity = new Vector2(Body.velocity.x, Body.velocity.y * _config.JumpCutMultiplier);
                }

                AudioManager.Instance?.PlaySfx(SfxId.Jump);   // M10
            }

            return allowed;
        }

        /// <summary>跳跃缓冲是否有效（触地瞬间自动起跳用）。</summary>
        public bool HasJumpBuffer => _jumpBufferCounter > 0f;

        /// <summary>清空跳跃缓冲（扒边"爬上去"会消费掉这次跳跃输入）。</summary>
        public void ConsumeJumpBuffer()
        {
            _jumpBufferCounter = 0f;
            _jumpCutPending = false;
        }

        // ===== 闪避（翻滚）=====

        /// <summary>当前是否可以起翻滚：冷却已结束，且（在地面，或空中次数未用尽）。</summary>
        public bool CanStartDodge =>
            _config != null && _dodgeCooldownTimer <= 0f
            && (IsGrounded || _airDodgesUsed < _config.AirDodgeCount);

        /// <summary>状态机消费闪避输入：仅当前帧有效，消费即清。</summary>
        public bool ConsumeDodgeInput()
        {
            if (!DodgePressedThisFrame)
            {
                return false;
            }

            DodgePressedThisFrame = false;
            return true;
        }

        /// <summary>进入翻滚：起冷却、开无敌窗、空中计数 +1，并按方向翻转朝向。</summary>
        public void BeginDodge(float dir)
        {
            _dodgeCooldownTimer = _config.DodgeCooldown;
            _invincibleTimer = Mathf.Max(_invincibleTimer, _config.DodgeInvincibleTime);
            if (!IsGrounded)
            {
                _airDodgesUsed++;
            }

            Sprite.flipX = dir < 0f;
        }

        /// <summary>
        /// 翻滚穿透：开启时本体碰撞体排除怪物层，翻滚中可穿过敌人、不被其碰撞体顶回；
        /// 关闭时还原为初始排除层。由 <see cref="PlayerDodgeState"/> 在 Enter/Exit 成对调用。
        /// </summary>
        public void SetDodgePassThrough(bool on)
        {
            if (_bodyCollider == null || _config == null)
            {
                return;
            }

            _bodyCollider.excludeLayers = on
                ? _baseExcludeLayers | _config.DodgePassThroughLayers.value
                : _baseExcludeLayers;
        }

        /// <summary>水平移动（状态机在 FixedTick 调用；air 参数控制空中控制力；speedFactor 为移速因子，喷洒减速用）。</summary>
        public void MoveHorizontally(bool inAir, float speedFactor = 1f)
        {
            // 蹬墙跳后的锁定期：无论玩家怎么按，都朝背离墙面方向推，避免立刻贴回同一面墙
            float input = _wallJumpLockTimer > 0f ? _wallJumpLockDir : HorizontalInput;

            float targetSpeed = input * _config.MoveSpeed * (inAir ? _config.AirControlRatio : 1f) * speedFactor;
            float newSpeed = Mathf.MoveTowards(Body.velocity.x, targetSpeed, _config.Acceleration * Time.fixedDeltaTime);
            Body.velocity = new Vector2(newSpeed, Body.velocity.y);

            if (Mathf.Abs(input) > 0.01f)
            {
                Sprite.flipX = input < 0f;
            }
        }

        public void StopHorizontal()
        {
            Body.velocity = new Vector2(0f, Body.velocity.y);
        }

        // ===== 冲刺 =====

        /// <summary>当前是否可以起冲刺：冷却结束、非蹲伏，且（在地面，或空中次数未用尽）。</summary>
        public bool CanStartDash =>
            _config != null && _dashCooldownTimer <= 0f && !IsCrouching
            && (IsGrounded || _airDashesUsed < _config.AirDashCount);

        /// <summary>状态机消费冲刺输入：仅当前帧有效，消费即清。</summary>
        public bool ConsumeDashInput()
        {
            if (!DashPressedThisFrame)
            {
                return false;
            }

            DashPressedThisFrame = false;
            return true;
        }

        /// <summary>进入冲刺：起冷却、空中计数 +1、朝向对齐冲刺方向。</summary>
        public void BeginDash(float dir)
        {
            if (_config == null)
            {
                return;
            }

            _dashCooldownTimer = _config.DashCooldown;
            _wallJumpLockTimer = 0f;
            if (!IsGrounded)
            {
                _airDashesUsed++;
            }

            Sprite.flipX = dir < 0f;
        }

        // ===== 蹲伏 =====

        /// <summary>
        /// 切换蹲伏：压缩碰撞体高度时保持脚底不动（offset 同步下移一半），
        /// 这样蹲下/起身不会把角色顶进地面或悬空。
        /// </summary>
        public void SetCrouch(bool on)
        {
            if (IsCrouching == on)
            {
                return;
            }

            if (_bodyCollider is BoxCollider2D box && _colliderSizeCaptured)
            {
                if (on)
                {
                    float ratio = _config != null ? Mathf.Clamp(_config.CrouchHeightRatio, 0.3f, 1f) : 0.62f;
                    float newHeight = _baseColliderSize.y * ratio;
                    float delta = _baseColliderSize.y - newHeight;
                    box.size = new Vector2(_baseColliderSize.x, newHeight);
                    box.offset = new Vector2(_baseColliderOffset.x, _baseColliderOffset.y - delta * 0.5f);
                }
                else
                {
                    box.size = _baseColliderSize;
                    box.offset = _baseColliderOffset;
                }
            }

            IsCrouching = on;
        }

        /// <summary>头顶是否有空间站起来（蹲在低矮通道里时不许弹出碰撞体，避免卡进天花板）。</summary>
        public bool CanStandUp()
        {
            if (!IsCrouching)
            {
                return true;
            }

            if (!(_bodyCollider is BoxCollider2D box) || !_colliderSizeCaptured)
            {
                return true;
            }

            float r = box.edgeRadius;
            Bounds b = box.bounds;
            float footY = b.min.y - r;                     // 蹲伏时脚底与站立一致
            float standHeight = _baseColliderSize.y + r * 2f;
            Vector2 center = new Vector2(b.center.x, footY + standHeight * 0.5f);
            Vector2 size = new Vector2(
                Mathf.Max(0.05f, _baseColliderSize.x - 0.04f),
                Mathf.Max(0.05f, _baseColliderSize.y - 0.04f));

            int mask = Physics2D.AllLayers & ~(1 << gameObject.layer);
            int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, _standHits, mask);
            for (int i = 0; i < count; i++)
            {
                if (IsSolidObstacle(_standHits[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void CaptureColliderSize()
        {
            if (_bodyCollider is BoxCollider2D box)
            {
                _baseColliderSize = box.size;
                _baseColliderOffset = box.offset;
                _colliderSizeCaptured = true;
            }
        }

        // ===== 墙面（WallSlide / WallJump）=====

        /// <summary>探测指定侧是否有可依附的墙面（实体、非触发器、非动态刚体）。</summary>
        public bool IsTouchingWall(int dir)
        {
            if (_bodyCollider == null || dir == 0)
            {
                return false;
            }

            Bounds b = _bodyCollider.bounds;
            float r = _bodyCollider is BoxCollider2D box ? box.edgeRadius : 0f;
            float skin = _config != null ? Mathf.Max(0.02f, _config.WallCheckDistance) : 0.1f;

            float edgeX = dir > 0 ? b.max.x + r : b.min.x - r;
            // 取身体上段探测：避开脚底，避免把地面当成墙
            Vector2 center = new Vector2(edgeX + dir * skin * 0.5f, b.center.y + b.extents.y * 0.2f);
            Vector2 size = new Vector2(skin, Mathf.Max(0.1f, b.size.y * 0.5f));

            int mask = Physics2D.AllLayers & ~(1 << gameObject.layer);
            int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, _wallHits, mask);
            for (int i = 0; i < count; i++)
            {
                if (IsSolidObstacle(_wallHits[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>消费跳跃缓冲并执行蹬墙跳（空中不经过土狼时间窗口，故单独一条路径）。</summary>
        public bool TryConsumeWallJump(int wallDir)
        {
            if (!HasJumpBuffer || _config == null || wallDir == 0)
            {
                return false;
            }

            int outDir = -wallDir;
            _jumpBufferCounter = 0f;
            _coyoteCounter = 0f;
            Body.velocity = new Vector2(outDir * _config.WallJumpPushSpeed, _config.WallJumpVelocity);
            _wallJumpLockTimer = _config.WallJumpLockTime;
            _wallJumpLockDir = outDir;
            Sprite.flipX = outDir < 0;

            PlayOneShot("WallJump");
            AudioManager.Instance?.PlaySfx(SfxId.Jump);
            return true;
        }

        // ===== 扒边（LedgeGrab）=====

        /// <summary>
        /// 探测前方可抓边缘，成功时给出"爬上之后"的站立落点。
        /// 判定链：墙面前方有实体 → 从头顶上方往下取墙面顶面 → 高度落在抓取区间 → 落点站得下。
        /// </summary>
        public bool TryFindLedge(int dir, out Vector2 standPos)
        {
            standPos = Vector2.zero;
            if (_bodyCollider == null || _config == null || dir == 0 || _ledgeCooldownTimer > 0f)
            {
                return false;
            }

            Bounds b = _bodyCollider.bounds;
            float r = _bodyCollider is BoxCollider2D box ? box.edgeRadius : 0f;
            float footY = b.min.y - r;
            float topY = b.max.y + r;

            float probeX = dir > 0 ? b.max.x + r + 0.06f : b.min.x - r - 0.06f;
            float fromY = topY + 1f;
            float maxDist = Mathf.Max(0.2f, fromY - (footY - 0.5f));

            int mask = Physics2D.AllLayers & ~(1 << gameObject.layer);
            int count = Physics2D.RaycastNonAlloc(new Vector2(probeX, fromY), Vector2.down, _rayHits, maxDist, mask);

            float ledgeY = float.NaN;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = _rayHits[i];
                if (hit.collider == null || !IsSolidObstacle(hit.collider))
                {
                    continue;
                }

                if (float.IsNaN(ledgeY) || hit.point.y > ledgeY)
                {
                    ledgeY = hit.point.y;   // 取最高的实体面作为边缘顶面
                }
            }

            if (float.IsNaN(ledgeY))
            {
                return false;
            }

            float height = ledgeY - footY;
            if (height < _config.LedgeMinHeight || height > _config.LedgeMaxHeight)
            {
                return false;
            }

            // 落点：脚踩到边缘顶面上方 3cm，水平方向向平台内侧收，收多少按实际余量试
            float halfW = b.extents.x + r;
            float centerY = ledgeY + 0.03f + b.extents.y + r;
            float[] insets = { Mathf.Min(halfW * 0.55f, 0.55f), Mathf.Min(halfW * 0.35f, 0.35f), 0.2f };

            for (int i = 0; i < insets.Length; i++)
            {
                Vector2 candidate = new Vector2(probeX + dir * insets[i], centerY);
                if (IsSpaceFree(candidate, b.size))
                {
                    standPos = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>指定位置是否放得下玩家碰撞体（忽略自身、触发器、动态刚体）。</summary>
        private bool IsSpaceFree(Vector2 center, Vector2 size)
        {
            Vector2 probe = new Vector2(Mathf.Max(0.05f, size.x - 0.06f), Mathf.Max(0.05f, size.y - 0.06f));
            int mask = Physics2D.AllLayers & ~(1 << gameObject.layer);
            int count = Physics2D.OverlapBoxNonAlloc(center, probe, 0f, _standHits, mask);
            for (int i = 0; i < count; i++)
            {
                if (IsSolidObstacle(_standHits[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>切换贴墙下滑标志（由墙面状态在 Enter/Exit 调用，供 Animator 读）。</summary>
        public void SetWallSliding(bool on)
        {
            IsWallSliding = on;
        }

        /// <summary>进入/退出扒边悬挂：悬挂期间冻结刚体，避免重力把角色从边缘拉下去。</summary>
        public void SetLedgeHanging(bool on)
        {
            if (IsLedgeGrabbing == on)
            {
                return;
            }

            if (on)
            {
                _baseConstraints = Body.constraints;
                Body.constraints = RigidbodyConstraints2D.FreezeAll;
                Body.velocity = Vector2.zero;
            }
            else
            {
                Body.constraints = _baseConstraints;
            }

            IsLedgeGrabbing = on;
        }

        /// <summary>爬上边缘：连人到脚底一并置位，并起一段再抓冷却。</summary>
        public void ClimbLedge(Vector2 standPos)
        {
            Body.velocity = Vector2.zero;
            Body.position = standPos;
            transform.position = new Vector3(standPos.x, standPos.y, transform.position.z);
            _ledgeCooldownTimer = _config != null ? _config.LedgeCooldown : 0.4f;
        }

        /// <summary>松开边缘：恢复重力，并起一段再抓冷却（否则松手瞬间会立刻抓回同一边）。</summary>
        public void ReleaseLedge()
        {
            _ledgeCooldownTimer = _config != null ? _config.LedgeCooldown : 0.4f;
        }

        /// <summary>实体障碍判定：排除自身/子物体、触发器、动态刚体（敌人、弹体不是墙）。</summary>
        private bool IsSolidObstacle(Collider2D collider)
        {
            if (collider == null || collider == _bodyCollider || !collider.enabled || collider.isTrigger)
            {
                return false;
            }

            if (collider.transform.IsChildOf(transform))
            {
                return false;
            }

            var attached = collider.attachedRigidbody;
            if (attached != null && attached.bodyType == RigidbodyType2D.Dynamic)
            {
                return false;
            }

            return true;
        }

        // ===== 脚步 / 草丛音效 =====

        /// <summary>草丛感应区进入时调用（可重叠，计数累加）。</summary>
        public void AddGrassContact()
        {
            _grassContacts++;
        }

        /// <summary>草丛感应区离开/销毁时调用。</summary>
        public void RemoveGrassContact()
        {
            if (_grassContacts > 0)
            {
                _grassContacts--;
            }
        }

        /// <summary>
        /// 迈步音效：着地且水平速度超过阈值时按速度间隔出声；
        /// 踩在草丛里换成沙沙声，普通地面用脚步。
        /// </summary>
        private void UpdateFootsteps()
        {
            if (IsDead || InputLocked || !IsGrounded)
            {
                _footstepTimer = 0f;
                _wasGrounded = IsGrounded;
                return;
            }

            // 落地首帧留出间隔，避免与 Land 音效叠在一起
            if (!_wasGrounded)
            {
                _wasGrounded = true;
                _footstepTimer = _footstepIntervalFast;
                return;
            }

            float speed = Mathf.Abs(Body.velocity.x);
            if (speed < _footstepSpeedThreshold)
            {
                _footstepTimer = 0f;   // 静止后再次起步立即出声
                return;
            }

            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer > 0f)
            {
                return;
            }

            float speedRatio = _config != null
                ? Mathf.Clamp01(speed / Mathf.Max(0.01f, _config.MoveSpeed))
                : 1f;
            _footstepTimer = Mathf.Lerp(_footstepIntervalSlow, _footstepIntervalFast, speedRatio);

            if (_grassContacts > 0)
            {
                AudioManager.Instance?.PlaySfx(SfxId.GrassRustle, 0.12f);
            }
            else
            {
                AudioManager.Instance?.PlaySfx(SfxId.Footstep, 0.07f);
            }
        }

        public void SetKnockback(Vector2 velocity)
        {
            Body.velocity = velocity;
        }

        private void UpdateGravity()
        {
            float targetScale = Body.velocity.y < 0f
                ? _config.GravityScale * _config.FallGravityMultiplier
                : _config.GravityScale;

            Body.gravityScale = Mathf.MoveTowards(Body.gravityScale, targetScale, Time.fixedDeltaTime * 10f);
        }

        private void ClampFallSpeed()
        {
            if (Body.velocity.y < -_config.MaxFallSpeed)
            {
                Body.velocity = new Vector2(Body.velocity.x, -_config.MaxFallSpeed);
            }
        }

        private void UpdateGrounded()
        {
            // 以“物理脚底”为基准做面积探测：真地面、草垛、台阶等任何非触发器实体碰撞体都能站。
            // 旧实现用三根零宽射线取样，会漏掉两种情况：
            //   1) 支撑面不平整（草垛顶是斜/凸面），中轴取样点落在低于脚底的位置，探距不够；
            //   2) 取样点取自比碰撞体更宽的精灵包围盒，可能落到支撑物轮廓之外。
            // BoxCollider2D 的 edgeRadius 会外扩物理外形，故真实脚底 = bounds.min.y - edgeRadius。
            if (_bodyCollider == null)
            {
                IsGrounded = false;
                return;
            }

            var colBounds = _bodyCollider.bounds;
            float edgeRadius = _bodyCollider is BoxCollider2D box ? box.edgeRadius : 0f;
            float footY = colBounds.min.y - edgeRadius;
            float halfWidth = Mathf.Max(colBounds.extents.x + edgeRadius * 0.5f, 0.05f);

            // 探测盒上沿略高于脚底（容差同旧的 GroundCheckSkin），下探一段覆盖支撑面
            float probeDepth = _config.GroundCheckSkin + 0.02f;
            Vector2 probeCenter = new Vector2(colBounds.center.x, footY + 0.02f - probeDepth * 0.5f);
            Vector2 probeSize = new Vector2(halfWidth * 2f, probeDepth);

            IsGrounded = HasSolidSupport(probeCenter, probeSize);
        }

        // 脚底支撑探测复用缓冲，避免每帧产生托管分配
        private static readonly Collider2D[] _supportHits = new Collider2D[8];

        // 墙面 / 起身 / 扒边落点的复用缓冲（同上，避免每帧分配）
        private static readonly Collider2D[] _wallHits = new Collider2D[8];
        private static readonly Collider2D[] _standHits = new Collider2D[8];
        private static readonly RaycastHit2D[] _rayHits = new RaycastHit2D[8];

        /// <summary>
        /// 脚底探测盒是否压着“非触发器实体支撑”（真地面、草垛、台阶等）。
        /// 排除自身碰撞体与动态刚体（敌人、弹体），避免被误判成地面。
        /// </summary>
        private bool HasSolidSupport(Vector2 center, Vector2 size)
        {
            int mask = Physics2D.AllLayers & ~(1 << gameObject.layer);
            int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, _supportHits, mask);
            for (int i = 0; i < count; i++)
            {
                var collider = _supportHits[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                // 自身/子物体碰撞体（如判定盒）不算支撑
                if (collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                // 动态刚体（敌人、弹体）会移动，不是可靠站台
                var attached = collider.attachedRigidbody;
                if (attached != null && attached.bodyType == RigidbodyType2D.Dynamic)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        // ===== 卡住自救 =====

        // 判定阈值：StuckEscapeSeconds 时间窗内总位移小于 StuckMoveEpsilon 视为卡住。
        private const float StuckEscapeSeconds = 0.5f;
        private const float StuckMoveEpsilon = 0.05f;
        private const float EscapeInputSeconds = 0.45f;   // 接管水平输入的持续时间
        private float _stuckProbeTime;
        private Vector2 _stuckProbePos;
        private float _escapeInputTimer;
        private float _escapeDir;

        /// <summary>
        /// 卡住自救（2026-10-19 实测补漏）：垃圾堆等可净化目标是 Default 层实体碰撞体，
        /// 旧地面检测只认 <see cref="PlayerConfig.GroundLayer"/>，玩家跳/走上堆顶时
        /// <see cref="IsGrounded"/> 恒为 false，状态机卡在 PlayerAirState 无法移动。
        /// （2026-10-20）<see cref="UpdateGrounded"/> 已支持识别草垛/台阶等实体支撑，
        /// 正常站上去不再触发本自救；本机制保留为其他异常静止场景的兜底。
        /// 判定用"时间窗内总位移"而非逐帧速度/位移：物理求解器的接触抖动（逐帧亚像素起伏）
        /// 不会误触发，只有真正静止才累计。触发后 <see cref="TryUnstick"/> 临时接管水平输入，
        /// 让玩家沿既有移动逻辑滑下支撑物回到真地面（不传送、无掉落风险）。
        /// 脚下探测不到非地面实体时不动作（交由坠落/溺水/复活逻辑处理）。
        /// </summary>
        private void UpdateStuckEscape()
        {
            // 死亡/输入锁/已落地都不需要自救；同时刷新探针，避免离地瞬间误判
            if (IsDead || InputLocked || IsGrounded || _config == null)
            {
                ResetStuckProbe();
                return;
            }

            if (Time.time - _stuckProbeTime < StuckEscapeSeconds)
            {
                return;
            }

            // 窗口内几乎没动 → 判定卡住，尝试推离支撑物；之后重置窗口（失败则 0.5s 后重试）
            if ((Body.position - _stuckProbePos).magnitude < StuckMoveEpsilon)
            {
                TryUnstick();
            }

            ResetStuckProbe();
        }

        private void ResetStuckProbe()
        {
            _stuckProbePos = Body.position;
            _stuckProbeTime = Time.time;
        }

        /// <summary>
        /// 探测脚底下压着的"非 GroundLayer 实体"（典型：Default 层的垃圾堆），
        /// 找到就临时接管水平输入、把玩家推离其中心，使其滑下堆顶落回真地面。
        /// 返回是否找到支撑物。
        /// </summary>
        private bool TryUnstick()
        {
            var col = GetComponent<Collider2D>();
            if (col == null)
            {
                return false;
            }

            Vector2 size = col.bounds.size;
            // 身体碰撞体带圆角时，物理外形比 bounds 外扩 r（bounds 仍按 size 返回）；
            // 又因碰撞体 offset 已下移对齐精灵脚底，真实脚底 = col.bounds.min.y - r，
            // 不能再假设碰撞体与 transform 同心（2026-10-20）。
            float edgeRadius = col is BoxCollider2D box ? box.edgeRadius : 0f;
            // 探测盒放在脚底正下方：下探 0.25u 覆盖堆顶
            Vector2 probeCenter = new Vector2(Body.position.x, col.bounds.min.y - edgeRadius - 0.02f);
            Vector2 probeSize = new Vector2(size.x * 0.9f, 0.25f);

            foreach (var c in Physics2D.OverlapBoxAll(probeCenter, probeSize, 0f))
            {
                if (c == null || c == col || !c.enabled || c.isTrigger)
                {
                    continue;
                }
                if (c.transform.IsChildOf(transform))
                {
                    continue;
                }
                // 真地面（GroundLayer）不算卡人来源
                if (((1 << c.gameObject.layer) & _config.GroundLayer.value) != 0)
                {
                    continue;
                }

                float dir = Mathf.Sign(Body.position.x - c.bounds.center.x);
                if (Mathf.Approximately(dir, 0f))
                {
                    dir = -1f;   // 正压在堆心：默认往左滑
                }

                _escapeDir = dir;
                _escapeInputTimer = EscapeInputSeconds;
                return true;
            }

            return false;
        }

        // ===== 受击 / 死亡（M3 起由伤害系统调用） =====

        public void ApplyHit(Vector2 fromDirection, int currentHp, int maxHp)
        {
            if (_invincibleTimer > 0f || IsDead)
            {
                return;
            }

            _pendingHurt = true;
            _invincibleTimer = _config.HurtInvincibleTime;
            _knockbackVelocity = new Vector2(Mathf.Sign(fromDirection.x) * -_config.HurtKnockback, _config.HurtKnockback * 0.6f);

            if (currentHp <= 0)
            {
                IsDead = true;
                InputLocked = true;
            }
        }

        /// <summary>状态机轮询：有无未处理的受击事件。</summary>
        public bool ConsumePendingHurt()
        {
            if (_pendingHurt)
            {
                _pendingHurt = false;
                return true;
            }

            return false;
        }

        public Vector2 TakeKnockback()
        {
            return _knockbackVelocity;
        }

        /// <summary>无敌期剩余时间（闪烁效果用）。</summary>
        public float InvincibleTimer => _invincibleTimer;

        /// <summary>临时延长无敌（Boss 击败演出期间用：输入已锁，避免残余小怪打断演出）。</summary>
        public void GrantInvincibility(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            _invincibleTimer = Mathf.Max(_invincibleTimer, seconds);
        }

        /// <summary>
        /// 蓄力进度比例 [0,1]（M7 HUD 蓄力条数据源）：非蓄力状态返回 0。
        /// 唯一改动点是本只读属性（M7 design §2.7 论证的必要最小增量）。
        /// </summary>
        public float ChargeRatio =>
            _machine != null && _machine.Current is PlayerChargeState charge
                ? charge.ChargeRatio
                : 0f;

        /// <summary>触发一次性动画（受击/死亡/攻击/互动），驱动器缺失时静默。</summary>
        public void TryFireAnim(int triggerHash)
        {
            Anim?.FireOneShot(triggerHash);
        }

        /// <summary>
        /// 直接播放一次性动画状态（绕过 AnyState trigger 转移）。
        /// trigger + AnyState 在 m_InterruptionSource=0 时不被评估，Play() 是社区标准做法。
        /// </summary>
        public void PlayOneShot(string stateName)
        {
            Anim?.PlayOneShot(stateName);
        }

        /// <summary>
        /// Animation Event 兜底接收器：攻击动画事件会发到 Animator 所在的 Player 根物体。
        /// 真正的命中逻辑仍由 AttackHitbox 负责；这里避免缺组件/引用时刷“has no receiver”。
        /// </summary>
        public void OnAttackWindowOpen()
        {
            GetComponent<StarEco.Combat.AttackHitbox>()?.OnAttackWindowOpen();
        }

        /// <summary>Animation Event 兜底接收器，见 OnAttackWindowOpen。</summary>
        public void OnAttackWindowClose()
        {
            GetComponent<StarEco.Combat.AttackHitbox>()?.OnAttackWindowClose();
        }

        private void UpdateInvincibility()
        {
            if (_invincibleTimer > 0f)
            {
                _invincibleTimer -= Time.deltaTime;
            }
        }
    }
}
