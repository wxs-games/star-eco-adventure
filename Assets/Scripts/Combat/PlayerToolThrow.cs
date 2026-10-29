using UnityEngine;
using StarEco.Player;

namespace StarEco.Combat
{
    /// <summary>
    /// 钳子投掷（第三段连击的收尾）：把手中的钳子甩出去并负责"还回来"。
    ///
    /// 与武器解耦架构的关系：投掷期间只是把 ToolAnchor 的 SpriteRenderer 关掉（不 SetActive，
    /// 因为动画 clip 每帧都会写 m_IsActive，用 SetActive 会被覆盖），飞出去的是同一个精灵的副本；
    /// 回旋归位抵达手心的那一帧再把渲染器开回来。
    ///
    /// 组件挂在 Player 根上；缺组件时 PlayerController 会自动补一个（见 PlayerController.Awake），
    /// 因此不需要改预制体结构。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerToolThrow : MonoBehaviour
    {
        private PlayerController _player;
        private PlayerCombat _combat;
        private PlayerToolAnchor _anchor;
        private ToolProjectile _projectile;

        /// <summary>钳子是否正在外面（飞行 / 回程）。</summary>
        public bool IsFlying => _projectile != null && _projectile.IsFlying;

        /// <summary>钳子是否在手上。</summary>
        public bool ToolInHand => !IsFlying;

        private void EnsureRefs()
        {
            if (_player == null)
            {
                _player = GetComponent<PlayerController>();
            }
            if (_combat == null)
            {
                _combat = GetComponent<PlayerCombat>();
            }
            if (_anchor == null)
            {
                // ToolAnchor 可能因动画曲线被整体隐藏（inactive），必须带 includeInactive
                _anchor = GetComponentInChildren<PlayerToolAnchor>(true);
            }
        }

        /// <summary>
        /// 出手。钳子已在空中 / 找不到工具精灵时返回 false（调用方当作"这一下空挥"处理）。
        /// </summary>
        public bool TryThrow()
        {
            EnsureRefs();
            if (_player == null || IsFlying)
            {
                return false;
            }

            Sprite sprite = _anchor != null ? _anchor.ToolSprite : null;
            if (sprite == null)
            {
                Debug.LogWarning("[PlayerToolThrow] 找不到钳子精灵，本次投掷跳过");
                return false;
            }

            CombatConfig cfg = _combat != null ? _combat.Config : null;
            var p = new ToolProjectile.FlightParams
            {
                Damage = cfg != null ? cfg.ThrowDamage : 3,
                Knockback = cfg != null ? cfg.ThrowKnockback : 11f,
                Speed = cfg != null ? cfg.ThrowSpeed : 17f,
                Range = cfg != null ? cfg.ThrowRange : 5.5f,
                ReturnSpeed = cfg != null ? cfg.ThrowReturnSpeed : 22f,
                HitRadius = cfg != null ? cfg.ThrowHitRadius : 0.26f,
                SpinSpeed = cfg != null ? cfg.ThrowSpinSpeed : 900f,
                MaxFlightTime = cfg != null ? cfg.ThrowMaxFlightTime : 4f,
                VisualScale = cfg != null ? cfg.ThrowVisualScale : 1.25f,
                TrailTime = cfg != null ? cfg.ThrowTrailTime : 0.14f,
                Source = gameObject,
                ReturnTarget = _anchor != null ? _anchor.transform : transform
            };

            int facing = _player.FacingDir;
            Vector2 start = _anchor != null
                ? (Vector2)_anchor.transform.position
                : (Vector2)transform.position + new Vector2(facing * 0.4f, 0.2f);

            if (_projectile == null)
            {
                SpriteRenderer body = _player.Sprite;
                int layerId = body != null ? body.sortingLayerID : 0;
                int order = body != null ? body.sortingOrder + 3 : 20;
                _projectile = ToolProjectile.Create(sprite, layerId, order);
            }

            _projectile.Launch(start, facing, p, OnToolReturned);
            _anchor?.SetToolVisible(false);
            return true;
        }

        /// <summary>回旋归位：把手中钳子显形。</summary>
        private void OnToolReturned()
        {
            if (this == null)
            {
                return;
            }

            EnsureRefs();
            _anchor?.SetToolVisible(true);
        }
    }
}
