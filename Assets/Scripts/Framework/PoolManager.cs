using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarEco.Framework
{
    /// <summary>
    /// 对象池管理器：以预制体为键统一管理各 ObjectPool。
    /// 外部只面向 prefab：Get(poolPrefab) / Release(poolPrefab, obj)。
    /// </summary>
    public class PoolManager : MonoSingleton<PoolManager>
    {
        // 预制体 → 非泛型池包装（对外 API 保持泛型，内部字典统一存储）
        private readonly Dictionary<GameObject, IPoolWrapper> _pools =
            new Dictionary<GameObject, IPoolWrapper>();

        /// <summary>按预制体取对象。prefab 必须是预制体资产，不能是场景对象。</summary>
        public T Get<T>(T prefab, int prewarm = 0) where T : Component
        {
            return GetPool(prefab, prewarm).Get();
        }

        /// <summary>归还对象。</summary>
        public void Release<T>(T prefab, T obj) where T : Component
        {
            if (_pools.TryGetValue(prefab.gameObject, out var pool))
            {
                pool.Release(obj);
            }
            else
            {
                Debug.LogWarning($"[PoolManager] 预制体 {prefab.name} 没有对应池，对象直接销毁处理。");
                Destroy(obj.gameObject);
            }
        }

        /// <summary>取（或创建）预制体对应的池。</summary>
        private ObjectPool<T> GetPool<T>(T prefab, int prewarm) where T : Component
        {
            var key = prefab.gameObject;
            if (!_pools.TryGetValue(key, out var wrapper))
            {
                var pool = new ObjectPool<T>(prefab, prewarm, transform);
                _pools[key] = new PoolWrapper<T>(pool);
                return pool;
            }

            return ((PoolWrapper<T>)wrapper).Pool;
        }

        protected override void OnSingletonAwake()
        {
            // 不随场景清理的池统一挂在自身节点下
        }

        #region 非泛型包装

        private interface IPoolWrapper
        {
            void Release(Component obj);
        }

        private class PoolWrapper<T> : IPoolWrapper where T : Component
        {
            public readonly ObjectPool<T> Pool;

            public PoolWrapper(ObjectPool<T> pool)
            {
                Pool = pool;
            }

            public void Release(Component obj)
            {
                if (obj is T typed)
                {
                    Pool.Release(typed);
                }
            }
        }

        #endregion
    }
}
