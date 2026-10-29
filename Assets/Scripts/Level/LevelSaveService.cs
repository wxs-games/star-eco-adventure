using UnityEngine;
using StarEco.Framework;

namespace StarEco.Level
{
    /// <summary>
    /// 存档服务（R6）：对 M1 `SaveSystem` 的薄封装，只负责 DTO 与槽位命名。
    /// 读写失败沿用 SaveSystem 语义（记日志、不抛异常、缺失返回默认值）。
    /// </summary>
    public static class LevelSaveService
    {
        /// <summary>结算存档槽位（SaveSystem 会自行补 .json 后缀，槽位名不带后缀）。</summary>
        public const string ResultSlot = "level01_result";

        /// <summary>进度存档槽位。</summary>
        public const string ProgressSlot = "progress";

        /// <summary>图鉴收集进度槽位（M7 R8，SaveSystem 自行补 .json 后缀）。</summary>
        public const string CodexSlot = "codex";

        /// <summary>写结算（覆盖）。</summary>
        public static void SaveResult(LevelResultData data)
        {
            if (data == null)
            {
                return;
            }

            SaveSystem.Save(ResultSlot, data);
        }

        /// <summary>读结算（缺失/损坏返回空数据）。</summary>
        public static LevelResultData LoadResult()
        {
            return SaveSystem.Load<LevelResultData>(ResultSlot) ?? new LevelResultData();
        }

        /// <summary>写进度（覆盖）。</summary>
        public static void SaveProgress(ProgressData data)
        {
            if (data == null)
            {
                return;
            }

            SaveSystem.Save(ProgressSlot, data);
        }

        /// <summary>读进度（缺失/损坏返回默认：仅解锁第 1 关）。</summary>
        public static ProgressData LoadProgress()
        {
            var data = SaveSystem.Load<ProgressData>(ProgressSlot);
            if (data == null || data.UnlockedLevelIndex < 1)
            {
                return new ProgressData { UnlockedLevelIndex = 1 };
            }

            return data;
        }

        /// <summary>是否存在可续关的通关进度（M7 的"继续游戏"按钮显示依据）。</summary>
        public static bool HasProgress()
        {
            return SaveSystem.Exists(ProgressSlot) && LoadProgress().LevelCleared;
        }

        /// <summary>结算写入 + 进度更新（解锁下一关、取历史最佳星级）。</summary>
        public static void CommitResult(LevelResultData result)
        {
            if (result == null)
            {
                return;
            }

            SaveResult(result);

            var progress = LoadProgress();
            progress.LevelCleared = progress.LevelCleared || result.Cleared;

            if (result.Cleared)
            {
                progress.BestStars = Mathf.Max(progress.BestStars, result.Stars);
                progress.UnlockedLevelIndex = Mathf.Max(progress.UnlockedLevelIndex, result.LevelIndex + 1);
            }

            SaveProgress(progress);
        }

        /// <summary>清档（调试/重置流程用）。</summary>
        public static void ClearAll()
        {
            SaveSystem.Delete(ResultSlot);
            SaveSystem.Delete(ProgressSlot);
            SaveSystem.Delete(CodexSlot);
        }

        /// <summary>仅清关卡进度与图鉴（M7"开始新游戏"用，保留设置）。</summary>
        public static void ClearProgress()
        {
            SaveSystem.Delete(ProgressSlot);
            SaveSystem.Delete(CodexSlot);
        }

        /// <summary>仅清结算记录（M7"开始新游戏"用）。</summary>
        public static void ClearResult()
        {
            SaveSystem.Delete(ResultSlot);
        }
    }
}
