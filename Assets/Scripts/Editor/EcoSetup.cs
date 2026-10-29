using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StarEco.EditorTools
{
    /// <summary>
    /// Eco 素材一键搭建（T18/T19/T20-fx）：
    /// 1. EcoConfig.asset + TestDrop.asset（T18）
    /// 2. Enemy01Config.DropTable 挂 TestDrop（T21 部分）
    /// 3. fx_purify clip + controller（T20）
    /// 4. GarbagePile / GarbageWall / PurifyPickup / fx_purify 四份 Prefab（T19）
    /// 菜单：StarEco → Setup → Eco Assets（幂等，可重复运行）
    /// </summary>
    public static class EcoSetup
    {
        private const string ConfigPath = "Assets/Settings/EcoConfig.asset";
        private const string DropPath = "Assets/Settings/Drops/TestDrop.asset";
        private const string FxFramesRoot = "Assets/Art/Art/FX/Frames/fx_purify";
        private const string FxClipPath = "Assets/Animations/FX/fx_purify.anim";
        private const string FxCtlPath = "Assets/Animations/FX/fx_purify.controller";
        private const string FxPrefabPath = "Assets/Prefabs/FX/fx_purify.prefab";

        [MenuItem("StarEco/Setup/Eco Assets")]
        public static void Run()
        {
            var log = new System.Text.StringBuilder();

            // ===== T18-1: EcoConfig.asset =====
            var ecoConfig = AssetDatabase.LoadAssetAtPath<StarEco.Eco.EcoConfig>(ConfigPath);
            if (ecoConfig == null)
            {
                ecoConfig = ScriptableObject.CreateInstance<StarEco.Eco.EcoConfig>();
                AssetDatabase.CreateAsset(ecoConfig, ConfigPath);
                log.AppendLine("已创建 EcoConfig.asset");
            }

            // ===== T18-2: TestDrop.asset =====
            if (!AssetDatabase.IsValidFolder("Assets/Settings/Drops"))
            {
                AssetDatabase.CreateFolder("Assets/Settings", "Drops");
            }

            var testDrop = AssetDatabase.LoadAssetAtPath<StarEco.Eco.DropTable>(DropPath);
            if (testDrop == null)
            {
                testDrop = ScriptableObject.CreateInstance<StarEco.Eco.DropTable>();
                testDrop.Entries.Add(new StarEco.Eco.DropTable.Entry
                {
                    Kind = StarEco.Eco.PickupKind.PurifyBottle,
                    Count = 1,
                    Probability = 1f
                });
                AssetDatabase.CreateAsset(testDrop, DropPath);
                log.AppendLine("已创建 TestDrop.asset（净化瓶x1 概率100%）");
            }

            // ===== T21 部分：Enemy01Config.DropTable 挂 TestDrop =====
            var e1config = AssetDatabase.LoadAssetAtPath<StarEco.EnemyAI.EnemyConfig>("Assets/Settings/Enemy01Config.asset");
            if (e1config != null && e1config.DropTable == null)
            {
                e1config.DropTable = testDrop;
                EditorUtility.SetDirty(e1config);
                log.AppendLine("Enemy01Config.DropTable 已挂 TestDrop");
            }

            // ===== T20: fx_purify clip + controller =====
            EnsureFolder("Assets/Animations/FX");
            EnsureFolder("Assets/Prefabs/FX");
            EnsureFolder("Assets/Prefabs/Eco");

            var fxClip = BuildFxClip();
            var fxCtl = BuildFxController(fxClip);

            // ===== T19: fx_purify.prefab =====
            var fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath);
            if (fxPrefab == null)
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(
                    fxClip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"));
                var fxGo = new GameObject("fx_purify");
                var fxSr = fxGo.AddComponent<SpriteRenderer>();
                fxSr.sprite = keys[0].value as Sprite;
                fxSr.sortingOrder = 5; // 玩家(默认0)之上
                // FX 素材世界尺寸过大，统一压到 1.2u 高
                var b = fxSr.sprite.bounds.size;
                fxGo.transform.localScale = Vector3.one * (1.2f / b.y);
                fxGo.AddComponent<Animator>().runtimeAnimatorController = fxCtl;
                fxGo.AddComponent<StarEco.Eco.FxOneShot>();
                PrefabUtility.SaveAsPrefabAsset(fxGo, FxPrefabPath);
                Object.DestroyImmediate(fxGo);
                log.AppendLine("已创建 fx_purify.prefab");
            }
            var fxOneShot = AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath).GetComponent<StarEco.Eco.FxOneShot>();

            // ===== T19: 垃圾堆 / 挡路墙 =====
            var white = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Settings/WhiteSquare.png");
            if (white == null)
            {
                Debug.LogError("[EcoSetup] WhiteSquare sprite 缺失，垃圾预制体跳过。");
            }
            else
            {
                BuildGarbage("GarbagePile", false, 0.9f, 1.6f, white, ecoConfig, fxOneShot, log);
                BuildGarbage("GarbageWall", true, 2.2f, 1.4f, white, ecoConfig, fxOneShot, log);
            }

            // ===== T19: PurifyPickup.prefab =====
            var pickupPath = "Assets/Prefabs/Eco/PurifyPickup.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(pickupPath) == null && white != null)
            {
                var go = new GameObject("PurifyPickup");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = white;
                sr.color = new Color(0.35f, 0.8f, 0.95f, 1f);
                sr.sortingOrder = 3;
                var s = 0.35f / white.bounds.size.y;
                go.transform.localScale = new Vector3(s, s, 1f);
                var col = go.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                col.radius = 0.45f / s; // 世界半径 0.45u
                var pickup = go.AddComponent<StarEco.Eco.PurifyPickup>();
                var so = new SerializedObject(pickup);
                so.FindProperty("_config").objectReferenceValue = ecoConfig;
                so.ApplyModifiedProperties();
                PrefabUtility.SaveAsPrefabAsset(go, pickupPath);
                Object.DestroyImmediate(go);
                log.AppendLine("已创建 PurifyPickup.prefab");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[EcoSetup] Eco 素材搭建完成 ✓\n" + log);
        }

        private static AnimationClip BuildFxClip()
        {
            var fxClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(FxClipPath);
            if (fxClip != null)
            {
                return fxClip;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { FxFramesRoot });
            var pngs = guids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(p => p)
                .ToList();

            if (pngs.Count == 0)
            {
                Debug.LogError($"[EcoSetup] {FxFramesRoot} 无帧图");
                return null;
            }

            foreach (var p in pngs)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                if (imp.textureType != TextureImporterType.Sprite)
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.SaveAndReimport();
                }
            }
            AssetDatabase.Refresh();

            var keys = pngs.Select((p, i) => new ObjectReferenceKeyframe
            {
                time = i / 12f,
                value = AssetDatabase.LoadAssetAtPath<Sprite>(p)
            }).ToArray();

            fxClip = new AnimationClip { frameRate = 12f, name = "fx_purify" };
            AnimationUtility.SetObjectReferenceCurve(
                fxClip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
            var settings = AnimationUtility.GetAnimationClipSettings(fxClip);
            settings.loopTime = false; // one-shot
            AnimationUtility.SetAnimationClipSettings(fxClip, settings);
            AssetDatabase.CreateAsset(fxClip, FxClipPath);
            Debug.Log($"[EcoSetup] 已生成 fx_purify.anim（{pngs.Count} 帧）");
            return fxClip;
        }

        private static AnimatorController BuildFxController(AnimationClip clip)
        {
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(FxCtlPath);
            if (ctl != null)
            {
                return ctl;
            }

            ctl = AnimatorController.CreateAnimatorControllerAtPath(FxCtlPath);
            var state = ctl.layers[0].stateMachine.AddState("fx_purify");
            state.motion = clip;
            ctl.layers[0].stateMachine.defaultState = state;
            Debug.Log("[EcoSetup] 已创建 fx_purify.controller");
            return ctl;
        }

        private static void BuildGarbage(string name, bool blocks, float worldH, float worldW, Sprite white,
            StarEco.Eco.EcoConfig config, StarEco.Eco.FxOneShot fx, System.Text.StringBuilder log)
        {
            var path = $"Assets/Prefabs/Eco/{name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                log.AppendLine($"{name}.prefab 已存在，跳过");
                return;
            }

            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = white;
            sr.color = blocks ? config.GarbagePollutedTint * 0.8f : config.GarbagePollutedTint;
            sr.sortingOrder = 1;
            var sx = worldW / white.bounds.size.x;
            var sy = worldH / white.bounds.size.y;
            go.transform.localScale = new Vector3(sx, sy, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = false;

            var pile = go.AddComponent<StarEco.Eco.GarbagePile>();
            var so = new SerializedObject(pile);
            so.FindProperty("_config").objectReferenceValue = config;
            so.FindProperty("_fxPurifyPrefab").objectReferenceValue = fx;
            so.FindProperty("_blocksPath").boolValue = blocks;
            so.FindProperty("_targetId").stringValue = name;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            log.AppendLine($"已创建 {name}.prefab (blocksPath={blocks})");
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
