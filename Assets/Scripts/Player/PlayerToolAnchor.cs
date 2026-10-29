using UnityEngine;

namespace StarEco.Player
{
    /// <summary>
    /// 手中工具的锚点跟随器（武器解耦架构的运行时部分）。
    ///
    /// 设计动机：早期美术把钳子烘焙进角色帧里，导致换动作就要重画钳子，且钳子无法单独做
    /// 挥击/切换。改为：角色帧只画空手，钳子是独立 SpriteRenderer 子物体，由 AnimationClip
    /// 里 ToolAnchor 的 m_LocalPosition 曲线逐帧贴到手上。
    ///
    /// 本组件的职责只有一件：角色用 SpriteRenderer.flipX 转向（而非 transform 缩放），
    /// 子物体不会自动镜像，所以在这里把动画算出的「朝右」锚点镜像成「朝左」，
    /// 同时让工具贴图一起翻转。动画系统每帧都会重新写入 localPosition，
    /// 因此这里的覆盖不会累积。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PlayerToolAnchor : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _tool;

        /// <summary>上一帧是否已经把锚点镜像成朝左，用于从被覆盖过的值反推朝右基准。</summary>
        private bool _appliedFlip;

        /// <summary>工具精灵（投掷物按同一张图生成，保证“飞出去的”和“手里的”是同一个东西）。</summary>
        public Sprite ToolSprite => _tool != null ? _tool.sprite : null;

        /// <summary>工具渲染器（投掷期间接管显隐、读取排序层用）。</summary>
        public SpriteRenderer ToolRenderer => _tool;

        /// <summary>
        /// 单独开关工具的可见性。
        /// 只动 SpriteRenderer.enabled，不动 GameObject.SetActive：动画 clip 每帧都会写
        /// ToolAnchor 的 m_IsActive，用 SetActive 隐藏会被下一帧动画覆盖回来。
        /// </summary>
        public void SetToolVisible(bool visible)
        {
            if (_tool != null)
            {
                _tool.enabled = visible;
            }
        }

        /// <summary>当前工具是否可见（被动画整体藏掉时也是 false）。</summary>
        public bool IsToolVisible => _tool != null && _tool.enabled && _tool.gameObject.activeInHierarchy;

        private void Reset()
        {
            _body = GetComponentInParent<SpriteRenderer>();
            _tool = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            if (_body == null)
            {
                return;
            }

            bool flip = _body.flipX;

            // 动画刚写入的是「朝右」的原始值；若上一帧我们镜像过它，先还原再重新判定。
            var pos = transform.localPosition;
            if (_appliedFlip)
            {
                pos.x = -pos.x;
            }

            var euler = transform.localEulerAngles;
            if (_appliedFlip)
            {
                euler.z = -euler.z;
            }

            if (flip)
            {
                pos.x = -pos.x;
                euler.z = -euler.z;
            }

            transform.localPosition = pos;
            transform.localEulerAngles = euler;

            if (_tool != null)
            {
                _tool.flipX = flip;

                // 排序必须运行时跟随：角色 SpriteRenderer 的 sortingOrder 不是 0 ——
                // 关卡脚本（FixAllLevelSpriteSorting 等）会按场景把角色抬到 15，
                // 而预制体固化时拿到的是 0。写死任何绝对值的后果都是工具被角色整个盖住。
                _tool.sortingLayerID = _body.sortingLayerID;
                _tool.sortingOrder = _body.sortingOrder + 1;
            }

            _appliedFlip = flip;
        }
    }
}
