using UnityEngine;
using StarEco.Framework;
using StarEco.Eco.Events;

namespace StarEco.Eco
{
    /// <summary>
    /// 净化值系统（场景级）：Start 时收集场景内全部 active 的 IPurifiable 统计总量，
    /// 每次净化完成累加并广播 PurityChangedEvent；100% 时 one-shot 广播 LevelPurifiedEvent。
    /// 净化值只增不减（本里程碑无再污染机制）。池化对象不参与统计。
    /// </summary>
    public class PuritySystem : MonoBehaviour
    {
        public int Total { get; private set; }
        public int Current { get; private set; }

        /// <summary>净化进度百分比 [0, 1]（无目标时防御性为 1，即 100%）。</summary>
        public float Percent => Total > 0 ? (float)Current / Total : 1f;

        /// <summary>污染度 = 1 - 百分比（氛围与 M6 刷怪依据）。</summary>
        public float Pollution => 1f - Percent;

        private bool _completed;   // LevelPurifiedEvent one-shot 守卫

        private void Start()
        {
            CollectTargets();
            Broadcast();   // 初始基线（R6：0% 广播一次，供 HUD/氛围获得起点）

            EventCenter.Subscribe<GarbagePurifiedEvent>(OnGarbagePurified);
        }

        private void OnDestroy()
        {
            EventCenter.Unsubscribe<GarbagePurifiedEvent>(OnGarbagePurified);
        }

        // ===== 收集与结算 =====

        private void CollectTargets()
        {
            // 仅 Start 一次性收集（R10 允许）；排除 inactive —— 池化对象不参与
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            int total = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPurifiable target && !target.IsPurified)
                {
                    total += target.PurityValue;
                }
            }

            Total = total;

            if (Total <= 0)
            {
                // 防御分支（R6）：无任何可净化目标视为配置错误
                Debug.LogWarning("[PuritySystem] 场景中不存在任何 IPurifiable 目标，净化值初始化为 100%。");
            }
        }

        private void OnGarbagePurified(GarbagePurifiedEvent evt)
        {
            // M10：任意污染源/堵塞点/塔净化完成，统一走命名音效（库缺失静默跳过）
            AudioManager.Instance?.PlaySfx(SfxId.PurifyComplete);

            Current += evt.PurityValue;

            if (Current > Total)
            {
                Current = Total;   // 防御：显式覆盖值导致超量的夹取
            }

            Broadcast();

            if (Percent >= 1f && !_completed && Total > 0)
            {
                _completed = true;
                EventCenter.Publish(new LevelPurifiedEvent());
            }
        }

        /// <summary>
        /// 重置关卡净化进度（M6 关卡重开）：Current 清零、解除 100% one-shot 守卫并重新广播基线。
        /// Total 不变（场景可净化目标集合未改变）。
        /// </summary>
        public void ResetProgress()
        {
            Current = 0;
            _completed = false;
            Broadcast();
        }

        private void Broadcast()
        {
            EventCenter.Publish(new PurityChangedEvent
            {
                Current = Current,
                Total = Total,
                Percent = Percent,
                Pollution = Pollution
            });
        }
    }
}
