using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarEco.Level;
using StarEco.UI;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 第二关一键搭建（M9 R5）：Level02.asset + Level02.unity 灰盒（生态森林）。
    /// 目标链（四段）：解救 2 只动物 → 清理 3 处堵塞疏通河道 → 净化 3 处污染源 → 击败 4 只毒植物。
    /// 空间动线：左岸（出生/解救/清堵）→ 河道（深水致命，木筏通路初始禁用）→ 右岸（净化/战斗）。
    /// 复用 M6 关卡系统与 M7 UI 面板；菜单：StarEco → Setup → Level02（幂等：场景已存在则跳过）。
    /// </summary>
    public static class Level02Setup
    {
        private const string ConfigPath = "Assets/Settings/Levels/Level02.asset";
        private const string ScenePath = "Assets/Scenes/Level02.unity";

        private const string PlayerPrefab = "Assets/Prefabs/Player/Player.prefab";
        private const string GarbagePilePrefab = "Assets/Prefabs/Eco/GarbagePile.prefab";
        private const string Enemy01Prefab = "Assets/Prefabs/Enemies/Enemy01.prefab";
        private const string Enemy02Prefab = "Assets/Prefabs/Enemies/Enemy02.prefab";
        private const string WhiteSquarePath = "Assets/Settings/WhiteSquare.png";

        // 美术资源（M10 R4 起：真素材优先，缺失才退灰盒）
        private const string TilesDir = "Assets/Art/Art/Environment/Tiles/";
        private const string PropsDir = "Assets/Art/Art/Environment/Props/";
        private const string BgDir = "Assets/Art/Art/Environment/Background/";


        // 关卡几何（世界单位）
        private const float GroundTop = 0f;
        private const float BankL = -7f;          // 左岸右缘
        private const float BankR = 7f;           // 右岸左缘
        private const float LevelLeft = -28f;
        private const float LevelRight = 28f;
        private const float WaterLowY = -1.6f;    // 水位低位（初始）
        private const float WaterHighY = 0.05f;   // 水位高位（解谜后）

        [MenuItem("StarEco/Setup/Level02")]
        public static void Run()
        {
            var log = new StringBuilder();

            EnsureFolder("Assets/Settings/Levels");
            EnsureFolder("Assets/Scenes");

            var config = BuildConfig(log);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                log.AppendLine("Level02.unity 已存在，跳过场景创建（需要重建请先删除该场景）");
                AssetDatabase.SaveAssets();
                Debug.Log("[Level02Setup] 完成 ✓\n" + log);
                return;
            }

            BuildScene(config, log);

            AssetDatabase.SaveAssets();
            Debug.Log("[Level02Setup] 完成 ✓\n" + log);
        }

        // ===== 配置 =====

        private static LevelConfig BuildConfig(StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<LevelConfig>(ConfigPath);
            if (config != null)
            {
                log.AppendLine("Level02.asset 已存在，跳过");
                return config;
            }

            config = ScriptableObject.CreateInstance<LevelConfig>();
            config.LevelIndex = 2;
            config.LevelName = "第二关：生态失衡的森林";
            config.SpawnPoint = new Vector2(-26f, 2f);
            config.Star3Purity = 100;
            config.Star2Purity = 60;
            config.Star1Purity = 30;
            config.TimeLimit = 0f;
            config.IntroText = "森林里的动物被困住了，河道也被污染物堵住。先解救动物，再清理堵塞物疏通河道。";

            // 四段目标链（顺序解锁）
            config.Objectives = new[]
            {
                new LevelConfig.LevelObjective(
                    LevelObjectiveType.RescueTargets, 2, "解救被困动物"),
                new LevelConfig.LevelObjective(
                    LevelObjectiveType.PuzzleSolved, 3, "清理堵塞物，疏通河道"),
                new LevelConfig.LevelObjective(
                    LevelObjectiveType.PurifyTargets, 3, "净化森林污染源"),
                new LevelConfig.LevelObjective(
                    LevelObjectiveType.DefeatEnemies, 4, "击败 4 只变异怪物")
            };

            // 刷怪表：远程毒植物只保留 1 只；另加近战怪 Enemy01（无远程攻击）
            config.SpawnEntries = new[]
            {
                new LevelConfig.SpawnEntry
                {
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Enemy02Prefab),
                    Anchors = new Transform[0],
                    MaxAlive = 1,
                    BaseInterval = 12f,
                    RequiredObjectiveIndex = -1
                },
                new LevelConfig.SpawnEntry
                {
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Enemy01Prefab),
                    Anchors = new Transform[0],
                    MaxAlive = 2,
                    BaseInterval = 8f,
                    RequiredObjectiveIndex = -1
                }
            };

            AssetDatabase.CreateAsset(config, ConfigPath);
            log.AppendLine("已创建 Level02.asset（四段目标链：1 只远程毒植物 + 2 只近战怪）");
            return config;
        }

        // ===== 场景 =====

        private static void BuildScene(LevelConfig config, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var white = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSquarePath);
            if (white == null)
            {
                Debug.LogError("[Level02Setup] WhiteSquare.png 缺失，灰盒无法构建。");
                return;
            }

            int groundLayer = LayerMask.NameToLayer("Ground");

            // —— 两岸地面 ——
            var groundL = MakeWorldBox("GroundL", white, 21f, 4f, new Vector3(-17.5f, -2f, 0f),
                new Color(0.32f, 0.42f, 0.30f, 1f), -5);
            groundL.layer = groundLayer;
            ApplyTile(groundL, "ground_forest.png");
            var groundR = MakeWorldBox("GroundR", white, 21f, 4f, new Vector3(17.5f, -2f, 0f),
                new Color(0.32f, 0.42f, 0.30f, 1f), -5);
            groundR.layer = groundLayer;
            ApplyTile(groundR, "ground_forest.png");
            log.AppendLine($"已创建 GroundL/R（x∈[{LevelLeft},{BankL}] / [{BankR},{LevelRight}]，顶面 y={GroundTop}）");

            // —— 河床与溺水区 ——
            var riverbed = MakeWorldBox("Riverbed", white, 15f, 2f, new Vector3(0f, -5f, 0f),
                new Color(0.20f, 0.24f, 0.30f, 1f), -6);
            riverbed.layer = groundLayer;
            ApplyTile(riverbed, "ground_forest.png");
            log.AppendLine("已创建 Riverbed（河道底，防坠落虚空）");

            var kill = MakeWorldBox("KillZone", white, 14.6f, 3.8f, new Vector3(0f, -2.4f, 0f),
                new Color(0.3f, 0.6f, 1f, 0.12f), -3);
            var killCol = kill.GetComponent<BoxCollider2D>();
            killCol.isTrigger = true;
            kill.AddComponent<DrownHazard>();
            log.AppendLine("已创建 KillZone（掉入河道即溺水，DrownHazard 走既有伤害流水线）");

            // —— 水域与木筏通路（WaterRoot 为水位面参考，木筏为其子物体随水位升降）——
            var waterRoot = new GameObject("WaterRoot");
            waterRoot.transform.position = new Vector3(0f, WaterLowY, 0f);

            var water = MakeWorldBox("Water", white, 15f, 5f, new Vector3(0f, -2.5f, 0f),
                new Color(0.25f, 0.50f, 0.85f, 0.55f), -2);
            water.transform.SetParent(waterRoot.transform, false);
            UnityEngine.Object.DestroyImmediate(water.GetComponent<BoxCollider2D>());   // 水域纯视觉

            var raftPath = new GameObject("RaftPath");
            raftPath.transform.SetParent(waterRoot.transform, false);
            raftPath.SetActive(false);   // 解谜前不可通行
            MakeRaftPlank(raftPath.transform, white, "Raft1", -4.5f);
            MakeRaftPlank(raftPath.transform, white, "Raft2", 0f);
            MakeRaftPlank(raftPath.transform, white, "Raft3", 4.5f);
            log.AppendLine($"已创建 WaterRoot（水位 {WaterLowY}→{WaterHighY}）+ RaftPath（3 段连通，初始禁用）");

            // —— 河道堵塞点 ×3（左岸，喷雾可及）——
            var blockages = new List<StarEco.Eco.WaterBlockage>();
            MakeBlockage(white, "WaterBlock1", new Vector3(-10f, 0.5f, 0f), 8, blockages);
            MakeBlockage(white, "WaterBlock2", new Vector3(-12.5f, 0.5f, 0f), 8, blockages);
            MakeBlockage(white, "WaterBlock3", new Vector3(-15f, 0.5f, 0f), 8, blockages);
            log.AppendLine("已放置 WaterBlock1~3（左岸堵塞点，各需 8 点净化）");

            // —— 陷阱笼 ×2（E 键解救）——
            MakeCage("AnimalCage01", "Animal01", new Vector3(-22f, 1.3f, 0f));
            MakeCage("AnimalCage02", "Animal02", new Vector3(-18f, 1.3f, 0f));
            log.AppendLine("已放置 AnimalCage01/02（RescueTarget，E 键解救）");

            // —— 右岸污染源 ×3（GarbagePile 预制体）——
            // 右岸地面顶面 y=0；草垛精灵 1.60×0.72u（半高 0.36），底部为向下收窄的碎屑锥形，
            // 故在半高基础上再下移 0.09u 补偿，避免视觉悬空：y=0.36-0.09=0.27。
            PlacePrefab(GarbagePilePrefab, "Garbage1", new Vector3(12f, 0.27f, 0f));
            // Garbage2 由 17 右移到 18.3：给刷怪锚点 B 腾出完整身位（精灵不透明体 1.60u 宽，
            // 压到锚点上时排序 10 > 敌人 0，会让毒植物出生就被草垛遮掉半截）。
            PlacePrefab(GarbagePilePrefab, "Garbage2", new Vector3(18.3f, 0.27f, 0f));
            PlacePrefab(GarbagePilePrefab, "Garbage3", new Vector3(22f, 0.27f, 0f));
            log.AppendLine("已放置 Garbage1~3（右岸污染源）");

            // —— 装饰树（无碰撞）——
            MakeTree(white, new Vector3(-26f, 1.6f, 0f), 1.4f);
            MakeTree(white, new Vector3(-20.5f, 1.8f, 0f), 1.7f);
            MakeTree(white, new Vector3(-13.5f, 1.5f, 0f), 1.3f);
            MakeTree(white, new Vector3(-8.5f, 1.7f, 0f), 1.5f);
            MakeTree(white, new Vector3(9f, 1.7f, 0f), 1.5f);
            MakeTree(white, new Vector3(14.5f, 1.5f, 0f), 1.3f);
            MakeTree(white, new Vector3(20.5f, 1.8f, 0f), 1.7f);
            // 由 26 左移到 24：让出右端刷怪锚点所需的空地，避免出生时被树遮挡
            MakeTree(white, new Vector3(24f, 1.6f, 0f), 1.4f);

            // —— 跳跃平台（点缀）——
            // 左岸两级台阶（地面顶面 y=0，跳跃上限约 2.45u；素材为侧视平顶木板，厚 0.41u）：
            // L1 可站立顶面 1.40，从地面可稳定跳上；L2 顶面 2.60，相对 L1 升高 1.2u，可连跳。
            MakePlatform(white, "PlatformL1", new Vector3(-24f, 1.40f, 0f), 2f, groundLayer);
            MakePlatform(white, "PlatformL2", new Vector3(-20.3f, 2.60f, 0f), 2f, groundLayer);
            // 右岸镜像两级台阶（朝河道方向上升）：R1 顶面 1.40，R2 顶面 2.60，与左岸同规格。
            MakePlatform(white, "PlatformR1", new Vector3(22f, 1.40f, 0f), 2f, groundLayer);
            MakePlatform(white, "PlatformR2", new Vector3(18.3f, 2.60f, 0f), 2f, groundLayer);
            log.AppendLine("已放置 8 棵树 + 4 个跳跃平台");

            // —— 刷怪锚点 ——
            // 两个锚点都必须落在空地：Enemy02 不透明体约 2.34u 宽（待机帧），而草垛/树（排序 15、10）
            // 都画在敌人（排序 0）前面，锚点压到道具上会让毒植物出生时半截身体被遮住。
            // A 放在 14.5 号树（右缘 14.50）与 Garbage2（左缘 17.50）之间的空档，取 16.0，左右各留 0.33u；
            // B 放在右端 24 号树（右缘 24.86）右侧的岸边空档，取 26.43，距关卡右边界 28 留 0.40u。
            // 左岸水面堵塞物贯通 -16.25~-8.75 且同样排序 15，不设锚点。
            MakeAnchor(white, "SpawnAnchorA", new Vector3(16f, 0.2f, 0f));
            MakeAnchor(white, "SpawnAnchorB", new Vector3(26.43f, 0.2f, 0f));

            log.AppendLine("已放置 SpawnAnchorA/B");

            // —— 相机（跟随，横向 56u 必须跟随）——
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = 6.5f;
            cam.backgroundColor = new Color(0.35f, 0.50f, 0.45f, 1f);
            camGo.transform.position = new Vector3(0f, 2.2f, -10f);
            var follow = camGo.AddComponent<CameraFollow2D>();
            var camSo = new SerializedObject(follow);
            camSo.FindProperty("_fixedY").floatValue = 2.2f;
            camSo.FindProperty("_levelLeft").floatValue = LevelLeft;
            camSo.FindProperty("_levelRight").floatValue = LevelRight;
            camSo.ApplyModifiedPropertiesWithoutUndo();
            MakeBackdrop(cam, 2.2f);
            log.AppendLine("已创建 Main Camera（ortho 6.5 + CameraFollow2D 钳制跟随）+背景层");


            // —— 玩家 ——
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (playerPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                // 出生点唯一事实来源是 LevelConfig.SpawnPoint，避免编辑器摆放与运行时复位两处漂移。
                player.transform.position = new Vector3(config.SpawnPoint.x, config.SpawnPoint.y, 0f);
                player.name = "Player";
                log.AppendLine("已放置 Player");
            }
            else
            {
                Debug.LogError($"[Level02Setup] 找不到 {PlayerPrefab}");
            }

            // —— 环保系统 ——
            var eco = new GameObject("EcoSystems");
            eco.AddComponent<StarEco.Eco.PuritySystem>();
            eco.AddComponent<StarEco.Eco.AtmosphereController>();
            eco.AddComponent<StarEco.Eco.DropSpawner>();

            // —— 关卡系统（LevelManager 挂非根节点，避免重载场景产生重复单例）——
            var levelGo = new GameObject("LevelSystems");
            var mgrGo = new GameObject("LevelManager");
            mgrGo.transform.SetParent(levelGo.transform, false);
            var manager = mgrGo.AddComponent<LevelManager>();

            var tracker = levelGo.AddComponent<LevelObjectiveTracker>();
            var spawner = levelGo.AddComponent<LevelSpawner>();
            levelGo.AddComponent<LevelStats>();

            // —— UIManager 必须挂场景根节点（M8 教训：缺了它面板 SafeOpen 静默失败）——
            var uiRootGo = new GameObject("UIManager");
            uiRootGo.AddComponent<UIManager>();

            levelGo.AddComponent<HUDController>();
            levelGo.AddComponent<PausePanel>();
            levelGo.AddComponent<ResultPanel>();
            levelGo.AddComponent<DialoguePanel>();
            levelGo.AddComponent<SettingsPanel>();
            levelGo.AddComponent<CodexPanel>();
            levelGo.AddComponent<LevelSelectPanel>();

            // —— 水解谜控制器 ——
            var puzzleGo = new GameObject("WaterPuzzle");
            var puzzle = puzzleGo.AddComponent<WaterPuzzleController>();
            var pzSo = new SerializedObject(puzzle);
            var blockProp = pzSo.FindProperty("_blockages");
            blockProp.arraySize = blockages.Count;
            for (int i = 0; i < blockages.Count; i++)
            {
                blockProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Blockage").objectReferenceValue = blockages[i];
            }
            pzSo.FindProperty("_waterVisual").objectReferenceValue = waterRoot.transform;
            pzSo.FindProperty("_lowY").floatValue = WaterLowY;
            pzSo.FindProperty("_highY").floatValue = WaterHighY;
            pzSo.FindProperty("_raftPath").objectReferenceValue = raftPath;
            pzSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("已挂载 WaterPuzzle（3 堵塞点 + 水位 + 木筏通路已回填）");

            // —— 注入配置 ——
            Inject(manager, "_config", config, log, "LevelManager");
            Inject(tracker, "_config", config, log, "LevelObjectiveTracker");
            Inject(spawner, "_config", config, log, "LevelSpawner");

            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine($"已创建场景 {ScenePath}");

            EnsureInBuildSettings(log);
        }

        // ===== 构件 =====

        /// <summary>
        /// 把白盒地面/水面换成可平铺真素材。
        ///
        /// 三个必须遵守的点（M10 R4 实测踩坑）：
        /// 1) localScale 必须归一到 1 —— 灰盒时代靠 scale(几十倍) 把 4px 白方块撑成
        ///    几十个单位，不归一会让 Tiled 沿该scale 二次放大（实测 bounds 1568×158）；
        /// 2) Tiled 模式下 sr.size 是**世界单位**，必须显式给原灰盒的世界尺寸，
        ///    否则会退回贴图原生尺寸；
        /// 3) 高度必须沿用原灰盒值（本关 4 / 2），改了会让顶面偏离 GroundTop，
        ///    树等装饰物就会悬空或入地。
        /// 素材缺失时静默退回白盒（保留灰盒可用性）。
        /// </summary>
        private static bool ApplyTile(GameObject go, string tileFile)
        {
            var tile = AssetDatabase.LoadAssetAtPath<Sprite>(TilesDir + tileFile);
            if (tile == null)
            {
                return false;
            }

            var sr = go.GetComponent<SpriteRenderer>();
            var col = go.GetComponent<BoxCollider2D>();
            // 碰撞体是 4px 白方块的局部尺寸 × scale；归一 scale 前先换算回世界单位
            var worldW = go.transform.localScale.x * (col != null ? col.size.x : 1f);
            var worldH = go.transform.localScale.y * (col != null ? col.size.y : 1f);

            sr.sprite = tile;
            sr.color = Color.white;
            sr.drawMode = SpriteDrawMode.Tiled;
            go.transform.localScale = Vector3.one;
            sr.size = new Vector2(worldW, worldH);
            return true;
        }

        /// <summary>
        /// 背景层：挂到相机下并按视野拉伸。
        ///
        /// 不用 CameraFollow2D 跟随 —— 关卡横跨 56 单位而单张背景只有 23 单位宽，
        /// 跟随只会在边界露出空白；挂成相机子物体则无论怎么移动都必定铺满。
        /// 尺寸走 localScale 而非 sr.size：实测本工程 Simple 模式下 sr.size
        /// 不改变 SpriteRenderer.bounds，localScale 才生效。
        /// </summary>
        private static void MakeBackdrop(Camera cam, float fixedY)
        {
            var bg = AssetDatabase.LoadAssetAtPath<Sprite>(BgDir + "bg_forest.png");
            if (bg == null)
            {
                return;
            }

            var go = new GameObject("Background");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = bg;
            sr.drawMode = SpriteDrawMode.Simple;
            sr.sortingOrder = -100;

            float viewW = cam.orthographicSize * 2f * cam.aspect * 1.05f;
            float viewH = cam.orthographicSize * 2f * 1.05f;
            var native = bg.bounds.size;
            go.transform.localScale = new Vector3(viewW / native.x, viewH / native.y, 1f);

            go.transform.SetParent(cam.transform, false);
            // Center pivot：精灵几何以 transform 为中心展开，直接置0 即居中于相机。
            // （旧代码按 TopLeft pivot 写了 -viewW/2, +viewH/2 的偏移，pivot 修正后会导致背景整体偏移半屏）
            go.transform.localPosition = new Vector3(0f, 0f, 10f);
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

        private static void MakeRaftPlank(Transform parent, Sprite white, string name, float localX)
        {
            var plank = MakeWorldBox(name, white, 4.5f, 0.4f, Vector3.zero,
                new Color(0.55f, 0.42f, 0.28f, 1f), 0);
            plank.transform.SetParent(parent, false);
            plank.transform.localPosition = new Vector3(localX, 0.15f, 0f);
        }

        private static void MakeBlockage(Sprite white, string targetId, Vector3 pos, int purifyRequired,
            List<StarEco.Eco.WaterBlockage> output)
        {
            var go = MakeWorldBox(targetId, white, 1.6f, 1.6f, pos,
                new Color(0.62f, 0.60f, 0.58f, 1f), 0);
            // 堵塞物用垃圾墙素材（garbage_wall.png：一堆积压的纸箱/瓶子/轮胎）。
            // 之前误用 water_river.png（一条水面横条）+ 整体染色 → 渲染成一大片黑色矩形，
            // 把树和背景全盖住，看着像"坏掉的贴图"。
            ApplyPropScaled(go.GetComponent<SpriteRenderer>(), go.transform,
                "garbage_wall.png", pos, 1.75f, 0f);
            // 贴地：贴图下缘是散开的碎屑，实心堆体略高于底
            go.transform.position = new Vector3(pos.x, 0.83f, pos.z);

            var col = go.GetComponent<UnityEngine.BoxCollider2D>();
            if (col != null)
            {
                col.size = new Vector2(2.05f, 1.45f);
                col.offset = new Vector2(0f, 0.05f);
            }

            var blockage = go.AddComponent<StarEco.Eco.WaterBlockage>();

            var so = new SerializedObject(blockage);
            so.FindProperty("_targetId").stringValue = targetId;
            so.FindProperty("_purifyRequired").intValue = purifyRequired;
            so.FindProperty("_purityValue").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();
            output.Add(blockage);
        }

        private static void MakeCage(string name, string targetId, Vector3 pos)
        {
            var white = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSquarePath);

            var cage = new GameObject(name);
            cage.transform.position = pos;

            // 笼体（真素材 animal_cage.png，缺失才退灰盒）
            var bars = MakeWorldBox("Bars", white, 1.3f, 1.3f, Vector3.zero,
                new Color(0.55f, 0.55f, 0.60f, 1f), 1);
            bars.transform.SetParent(cage.transform, false);
            bars.transform.localPosition = Vector3.zero;
            ApplyProp(bars.GetComponent<SpriteRenderer>(), bars.transform,
                "animal_cage.png", Vector3.zero, 1.3f, 0f, true);

            // 笼中动物（真素材 animal_otter.png：站在笼内底部）
            var animal = MakeWorldBox("Animal", white, 0.7f, 0.7f, Vector3.zero,
                new Color(0.80f, 0.62f, 0.42f, 1f), 2);
            animal.transform.SetParent(cage.transform, false);
            animal.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ApplyProp(animal.GetComponent<SpriteRenderer>(), animal.transform,
                "animal_otter.png", Vector3.zero, 0f, -0.31f, false);
            UnityEngine.Object.DestroyImmediate(animal.GetComponent<BoxCollider2D>());
            UnityEngine.Object.DestroyImmediate(bars.GetComponent<BoxCollider2D>());

            var col = cage.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1.3f, 1.3f);
            // 必须设为触发器：RescueTarget 用的是距离检测（Vector2.Distance <= _radius），
            // 不依赖碰撞体。设成实体碰撞体会让笼子变成一堵墙，玩家走到笼前就被挡住，
            // 导致关卡推进不了。
            col.isTrigger = true;

            var rescue = cage.AddComponent<RescueTarget>();
            var so = new SerializedObject(rescue);
            so.FindProperty("_targetId").stringValue = targetId;
            so.FindProperty("_radius").floatValue = 2.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void MakeTree(Sprite white, Vector3 pos, float size)
        {
            var tree = MakeWorldBox("Trunk", white, 0.3f, size * 0.5f, pos,
                new Color(0.40f, 0.30f, 0.22f, 1f), -4);
            UnityEngine.Object.DestroyImmediate(tree.GetComponent<BoxCollider2D>());
            // 真素材 tree_forest.png 为整树图（树干+树冠合一），Crown 灰盒删除
            if (ApplyProp(tree.GetComponent<SpriteRenderer>(), tree.transform,
                    "tree_forest.png", pos, GroundTop, 0f, false))
            {
                tree.name = "Tree";
            }
            else
            {
                // 灰盒回退：保留原树干+树冠两段结构
                var crown = MakeWorldBox("Crown", white, size, size,
                    pos + new Vector3(0f, size * 0.75f, 0f),
                    new Color(0.25f, 0.50f, 0.28f, 1f), -3);
                UnityEngine.Object.DestroyImmediate(crown.GetComponent<BoxCollider2D>());
            }
        }

        // platform_flat.png：1200×245、PPU 400 → 原生 3.00×0.6125u，Pivot=Center。
        // 木面水平线（真正可站立的顶面）在精灵局部坐标 y=+0.1735，其上还有约 0.09u 的草叶。
        private const float PlatformWoodLineY = 0.1735f;

        /// <param name="pos">pos.y 传「可站立的木面顶面高度」，不是物件中心。</param>
        private static void MakePlatform(Sprite white, string name, Vector3 pos, float width, int groundLayer)
        {
            var go = MakeWorldBox(name, white, width, 0.5f, pos,
                new Color(0.38f, 0.46f, 0.34f, 1f), -4);
            go.layer = groundLayer;

            var spr = AssetDatabase.LoadAssetAtPath<Sprite>(PropsDir + "platform_flat.png");
            if (spr == null)
                return;

            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.color = Color.white;
            sr.drawMode = SpriteDrawMode.Simple;
            sr.sortingOrder = 5;

            float s = width / 3f;                       // 精灵原生宽 3.0u，按目标宽度等比缩放
            go.transform.localScale = new Vector3(s, s, 1f);

            // 碰撞体世界厚度固定 0.56u（薄平台要留厚度防高速下落穿透），顶面对齐木面水平线
            var col = go.GetComponent<BoxCollider2D>();
            col.size = new Vector2(3f, 0.56f / s);
            col.offset = new Vector2(0f, PlatformWoodLineY - col.size.y / 2f);

            // 单向平台（2D 横版社区主流做法）：从下方/侧面可穿过，只从上方落下时踩住。
            // 浮空平台若做实体，在水平通道上必然二选一——顶面 ≤2.45 才跳得上，底面 ≥2.0 才走得过去，互相矛盾。
            // surfaceArc 必须小于 180：默认 180 把左右侧面也算成可碰撞面，走路横向撞上去仍会被挡住。
            // 150（±75°）只保留顶面，实测可正常从下方穿过、从上方落稳。
            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 150f;
            col.usedByEffector = true;

            go.transform.position = new Vector3(pos.x, pos.y - PlatformWoodLineY * s, pos.z);
        }

        private static Transform MakeAnchor(Sprite white, string name, Vector3 pos)
        {
            var go = MakeWorldBox(name, white, 0.3f, 0.3f, pos, new Color(1f, 0.4f, 0.4f, 0.5f), -3);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider2D>());
            // 锚点是纯逻辑标记（刷怪点），无需可视 -> 关闭渲染器（同 Level01 MakeAnchor）
            go.GetComponent<SpriteRenderer>().enabled = false;
            return go.transform;
        }

        /// <summary>
        /// 贴真素材（优先），素材缺失返回 false 由调用方退回灰盒。
        /// pivot 实测为 Center：底边贴地靠 position.y = groundY + 高/2。
        /// parented=true 时 pos/localGroundY 语义是父节点局部空间（用于笼体子物体）。
        /// </summary>
        private static bool ApplyProp(SpriteRenderer sr, Transform tf, string propFile,
            Vector3 pos, float groundY, float yOffset, bool parented)
        {
            var prop = AssetDatabase.LoadAssetAtPath<Sprite>(PropsDir + propFile);
            if (prop == null)
            {
                return false;
            }

            sr.sprite = prop;
            sr.color = Color.white;
            sr.drawMode = SpriteDrawMode.Simple;
            sr.size = prop.bounds.size;
            tf.localScale = Vector3.one;
            tf.position = new Vector3(pos.x, groundY + prop.bounds.size.y * 0.5f + yOffset, pos.z);
            return true;
        }

        /// <summary>
        /// ApplyProp 的可控高度版本：按目标世界高度等比缩放贴图，再贴到 groundY。
        /// ApplyProp 用贴图原生尺寸（garbage_wall 原生高约 3.1u），直接用会过大且压不住场景比例。
        /// </summary>
        private static bool ApplyPropScaled(SpriteRenderer sr, Transform tf, string propFile,
            Vector3 pos, float targetHeight, float yOffset)
        {
            var prop = AssetDatabase.LoadAssetAtPath<Sprite>(PropsDir + propFile);
            if (prop == null)
            {
                return false;
            }

            sr.sprite = prop;
            sr.color = new Color(0.62f, 0.60f, 0.58f, 1f);
            sr.drawMode = SpriteDrawMode.Simple;
            sr.size = prop.bounds.size;

            float nativeH = prop.bounds.size.y;
            float s = nativeH > 0.0001f ? targetHeight / nativeH : 1f;
            tf.localScale = new Vector3(s, s, 1f);
            tf.position = new Vector3(pos.x, targetHeight * 0.5f + yOffset, pos.z);
            return true;
        }


        private static void PlacePrefab(string prefabPath, string name, Vector3 pos)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Level02Setup] 缺少预制体 {prefabPath}");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = pos;
            go.name = name;
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

        /// <summary>
        /// 把关卡场景加入 Build Settings，并固定顺序 Title(0)/Level01(1)/Level02(2)/Level03(3)
        /// （LevelIndex 与 buildIndex 一致是不变量：ResultPanel 下一关、MainMenu 继续游戏都依赖它）。
        /// 顺手移除遗留的 SampleScene（默认样例，不属于本项目）。
        /// </summary>
        private static void EnsureInBuildSettings(StringBuilder log)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            // 移除默认样例场景（占着 index 2 会破坏 LevelIndex==buildIndex 不变式）
            scenes.RemoveAll(s => s.path.Contains("SampleScene"));

            bool exists = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }

            // 固定顺序：Title / Level01 / Level02 / Level03 在前，其余保持相对顺序垫后
            var ordered = new List<EditorBuildSettingsScene>();
            ordered.Add(Find(scenes, "Assets/Scenes/Title.unity"));
            ordered.Add(Find(scenes, "Assets/Scenes/Level01.unity"));
            ordered.Add(Find(scenes, ScenePath));
            ordered.Add(Find(scenes, "Assets/Scenes/Level03.unity"));
            for (int i = 0; i < scenes.Count; i++)
            {
                if (!ordered.Contains(scenes[i]))
                {
                    ordered.Add(scenes[i]);
                }
            }
            ordered.RemoveAll(s => s == null);

            EditorBuildSettings.scenes = ordered.ToArray();

            var names = new List<string>();
            for (int i = 0; i < ordered.Count; i++)
            {
                names.Add($"{i}:{System.IO.Path.GetFileNameWithoutExtension(ordered[i].path)}");
            }
            log.AppendLine("Build Settings 顺序 → " + string.Join(" | ", names));
        }

        private static EditorBuildSettingsScene Find(List<EditorBuildSettingsScene> scenes, string path)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == path)
                {
                    return scenes[i];
                }
            }
            return null;
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
