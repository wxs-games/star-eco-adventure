using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarEco.Level;
using StarEco.UI;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 第三关一键搭建（M8 R7）：Level03.asset + Level03.unity 灰盒（能源工厂）。
    /// 目标链（用户 2026-10-18 确认的两段式）：净化 3 处工厂污染源 → 击败 Boss01。
    /// 复用 M6 的关卡系统与 M7 的 UI 面板，不新增平行系统。
    /// 菜单：StarEco → Setup → Level03（幂等：场景已存在则跳过）
    /// </summary>
    public static class BossSetup
    {
        private const string ConfigPath = "Assets/Settings/Levels/Level03.asset";
        private const string ScenePath = "Assets/Scenes/Level03.unity";

        private const string PlayerPrefab = "Assets/Prefabs/Player/Player.prefab";
        private const string BossPrefab = "Assets/Prefabs/Boss/Boss01.prefab";
        private const string GarbagePilePrefab = "Assets/Prefabs/Eco/GarbagePile.prefab";
        private const string Enemy03Prefab = "Assets/Prefabs/Enemies/Enemy03.prefab";
        private const string WhiteSquarePath = "Assets/Settings/WhiteSquare.png";

        [MenuItem("StarEco/Setup/Level03")]
        public static void Run()
        {
            var log = new StringBuilder();

            EnsureFolder("Assets/Settings/Levels");
            EnsureFolder("Assets/Scenes");

            var config = BuildConfig(log);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                log.AppendLine("Level03.unity 已存在，跳过场景创建（需要重建请先删除该场景）");
                AssetDatabase.SaveAssets();
                Debug.Log("[BossSetup] 完成 ✓\n" + log);
                return;
            }

            BuildScene(config, log);

            AssetDatabase.SaveAssets();
            Debug.Log("[BossSetup] 完成 ✓\n" + log);
        }

        // ===== 配置 =====

        private static LevelConfig BuildConfig(StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<LevelConfig>(ConfigPath);
            if (config != null)
            {
                log.AppendLine("Level03.asset 已存在，跳过");
                return config;
            }

            config = ScriptableObject.CreateInstance<LevelConfig>();
            config.LevelIndex = 3;
            config.LevelName = "第三关：能源工厂的危机";
            config.SpawnPoint = new Vector2(-12f, 2f);
            config.Star3Purity = 100;
            config.Star2Purity = 60;
            config.Star1Purity = 30;
            config.TimeLimit = 0f;
            config.IntroText = "工厂的污染核心还在运转。先清理污染源，再击败它。";

            // 两段式目标链：净化 3 处污染源 → 击败 Boss01
            config.Objectives = new[]
            {
                new LevelConfig.LevelObjective(
                    LevelObjectiveType.PurifyTargets, 3, "清理工厂污染源"),
                new LevelConfig.LevelObjective(
                    LevelObjectiveType.DefeatEnemies, 1, "击败工厂污染核心 Boss01")
            };

            // 第三关小怪：Enemy03 追击型 1 条（Boss 战前施压）
            config.SpawnEntries = new[]
            {
                new LevelConfig.SpawnEntry
                {
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Enemy03Prefab),
                    Anchors = new Transform[0],
                    MaxAlive = 2,
                    BaseInterval = 8f,
                    RequiredObjectiveIndex = -1
                }
            };

            AssetDatabase.CreateAsset(config, ConfigPath);
            log.AppendLine("已创建 Level03.asset（目标链：净化 3 污染源 → 击败 Boss01）");
            return config;
        }

        // ===== 场景 =====

        private static void BuildScene(LevelConfig config, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var white = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSquarePath);
            if (white == null)
            {
                Debug.LogError("[BossSetup] WhiteSquare.png 缺失，灰盒无法构建。");
                return;
            }

            // 地面：Boss 战需要横向空间（48u 单层长平台）
            var ground = MakeWorldBox("Ground", white, 48f, 1f, new Vector3(0f, -0.5f, 0f),
                new Color(0.28f, 0.28f, 0.32f, 1f), -5);
            ground.layer = LayerMask.NameToLayer("Ground");
            log.AppendLine("已创建 Ground（48x1u，Boss 战横向空间）");

            // 两侧平台（躲弹幕用）
            // 平台碰撞体排除 Enemy 层：Boss 精灵高 4.64u、碰撞体高 4.18u，站在 x=16 时
            // 身体会与 PlatformR（底部 y=2.95）重叠 0.75u，被"地面 + 平台"夹住而稳定陷地 0.48u。
            // 平台对敌人透明后 Boss 正常落地，对玩家的站立/躲弹功能完全不变（2026-10-20 实测修复）。
            int enemyLayer = 1 << LayerMask.NameToLayer("Enemy");
            var platformL = MakeWorldBox("PlatformL", white, 6f, 0.6f, new Vector3(-14f, 3.2f, 0f),
                new Color(0.34f, 0.34f, 0.38f, 1f), -4);
            platformL.layer = LayerMask.NameToLayer("Ground");
            platformL.GetComponent<BoxCollider2D>().excludeLayers = enemyLayer;

            var platformR = MakeWorldBox("PlatformR", white, 6f, 0.6f, new Vector3(14f, 3.2f, 0f),
                new Color(0.34f, 0.34f, 0.38f, 1f), -4);
            platformR.layer = LayerMask.NameToLayer("Ground");
            platformR.GetComponent<BoxCollider2D>().excludeLayers = enemyLayer;

            log.AppendLine("已创建 PlatformL / PlatformR（碰撞体已排除 Enemy 层，Boss 不再被平台夹住）");

            // 相机（视口略大，容纳 Boss）
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.5f;
            cam.backgroundColor = new Color(0.30f, 0.26f, 0.34f, 1f);
            camGo.transform.position = new Vector3(0f, 2.5f, -10f);

            // 横向跟随：Level03 宽 48 单位，静态相机会让玩家走出视口（M8 遗留缺陷）。
            // 与 M9 的 CameraFollow2D 同构，按关卡边界 ±24 钳制。
            var follow = camGo.AddComponent<StarEco.Level.CameraFollow2D>();
            var fso = new SerializedObject(follow);
            fso.FindProperty("_levelLeft").floatValue = -24f;
            fso.FindProperty("_levelRight").floatValue = 24f;
            fso.FindProperty("_fixedY").floatValue = 2.5f;
            fso.ApplyModifiedPropertiesWithoutUndo();

            // 背景层（挂相机子物体，随相机移动必定铺满）。
            // Center pivot：localPosition 置 0 即居中。
            var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Art/Environment/Background/bg_factory.png");
            if (bgSprite != null)
            {
                var bgGo = new GameObject("Background");
                var bgSr = bgGo.AddComponent<SpriteRenderer>();
                bgSr.sprite = bgSprite;
                bgSr.drawMode = SpriteDrawMode.Simple;
                bgSr.sortingOrder = -100;
                float bgViewW = cam.orthographicSize * 2f * cam.aspect * 1.05f;
                float bgViewH = cam.orthographicSize * 2f * 1.05f;
                var bgNative = bgSprite.bounds.size;
                bgGo.transform.localScale = new Vector3(bgViewW / bgNative.x, bgViewH / bgNative.y, 1f);
                bgGo.transform.SetParent(camGo.transform, false);
                bgGo.transform.localPosition = new Vector3(0f, 0f, 10f);
                log.AppendLine("已创建背景层（相机子物体，Center pivot 居中）");
            }

            // 玩家
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (playerPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                // 出生点唯一事实来源是 LevelConfig.SpawnPoint，避免编辑器摆放与运行时复位两处漂移。
                player.transform.position = new Vector3(config.SpawnPoint.x, config.SpawnPoint.y, 0f);
                player.name = "Player";
                log.AppendLine("已放置 Player");
            }

            // 污染源 3 处（目标链第一段）
            // 地面顶面 y=0；草垛精灵 1.60×0.72u（半高 0.36），底部为向下收窄的碎屑锥形，
            // 故在半高基础上再下移 0.09u 补偿，避免视觉悬空：y=0.36-0.09=0.27。
            PlacePrefab(GarbagePilePrefab, "Garbage1", new Vector3(-6f, 0.27f, 0f), log);
            PlacePrefab(GarbagePilePrefab, "Garbage2", new Vector3(2f, 0.27f, 0f), log);
            PlacePrefab(GarbagePilePrefab, "Garbage3", new Vector3(9f, 0.27f, 0f), log);
            log.AppendLine("已放置 3 处工厂污染源");

            // Boss01（目标链第二段）
            var bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefab);
            if (bossPrefab != null)
            {
                var boss = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab);
                boss.transform.position = new Vector3(16f, 2.2f, 0f);
                boss.name = "Boss01";
                var controller = boss.GetComponent<StarEco.Boss.BossController>();
                if (controller != null)
                {
                    controller.SetLevelIndex(3);
                }

                log.AppendLine("已放置 Boss01（目标链第二段：DefeatEnemies 1）");
            }
            else
            {
                log.AppendLine("未找到 Boss01 预制体 —— 请先运行 StarEco → Setup → Boss01 Animation");
            }

            // 刷怪锚点
            MakeAnchor("SpawnAnchorA", new Vector3(-8f, 0.2f, 0f));
            MakeAnchor("SpawnAnchorB", new Vector3(10f, 0.2f, 0f));

            log.AppendLine("已放置两个刷怪锚点");

            // 环保系统
            var eco = new GameObject("EcoSystems");
            eco.AddComponent<StarEco.Eco.PuritySystem>();
            eco.AddComponent<StarEco.Eco.AtmosphereController>();
            eco.AddComponent<StarEco.Eco.DropSpawner>();

            // 关卡系统（LevelManager 必须挂非根节点，避免重载场景产生重复单例）
            var levelGo = new GameObject("LevelSystems");
            var mgrGo = new GameObject("LevelManager");
            mgrGo.transform.SetParent(levelGo.transform, false);
            var manager = mgrGo.AddComponent<LevelManager>();

            var tracker = levelGo.AddComponent<LevelObjectiveTracker>();
            var spawner = levelGo.AddComponent<LevelSpawner>();
            levelGo.AddComponent<LevelStats>();

            // UIManager 必须挂场景根节点：MonoSingleton 只对根节点做 DontDestroyOnLoad，
            // 跨 Title/关卡两场景常驻。缺了它，各面板的 UIManager.SafeOpen 会静默失败
            // （结算面板弹不出来）——M7 同类教训。
            var uiRootGo = new GameObject("UIManager");
            uiRootGo.AddComponent<UIManager>();

            // UI 面板（沿用 M7）
            levelGo.AddComponent<HUDController>();
            levelGo.AddComponent<PausePanel>();
            levelGo.AddComponent<ResultPanel>();
            levelGo.AddComponent<DialoguePanel>();
            levelGo.AddComponent<SettingsPanel>();
            levelGo.AddComponent<CodexPanel>();
            levelGo.AddComponent<LevelSelectPanel>();
            log.AppendLine("已挂载 LevelSystems（含 HUD/暂停/结算/设置/图鉴）");

            // Boss 血条（挂在场景根，独立 Canvas）
            var barGo = new GameObject("BossHealthBar");
            barGo.AddComponent<StarEco.Boss.BossHealthBar>();
            log.AppendLine("已挂载 BossHealthBar");

            // 注入配置
            Inject(manager, "_config", config, log, "LevelManager");
            Inject(tracker, "_config", config, log, "LevelObjectiveTracker");
            Inject(spawner, "_config", config, log, "LevelSpawner");

            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine($"已创建场景 {ScenePath}");

            EnsureInBuildSettings(log);
        }

        private static void Inject(Object target, string field, Object value, StringBuilder log, string label)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                log.AppendLine($"[{label}] 未找到字段 {field}，请手动在 Inspector 绑定配置");
            }
        }

        private static void EnsureInBuildSettings(StringBuilder log)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    log.AppendLine("Build Settings 已含 Level03，跳过");
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            log.AppendLine($"已把 Level03 加入 Build Settings（index {scenes.Count - 1}）");
        }

        private static void PlacePrefab(string prefabPath, string name, Vector3 pos, StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                log.AppendLine($"缺少预制体 {prefabPath}");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = pos;
            go.name = name;
        }

        private static GameObject MakeWorldBox(string name, Sprite white, float worldW, float worldH,
            Vector3 pos, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = white;
            sr.color = color;
            sr.sortingOrder = sortingOrder;

            go.transform.localScale = new Vector3(
                worldW / white.bounds.size.x,
                worldH / white.bounds.size.y, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(white.bounds.size.x, white.bounds.size.y);

            return go;
        }

        private static void MakeAnchor(string name, Vector3 pos)
        {
            var white = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSquarePath);
            var go = MakeWorldBox(name, white, 0.3f, 0.3f, pos, new Color(1f, 0.4f, 0.4f, 0.5f), -3);
            Object.DestroyImmediate(go.GetComponent<BoxCollider2D>());
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
