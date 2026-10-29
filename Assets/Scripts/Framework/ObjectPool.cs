using System.Collections.Generic;
using UnityEngine;

namespace StarEco.Framework
{
    /// <summary>
    /// 通用组件对象池（非 Mono 纯 C# 类）。
    /// Get 取出并激活，Release 归还并隐藏，全程不销毁对象。
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _root;
        private readonly Stack<T> _free = new Stack<T>();
        private readonly HashSet<T> _loaned = new HashSet<T>();

        public int FreeCount => _free.Count;
        public int LoanedCount => _loaned.Count;

        /// <param name="prefab">池化预制体</param>
        /// <param name="prewarm">预热数量（提前实例化空闲对象）</param>
        /// <param name="parent">池根节点的父物体（通常传 PoolManager 的容器）</param>
        public ObjectPool(T prefab, int prewarm = 0, Transform parent = null)
        {
            _prefab = prefab;

            _root = new GameObject($"Pool_{prefab.name}").transform;
            if (parent != null)
            {
                _root.SetParent(parent, false);
            }

            for (int i = 0; i < prewarm; i++)
            {
                _free.Push(Create());
            }
        }

        /// <summary>取一个对象（空闲优先，池空则实例化）。</summary>
        public T Get()
        {
            T obj;
            if (_free.Count > 0)
            {
                obj = _free.Pop();
            }
            else
            {
                obj = Create();
            }

            obj.transform.SetParent(null, false);
            obj.gameObject.SetActive(true);
            _loaned.Add(obj);
            return obj;
        }

        /// <summary>归还对象：隐藏、挂回池根、入队。重复归还安全忽略。</summary>
        public void Release(T obj)
        {
            if (obj == null)
            {
                return;
            }

            if (!_loaned.Remove(obj))
            {
                // 不属于本池的借出记录：可能是重复归还或外部对象，防御性忽略
                Debug.LogWarning($"[ObjectPool] {typeof(T).Name} 的对象重复归还或非本池借出，已忽略。");
                return;
            }

            obj.gameObject.SetActive(false);
            obj.transform.SetParent(_root, false);
            _free.Push(obj);
        }

        private T Create()
        {
            var obj = Object.Instantiate(_prefab, _root);
            obj.gameObject.SetActive(false);
            return obj;
        }
    }
}
