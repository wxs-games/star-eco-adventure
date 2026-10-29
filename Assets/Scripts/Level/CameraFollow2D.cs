using UnityEngine;
using UnityEngine.Audio;

namespace StarEco.Level
{
    /// <summary>
    /// 2D 相机跟随（M9 R5）：Level02 横向 56u，静态相机会让玩家走出视口。
    /// 最简实现：仅跟随 X（Y 固定），按关卡边界与视口半宽动态钳制；
    /// 关卡片段内生效，不影响 M6/M8 场景（它们不挂本组件）。
    /// </summary>
    public class CameraFollow2D : MonoBehaviour
    {
        [Tooltip("跟随目标（为空则自动找 Player）")]
        [SerializeField] private Transform _target;

        [Tooltip("X 平滑系数（越大越紧跟）")]
        [SerializeField] private float _smoothX = 5f;

        [Tooltip("固定 Y")]
        [SerializeField] private float _fixedY = 2.2f;

        [Tooltip("关卡左边界 X")]
        [SerializeField] private float _levelLeft = -28f;

        [Tooltip("关卡右边界 X")]
        [SerializeField] private float _levelRight = 28f;

        private Camera _cam;
        private Transform _self;
        private bool _snapRequested;
        private Vector3? _snapTo;

        // 命中震屏：只叠在最终位置上，不改变跟随逻辑与钳制结果
        private float _shakeTimer;
        private float _shakeDuration;
        private float _shakeAmplitude;

        /// <summary>请求一次轻震屏（时长秒 + 振幅世界单位）。重复调用取较强的一次。</summary>
        public void Shake(float duration, float amplitude)
        {
            if (duration <= 0f || amplitude <= 0f)
            {
                return;
            }

            _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
            _shakeTimer = Mathf.Max(_shakeTimer, duration);
            _shakeDuration = Mathf.Max(_shakeDuration, _shakeTimer);
        }

        /// <summary>
        /// 让相机在下一帧直接定位到目标的合法位置，而不是平滑插值过去。
        /// 用于玩家瞬移等场景：否则相机要花近 1 秒才追上玩家，期间画面停在被传送前的地方。
        /// </summary>
        public void SnapToTarget(Transform target = null)
        {
            if (target != null)
            {
                _target = target;
            }

            _snapRequested = true;
        }

        /// <summary>直接定位到指定世界坐标（仅 X 生效，Y 仍用固定值），同样跳过插值。</summary>
        public void SnapToX(float worldX)
        {
            _snapTo = new Vector3(worldX, 0f, 0f);
            _snapRequested = true;
        }

        private void Awake()
        {
            _self = transform;
            _cam = GetComponent<Camera>();

            // 运行态兜底：场景若漏配 AudioListener，会导致控制台持续刷屏，表现像“卡住”。
            if (_cam != null && GetComponent<AudioListener>() == null)
            {
                if (FindObjectOfType<AudioListener>() == null)
                {
                    gameObject.AddComponent<AudioListener>();
                }
            }
        }

        private void LateUpdate()
        {
            if (_cam == null || _self == null)
            {
                return;
            }

            if (_target == null)
            {
                var pc = FindObjectOfType<StarEco.Player.PlayerController>();
                if (pc == null)
                {
                    return;
                }
                _target = pc.transform;
            }

            float halfWidth = _cam.orthographicSize * _cam.aspect;

            float targetX = _target.position.x;

            // 瞬移请求：本帧直接到达，不做插值（玩家瞬移）
            if (_snapRequested)
            {
                if (_snapTo.HasValue)
                {
                    targetX = _snapTo.Value.x;
                    _snapTo = null;
                }

                float snappedX = ClampX(targetX, halfWidth);
                _self.position = ApplyShake(new Vector3(snappedX, _fixedY, _self.position.z));
                _snapRequested = false;
                return;
            }

            targetX = ClampX(targetX, halfWidth);

            float newX = Mathf.Lerp(_self.position.x, targetX, Mathf.Clamp01(_smoothX * Time.deltaTime));
            _self.position = ApplyShake(new Vector3(newX, _fixedY, _self.position.z));
        }

        /// <summary>叠加震动偏移：用 unscaledDeltaTime，顿帧期间也能看到抖动。</summary>
        private Vector3 ApplyShake(Vector3 basePosition)
        {
            if (_shakeTimer <= 0f)
            {
                _shakeAmplitude = 0f;
                return basePosition;
            }

            _shakeTimer -= Time.unscaledDeltaTime;
            float ratio = _shakeDuration > 0.0001f ? Mathf.Clamp01(_shakeTimer / _shakeDuration) : 0f;
            Vector2 offset = Random.insideUnitCircle * (_shakeAmplitude * ratio);
            return new Vector3(basePosition.x + offset.x, basePosition.y + offset.y, basePosition.z);
        }

        /// <summary>按关卡边界与视口半宽钳制相机 X。</summary>
        private float ClampX(float x, float halfWidth)
        {
            float minX = _levelLeft + halfWidth;
            float maxX = _levelRight - halfWidth;

            // 关卡比视口还窄时，居中显示
            return maxX > minX ? Mathf.Clamp(x, minX, maxX) : (_levelLeft + _levelRight) * 0.5f;
        }
    }
}
