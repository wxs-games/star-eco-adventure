using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StarEco.Eco;
using StarEco.Eco.Events;
using StarEco.Framework;
using StarEco.Level;

namespace StarEco.Boss
{
    /// <summary>
    /// Boss 登场闸门（挂在第三关封锁墙 Decor_Wall_03R 上）。
    /// 关卡开局把 Boss 隐藏起来；只有当封锁墙被净化（GarbagePurifiedEvent.TargetId 命中）后，
    /// 墙体崩解消失，Boss 才突然窜出、震屏登场，随后立刻进入正常战斗状态。
    /// 这样"墙没净化 → 没有 Boss"，玩家必须先处理封锁墙才会触发 Boss 战。
    /// </summary>
    public class BossGate : MonoBehaviour
    {
        [Tooltip("登场 Boss（留空则运行时在场景内（含未激活对象）自动查找）")]
        [SerializeField] private BossController _boss;

        [Tooltip("触发闸门的净化目标标识（必须与墙上 GarbagePile._targetId 一致）")]
        [SerializeField] private string _gateTargetId = "BossGateWall";

        [Tooltip("登场点相对封锁墙的水平偏移（负数朝关卡内侧，避免 Boss 半身悬在地图边缘外）")]
        [SerializeField] private float _spawnOffsetX = -0.6f;

        [Tooltip("墙体崩解（淡出 + 压扁）时长（秒）")]
        [SerializeField] private float _wallBreakSeconds = 0.22f;

        [Tooltip("Boss 登场前的地震蓄势时长（秒）：先用持续增强的震屏把气氛顶起来，再让 Boss 出来")]
        [SerializeField] private float _quakeSeconds = 1.8f;

        [Tooltip("地震蓄势阶段的震屏峰值振幅（世界单位），越大越像地震")]
        [SerializeField] private float _quakeMaxAmplitude = 0.35f;

        [Tooltip("Boss 从起跳到落定的时长（秒），越短越突然")]
        [SerializeField] private float _burstSeconds = 0.3f;

        [Tooltip("Boss 起跳前下沉的深度（世界单位）")]
        [SerializeField] private float _burstDepth = 1.6f;

        [Tooltip("Boss 起跳前的缩放比例（从很小突然撑满，做出窜出来的爆开感）")]
        [SerializeField] private float _burstStartScale = 0.35f;

        [Tooltip("登场震屏振幅（世界单位）")]
        [SerializeField] private float _shakeAmplitude = 0.22f;

        [Tooltip("登场爆发特效（复用 fx_purify，留空则只有震屏）")]
        [SerializeField] private FxOneShot _fxBurstPrefab;

        private SpriteRenderer _wallSprite;
        private bool _revealed;

        private void Awake()
        {
            _wallSprite = GetComponent<SpriteRenderer>();

            if (_boss == null)
            {
                _boss = FindObjectOfType<BossController>(true);
            }

            // 开局隐藏：不可见、不感知、不攻击，直到封锁墙被净化
            if (_boss != null)
            {
                _boss.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            EventCenter.Subscribe<GarbagePurifiedEvent>(OnGarbagePurified);
        }

        private void OnDisable()
        {
            EventCenter.Unsubscribe<GarbagePurifiedEvent>(OnGarbagePurified);
        }

        private void OnGarbagePurified(GarbagePurifiedEvent evt)
        {
            if (_revealed || evt.TargetId != _gateTargetId)
            {
                return;
            }

            _revealed = true;
            StartCoroutine(Reveal());
        }

        private IEnumerator Reveal()
        {
            var cam = FindObjectOfType<CameraFollow2D>();
            cam?.Shake(_wallBreakSeconds, 0.10f);
            AudioManager.Instance?.PlaySfx(SfxId.PurifyComplete);

            // 1) 封锁墙崩解：淡出并压扁，随后整块移除
            if (_wallSprite != null)
            {
                Color wallColor = _wallSprite.color;
                Vector3 wallScale = transform.localScale;

                for (float t = 0f; t < _wallBreakSeconds; t += Time.deltaTime)
                {
                    float k = Mathf.Clamp01(t / _wallBreakSeconds);
                    wallColor.a = 1f - k;
                    _wallSprite.color = wallColor;
                    transform.localScale = new Vector3(
                        wallScale.x * (1f + 0.06f * k),
                        wallScale.y * (1f - 0.55f * k),
                        wallScale.z);
                    yield return null;
                }
            }

            // 只关掉墙体渲染，不能关整个物体：BossGate 自己挂在这面墙上，
            // SetActive(false) 会连带停掉本协程，Boss 就永远等不出来了。
            if (_wallSprite != null)
            {
                _wallSprite.enabled = false;
            }

            // 2) 地震蓄势：Boss 还埋在墙下，先用越来越强的震屏把"要出大事"的压迫感顶起来
            if (_boss == null)
            {
                Debug.LogWarning("[BossGate] 场景内没有 BossController，闸门打开后无 Boss 登场。");
                yield break;
            }

            yield return Quake();

            // 3) Boss 突然窜出
            var boss = _boss;
            boss.gameObject.SetActive(true);

            // 登场期间关掉控制器：否则状态机立刻开始追击，会与位移演出打架
            boss.enabled = false;

            var body = boss.Body;
            var sprite = boss.Sprite;

            // 登场点：直接从刚崩掉的封锁墙位置窜出来（往关卡内侧收一点，保证 Boss 站得进地面）
            Vector3 spawn = new Vector3(
                transform.position.x + _spawnOffsetX,
                boss.transform.position.y,
                boss.transform.position.z);

            Vector3 baseScale = boss.transform.localScale;
            Color spriteColor = sprite != null ? sprite.color : Color.white;

            boss.transform.position = spawn;

            // Rigidbody2D 会缓存自己的位置，直接改 transform 时 collider.bounds 还停在旧位置，
            // 后面量出来的占位范围就会错位（把玩家推向 Boss 真正所在的位置）。这里同步一次。
            if (body != null)
            {
                body.position = spawn;
            }

            Physics2D.SyncTransforms();

            // 先趁碰撞体还开着量出落定后的占位范围，再临时关掉所有非 Trigger 碰撞体：
            // Boss 体型很大（世界约 4.4×4.2），从玩家脚下爆出来会把玩家顶穿地面摔出关卡。
            Bounds bossBounds = default;
            bool hasBossBounds = false;
            var blockers = new List<Collider2D>();

            foreach (var col in boss.GetComponentsInChildren<Collider2D>(true))
            {
                if (col.isTrigger)
                {
                    continue;
                }

                if (!hasBossBounds)
                {
                    bossBounds = col.bounds;
                    hasBossBounds = true;
                }
                else
                {
                    bossBounds.Encapsulate(col.bounds);
                }

                col.enabled = false;
                blockers.Add(col);
            }

            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.bodyType = RigidbodyType2D.Kinematic;
            }

            // Boss 破墙而出的一瞬间把站在占位里的玩家推开，避免落地时被物理挤穿地面
            if (hasBossBounds)
            {
                PushPlayerClearOf(bossBounds);
            }

            Vector3 start = spawn + Vector3.down * _burstDepth;
            boss.transform.position = start;
            boss.transform.localScale = baseScale * _burstStartScale;
            SetSpriteAlpha(sprite, spriteColor, 0f);

            cam?.Shake(_burstSeconds, _shakeAmplitude);

            // 缓出走位 + 中途轻微鼓胀，收尾时正好落回原始缩放
            for (float t = 0f; t < _burstSeconds; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / _burstSeconds);
                float rise = 1f - Mathf.Pow(1f - k, 3f);

                boss.transform.position = Vector3.Lerp(start, spawn, rise);
                float scale = Mathf.Lerp(_burstStartScale, 1f, rise) + Mathf.Sin(k * Mathf.PI) * 0.10f;
                boss.transform.localScale = baseScale * scale;
                SetSpriteAlpha(sprite, spriteColor, Mathf.Clamp01(k * 4f));

                yield return null;
            }

            // 4) 落定：复位位置/缩放/透明度，恢复碰撞体与刚体，控制器接管后正式开打
            boss.transform.position = spawn;
            boss.transform.localScale = baseScale;
            SetSpriteAlpha(sprite, spriteColor, 1f);

            foreach (var col in blockers)
            {
                col.enabled = true;
            }

            FxOneShot.Play(_fxBurstPrefab, spawn);
            cam?.Shake(0.18f, _shakeAmplitude);

            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Dynamic;
            }

            boss.enabled = true;

            Debug.Log("[BossGate] 封锁墙已净化，Boss 登场并开始战斗。");
        }

        /// <summary>
        /// 地震蓄势：Boss 仍处于隐藏状态，镜头持续抖动且振幅逐级增强，
        /// 中途夹杂塌落声与地面扬尘，营造地底有巨物正在破土的压迫感。
        /// </summary>
        private IEnumerator Quake()
        {
            var cam = FindObjectOfType<CameraFollow2D>();
            Vector3 epicenter = transform.position;

            float elapsed = 0f;
            int playedRumble = 0;

            while (elapsed < _quakeSeconds)
            {
                float k = Mathf.Clamp01(elapsed / _quakeSeconds);

                // 平方向上走：前段只是轻微颤动，后段才明显摇晃
                float amplitude = Mathf.Lerp(0.05f, _quakeMaxAmplitude, k * k);

                // 每帧续一小段；Shake 内部取最大值，于是看出持续不衰减、越震越猛的地震
                cam?.Shake(0.12f, amplitude);

                if (playedRumble < 2 && k >= 0.35f + playedRumble * 0.3f)
                {
                    AudioManager.Instance?.PlaySfx(SfxId.BreakStone, 0.15f);
                    FxOneShot.Play(_fxBurstPrefab, epicenter + (Vector3)(Random.insideUnitCircle * 0.9f));
                    playedRumble++;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // 蓄势到顶：把峰值震动交给紧随其后的 Boss 窜出，中间不留空档
            cam?.Shake(_burstSeconds, _quakeMaxAmplitude);
        }

        /// <summary>
        /// 若玩家站在 Boss 落定后的碰撞范围里，沿水平方向推到最近的外侧（附带余量）并清零速度。
        /// Boss 从封锁墙旁窜出时玩家往往正贴着墙，不做这一步会被物理挤穿地面。
        /// </summary>
        private static void PushPlayerClearOf(Bounds bossBounds)
        {
            const float margin = 0.25f;

            var player = FindObjectOfType<StarEco.Player.PlayerController>();
            if (player == null)
            {
                return;
            }

            // 用玩家全部碰撞体算包围盒：只取第一个会低估宽度，推完仍可能重叠
            Bounds playerBounds = default;
            bool hasPlayerBounds = false;

            foreach (var col in player.GetComponentsInChildren<Collider2D>())
            {
                if (col.isTrigger)
                {
                    continue;
                }

                if (!hasPlayerBounds)
                {
                    playerBounds = col.bounds;
                    hasPlayerBounds = true;
                }
                else
                {
                    playerBounds.Encapsulate(col.bounds);
                }
            }

            if (!hasPlayerBounds || !playerBounds.Intersects(bossBounds))
            {
                return;
            }

            float pushRight = bossBounds.max.x + margin - playerBounds.min.x;
            float pushLeft = bossBounds.min.x - margin - playerBounds.max.x;
            float dx = Mathf.Abs(pushRight) <= Mathf.Abs(pushLeft) ? pushRight : pushLeft;

            var playerBody = player.GetComponent<Rigidbody2D>();
            if (playerBody != null)
            {
                playerBody.velocity = Vector2.zero;
            }

            player.transform.position += new Vector3(dx, 0f, 0f);
            Debug.Log($"[BossGate] Boss 登场点与玩家重叠，已将玩家水平推开 {dx:F2}。");
        }

        private static void SetSpriteAlpha(SpriteRenderer sprite, Color baseColor, float alpha)
        {
            if (sprite == null)
            {
                return;
            }

            Color c = baseColor;
            c.a = alpha;
            sprite.color = c;
        }
    }
}
