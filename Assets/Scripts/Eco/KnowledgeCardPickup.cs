using UnityEngine;
using StarEco.Level;

namespace StarEco.Eco
{
    /// <summary>
    /// 知识卡片拾取物（M7 R8）：玩家碰到即收集，写图鉴进度并隐藏自身（幂等）。
    /// 场景中每张卡片一个实例；CardId 对应 knowledge_cards.json 的 Id。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class KnowledgeCardPickup : MonoBehaviour
    {
        [Tooltip("卡片 Id（对应 Resources/Data/knowledge_cards.json 的 Id）")]
        [SerializeField] private string _cardId = "kc_plastic_bottle";

        [Tooltip("是否已在本局被拾取（场景保存用，避免重载前重复）")]
        [SerializeField] private bool _taken;

        private bool _collected;
        private SpriteRenderer _sprite;

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            NormalizeTriggerRadius();
        }

        /// <summary>
        /// 把触发半径归一到目标世界尺寸（默认 0.6u）。
        /// 场景搭建用 WhiteSquare 放大色块（bounds 仅 0.04u，scale≈12.5），
        /// 不归一的话 radius=0.6 会变成 7.5u 世界半径，把大片区域算进触发区。
        /// </summary>
        private void NormalizeTriggerRadius()
        {
            var col = GetComponent<CircleCollider2D>();
            if (col == null)
            {
                return;
            }

            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            if (scale > 0.0001f)
            {
                col.radius = 0.6f / scale;
            }
        }

        private void Start()
        {
            // 已入档则直接隐藏（重开场景后不再显示已收集的卡片）
            _collected = _taken || UI.CodexService.IsCollected(_cardId);
            if (_collected && _sprite != null)
            {
                _sprite.enabled = false;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_collected)
            {
                return;
            }

            var player = other.GetComponent<StarEco.Player.PlayerController>();
            if (player == null)
            {
                return;
            }

            var card = UI.CodexService.Find(_cardId);
            if (card == null)
            {
                Debug.LogError($"[KnowledgeCardPickup] 未知卡片 Id：{_cardId}");
                return;
            }

            bool isNew = UI.CodexService.CollectCard(_cardId);
            _collected = true;
            _taken = true;

            if (_sprite != null)
            {
                _sprite.enabled = false;
            }

            if (isNew)
            {
                Debug.Log($"[KnowledgeCardPickup] 获得知识卡片：{card.Title}");
            }
        }

        private void OnValidate()
        {
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }
    }
}
