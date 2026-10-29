using System.Collections.Generic;
using UnityEngine;
using StarEco.Combat;

namespace StarEco.Eco
{
    /// <summary>
    /// 喷洒判定盒（Player 子物体，BoxCollider2D IsTrigger=true）：
    /// Enter 加入 / Exit 移除 / Stay 兜底补录，维护目标列表并缓存接口引用（结算零 GetComponent）。
    /// 池化目标中途失活由 Unity 补发 Exit 剔除 + 结算前 PruneInvalid 双保险（M4 教训）。
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class SprayHitbox : MonoBehaviour
    {
        /// <summary>单个结算目标：碰撞体 + 两条通道的缓存引用（均为 null 表示无效）。</summary>
        public struct Target
        {
            public Collider2D Collider;
            public IPurifiable Purifiable;
            public IHittable Hittable;
        }

        private readonly List<Target> _targets = new List<Target>();

        /// <summary>当前判定盒内目标（结算方调用前应先 PruneInvalid）。</summary>
        public IReadOnlyList<Target> Targets => _targets;

        private void OnTriggerEnter2D(Collider2D other)
        {
            AddTarget(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // 兜底补录：Enter 丢失（目标先激活/同帧重叠）时 Stay 补上
            AddTarget(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            RemoveTarget(other);
        }

        private void AddTarget(Collider2D other)
        {
            if (other == null)
            {
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Collider == other)
                {
                    return;   // 已在列表
                }
            }

            // 两条通道任一命中即登记（垃圾/塔只有 IPurifiable，敌人只有 IHittable）
            var purifiable = other.GetComponentInParent<IPurifiable>();
            var hittable = other.GetComponentInParent<IHittable>();
            if (purifiable == null && hittable == null)
            {
                return;
            }

            _targets.Add(new Target
            {
                Collider = other,
                Purifiable = purifiable,
                Hittable = hittable
            });
        }

        private void RemoveTarget(Collider2D other)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Collider == other)
                {
                    _targets.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>剔除已失效条目（池化对象失活/销毁），结算前由结算方调用。</summary>
        public void PruneInvalid()
        {
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                if (_targets[i].Collider == null)
                {
                    _targets.RemoveAt(i);
                }
            }
        }

        /// <summary>清空目标列表（判定盒关闭时调用，防残留）。</summary>
        public void Clear()
        {
            _targets.Clear();
        }
    }
}
