using UnityEngine;

namespace StarEco.Framework
{
    /// <summary>
    /// 框架自检脚本：进入 Play 模式时自动运行 M1 五件套的功能验证，结果输出到 Console。
    /// 位置：RuntimeInitializeOnLoadMethod，无需场景挂载。
    /// </summary>
    public static class FrameworkSelfTest
    {
        private static bool _eventReceived;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            Debug.Log("========== [FrameworkSelfTest] 开始自检 ==========");

            TestEventCenter();
            TestObjectPool();
            TestSaveSystem();
            TestAudioManager();

            Debug.Log("========== [FrameworkSelfTest] 自检结束，请检查上方是否存在 FAIL ==========");
        }

        // R2：订阅 → 发布 → 接收 → 取消订阅
        private static void TestEventCenter()
        {
            EventCenter.Subscribe<TestEvent>(OnTestEvent);
            _eventReceived = false;
            EventCenter.Publish(new TestEvent { Value = 42 });
            Check("EventCenter 发布/接收", _eventReceived && TestEvent.LastValue == 42);

            EventCenter.Unsubscribe<TestEvent>(OnTestEvent);
            _eventReceived = false;
            EventCenter.Publish(new TestEvent { Value = 1 });
            Check("EventCenter 取消订阅", !_eventReceived);
        }

        private class TestEvent
        {
            public static int LastValue;
            public int Value;
        }

        private static void OnTestEvent(TestEvent e)
        {
            _eventReceived = true;
            TestEvent.LastValue = e.Value;
        }

        // R3：池取还、激活/隐藏、重复归还防御
        private static void TestObjectPool()
        {
            var prefab = new GameObject("TestPrefab");
            var pool = new ObjectPool<Transform>(prefab.transform, prewarm: 2);

            var freeBefore = pool.FreeCount;
            var item = pool.Get();
            Check("ObjectPool 预热与取出", freeBefore == 2 && item.gameObject.activeSelf && pool.FreeCount == 1);

            pool.Release(item);
            Check("ObjectPool 归还隐藏", !item.gameObject.activeSelf && pool.FreeCount == 2);

            pool.Release(item); // 重复归还：应被忽略
            Check("ObjectPool 重复归还防御", pool.FreeCount == 2);

            Object.Destroy(prefab);
            Object.Destroy(item.gameObject);
        }

        // R4：存档往返、损坏容错、删除
        private static void TestSaveSystem()
        {
            const string slot = "selftest_temp";

            SaveSystem.Delete(slot);
            Check("SaveSystem 默认值（无档）", SaveSystem.Load<TestSaveData>(slot) != null && SaveSystem.Load<TestSaveData>(slot).Score == 0);

            SaveSystem.Save(slot, new TestSaveData { Score = 99 });
            Check("SaveSystem 写读往返", SaveSystem.Exists(slot) && SaveSystem.Load<TestSaveData>(slot).Score == 99);

            FileCorrupt(slot);
            Check("SaveSystem 损坏容错", SaveSystem.Load<TestSaveData>(slot) != null);

            SaveSystem.Delete(slot);
            Check("SaveSystem 删除", !SaveSystem.Exists(slot));
        }

        private class TestSaveData
        {
            public int Score;
        }

        private static void FileCorrupt(string slot)
        {
            var path = System.IO.Path.Combine(Application.persistentDataPath, slot + ".json");
            if (System.IO.File.Exists(path))
            {
                System.IO.File.WriteAllText(path, "{ This is not valid JSON !!");
            }
        }

        // R5：音量设置与持久化
        private static void TestAudioManager()
        {
            var audio = AudioManager.Instance;
            Check("AudioManager 单例创建", audio != null);

            audio.SetSfxVolume(0.5f);
            var data = SaveSystem.Load<GameSettingsData>("settings");
            Check("AudioManager 音量持久化", Mathf.Approximately(data.sfxVolume, 0.5f));

            audio.SetSfxVolume(1f); // 恢复默认
        }

        private static void Check(string name, bool pass)
        {
            Debug.Log($"[FrameworkSelfTest] {(pass ? "PASS" : "<color=red>FAIL</color>")} —— {name}");
        }
    }
}
