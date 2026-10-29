using System.Collections.Generic;
using NUnit.Framework;
using StarEco.Combat;
using StarEco.Framework;
using UnityEngine;

namespace StarEco.Tests.EditMode
{
    /// <summary>
    /// TDD §6规划的 EditMode 单元测试：伤害数据构造、净化值累加与百分比、存档序列化往返。
    /// 全部为纯逻辑测试，不依赖场景与 Play 模式，可在 CI 无头运行。
    /// </summary>
    public class CoreLogicTests
    {
        // ===== 1. DamageInfo 构造 =====

        [Test]
        public void DamageInfo_构造函数赋值Amount与方向()
        {
            var src = new GameObject("src");
            try
            {
                var dmg = new DamageInfo(15, Vector2.left, src);
                Assert.AreEqual(15, dmg.Amount);
                Assert.AreEqual(Vector2.left, dmg.SourceDirection);
                Assert.AreSame(src, dmg.Source);
            }
            finally
            {
                Object.DestroyImmediate(src);
            }
        }

        [Test]
        public void DamageInfo_默认值击退为0且非净化()
        {
            var dmg = new DamageInfo(5, Vector2.right, null);
            Assert.AreEqual(0f, dmg.KnockbackPower, 1e-5f);
            Assert.IsFalse(dmg.IsPurify);
        }

        [Test]
        public void DamageInfo_净化标记可独立设置()
        {
            var dmg = new DamageInfo(10, Vector2.up, null) { IsPurify = true, KnockbackPower = 3.5f };
            Assert.IsTrue(dmg.IsPurify);
            Assert.AreEqual(3.5f, dmg.KnockbackPower, 1e-5f);
            Assert.AreEqual(10, dmg.Amount);
        }

        [Test]
        public void DamageInfo_是值类型_赋值不影响原实例()
        {
            var a = new DamageInfo(7, Vector2.left, null);
            var b = a;
            b.Amount = 99;
            Assert.AreEqual(7, a.Amount, "DamageInfo 为 struct，拷贝不应共享状态");
            Assert.AreEqual(99, b.Amount);
        }

        // ===== 2. 净化值百分比（复刻 PuritySystem 的公开契约） =====

        /// <summary>PuritySystem.Percent 的等价纯函数，用于在不启动场景的前提下验证边界。</summary>
        private static float Percent(int current, int total)
        {
            return total > 0 ? (float)current / total : 1f;
        }

        [Test]
        public void Percent_零目标视为已净化()
        {
            Assert.AreEqual(1f, Percent(0, 0), 1e-5f);
        }

        [Test]
        public void Percent_初始为零()
        {
            Assert.AreEqual(0f, Percent(0, 5), 1e-5f);
        }

        [Test]
        public void Percent_累加单调递增且终为1()
        {
            const int total = 3;
            int current = 0;
            var seq = new List<float>();
            for (int i = 0; i < total; i++)
            {
                current += 1;
                seq.Add(Percent(current, total));
            }
            for (int i = 1; i < seq.Count; i++)
            {
                Assert.Greater(seq[i], seq[i - 1], "净化值只增不减，百分比必须单调递增");
            }
            Assert.AreEqual(1f, seq[seq.Count - 1], 1e-5f);
        }

        [Test]
        public void Percent_超量累加由Current钳取后不再溢出()
        {
            // PuritySystem.Percent 本身不做钳制，钳制发生在 OnGarbagePurified：
            // Current += PurityValue 后显式 Current = Mathf.Min(Current, Total)。
            // 这里复刻该钳取逻辑，验证防御分支有效。
            int total = 5;
            int current = 0;
            current = System.Math.Min(current + 99, total);
            Assert.AreEqual(1f, Percent(current, total), 1e-5f, "超量累加应被夹取到 100%");
        }

        // ===== 3. 存档序列化往返 =====

        [System.Serializable]
        private class SampleSave
        {
            public int LevelIndex;
            public float Purity;
            public string UnlockedLevel;
            public List<int> Cards = new List<int>();
        }

        private const string TestSlot = "unit_test_slot";

        [SetUp]
        public void SetUp() => SaveSystem.Delete(TestSlot);

        [TearDown]
        public void TearDown() => SaveSystem.Delete(TestSlot);

        [Test]
        public void SaveSystem_写入后可读回且字段一致()
        {
            var data = new SampleSave
            {
                LevelIndex = 3,
                Purity = 0.75f,
                UnlockedLevel = "level03",
                Cards = new List<int> { 1, 2, 3 }
            };

            SaveSystem.Save(TestSlot, data);
            Assert.IsTrue(SaveSystem.Exists(TestSlot), "写档后应存在存档文件");

            var loaded = SaveSystem.Load<SampleSave>(TestSlot);
            Assert.AreEqual(3, loaded.LevelIndex);
            Assert.AreEqual(0.75f, loaded.Purity, 1e-5f);
            Assert.AreEqual("level03", loaded.UnlockedLevel);
            Assert.AreEqual(3, loaded.Cards.Count);
            Assert.AreEqual(2, loaded.Cards[1]);
        }

        [Test]
        public void SaveSystem_槽位不存在返回默认值而非抛异常()
        {
            var loaded = SaveSystem.Load<SampleSave>("unit_test_missing_slot");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.LevelIndex);
            Assert.AreEqual(0, loaded.Cards.Count);
        }

        [Test]
        public void SaveSystem_删除后Exists为false()
        {
            SaveSystem.Save(TestSlot, new SampleSave { LevelIndex = 1 });
            Assert.IsTrue(SaveSystem.Exists(TestSlot));
            SaveSystem.Delete(TestSlot);
            Assert.IsFalse(SaveSystem.Exists(TestSlot));
        }

        [Test]
        public void SaveSystem_槽位名不带扩展名时不会重复追加后缀()
        {
            SaveSystem.Save(TestSlot, new SampleSave { LevelIndex = 2 });
            var path = System.IO.Path.Combine(Application.persistentDataPath, TestSlot + ".json");
            Assert.IsTrue(System.IO.File.Exists(path), "槽位常量不应带 .json，否则会生成 xxx.json.json");
            Assert.IsFalse(System.IO.File.Exists(path + ".json"));
        }

        // ===== 4. JsonUtility 键名大小写敏感回归（M7 已踩坑） =====

        [Test]
        public void JsonUtility_顶层键名必须与字段名完全一致()
        {
            var lower = JsonUtility.FromJson<SampleSave>("{\"levelIndex\":9}");
            Assert.AreEqual(0, lower.LevelIndex, "小写键名解析不出值且不报错，属静默失败");

            var proper = JsonUtility.FromJson<SampleSave>("{\"LevelIndex\":9}");
            Assert.AreEqual(9, proper.LevelIndex);
        }
    }
}