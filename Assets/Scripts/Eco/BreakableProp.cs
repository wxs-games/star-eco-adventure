using UnityEngine;
using StarEco.Combat;
using StarEco.Framework;

namespace StarEco.Eco
{
    public enum BreakableKind
    {
        Plant,
        Stone,
    }

    /// <summary>
    /// 可破坏道具（水草 / 石头）：一击即碎，纯装饰不挡路。
    /// 碰撞体是 Trigger，玩家与怪物都能直接穿过，只有攻击命中盒会结算；
    /// 碎裂时按命中方向抛出一组碎块精灵（重力 + 自旋 + 淡出），并播放对应材质的碎裂音。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BreakableProp : MonoBehaviour, IHittable
    {
        [SerializeField] private BreakableKind _kind = BreakableKind.Plant;
        [Tooltip("碎裂音：Plant→BreakPlant，Stone→BreakStone")]
        [SerializeField] private Sprite[] _debrisSprites;
        [SerializeField] private float _debrisLifetime = 1.15f;
        [SerializeField] private float _spreadX = 0.45f;
        [SerializeField] private float _spreadY = 0.35f;
        [SerializeField] private float _impulseMin = 2.0f;
        [SerializeField] private float _impulseMax = 4.2f;
        [SerializeField] private float _upwardBias = 2.6f;
        [SerializeField] private float _gravity = 17f;

        private SpriteRenderer _renderer;
        private bool _broken;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        public void TakeDamage(DamageInfo info)
        {
            if (_broken)
            {
                return;
            }

            _broken = true;

            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            SpawnDebris(info.SourceDirection);

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            AudioManager.Instance?.PlaySfx(
                _kind == BreakableKind.Plant ? SfxId.BreakPlant : SfxId.BreakStone, 0.07f);

            Destroy(gameObject);
        }

        /// <summary>沿命中反方向抛出碎块；SourceDirection 语义是「受击者 → 来源」，故取反。</summary>
        private void SpawnDebris(Vector2 sourceDirection)
        {
            if (_debrisSprites == null || _debrisSprites.Length == 0)
            {
                return;
            }

            Bounds bounds = _renderer != null
                ? _renderer.bounds
                : new Bounds(transform.position, Vector3.one * 0.8f);

            float away = 0f;
            if (sourceDirection.sqrMagnitude > 0.0001f)
            {
                away = sourceDirection.x > 0f ? -1f : 1f;
            }

            for (int i = 0; i < _debrisSprites.Length; i++)
            {
                var sprite = _debrisSprites[i];
                if (sprite == null)
                {
                    continue;
                }

                var go = new GameObject("Debris");
                go.transform.position = bounds.center + new Vector3(
                    Random.Range(-_spreadX, _spreadX) * bounds.extents.x,
                    Random.Range(-_spreadY, _spreadY) * bounds.extents.y, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-25f, 25f));

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = _renderer != null ? _renderer.sortingOrder + 3 : 8;

                float speed = Random.Range(_impulseMin, _impulseMax);
                float dirX = away != 0f ? away : (Random.value < 0.5f ? -1f : 1f);
                var velocity = new Vector2(
                    dirX * speed * Random.Range(0.45f, 1f),
                    Random.Range(0.35f, 1f) * _upwardBias);

                // 地面高度取原道具精灵的底边：碎块落地不会穿进地面
                var debris = go.AddComponent<BreakableDebris>();
                debris.Launch(velocity, Random.Range(-420f, 420f), _gravity, _debrisLifetime, bounds.min.y);
            }
        }
    }
}
