using UnityEngine;

namespace StarEco.Eco
{
    /// <summary>
    /// 可破坏道具的碎块：不走 Rigidbody2D（避免碎片与玩家/怪物产生实体碰撞），
    /// 用固定重力 + 空气阻力做抛物线，同时自旋并按时间淡出。
    /// 顿帧期间 Time.deltaTime = 0，碎块会跟着一起“定格”，反而强化打击感。
    /// </summary>
    public class BreakableDebris : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _baseColor;
        private Vector2 _velocity;
        private float _spin;
        private float _gravity;
        private float _lifetime;
        private float _age;

        // 落地：碎块不穿地面，落地后弹跳两下再贴地淡出
        private float _restY;
        private int _bounces;

        public void Launch(Vector2 velocity, float spin, float gravity, float lifetime, float floorY)
        {
            _renderer = GetComponent<SpriteRenderer>();
            _baseColor = _renderer != null ? _renderer.color : Color.white;
            _velocity = velocity;
            _spin = spin;
            _gravity = gravity;
            _lifetime = Mathf.Max(0.1f, lifetime);

            float halfHeight = _renderer != null ? _renderer.bounds.size.y * 0.5f : 0.1f;
            _restY = floorY + halfHeight;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            _age += dt;
            _velocity.y -= _gravity * dt;
            _velocity.x *= 1f - Mathf.Min(0.6f, 1.4f * dt);

            transform.position += (Vector3)(_velocity * dt);
            transform.Rotate(0f, 0f, _spin * dt);

            // 落地反馈：弹两下后贴地，避免碎块穿进地面消失
            if (transform.position.y < _restY)
            {
                var p = transform.position;
                p.y = _restY;
                transform.position = p;

                if (_bounces < 2)
                {
                    _bounces++;
                    _velocity.y = Mathf.Abs(_velocity.y) * 0.32f;
                    _velocity.x *= 0.55f;
                    _spin *= 0.45f;
                }
                else
                {
                    _velocity = Vector2.zero;
                    _spin = 0f;
                }
            }

            if (_renderer != null)
            {
                float t = Mathf.Clamp01((_age / _lifetime - 0.45f) / 0.55f);
                var c = _baseColor;
                c.a = 1f - t;
                _renderer.color = c;
            }

            if (_age >= _lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
