using UnityEngine;
using StarEco.Framework;
using StarEco.Player;
using StarEco.Combat;
using StarEco.Eco;
using StarEco.Eco.Events;
using StarEco.Level.Events;

namespace StarEco.Level
{
    /// <summary>
    /// 关卡管理器（M6 流程中枢，R2）：单例、状态机、终态结算与写档。
    /// 对 M1~M5 零改动，只调用公开接口与订阅事件。
    /// 依赖（PuritySystem / LevelStats / LevelObjectiveTracker / LevelSpawner）用惰性查找，
    /// 因为场景各组件 Awake 顺序不确定。
    /// </summary>
    public class LevelManager : MonoSingleton<LevelManager>
    {
        [SerializeField] private LevelConfig _config;

        /// <summary>当前关卡状态。</summary>
        public LevelState State { get; private set; } = LevelState.NotStarted;

        /// <summary>当前使用的关卡配置。</summary>
        public LevelConfig Config => _config;

        private PlayerController _player;
        private PuritySystem _purity;
        private LevelStats _stats;
        private LevelObjectiveTracker _tracker;
        private LevelSpawner _spawner;

        private bool _finished;   // 终态幂等守卫

        protected override void OnSingletonAwake()
        {
            State = LevelState.NotStarted;
            EventCenter.Subscribe<LevelPurifiedEvent>(OnLevelPurified);
            EventCenter.Subscribe<RestartLevelEvent>(OnRestartRequested);
        }

        protected override void OnDestroy()
        {
            EventCenter.Unsubscribe<LevelPurifiedEvent>(OnLevelPurified);
            EventCenter.Unsubscribe<RestartLevelEvent>(OnRestartRequested);

            if (_spawner != null)
            {
                _spawner.Stop();
            }
        }

        private void Start()
        {
            ResolveDependencies();

            if (_config == null)
            {
                _config = Resources.Load<LevelConfig>("Level01Config");
            }

            if (!HasRequiredDependencies())
            {
                Debug.LogError("[LevelManager] 关卡依赖缺失（LevelConfig / PlayerController / PuritySystem），关卡无法开始。");
                SetState(LevelState.Failed);
                return;
            }

            StartLevel();
        }

        private void ResolveDependencies()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
            }

            if (_purity == null)
            {
                _purity = FindObjectOfType<PuritySystem>();
            }

            if (_stats == null)
            {
                _stats = FindObjectOfType<LevelStats>();
            }

            if (_tracker == null)
            {
                _tracker = FindObjectOfType<LevelObjectiveTracker>();
            }

            if (_spawner == null)
            {
                _spawner = FindObjectOfType<LevelSpawner>();
            }
        }

        private bool HasRequiredDependencies()
        {
            return _config != null && _player != null && _purity != null && _stats != null && _tracker != null;
        }

        // ===== 流程 =====

        /// <summary>开始关卡：进入 Playing 并启动目标链与刷怪（R2）。</summary>
        public void StartLevel()
        {
            ResolveDependencies();

            if (!HasRequiredDependencies())
            {
                Debug.LogError("[LevelManager] 关卡依赖缺失，无法开始。");
                SetState(LevelState.Failed);
                return;
            }

            if (_config == null)
            {
                _config = LevelConfig.CreateDefault();
            }

            _finished = false;

            _stats.Reset();
            _tracker.Initialize(_config, _purity, _stats, OnObjectiveChainCompleted);

            if (_player != null)
            {
                _player.InputLocked = false;
                _player.transform.position = _config.SpawnPoint;
            }

            SetState(LevelState.Playing);

            // M10：进入关卡播放对应 BGM（素材缺失静默跳过）
            AudioManager.Instance?.PlayBgmForLevel(_config.LevelIndex);

            if (_spawner != null)
            {
                _spawner.Begin(_config, _tracker, _player != null ? _player.transform : null);
            }
        }

        /// <summary>
        /// 重开关卡（结算面板"重新开始"）：重载当前场景。
        /// 为什么重载而不是原地重置：已净化对象、熄火/永久关闭的敌人等运行时状态都是单向的，
        /// 原地重置无法真正回到初始态；重载场景让所有对象重新初始化，是唯一彻底的方案。
        /// </summary>
        public void RestartLevel()
        {
            var scene = gameObject.scene;

            // LevelManager 挂在非根节点上（LevelSetup 保证），故不会被 MonoSingleton 标记为
            // DontDestroyOnLoad，随场景一起销毁；这里显式禁用以确保本帧不再 Update。
            enabled = false;
            gameObject.SetActive(false);

            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
        }

        private void Update()
        {
            if (State != LevelState.Playing)
            {
                return;
            }

            if (_player != null && _player.IsDead)
            {
                // 死亡即结束本局：直接进入失败终态并弹出结算面板（重新开始 / 返回主菜单）。
                // 重开本关由结算面板的"重新开始"重载场景完成，不做原地续命。
                FinishLevel(false, "玩家被击败");
            }
        }

        private void SetState(LevelState next)
        {
            if (State == next)
            {
                return;
            }

            var old = State;
            State = next;
            EventCenter.Publish(new LevelStateChangedEvent { OldState = old, NewState = next });
        }

        /// <summary>进入终态：停表 → 冻结输入 → 生成结算数据 → 写档 → 广播（R2/R6）。</summary>
        private void FinishLevel(bool success, string reason)
        {
            if (_finished)
            {
                return;   // 终态幂等（R2）
            }

            _finished = true;

            _spawner?.Stop();

            if (_player != null)
            {
                _player.InputLocked = true;
            }

            var result = BuildResult(success);
            LevelSaveService.CommitResult(result);

            if (success)
            {
                AudioManager.Instance?.PlaySfx(SfxId.LevelComplete);   // M10
                SetState(LevelState.Completed);
                EventCenter.Publish(new LevelCompletedEvent { Result = result });
            }
            else
            {
                AudioManager.Instance?.PlaySfx(SfxId.LevelFail);   // M10
                SetState(LevelState.Failed);
                EventCenter.Publish(new LevelFailedEvent { Reason = reason });
            }
        }

        private LevelResultData BuildResult(bool success)
        {
            float percent = _purity != null ? _purity.Percent * 100f : 0f;

            return new LevelResultData
            {
                LevelIndex = _config != null ? _config.LevelIndex : 1,
                Cleared = success,
                PurityPercent = percent,
                ElapsedSeconds = _stats != null ? _stats.ElapsedSeconds : 0f,
                KillCount = _stats != null ? _stats.KillCount : 0,
                PickupCount = _stats != null ? _stats.PickupCount : 0,
                Stars = success && _config != null ? _config.EvaluateStars(percent) : 0,
                TimestampUtcTicks = System.DateTime.UtcNow.Ticks
            };
        }

        // ===== 事件 =====

        private void OnLevelPurified(LevelPurifiedEvent evt)
        {
            if (State == LevelState.Playing)
            {
                FinishLevel(true, "净化值达到 100%");
            }
        }

        private void OnObjectiveChainCompleted()
        {
            // 关卡完成判据：目标链全完成或净化值 100%，先到者为准（R3）
            if (State == LevelState.Playing)
            {
                FinishLevel(true, "全部关卡目标完成");
            }
        }

        private void OnRestartRequested(RestartLevelEvent evt)
        {
            RestartLevel();
        }
    }
}
