using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarEco.Eco;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 可破坏道具（水草 / 石头）一键装配：
    /// 1) 绑定新增碎裂音效（AudioLibrarySetup）；
    /// 2) 生成 BreakableReed / BreakableRock 预制体（SpriteRenderer + Trigger 碰撞体 + BreakableProp）；
    /// 3) 打开 Level02.unity，在两岸沿路与水边摆放 18 个实例（幂等：整体重建 Breakables 容器）。
    /// 菜单：StarEco → Setup → Breakables
    /// </summary>
    public static class BreakableSetup
    {
        private const string PropsDir = "Assets/Art/Art/Environment/Props/";
        private const string DebrisDir = "Assets/Art/Art/Environment/Props/Debris/";
        private const string PrefabDir = "Assets/Prefabs/Eco/";
        private const string ScenePath = "Assets/Scenes/Level02.unity";
        private const string ContainerName = "Breakables";

        private const int ReedSortingOrder = 6;   // 玩家(0)之前、装饰树(10)之后
        private const int RockSortingOrder = 5;

        private struct Placement
        {
            public bool IsRock;
            public float X;
            public float Scale;

            public Placement(float x, float scale, bool isRock = false)
            {
                X = x;
                Scale = scale;
                IsRock = isRock;
            }
        }

        // 地面顶面 y=0；只避开笼子、垃圾堆、堵塞物、平台站立面与刷怪锚点，
        // 与装饰树重叠仅限树基/树冠（树排序 10 更高，视觉上像长在树下）。
        private static readonly Placement[] Layout =
        {
            // 三个河道堵塞物占 [-15.8,-14.2] [-13.3,-11.7] [-10.8,-9.2]，
            // 左岸物件必须落在这些区间之外，否则会与水面污物相互穿插。
            // —— 左岸 ——
            new Placement(-27.10f, 1.00f, true),    // 关卡最左端，树基
            new Placement(-25.70f, 0.70f),          // 平台 L1 左侧（矮水草不穿平台）
            new Placement(-23.60f, 0.95f, true),    // 平台 L1 下方，矮石不穿平台
            new Placement(-20.20f, 0.95f),          // 树基
            new Placement(-19.15f, 0.55f, true),    // 树与笼子之间的窄缝
            new Placement(-16.60f, 0.90f, true),    // 笼子与堵塞物 3 之间
            new Placement(-13.75f, 0.60f),          // 堵塞物 2/3 之间的窄缝
            new Placement(-11.25f, 0.60f),          // 堵塞物 1/2 之间的窄缝
            new Placement(-8.50f, 0.90f),           // 树基
            new Placement(-7.20f, 1.00f),           // 水边芦苇

            // —— 右岸 ——
            new Placement(7.30f, 0.90f),            // 水边芦苇
            new Placement(8.90f, 0.75f, true),      // 树基
            new Placement(10.50f, 0.90f),           // 树与垃圾堆之间
            new Placement(13.20f, 0.60f, true),     // 垃圾堆与树之间
            new Placement(19.80f, 0.85f),
            new Placement(23.50f, 0.60f, true),     // 树基
            new Placement(24.85f, 0.60f),           // 刷怪锚点 B 前的空地
        };

        [MenuItem("StarEco/Setup/Breakables")]
        public static void Run()
        {
            AudioLibrarySetup.Setup();
            var reedPrefab = BuildPrefab("BreakableReed", "breakable_reed", "reed",
                BreakableKind.Plant, ReedSortingOrder);
            var rockPrefab = BuildPrefab("BreakableRock", "breakable_rock", "rock",
                BreakableKind.Stone, RockSortingOrder);

            if (reedPrefab == null || rockPrefab == null)
            {
                Debug.LogError("[BreakableSetup] 预制体构建失败，未改动场景。");
                return;
            }

            PlaceInLevel02(reedPrefab, rockPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BreakableSetup] 完成 ✓ 已重建 Level02 的 Breakables 容器（{Layout.Length} 个可破坏物）");
        }

        private static GameObject BuildPrefab(string prefabName, string spriteName, string debrisPrefix,
            BreakableKind kind, int sortingOrder)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PropsDir + spriteName + ".png");
            if (sprite == null)
            {
                Debug.LogError($"[BreakableSetup] 缺少精灵 {PropsDir}{spriteName}.png");
                return null;
            }

            var debris = LoadDebris(debrisPrefix);
            if (debris.Count == 0)
            {
                Debug.LogWarning($"[BreakableSetup] 缺少碎块图 {DebrisDir}{debrisPrefix}_*.png，碎裂时不产生碎片。");
            }

            var go = new GameObject(prefabName);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;   // 纯装饰：玩家/怪物可直接穿过
            col.size = new Vector2(sprite.bounds.size.x * 0.88f, sprite.bounds.size.y * 0.88f);

            var prop = go.AddComponent<BreakableProp>();
            var so = new SerializedObject(prop);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            var arr = so.FindProperty("_debrisSprites");
            arr.arraySize = debris.Count;
            for (int i = 0; i < debris.Count; i++)
            {
                arr.GetArrayElementAtIndex(i).objectReferenceValue = debris[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // 水草同时充当“草丛”：玩家走过时出沙沙声（复用同一个 Trigger）
            if (kind == BreakableKind.Plant)
            {
                go.AddComponent<GrassZone>();
            }

            EnsureFolder(PrefabDir.TrimEnd('/'));
            var path = PrefabDir + prefabName + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static List<Sprite> LoadDebris(string prefix)
        {
            var result = new List<Sprite>();
            if (!AssetDatabase.IsValidFolder(DebrisDir.TrimEnd('/')))
            {
                return result;
            }

            var guids = AssetDatabase.FindAssets("t:Sprite", new[] { DebrisDir.TrimEnd('/') });
            var paths = new List<string>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).StartsWith(prefix + "_"))
                {
                    paths.Add(path);
                }
            }
            paths.Sort(System.StringComparer.Ordinal);

            foreach (var path in paths)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    result.Add(sprite);
                }
            }
            return result;
        }

        private static void PlaceInLevel02(GameObject reedPrefab, GameObject rockPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogError($"[BreakableSetup] 找不到场景 {ScenePath}");
                return;
            }

            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var existing = GameObject.Find(ContainerName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var container = new GameObject(ContainerName);
            int reedIndex = 0;
            int rockIndex = 0;

            foreach (var p in Layout)
            {
                var prefab = p.IsRock ? rockPrefab : reedPrefab;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.transform);
                var sr = instance.GetComponent<SpriteRenderer>();
                float height = sr != null && sr.sprite != null ? sr.sprite.bounds.size.y : 1f;

                instance.name = p.IsRock
                    ? $"Breakable_Rock_{++rockIndex:00}"
                    : $"Breakable_Reed_{++reedIndex:00}";
                instance.transform.localScale = Vector3.one * p.Scale;
                instance.transform.position = new Vector3(p.X, height * p.Scale * 0.5f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BreakableSetup] 已在 Level02 放置 {Layout.Length} 个可破坏物（芦苇 {reedIndex} / 石头 {rockIndex}）");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
