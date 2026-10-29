using UnityEngine;
using StarEco.Combat;
using StarEco.Framework;
using StarEco.UI;

namespace StarEco.EnemyAI
{
    /// <summary>
    /// 毒气弹体：经对象池复用，命中玩家结算一次伤害 + fx_gas，触地/超程归还。
    /// 位移只在 FixedUpdate（性能红线）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyProjectile : MonoBehaviour
    {
        private Rigidbody2D _body;
        private SpriteRenderer _sprite;
        private TrailRenderer _trail;

        private EnemyProjectile _sourcePrefab;
        private Vector2 _direction;
        private float _speed;
        private float _maxRange;
        private int _damage;
        private float _knockback;
        private GameObject _source;
        private int _ownerLayer;

        private float _traveledDistance;
        private bool _resolved;
        private bool _launched;

        /// <summary>弹体发射参数。</summary>
        public struct ProjectileParams
        {
            public EnemyProjectile Prefab;
            public Vector2 Direction;
            public float Speed;
            public float MaxRange;
            public int Damage;
            public float Knockback;
            public GameObject Source;
            public int OwnerLayer;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _sprite = GetComponent<SpriteRenderer>();
            _trail = GetComponent<TrailRenderer>();
        }

        private void OnEnable()
        {
            _traveledDistance = 0f;
            _resolved = false;
            _launched = false;
        }

        /// <summary>发射弹体（池取出后调用）。</summary>
        public void Launch(ProjectileParams p)
        {
            _sourcePrefab = p.Prefab;
            _direction = p.Direction.sqrMagnitude > 0.0001f ? p.Direction.normalized : Vector2.right;
            _speed = p.Speed;
            _maxRange = p.MaxRange;
            _damage = p.Damage;
            _knockback = p.Knockback;
            _source = p.Source;
            _ownerLayer = p.OwnerLayer;
            _traveledDistance = 0f;
            _resolved = false;
            _launched = true;

            // 弹体造型朝右，按飞行方向旋转（扇形弹幕也能正对目标）
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg);
            if (_sprite != null)
            {
                _sprite.flipX = false;
            }
            // 池复用后清掉上一次的拖尾残影，避免从旧位置拉出一条长线
            if (_trail != null)
            {
                _trail.Clear();
                _trail.emitting = true;
            }
        }

        private void FixedUpdate()
        {
            if (!_launched || _resolved)
            {
                return;
            }

            Vector2 next = _body.position + _direction * _speed * Time.fixedDeltaTime;
            _body.MovePosition(next);
            _traveledDistance += _speed * Time.fixedDeltaTime;

            if (_traveledDistance >= _maxRange)
            {
                ResolveWithoutHit();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_launched || _resolved)
            {
                return;
            }

            if (_source != null && other.transform.root == _source.transform.root)
            {
                return;
            }
            if (other.gameObject.layer == _ownerLayer)
            {
                return;
            }

            var hittable = other.GetComponentInParent<IHittable>();
            if (hittable != null)
            {
                ResolveWithHit(hittable);
                return;
            }

            // 装饰性触发体（知识卡/告示牌/剧情触发区等）不吸收弹体。
            // 塔口/花蕊旁的装饰触发体会在出膛瞬间吃掉弹体，导致炮塔与远程怪永远打不到玩家；
            // 只有实体碰撞体（地面/墙体/障碍）才按「触地归还」结算。
            if (other.isTrigger)
            {
                return;
            }

            ResolveWithoutHit();
        }

        private void ResolveWithHit(IHittable target)
        {
            // 对话期间敌人暂停攻击：已飞出的弹体不结算伤害，继续飞行至射程后归还
            if (DialoguePanel.IsDialogueActive)
            {
                return;
            }

            _resolved = true;
            var dmg = new DamageInfo(_damage, -_direction, _source)
            {
                KnockbackPower = _knockback
            };
            target.TakeDamage(dmg);
            ReturnToPool();
        }

        private void ResolveWithoutHit()
        {
            _resolved = true;
            ReturnToPool();
        }

        private void ReturnToPool()
        {
            if (_sourcePrefab != null && PoolManager.Instance != null)
            {
                PoolManager.Instance.Release(_sourcePrefab, this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
