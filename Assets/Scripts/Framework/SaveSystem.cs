using System;
using System.IO;
using UnityEngine;

namespace StarEco.Framework
{
    /// <summary>
    /// 存档系统（静态类）：JsonUtility 序列化到 persistentDataPath。
    /// 数据类需 [Serializable]，仅含 JsonUtility 支持的字段（基本类型 / string / List / 嵌套可序列化类）。
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>写档：JSON 缩进美化，失败时记错误不抛出。</summary>
        public static void Save<T>(string slot, T data) where T : class
        {
            try
            {
                var path = PathFor(slot);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] 写档 {slot} 失败：{ex}");
            }
        }

        /// <summary>读档：文件缺失或损坏一律返回 new T() 默认值。</summary>
        public static T Load<T>(string slot) where T : class, new()
        {
            try
            {
                var path = PathFor(slot);
                if (!File.Exists(path))
                {
                    return new T();
                }

                var json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<T>(json);
                return data ?? new T();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] 读档 {slot} 损坏或失败，返回默认值：{ex}");
                return new T();
            }
        }

        /// <summary>存档是否存在。</summary>
        public static bool Exists(string slot)
        {
            return File.Exists(PathFor(slot));
        }

        /// <summary>删除存档（不存在时静默）。</summary>
        public static void Delete(string slot)
        {
            try
            {
                var path = PathFor(slot);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] 删除 {slot} 失败：{ex}");
            }
        }

        private static string PathFor(string slot)
        {
            return Path.Combine(Application.persistentDataPath, slot + ".json");
        }
    }
}
