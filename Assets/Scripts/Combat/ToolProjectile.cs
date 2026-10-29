using System.Collections.Generic;
using UnityEngine;

namespace StarEco.Combat
{
    /// <summary>
    /// 第三段投掷出去的钳子（回旋镖式投掷物）。
    ///
    /// 行为：从手心朝面朝方向飞出 → 沿途自动检测敌人（每个飞行阶段对同一目标只结算一次）→
    /// 飞满射程 / 撞到实体障碍 / 超时后折返 → 朝玩家手中锚点归位 → 抵达后通知投掷者恢复手中工具。
    ///
    /// 判定方式：不做物理碰撞，改为每物理帧沿位移路径做一次圆形扫描（CircleCast）。
    /// 高速飞行时若用 OnTriggerEnter 会整帧穿过敌人，扫描法没有这个漏判。
    /// </summary>
    [DisallowMultipleComponent]
    public class ToolProjectile : MonoBehaviour
    {
        /// <summary>一次投掷的全部参数（由 PlayerToolThrow 从 CombatConfig 组装）。</summary>
        public struct FlightParams
        {
            public int Damage;
            public float Knockback;
            public float Speed;
            public float Range;
            public float ReturnSpeed;
            public float HitRadius;
            public float SpinSpeed;
            public float MaxFlightTime;
            public float VisualScale;
            public float TrailTime;
            public GameObject Source;
            public Transform ReturnTarget;
        }

        /// <summary>归位判定距离：到这个距离内就算"回到手上"。</summary>
        private const float ArriveRadius = 0.3f;

        /// <summary>拖尾渐隐用的钳子钢青色（尾端全透明）。</summary>
        private static readonly Color TrailTint = new Color(0.62f, 0.90f, 1f, 1f);

        private SpriteRenderer _sprite;
        private TrailRenderer _trail;
        private FlightParams _p;

        private Vector2 _dir = Vector2.right;
        private float _traveled;
        private float _flightTime;
        private float _spin;
        private bool _returning;
        private bool _active;
        private System.Action _onReturned;

        // 同一飞行阶段（出程 / 回程各算一次）内不重复结算同一目标
        private readonly HashSet<IHittable> _hitThisPass = new HashSet<IHittable>();

        private static readonly RaycastHit2D[] _castHits = new RaycastHit2D[16];

        public bool IsFlying => _active;

        /// <summary>按工具精灵与排序参数创建一个待命（未激活）的投掷物实例。</summary>
        public static ToolProjectile Create(Sprite sprite, int sortingLayerId, int sortingOrder)
        {
            var go = new GameObject("PlayerToolProjectile");
            int layer = LayerMask.NameToLayer("Projectile");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = sortingOrder;

            // 拖尾要在 ToolProjectile 之前挂上：ToolProjectile.Awake 里会 GetComponent 取它
            AddTrail(go, sortingLayerId, Mathf.Max(0, sortingOrder - 1));

            var projectile = go.AddComponent<ToolProjectile>();
            go.SetActive(false);
            return projectile;
        }

        /// <summary>
        /// 飞行拖尾：钢青色渐隐。材质用精灵默认着色器（2D 项目内置管线可用），
        /// 找不到着色器就把拖尾整体禁用 —— 宁可不显示，也不要粉红方块。
        /// </summary>
        private static void AddTrail(GameObject go, int sortingLayerId, int sortingOrder)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            }

            var trail = go.AddComponent<TrailRenderer>();
            if (shader == null)
            {
                trail.enabled = false;
                return;
            }

            trail.material = new Material(shader) { name = "PlayerToolTrail" };
            trail.time = 0.14f;
            trail.startWidth = 0.22f;
            trail.endWidth = 0f;
            trail.widthMultiplier = 1f;
            trail.minVertexDistance = 0.03f;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.sortingLayerID = sortingLayerId;
            trail.sortingOrder = sortingOrder;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.generateLightingData = false;
            trail.autodestruct = false;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(TrailTint, 0f),
                    new GradientColorKey(TrailTint, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0.22f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            trail.colorGradient = gradient;

            trail.emitting = false;
        }

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            _trail = GetComponent<TrailRenderer>();
        }

        /// <summary>出手。</summary>
        public void Launch(Vector2 worldPos, int facing, FlightParams p, System.Action onReturned)
        {
            _p = p;
            _dir = facing < 0 ? Vector2.left : Vector2.right;
            _traveled = 0f;
            _flightTime = 0f;
            _spin = 0f;
            _returning = false;
            _active = true;
            _onReturned = onReturned;
            _hitThisPass.Clear();

            transform.position = worldPos;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one * Mathf.Max(0.1f, p.VisualScale);
            if (_sprite == null)
            {
                _sprite = GetComponent<SpriteRenderer>();
            }
            if (_sprite != null)
            {
                _sprite.flipX = facing < 0;
            }

            if (_trail == null)
            {
                _trail = GetComponent<TrailRenderer>();
            }
            if (_trail != null && _trail.enabled)
            {
                _trail.time = Mathf.Max(0f, p.TrailTime);
                _trail.emitting = _trail.time > 0.001f;
                _trail.Clear();
            }

            gameObject.SetActive(true);
        }

        /// <summary>强制收手（玩家死亡 / 切场景时由投掷者调用）。</summary>
        public void Cancel()
        {
            if (_active)
            {
                Finish();
            }
        }

        private void FixedUpdate()
        {
            if (!_active)
            {
                return;
            }

            _flightTime += Time.fixedDeltaTime;

            // 玩家没了（切场景 / 被销毁）→ 直接收掉，避免钳子永远留在空中
            if (_p.ReturnTarget == null)
            {
                Finish();
                return;
            }

            if (!_returning)
            {
                Vector2 pos = transform.position;
                Vector2 step = _dir * (_p.Speed * Time.fixedDeltaTime);
                if (Sweep(pos, _dir, step.magnitude))
                {
                    // 撞到实体障碍：不等飞满射程，立刻折返
                    BeginReturn();
                }
                else
                {
                    transform.position = pos + step;
                    _traveled += step.magnitude;
                    if (_traveled >= _p.Range || _flightTime >= _p.MaxFlightTime)
                    {
                        BeginReturn();
                    }
                }
            }
            else
            {
                Vector2 pos = transform.position;
                Vector2 delta = (Vector2)_p.ReturnTarget.position - pos;
                float dist = delta.magnitude;

                if (dist <= ArriveRadius)
                {
                    Finish();
                    return;
                }

                float stepLen = Mathf.Min(_p.ReturnSpeed * Time.fixedDeltaTime, dist);
                Vector2 dir = delta / Mathf.Max(dist, 0.0001f);
                Sweep(pos, dir, stepLen);
                transform.position = pos + dir * stepLen;

                // 回程也给一个上限：目标在跑动中追上耗时偏长时不至于无限飞
                if (_flightTime >= _p.MaxFlightTime * 2f)
                {
                    Finish();
                    return;
                }
            }

            if (_p.SpinSpeed != 0f)
            {
                _spin = Mathf.Repeat(_spin + _p.SpinSpeed * Time.fixedDeltaTime, 360f);
                transform.rotation = Quaternion.Euler(0f, 0f, _spin);
            }
        }

        private void BeginReturn()
        {
            _returning = true;
            _hitThisPass.Clear();   // 回程可以再打一次（同一阶段内仍不重复）
        }

        /// <summary>
        /// 沿 origin → origin+dir*distance 做圆形扫描：结算命中、判断是否撞墙。
        /// 返回 true 表示路径上有实体障碍（需要折返）。
        /// </summary>
        private bool Sweep(Vector2 origin, Vector2 dir, float distance)
        {
            if (distance <= 0.0001f)
            {
                return false;
            }

            int count = Physics2D.CircleCastNonAlloc(
                origin, Mathf.Max(0.05f, _p.HitRadius), dir, _castHits, distance, Physics2D.AllLayers);

            bool hitWall = false;
            for (int i = 0; i < count; i++)
            {
                Collider2D col = _castHits[i].collider;
                if (col == null || !col.enabled)
                {
                    continue;
                }

                // 自己（玩家本体 / 工具锚点 / 判定盒）不算命中也不算墙
                if (_p.Source != null && col.transform.IsChildOf(_p.Source.transform))
                {
                    continue;
                }

                var target = col.GetComponentInParent<IHittable>();
                if (target != null)
                {
                    if (target is PlayerCombat)
                    {
                        continue;   // 不打自己
                    }

                    if (_hitThisPass.Add(target))
                    {
                        ApplyHit(target, dir);
                    }
                    continue;   // 打中目标不折返，继续往前飞
                }

                // 非目标的实体碰撞体才是墙；装饰性触发器不挡道
                if (!col.isTrigger)
                {
                    hitWall = true;
                }
            }

            return hitWall;
        }

        private void ApplyHit(IHittable target, Vector2 flightDir)
        {
            // SourceDirection 语义：受击者 → 来源，所以取飞行方向的反向
            var info = new DamageInfo(_p.Damage, -flightDir, _p.Source)
            {
                KnockbackPower = _p.Knockback
            };
            target.TakeDamage(info);

            // 命中手感：轻微顿帧 + 震屏（比近战第三段轻，避免连续触发太吵）
            HitFeel.Play(0.055f, 0.08f);
        }

        private void Finish()
        {
            _active = false;
            if (_trail != null && _trail.enabled)
            {
                // 先收起发射再清点，否则关物体那一帧可能残留一段拖尾
                _trail.emitting = false;
                _trail.Clear();
            }
            gameObject.SetActive(false);

            var callback = _onReturned;
            _onReturned = null;
            callback?.Invoke();
        }
    }
}
