using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StarEco.Framework
{
    /// <summary>
    /// 全局事件中心：基于事件类型的发布/订阅，用于系统间解耦通信。
    /// 仅主线程使用。订阅者为 Unity 对象方法时，对象销毁后自动清理失效订阅。
    /// </summary>
    public static class EventCenter
    {
        private static readonly Dictionary<Type, List<Delegate>> Subscribers = new Dictionary<Type, List<Delegate>>();

        /// <summary>订阅事件。同一处理器重复订阅只会登记一次。</summary>
        public static void Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
            {
                Debug.LogWarning("[EventCenter] Subscribe 收到空处理器，已忽略。");
                return;
            }

            var type = typeof(TEvent);
            if (!Subscribers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                Subscribers[type] = list;
            }

            if (!list.Contains(handler))
            {
                list.Add(handler);
            }
        }

        /// <summary>取消订阅。</summary>
        public static void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
            {
                return;
            }

            if (Subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                list.Remove(handler);
            }
        }

        /// <summary>发布事件：通知该类型的全部有效订阅者，失效订阅自动清理。</summary>
        public static void Publish<TEvent>(TEvent evt)
        {
            if (!Subscribers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
            {
                return;
            }

            // 倒序遍历：嵌套 Publish / 失效清理时修改列表不会破坏迭代
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var handler = list[i] as Action<TEvent>;
                if (handler == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                // Unity 伪 null 检测：订阅者所属对象已销毁则清理
                if (handler.Target is Object unityTarget && unityTarget == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                try
                {
                    handler.Invoke(evt);
                }
                catch (Exception ex)
                {
                    // 单个订阅者异常不应中断其余订阅者
                    Debug.LogError($"[EventCenter] 事件 {typeof(TEvent).Name} 的订阅者抛出异常：{ex}");
                }
            }
        }

        /// <summary>清空全部订阅（切应用/测试时用）。</summary>
        public static void ClearAll()
        {
            Subscribers.Clear();
        }
    }
}
