using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarEco.UI
{
    /// <summary>对话单句（M7 R9）。</summary>
    [Serializable]
    public class DialogueEntry
    {
        /// <summary>说话人名（为空表示旁白）。</summary>
        public string Speaker = string.Empty;

        /// <summary>头像占位色（#RRGGBB）。</summary>
        public string AvatarTint = "#7CFC98";

        /// <summary>台词文本。</summary>
        public string Text = string.Empty;

        /// <summary>本句结束时要广播的事件 Id（可空）。</summary>
        public string EventId = string.Empty;
    }

    /// <summary>一组对话（NPC 或提示牌共用数据格式）。</summary>
    [Serializable]
    public class DialogueData
    {
        public string Id = string.Empty;

        /// <summary>是否只播一次（当前触发器不持久化 Once 状态，字段预留）。</summary>
        public bool Once;

        public List<DialogueEntry> Entries = new List<DialogueEntry>();
    }

    /// <summary>对话数据集合（JsonUtility 顶层需 class）。</summary>
    [Serializable]
    public class DialogueCatalog
    {
        public List<DialogueData> Dialogues = new List<DialogueData>();
    }

    /// <summary>
    /// 对话数据加载（M7 R9）：Resources/Data/dialogues.json。
    /// </summary>
    public static class DialogueRepository
    {
        private const string ResourcePath = "Data/dialogues";

        private static DialogueCatalog _catalog;

        public static List<DialogueData> All
        {
            get
            {
                EnsureLoaded();
                return _catalog != null ? _catalog.Dialogues : new List<DialogueData>();
            }
        }

        public static DialogueData Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var d in All)
            {
                if (d.Id == id)
                {
                    return d;
                }
            }

            return null;
        }

        private static void EnsureLoaded()
        {
            if (_catalog != null)
            {
                return;
            }

            _catalog = new DialogueCatalog();

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[DialogueRepository] 未找到对话资源 Resources/{ResourcePath}。");
                return;
            }

            try
            {
                _catalog = JsonUtility.FromJson<DialogueCatalog>(asset.text) ?? new DialogueCatalog();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DialogueRepository] 对话数据解析失败：{ex.Message}");
                _catalog = new DialogueCatalog();
            }
        }
    }
}
