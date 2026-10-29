using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarEco.Combat;
using StarEco.Player;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 「边跑边打 + 第三段投掷钳子」验收 harness（2026-10-28）。
    ///
    /// 为什么需要：投掷链路（出手时机 → 飞行命中 → 击退回旋 → 恢复手中工具）全靠时序，
    /// 只做编译检查证明不了。本 harness 在真实 Play 里按时刻表注入输入，全程走生产链路：
    ///   Run/Idle.Tick 消费 AttackPressedThisFrame → PlayerAttackState 连段 → 第三段 TryThrow
    ///   → ToolProjectile 扫描命中 → TestTarget 扣血 + 击退 → 回程归位恢复 ToolAnchor。
    ///
    /// 输入注入沿用 T11Acceptance 的做法：禁用 PlayerCombat.Update（否则每帧被真实 Input 覆盖），
    /// 反射写 AttackPressedThisFrame；水平输入写在 Application.onBeforeRender（帧末），
    /// 这样下一帧的 FixedUpdate 才读得到（FixedUpdate 先于 Update，不会被 ReadInput 冲掉）。
    ///
    /// 观测点跟着状态机内部字段（_segment / _throwCountdown）走，不靠绝对时间猜连段进度。
    /// 菜单：StarEco/Tests/Run Throw Acceptance
    /// 报告：.workbuddy/tmp/throw_acceptance.txt
    /// </summary>
    public static class PlayerThrowAcceptance
    {
        private const string ArmedKey = "StarEco.ThrowAcceptance.Armed";

        private static readonly StringBuilder Sb = new StringBuilder();
        private static readonly string ReportPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".workbuddy", "tmp", "throw_acceptance.txt"));

        private static readonly string ShotDir =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".workbuddy", "tmp"));

        private static bool _running;
        private static float _t0;
        private static bool _setup;

        private static PlayerController _player;
        private static PlayerCombat _combat;
        private static PlayerToolAnchor _anchor;
        private static PlayerToolThrow _throw;
        private static Animator _animator;

        private static GameObject _targetGo;
        private static Rigidbody2D _targetBody;
        private static TestTarget _target;

        private static Vector3 _home;
        private static bool _holdInput;
        private static bool _fireAttack;

        // 观测状态
        private static string _lastState = "";
        private static string _lastAnim = "";
        private static int _lastSeg = -1;
        private static bool _moveLogged;
        private static float _attackStartAt;
        private static float _vBefore, _xBefore;
        private static float _seg3At = -1f;
        private static float _releaseAt = -1f;
        private static bool _checkedFlight, _checkedHit, _checkedBack, _meleeChecked;
        private static bool _inject1, _inject2, _inject3;
        private static int _hpBefore = -1;
        private static float _knockX;

        private static FieldInfo _horizField;
        private static FieldInfo _hpField;
        private static FieldInfo _machineField;
        private static MethodInfo _attackSetter;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.update += Tick;
            Application.onBeforeRender += OnBeforeRender;
        }

        [MenuItem("StarEco/Tests/Run Throw Acceptance")]
        public static void Run()
        {
            SessionState.SetBool(ArmedKey, true);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        private static void OnBeforeRender()
        {
            if (!_running || _player == null)
            {
                return;
            }

            _horizField?.SetValue(_player, _holdInput ? 1f : 0f);

            if (_fireAttack && _attackSetter != null && _combat != null)
            {
                _attackSetter.Invoke(_combat, new object[] { true });
                _fireAttack = false;
            }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ArmedKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                _running = false;
                return;
            }

            if (!_setup)
            {
                Setup();
                return;
            }

            float el = Time.time - _t0;
            object state = CurrentState();

            // ===== 状态机 / 动画轨迹（变化才记）=====
            string st = state?.GetType().Name ?? "null";
            if (st != _lastState)
            {
                Log($"T+{el:F2} state {_lastState} -> {st}");
                _lastState = st;
            }

            string anim = AnimClipName();
            if (anim != _lastAnim)
            {
                Log($"T+{el:F2} anim -> {anim}");
                _lastAnim = anim;
            }

            int seg = ReadInt(state, "_segment", -1);
            if (seg != _lastSeg)
            {
                Log($"T+{el:F2} 段号 -> {seg}（连段窗剩余 {ReadFloat(state, "_comboWindowTimer", -1f):F3}s, " +
                    $"队列={ReadBool(state, "_nextQueued")}, 出手倒计时={ReadFloat(state, "_throwCountdown", -1f):F3}s）");
                _lastSeg = seg;
            }

            // ===== 起手 =====
            // 注意：注入写在帧末，只有 FixedUpdate（先于 Update 执行）读得到；
            // Update 里的 ReadInput 会把它覆盖回 0，所以注入无法驱动 Idle/Run 的转移判定，
            // 只能验证「攻击状态里 MoveHorizontally 是否还在全速推进」。这一点够用：
            // 旧实现在 FixedTick 里 StopHorizontal，速度会稳定是 0。
            if (!_inject1 && el >= 0.30f)
            {
                _inject1 = true;
                _vBefore = Mathf.Abs(_player.Body.velocity.x);
                _xBefore = _player.transform.position.x;
                _attackStartAt = Time.time;
                Log($"T+{el:F2} 起手前：速度={_vBefore:F2} x={_xBefore:F2}");
                RequestAttack(1, el);
            }

            // ===== 攻击中速度检查（起手 +0.12s，仍在段 1）=====
            if (_inject1 && !_moveLogged && Time.time - _attackStartAt >= 0.12f)
            {
                _moveLogged = true;
                float v = Mathf.Abs(_player.Body.velocity.x);
                float dx = _player.transform.position.x - _xBefore;
                float moveSpeed = _player.Config != null ? _player.Config.MoveSpeed : 6f;
                Log($"T+{el:F2} 攻击中：速度={v:F2}（MoveSpeed={moveSpeed:F2}）位移={dx:F3} anim={anim}");
                Log(v >= moveSpeed * 0.6f
                    ? "  [PASS] 攻击中水平移动仍按全速推进（旧实现 StopHorizontal 会当帧归零）"
                    : "  [FAIL] 攻击中速度掉下来了");

                // 采样完就撤掉方向输入：避免角色一直跑、跑出平台打断连段
                _holdInput = false;
                Log("  （采样结束，撤掉移动输入，保证后面连段不被打断）");
            }

            // ===== 段 1 连段窗口内注入第 2 段 =====
            if (_inject1 && !_inject2 && seg == 1 && Time.time - _attackStartAt >= 0.06f)
            {
                _inject2 = true;
                RequestAttack(2, el);
            }

            // ===== 段 2 起手即注入第 3 段 =====
            if (_inject2 && !_inject3 && seg == 2)
            {
                _inject3 = true;
                RequestAttack(3, el);
            }

            // ===== 段 3 起手：停输入 + 归位 + 放靶子 =====
            if (seg == 3 && _seg3At < 0f)
            {
                _seg3At = Time.time;
                _holdInput = false;
                Log($"T+{el:F2} 进入第三段：出手倒计时={ReadFloat(state, "_throwCountdown", -1f):F3}s");
            }

            // ===== 段 3 近身判定应为关闭（投掷替代近身）=====
            if (seg == 3 && !_meleeChecked && Time.time - _seg3At >= 0.09f)
            {
                _meleeChecked = true;
                var hitboxComp = _player.GetComponent<AttackHitbox>();
                var hbField = typeof(AttackHitbox).GetField("_hitbox", BindingFlags.NonPublic | BindingFlags.Instance);
                var hitCol = hbField?.GetValue(hitboxComp) as Collider2D;
                bool open = hitCol != null && hitCol.enabled;
                Log(open
                    ? "  [FAIL] 第三段近身判定窗被打开了（应为纯投掷）"
                    : "  [PASS] 第三段近身判定窗保持关闭，伤害全部来自投掷");
            }

            // ===== 出手前放靶子（段 3 起 +0.10s，早于出手 +0.19s；第三段近身判定已关，不会误伤）=====
            if (_seg3At > 0f && _targetGo == null && Time.time - _seg3At >= 0.10f)
            {
                float anchorY = _anchor != null ? _anchor.transform.position.y : _player.transform.position.y;
                int dir = _player.FacingDir;
                SpawnTarget(new Vector3(_player.transform.position.x + dir * 3f, anchorY, 0f));
                Log($"T+{el:F2} 靶子放到 {dir * 3f:+0;-0}u 外、高度 {anchorY:F2}（与手心同高）");
            }

            // ===== 出手时刻（段 3 起 +0.19s）=====
            if (_seg3At > 0f && _releaseAt < 0f && Time.time - _seg3At >= 0.19f)
            {
                _releaseAt = Time.time;
                Log($"T+{el:F2} 出手帧");
            }

            if (_releaseAt > 0f && !_checkedFlight && Time.time - _releaseAt >= 0.06f)
            {
                _checkedFlight = true;
                bool flying = _throw != null && _throw.IsFlying;
                bool hidden = _anchor != null && _anchor.ToolRenderer != null && !_anchor.ToolRenderer.enabled;
                Log($"T+{el:F2} 出手后 0.06s：IsFlying={flying} 手中工具隐藏={hidden}");
                Log(flying ? "  [PASS] 第三段已把钳子甩出去" : "  [FAIL] 没有投出（TryThrow 失败或没到段 3）");
                Log(hidden ? "  [PASS] 飞行期间手中工具已隐藏" : "  [WARN] 手中工具仍显示");

                // 画面取证：飞行中的钳子
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, "throw_flight.png"));
            }

            if (_releaseAt > 0f && !_checkedHit && Time.time - _releaseAt >= 0.30f)
            {
                _checkedHit = true;
                int hp = ReadTargetHp();
                _knockX = _targetBody != null ? _targetBody.velocity.x : 0f;
                Log($"T+{el:F2} 命中：HP {_hpBefore} -> {hp}（差值 {_hpBefore - hp}）击退 x={_knockX:F2}");
                Log(_hpBefore - hp >= 2
                    ? "  [PASS] 飞行中的钳子自动检测到敌人并扣血"
                    : "  [FAIL] 飞行中没有检测到命中");
                Log(_knockX > 0.5f ? "  [PASS] 命中后敌人被击退" : $"  [WARN] 击退速度偏小 {_knockX:F2}");

                ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, "throw_hit.png"));
            }

            if (_releaseAt > 0f && !_checkedBack && Time.time - _releaseAt >= 1.20f)
            {
                _checkedBack = true;
                bool flying = _throw != null && _throw.IsFlying;
                bool shown = _anchor != null && _anchor.ToolRenderer != null && _anchor.ToolRenderer.enabled;
                Log($"T+{el:F2} 归位：IsFlying={flying} 手中工具可见={shown}");
                Log(!flying && shown ? "  [PASS] 钳子回旋归位，手中工具恢复" : "  [FAIL] 钳子没回到手上");
                Log("=== 本轮观测结束 ===");
                _running = false;
                SessionState.SetBool(ArmedKey, false);
                EditorApplication.isPlaying = false;
            }

            // 兜底：段 3 迟迟不来（相对起手计时，别用绝对秒数）
            if (_seg3At < 0f && _inject3 && Time.time - _attackStartAt >= 2.5f)
            {
                Log($"T+{el:F2} [FAIL] 第三段始终没进入（最后 state={st} 段号={seg}）");
                _running = false;
                SessionState.SetBool(ArmedKey, false);
                EditorApplication.isPlaying = false;
            }
        }

        private static void Setup()
        {
            foreach (var pc in Object.FindObjectsOfType<PlayerController>())
            {
                _player = pc;
                break;
            }
            if (_player == null)
            {
                return;   // 场景还在加载
            }

            _setup = true;
            _running = true;
            _t0 = Time.time;
            _home = _player.transform.position;
            _combat = _player.GetComponent<PlayerCombat>();
            _animator = _player.GetComponent<Animator>();
            _anchor = _player.GetComponentInChildren<PlayerToolAnchor>(true);
            _throw = _player.ToolThrow;

            _horizField = typeof(PlayerController).GetField(
                "<HorizontalInput>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            _machineField = typeof(PlayerController).GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance);
            var atkProp = typeof(PlayerCombat).GetProperty("AttackPressedThisFrame");
            _attackSetter = atkProp != null ? atkProp.GetSetMethod(true) : null;
            _hpField = typeof(TestTarget).GetField("_hp", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_combat != null)
            {
                _combat.enabled = false;   // 停掉真实 Input 覆盖
            }

            _holdInput = true;

            Sb.Length = 0;
            Log("=== 投掷 / 边跑边打 验收 ===");
            Log($"反射：水平输入={_horizField != null} 攻击注入={_attackSetter != null} " +
                $"ToolThrow={_throw != null} Anchor={_anchor != null} " +
                $"ToolSprite={(_anchor != null && _anchor.ToolSprite != null)}");
            Log($"出生点={_home} 段时长={DumpSegTimes()}");

            int muted = 0;
            foreach (var e in Object.FindObjectsOfType<StarEco.EnemyAI.EnemyControllerBase>())
            {
                if (e.gameObject.activeSelf)
                {
                    e.gameObject.SetActive(false);
                    muted++;
                }
            }
            Log($"临时关闭场景敌人 {muted} 个，保证命中来源唯一");
        }

        private static string DumpSegTimes()
        {
            var cfg = _combat != null ? _combat.Config : null;
            if (cfg == null || cfg.SegmentEndTimes == null)
            {
                return "null";
            }
            return $"throw(dmg={cfg.ThrowDamage},kb={cfg.ThrowKnockback},speed={cfg.ThrowSpeed},range={cfg.ThrowRange}) " +
                   string.Join(",", System.Array.ConvertAll(cfg.SegmentEndTimes, v => v.ToString("F3")));
        }

        private static void SpawnTarget(Vector3 pos)
        {
            if (_targetGo != null)
            {
                return;
            }

            _targetGo = new GameObject("__ThrowTestTarget");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
            {
                _targetGo.layer = enemyLayer;
            }
            _targetGo.transform.position = pos;

            _targetBody = _targetGo.AddComponent<Rigidbody2D>();
            _targetBody.gravityScale = 0f;
            _targetBody.freezeRotation = true;

            var col = _targetGo.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.8f, 1.8f);
            col.isTrigger = true;   // 触发器：不挡玩家跑位，但仍会被圆形扫描命中

            _target = _targetGo.AddComponent<TestTarget>();
            _hpBefore = ReadTargetHp();
            Log($"靶子就位 HP={_hpBefore} @ {pos}");
        }

        private static int ReadTargetHp()
        {
            return _target == null || _hpField == null ? -1 : (int)_hpField.GetValue(_target);
        }

        private static void RequestAttack(int idx, float el)
        {
            _fireAttack = true;
            Log($"T+{el:F2} 注入第 {idx} 段");
        }

        private static object CurrentState()
        {
            if (_player == null || _machineField == null)
            {
                return null;
            }
            object machine = _machineField.GetValue(_player);
            return machine?.GetType().GetProperty("Current")?.GetValue(machine, null);
        }

        private static int ReadInt(object o, string field, int fallback)
        {
            var f = o?.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null && f.FieldType == typeof(int) ? (int)f.GetValue(o) : fallback;
        }

        private static float ReadFloat(object o, string field, float fallback)
        {
            var f = o?.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(o) : fallback;
        }

        private static bool ReadBool(object o, string field)
        {
            var f = o?.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null && f.FieldType == typeof(bool) && (bool)f.GetValue(o);
        }

        private static string AnimClipName()
        {
            if (_animator == null)
            {
                return "-";
            }
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Attack")) return "Attack";
            if (info.IsName("Attack02")) return "Attack02";
            if (info.IsName("Attack03")) return "Attack03";
            if (info.IsName("Run")) return "Run";
            if (info.IsName("Idle")) return "Idle";
            return info.shortNameHash.ToString();
        }

        private static void Log(string msg)
        {
            Sb.Append(msg).AppendLine();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, Sb.ToString());
            }
            catch
            {
                // 写盘失败不影响验收
            }
            Debug.Log("[ThrowTest] " + msg);
        }
    }
}
