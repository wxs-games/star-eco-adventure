using UnityEngine;
using StarEco.Framework;

namespace StarEco.Eco
{
    /// <summary>
    /// 通用一次性特效：播放方经对象池取出后 Init 注入来源 prefab，动画播完自动归还池。
    /// 不绑定具体特效（M6+ 可复用于任意 fx_* 预制体）。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FxOneShot : MonoBehaviour
    {
        private Animator _animator;
        private FxOneShot _sourcePrefab;
        private bool _resolved;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            _resolved = false;
        }

        /// <summary>池取出后由播放方调用：注入来源预制体（播完按此归还）。</summary>
        public void Init(FxOneShot sourcePrefab)
        {
            _sourcePrefab = sourcePrefab;
        }

        /// <summary>便捷播放：经对象池取出、摆位、注入 prefab（prefab 为空静默跳过）。</summary>
        public static void Play(FxOneShot prefab, Vector3 position)
        {
            if (prefab == null || PoolManager.Instance == null)
            {
                return;
            }

            var fx = PoolManager.Instance.Get(prefab);
            fx.transform.position = position;
            fx.Init(prefab);
        }

        private void Update()
        {
            if (_resolved)
            {
                return;
            }

            if (_animator != null && _animator.isActiveAndEnabled
                && _animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f)
            {
                Resolve();
            }
        }

        private void Resolve()
        {
            _resolved = true;

            if (_sourcePrefab != null && PoolManager.Instance != null)
            {
                PoolManager.Instance.Release(_sourcePrefab, this);
            }
            else
            {
                // 兜底：无池引用时直接隐藏（与 M4 Recycle 兜底同策略）
                gameObject.SetActive(false);
                if (_sourcePrefab == null)
                {
                    Debug.LogWarning("[FxOneShot] 缺少来源 prefab 引用，特效直接隐藏未归池。");
                }
            }
        }
    }
}
