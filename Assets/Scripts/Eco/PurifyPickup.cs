using UnityEngine;
using StarEco.Framework;

namespace StarEco.Eco
{
    /// <summary>
    /// 净化剂拾取物：Trigger 碰撞玩家 → 回复净化剂 → 广播事件 → 归池。
    /// 无需按键，触碰即拾取；归池不 Destroy（同 FxOneShot 模式）。
    /// </summary>
    public class PurifyPickup : MonoBehaviour
    {
        [SerializeField] private EcoConfig _config;

        private PurifyPickup _sourcePrefab;

        public EcoConfig Config => _config;

        private void Awake()
        {
            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }
        }

        /// <summary>池化初始化：由生成方（DropSpawner）调用，记录来源预制体供归还。</summary>
        public void Init(PurifyPickup sourcePrefab)
        {
            _sourcePrefab = sourcePrefab;
        }

        /// <summary>池化生成后的统一入口（同 FxOneShot.Play 模式）。</summary>
        public static void Spawn(PurifyPickup prefab, Vector3 position)
        {
            if (prefab == null || PoolManager.Instance == null)
            {
                Debug.LogWarning("[PurifyPickup] 预制体或 PoolManager 缺失，跳过生成。");
                return;
            }

            var pickup = PoolManager.Instance.Get<PurifyPickup>(prefab);
            pickup.transform.position = position;
            pickup.Init(prefab);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 玩家层判定（碰撞回调内一次性 GetComponent，符合规范）
            var tank = other.GetComponentInParent<PurifyTank>();
            if (tank == null)
            {
                return;
            }

            int amount = _config != null ? _config.PickupTankAmount : 20;
            tank.Restore(amount);

            AudioManager.Instance?.PlaySfx(SfxId.Pickup);   // M10

            EventCenter.Publish(new Events.PickupCollectedEvent
            {
                Kind = PickupKind.PurifyBottle,
                Amount = amount
            });

            ReturnToPool();
        }

        private void ReturnToPool()
        {
            if (_sourcePrefab != null && PoolManager.Instance != null)
            {
                PoolManager.Instance.Release<PurifyPickup>(_sourcePrefab, this);
                return;
            }

            // 兜底：非池化实例直接隐藏（同 FxOneShot 兜底策略）
            if (_sourcePrefab == null)
            {
                Debug.LogWarning("[PurifyPickup] 缺少来源预制体引用，拾取物仅隐藏不归池。");
            }

            gameObject.SetActive(false);
        }
    }
}
