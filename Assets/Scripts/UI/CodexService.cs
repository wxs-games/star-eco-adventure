using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using StarEco.Framework;
using StarEco.Level;

namespace StarEco.UI
{
    /// <summary>
    /// 知识卡片数据（M7 R8）：JsonUtility 可序列化。
    /// </summary>
    [Serializable]
    public class KnowledgeCardData
    {
        public string Id;
        public string Title;
        public string Body;
        public int LevelIndex;
    }

    /// <summary>
    /// 卡片数据集合（JsonUtility 顶层需 class，不能直接序列化数组）。
    /// </summary>
    [Serializable]
    public class KnowledgeCardCatalog
    {
        public List<KnowledgeCardData> Cards = new List<KnowledgeCardData>();
    }

    /// <summary>
    /// 图鉴收集进度存档 DTO（槽位 codex.json）。
    /// </summary>
    [Serializable]
    public class CodexProgress
    {
        public List<string> CollectedIds = new List<string>();
    }

    /// <summary>
    /// 图鉴服务（M7 R8）：卡片目录加载 + 收集进度读写（幂等）。
    /// </summary>
    public static class CodexService
    {
        private const string CatalogResourcePath = "Data/knowledge_cards";

        private static KnowledgeCardCatalog _catalog;
        private static CodexProgress _runtime;

        /// <summary>全部卡片（目录加载失败返回空列表，不抛异常）。</summary>
        public static List<KnowledgeCardData> Cards
        {
            get
            {
                EnsureCatalog();
                return _catalog != null ? _catalog.Cards : new List<KnowledgeCardData>();
            }
        }

        /// <summary>收集进度（运行时缓存，写档后同步）。</summary>
        public static CodexProgress Progress
        {
            get
            {
                if (_runtime == null)
                {
                    _runtime = LoadProgress();
                }

                return _runtime;
            }
        }

        /// <summary>读取收集进度（缺失/损坏返回空进度）。</summary>
        public static CodexProgress LoadProgress()
        {
            var data = SaveSystem.Load<CodexProgress>(LevelSaveService.CodexSlot);
            if (data == null || data.CollectedIds == null)
            {
                return new CodexProgress();
            }

            return data;
        }

        /// <summary>写入收集进度。</summary>
        public static void SaveProgress()
        {
            SaveSystem.Save(LevelSaveService.CodexSlot, Progress);
        }

        /// <summary>收集一张卡片（幂等：已收集不重复入档）。返回是否为新收集。</summary>
        public static bool CollectCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return false;
            }

            if (Progress.CollectedIds.Contains(cardId))
            {
                return false;
            }

            Progress.CollectedIds.Add(cardId);
            SaveProgress();
            return true;
        }

        /// <summary>是否已收集。</summary>
        public static bool IsCollected(string cardId)
        {
            return !string.IsNullOrEmpty(cardId) && Progress.CollectedIds.Contains(cardId);
        }

        /// <summary>按 Id 找卡片。</summary>
        public static KnowledgeCardData Find(string cardId)
        {
            foreach (var card in Cards)
            {
                if (card.Id == cardId)
                {
                    return card;
                }
            }

            return null;
        }

        /// <summary>清空收集进度（"开始新游戏"用）。</summary>
        public static void ResetProgress()
        {
            _runtime = new CodexProgress();
            SaveProgress();
        }

        private static void EnsureCatalog()
        {
            if (_catalog != null)
            {
                return;
            }

            _catalog = new KnowledgeCardCatalog();

            var asset = Resources.Load<TextAsset>(CatalogResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[CodexService] 未找到卡片目录资源 Resources/{CatalogResourcePath}。");
                return;
            }

            try
            {
                _catalog = JsonUtility.FromJson<KnowledgeCardCatalog>(asset.text) ?? new KnowledgeCardCatalog();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CodexService] 卡片目录解析失败：{ex.Message}");
                _catalog = new KnowledgeCardCatalog();
            }
        }
    }
}
