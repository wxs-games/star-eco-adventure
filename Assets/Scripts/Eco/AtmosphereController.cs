using UnityEngine;
using StarEco.Framework;
using StarEco.Eco.Events;

namespace StarEco.Eco
{
    /// <summary>
    /// 场景氛围控制器：订阅净化值变化 → 污染度 → 主摄像机背景色在
    /// 灰紫（污染）/ 自然绿（清新）间平滑插值。仅插值 Camera.backgroundColor，
    /// 不引入后处理栈（内置渲染管线，TDD §4 风险对策）。
    /// </summary>
    public class AtmosphereController : MonoBehaviour
    {
        [SerializeField] private EcoConfig _config;

        private Camera _camera;
        private Color _targetColor;

        /// <summary>当前污染度 [0, 1]（M6 刷怪频率预留查询）。</summary>
        public float CurrentPollution { get; private set; } = 1f;

        private void Awake()
        {
            if (_config == null)
            {
                _config = Resources.Load<EcoConfig>("EcoConfig") ?? EcoConfig.CreateDefault();
            }
        }

        private void Start()
        {
            _camera = Camera.main;

            // 初始基线：立即应用污染度 100% 对应色（R7：不瞬跳）
            _targetColor = _config.PollutedColor;
            CurrentPollution = 1f;
            if (_camera != null)
            {
                _camera.backgroundColor = _config.PollutedColor;
            }

            EventCenter.Subscribe<PurityChangedEvent>(OnPurityChanged);
        }

        private void OnDestroy()
        {
            EventCenter.Unsubscribe<PurityChangedEvent>(OnPurityChanged);
        }

        private void Update()
        {
            if (_camera == null)
            {
                return;
            }

            // 每帧向目标色平滑（design §2.4：Lerp 因子 = 速率 × dt）
            float t = Mathf.Clamp01((_config != null ? _config.AtmosphereLerpSpeed : 2f) * Time.deltaTime);
            _camera.backgroundColor = Color.Lerp(_camera.backgroundColor, _targetColor, t);
        }

        private void OnPurityChanged(PurityChangedEvent evt)
        {
            CurrentPollution = evt.Pollution;

            // 目标色：污染度 0 = 清新绿，1 = 灰紫
            Color clean = _config != null ? _config.CleanColor : Color.green;
            Color polluted = _config != null ? _config.PollutedColor : Color.gray;
            _targetColor = Color.Lerp(clean, polluted, evt.Pollution);
        }
    }
}
