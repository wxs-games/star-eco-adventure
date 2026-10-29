using UnityEngine;

namespace StarEco.Combat
{
    /// <summary>
    /// 测试靶（T9）：占位 IHittable 对象，验证命中回调、伤害数值与第三段击退。
    /// 扣血到 0 打印死亡日志并 2s 后自复位，便于反复测试。
    /// </summary>
    public class TestTarget : MonoBehaviour, IHittable
    {
        [Tooltip("测试靶 HP（够打几轮即可）")]
        public int MaxHP = 10;

        [Tooltip("击退速度：受 KnockbackPower 影响，0 = 不动")]
        public float KnockbackResponse = 1f;

        private int _hp;
        private Rigidbody2D _body;

        private void Awake()
        {
            _hp = MaxHP;
            _body = GetComponent<Rigidbody2D>();
        }

        public void TakeDamage(DamageInfo info)
        {
            _hp -= info.Amount;
            Debug.Log($"[TestTarget] {name} 受到 {info.Amount} 点伤害，剩余 HP {_hp}"
                      + (info.KnockbackPower > 0f ? $"（第三段击退 {info.KnockbackPower}）" : ""),
                      this);

            // 验证击退：沿伤害来源方向的反方向推开
            if (info.KnockbackPower > 0f && _body != null)
            {
                Vector2 push = -info.SourceDirection.normalized * info.KnockbackPower * KnockbackResponse;
                _body.velocity = new Vector2(push.x, Mathf.Abs(push.y));
            }

            if (_hp <= 0)
            {
                Debug.LogWarning($"[TestTarget] {name} 被击败，2s 后复位", this);
                _hp = MaxHP;
            }
        }
    }
}
