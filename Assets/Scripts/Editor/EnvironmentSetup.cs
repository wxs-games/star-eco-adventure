using UnityEditor;
using UnityEngine;

namespace StarEco.Level
{
    /// <summary>
    /// 环境层搭建（M10 R1）：把 Level01 的纯色灰盒换成正式环境素材。
    ///
    /// 规格基准（与 Assets/Art/README_素材说明.md 一致）：
    /// - 地面瓦片 ground_polluted.png760×190，Horizontal Seamless，
    ///   PPU 190 → 4×1 世界单位；地面高1 单位 = 角色高的 1/2。
    /// - 背景bg_polluted_planet.png 1920×1080（16:9，与视野同比例），
    ///   地平线落在图内 72.2%，正好对齐地面顶面（世界 y=0）。
    ///   相机 orthographicSize=4.5、y=2.0 时，地面顶面占屏高
    ///   (2.0+4.5-0)/9 = 72.2%。改相机参数时须同步重算此图。
    ///
    /// 地面横向可平铺：SpriteRenderer drawMode = Tiled，
    /// size 传**世界单位**（Tiled 模式与 Simple 不同，不认像素 —— 传像素会
    /// 放大 PPU 倍铺满全屏），WrapMode 需为 Repeat，Mesh Type 需为 FullRect。
    /// </summary>
    public static class EnvironmentSetup
    {
        private const string TilesPath = "Assets/Art/Art/Environment/Tiles/ground_polluted.png";
        private const string BackgroundPath = "Assets/Art/Art/Environment/Background/bg_polluted_planet.png";

        /// <summary>瓦片 PPU，与素材 760×190 对应 4×1 世界单位。</summary>
        private const float TilePPU = 190f;

        /// <summary>地面顶面（世界 y）。碰撞与角色站立面。</summary>
        public const float GroundTopY = 0f;

        [MenuItem("StarEco/Setup/Environment (Level01)")]
        public static void Run()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.name != "Level01")
            {
                Debug.LogWarning($"[EnvironmentSetup] 当前场景是 {scene.name}，请切到 Level01 再执行。");
                return;
            }

            var log = new System.Text.StringBuilder();
            log.AppendLine("[EnvironmentSetup] 环境层搭建");

            var ground = BuildGround(log);
            var bg = BuildBackground(log, ground);

            if (ground != null && bg != null)
            {
                log.AppendLine($"  ✓ 地面 {ground.name} / 背景 {bg.name} 已就位");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(log.ToString());
        }

        /// <summary>地面：替换旧灰盒Ground 的 SpriteRenderer，改为可平铺瓦片。</summary>
        private static GameObject BuildGround(System.Text.StringBuilder log)
        {
            var old = GameObject.Find("Ground");
            if (old == null)
            {
                log.AppendLine("  ! 场景无 Ground，跳过地面");
                return null;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TilesPath);
            if (sprite == null)
            {
                log.AppendLine($"  ! 未找到 {TilesPath}");
                return null;
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TilesPath);
            ConfigureImporter(TilesPath, TilePPU);

            var sr = old.GetComponent<SpriteRenderer>();
            if (sr == null) sr = old.AddComponent<SpriteRenderer>();

            var box = old.GetComponent<BoxCollider2D>();

            // 旧灰盒 Ground 用 WhiteSquare（sprite bounds 仅 0.04u），靠
            // transform.scale=(1000,25) 撑成 40×1 世界单位，collider.size 存的
            // 是被scale 放大的局部单位 0.04。换成真素材前必须先把 scale 归一，
            // 再把碰撞体改回世界单位，否则会被放大数万倍。
            float worldW = box != null ? box.size.x * old.transform.localScale.x : 40f;
            float worldH = box != null ? box.size.y * old.transform.localScale.y : 1f;

            old.transform.localScale = Vector3.one;
            if (box != null)
            {
                box.size = new Vector2(worldW, worldH);
                box.offset = Vector2.zero;
            }

            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            // 注意：Tiled drawMode 下 SpriteRenderer.size 是**世界单位**，
            // 只有 Simple 模式才是像素。传像素值会放大 PPU 倍铺满全屏。
            sr.size = new Vector2(worldW, worldH);
            sr.sortingOrder = -10;
            sr.color = Color.white;

            // 瓦片 pivot = Center，视觉顶边 = 位置 y + h/2，令其贴到碰撞顶面
            old.transform.position = new Vector3(old.transform.position.x, GroundTopY - worldH * 0.5f, 0f);

            log.AppendLine($"  地面瓦片平铺 {worldW:F1}×{worldH:F1} 单位（{worldW / 4f:F0} 个 4u 瓦片）");
            return old;
        }

        /// <summary>背景：跟随相机的全屏层，排在所有精灵之后。</summary>
        private static GameObject BuildBackground(System.Text.StringBuilder log, GameObject ground)
        {
            var old = GameObject.Find("Background");
            if (old == null)
            {
                old = new GameObject("Background");
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            if (sprite == null)
            {
                log.AppendLine($"  ! 未找到 {BackgroundPath}");
                return null;
            }

            ConfigureImporter(BackgroundPath, 100f);

            var sr = old.GetComponent<SpriteRenderer>();
            if (sr == null) sr = old.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // 背景是单张固定图, 不需要九宫格: 用 Simple + scale 拉伸即可。
            // 曾用 Sliced + size=(1920,1080), 但该图 .meta 的 border 为 0,
            // Sliced 退化后按 1920×1080 像素粒度切块, 运行时会斜铺出巨大白带。
            sr.drawMode = SpriteDrawMode.Simple;
            sr.size = sprite.bounds.size;
            sr.sortingOrder = -100;

            // 背景以相机为中心：Level01 相机固定于 (0, 2)，宽高覆盖视野
            float camHalfH = 4.5f;      // orthographicSize
            float camHalfW = camHalfH * 16f / 9f;
            old.transform.position = new Vector3(0f, 2f, 10f);
            old.transform.localScale = new Vector3(camHalfW * 2f / 1920f, camHalfH * 2f / 1080f, 1f);

            log.AppendLine($"  背景 {camHalfW * 2f:F1}×{camHalfH * 2f:F1} 单位，地平线对齐地面顶面");
            return old;
        }

        /// <summary>统一导入设置：Bilinear、关Mipmap、Repeat、Compression。</summary>
        private static void ConfigureImporter(string path, float ppu)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            imp.spritePixelsPerUnit = ppu;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            // 注意: 本工程的 Unity 2022.3.62f3c1 构建中 TextureImporter 不暴露
            // spriteMeshType / spriteAlignment (反射确认仅有无 spritePivot),
            // 因此 FullRect 与 pivot 一律在 .meta 文本层设置, 不走 API。
            // 场景侧统一按 Center pivot 处理: 摆放时用 position.y = 底面 + 高/2。
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Repeat;      // tiled drawMode 需要 Repeat 才能横向无缝
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
        }
    }
}