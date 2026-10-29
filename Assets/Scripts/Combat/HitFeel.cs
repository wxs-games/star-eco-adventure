using System.Collections;
using UnityEngine;
using StarEco.Level;

namespace StarEco.Combat
{
    /// <summary>
    /// 命中手感：极短顿帧 + 轻震屏。
    /// 空洞骑士式反馈的核心是“命中瞬间时间几乎停住，随后立刻恢复”，
    /// 时长控制在 40~70ms，不改变任何伤害/位移数值，只影响观感节奏。
    /// </summary>
    public static class HitFeel
    {
        private const float DefaultStopSeconds = 0.045f;
        private const float DefaultShakeAmplitude = 0.05f;

        private static HitFeelRunner _runner;
        private static CameraFollow2D _cameraFollow;

        /// <summary>玩家攻击命中后调用：顿帧 + 轻震屏。</summary>
        public static void Play(float stopSeconds = DefaultStopSeconds,
            float shakeAmplitude = DefaultShakeAmplitude)
        {
            // 暂停中不抢 timeScale
            if (Time.timeScale <= 0f)
            {
                return;
            }

            if (_runner == null)
            {
                var go = new GameObject("HitFeelRunner", typeof(HitFeelRunner));
                Object.DontDestroyOnLoad(go);
                _runner = go.GetComponent<HitFeelRunner>();
            }

            _runner.Play(stopSeconds);

            if (_cameraFollow == null)
            {
                _cameraFollow = Object.FindObjectOfType<CameraFollow2D>();
            }

            if (_cameraFollow != null)
            {
                _cameraFollow.Shake(Mathf.Max(0.08f, stopSeconds * 2.2f), shakeAmplitude);
            }
        }
    }

    /// <summary>顿帧协程宿主：全局唯一，避免每次命中新建对象。</summary>
    public class HitFeelRunner : MonoBehaviour
    {
        private Coroutine _running;

        public void Play(float seconds)
        {
            if (_running != null)
            {
                return;   // 连续命中不叠加，避免顿帧越来越长
            }

            _running = StartCoroutine(StopFrames(Mathf.Max(0.01f, seconds)));
        }

        private IEnumerator StopFrames(float seconds)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(seconds);

            // 顿帧期间若被暂停菜单接管（timeScale 仍为 0），不要擅自恢复。
            Time.timeScale = 1f;
            _running = null;
        }
    }
}
