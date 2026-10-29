using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 一键搭建玩家动画管线：
    /// 1. 把 Art/Art/Player/Frames 下的 png 统一设为 Sprite 导入
    /// 2. 按目录生成 AnimationClip（12fps）
    /// 3. 生成 AnimatorController（状态机 + 转移条件）
    /// 4. 生成 PlayerConfig 资产与 Player 预制体
    /// 菜单：StarEco → Setup → Player Animation（幂等，可重复运行）
    /// </summary>
    public static class PlayerAnimationSetup
    {
        private const string FramesRoot = "Assets/Art/Art/Player/Frames";
        private const string OutputRoot = "Assets/Animations/Player";
        private const string ConfigPath = "Assets/Settings/PlayerConfig.asset";
        private const string ControllerPath = "Assets/Animations/Player/Player.controller";
        private const string PrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const string BodyPhysicsMaterialPath = "Assets/Settings/PlayerFrictionless.physicsMaterial2D";
        // 身体碰撞体圆角半径：BoxCollider2D 的 edgeRadius 会把物理外形整体向外膨胀 r
        // （而 Collider2D.bounds 仍按 size 返回），所以必须同时把 size 扣掉 2r，
        // 物理足迹才与无圆角时完全一致，贴地高度与地面检测不受影响。
        private const float BodyEdgeRadius = 0.15f;

        // 目录名 → (clip名, 是否循环, 采样率)。采样率取排期表 Samples 列（未声明则用默认 12）。
        private const float DefaultFrameRate = 12f;
        private static readonly Dictionary<string, (string clip, bool loop, float fps)> AnimMap = new Dictionary<string, (string, bool, float)>
        {
            // Idle 用 14.4fps：帧数由 10 提到 12 后，若仍按 12fps 循环会拉长到 1.0s，
            // 呼吸节奏变慢；12/14.4 = 0.8333s 与旧 10 帧版周期一致。
            { "Player_Idle",        ("Idle", true, 14.4f) },
            // Run 用 36fps：帧数由 8 提到 12 后，若仍按 24fps 循环会拉长到 0.5s，
            // 步频与移动速度脱节会导致脚底打滑；12/36 = 0.333s 与旧 8 帧版周期一致。
            { "Player_Run",         ("Run", true, 36) },
            // Jump 用 27fps：帧数由 3 提到 12 后，动画时长按上升段配平。
            // 起跳速度 13、重力 3*9.81=29.43 => 上升 0.4417s，12/27 = 0.4444s，
            // 播到顶点恰好满足 Jump->Fall 的 VelocityY<-0.1 切换条件。
            { "Player_Jump",        ("Jump", false, 27) },
            // Fall 用 36fps：帧数由 4 提到 12 后，12/36 = 0.333s 与旧 4 帧版周期一致。
            { "Player_Fall",        ("Fall", true, 36) },
            // Land 用 30fps：旧 3 帧 @24fps 只有 0.125s（占位时长），12 帧按 30fps = 0.4s，
            // 落地缓冲才有可读的"沉下去再起来"。
            { "Player_Land",        ("Land", false, 30) },
            { "Player_Hurt",        ("Hurt", false, 20) },
            // Die 用 15fps：帧数由 8 提到 10 后，10/15 = 0.667s。旧 8 帧 @18fps 只有 0.44s，
            // 倒地过程太快，看不出「先失衡、再加速下坠、落地回弹」的三段节奏。
            { "Player_Die",         ("Death", false, 15) },
            { "Player_Attack01",    ("Attack", false, 24) },
            { "Player_Interact",    ("Interact", false, 18) },
            { "Player_Dash",        ("Dash", false, 30) },
            { "Player_Roll",        ("Roll", false, 24) },
            { "Player_Attack02",    ("Attack02", false, 24) },
            { "Player_Attack03",    ("Attack03", false, 24) },
            { "Player_AttackAir",   ("AttackAir", false, 24) },
            { "Player_Victory",     ("Victory", true, 15) },
            { "Player_Turn",        ("Turn", false, 24) },
            { "Player_WallSlide",   ("WallSlide", true, 12) },
            { "Player_WallJump",    ("WallJump", false, 24) },
            { "Player_LedgeGrab",   ("LedgeGrab", true, 12) },
            { "Player_Crouch",      ("Crouch", false, 15) },
            { "Player_Idle02",      ("Idle02", true, 10) },
            { "Player_HeavyAttack", ("HeavyAttack", false, 24) },
        };

        // ===== 武器解耦：手中工具的逐帧锚点 =====
        // 角色帧只画空手，钳子由 Player 下的 ToolAnchor 子物体承载（见 PlayerPrefabSetup）。
        // 这里把「呼吸时手上下浮动」写进 clip，让工具跟着手走而不是浮在原地。
        // 数据来源：art_gen/gen12/Idle12c/anchors.json 的 unity_local 字段（画布像素 → 局部坐标）。
        //
        // 只有列在这里的动作才显示独立工具；其余动作的帧里本来就画了钳子，
        // 由同一条 m_IsActive=0 曲线把子物体藏起来，避免出现两把钳子。
        private const string ToolAnchorName = "ToolAnchor";

        private static readonly Dictionary<string, Vector2[]> ToolAnchors = new Dictionary<string, Vector2[]>
        {
            {
                "Idle", new[]
                {
                    new Vector2(-0.05291f, -0.48280f),
                    new Vector2(-0.05291f, -0.48545f),
                    new Vector2(-0.05291f, -0.48810f),
                    new Vector2(-0.05291f, -0.49074f),
                    new Vector2(-0.05291f, -0.49339f),
                    new Vector2(-0.05291f, -0.49603f),
                    new Vector2(-0.05291f, -0.49868f),
                    new Vector2(-0.05291f, -0.49603f),
                    new Vector2(-0.05291f, -0.49339f),
                    new Vector2(-0.05291f, -0.49074f),
                    new Vector2(-0.05291f, -0.48810f),
                    new Vector2(-0.05291f, -0.48545f),
                }
            },
            {
                // 长待机：低头含胸、重心压后腿。手心比 Idle 高约 1px、略靠前 1px，
                // 但呼吸幅度更小（腰部权重低），所以纵向只走 3px。
                "Idle02", new[]
                {
                    new Vector2(-0.05556f, -0.48016f),
                    new Vector2(-0.05556f, -0.48148f),
                    new Vector2(-0.05556f, -0.48280f),
                    new Vector2(-0.05556f, -0.48413f),
                    new Vector2(-0.05556f, -0.48677f),
                    new Vector2(-0.05556f, -0.48810f),
                    new Vector2(-0.05556f, -0.48810f),
                    new Vector2(-0.05556f, -0.48810f),
                    new Vector2(-0.05556f, -0.48677f),
                    new Vector2(-0.05556f, -0.48413f),
                    new Vector2(-0.05556f, -0.48280f),
                    new Vector2(-0.05556f, -0.48148f),
                }
            },
            {
                // 受击：绕脚踝后仰 + 头部反向抬起，手被带得先向前甩再收回。
                // 横向是全场变化最大的（+0.063 → -0.053），纵向只走 7px。
                "Hurt", new[]
                {
                    new Vector2(0.00820f, -0.48042f),
                    new Vector2(0.06349f, -0.48809f),
                    new Vector2(0.03598f, -0.48306f),
                    new Vector2(-0.00079f, -0.48015f),
                    new Vector2(-0.03148f, -0.48068f),
                    new Vector2(-0.05291f, -0.48280f),
                }
            },
            {
                // 倒地：4 段骨骼（脚/小腿/大腿/躯干）LBS 变形，膝盖在跌倒过程中真的会弯。
                // 手被带着先下沉、再随身体落到地面；纵向是全场变化最大的（-0.48 → -0.90）。
                // 帧 0 与 Hurt 末帧同值（都是站立基准手的解析解），保证 Hurt→Die 不跳。
                // 数据来自 art_gen/gen12/Die/proc/anchors_die.json。
                "Death", new[]
                {
                    new Vector2(-0.05291f, -0.48280f),
                    new Vector2(-0.06528f, -0.48784f),
                    new Vector2(-0.09740f, -0.52176f),
                    new Vector2(-0.12170f, -0.58308f),
                    new Vector2(-0.11175f, -0.63977f),
                    new Vector2(-0.07295f, -0.66749f),
                    new Vector2(-0.07177f, -0.68658f),
                    new Vector2(-0.09635f, -0.68056f),
                    new Vector2(-0.11894f, -0.76276f),
                    new Vector2(-0.12030f, -0.89911f),
                }
            },
            {
                // 普攻：空手挥击，钳子由 ToolAnchor 逐帧跟手（攻击解耦，武器可单独拿掉/丢出）。
                // 由 .workbuddy/tmp/tool_preview.py 自动求解：皮肤连通域去掉脸后，面积最大的
                // 那块是近侧手臂，其上离脸最远的点即拳尖，向块心回退 18% 得掌心。
                // 横轨迹 -0.17 → -0.19（后拉）→ 0.17 → 0.41 → 0.72（命中）→ 0.35 → -0.01 → -0.14。
                "Attack", new[]
                {
                    new Vector2(-0.17200f, -0.44800f),
                    new Vector2(-0.18500f, -0.56500f),
                    new Vector2(0.17000f, -0.35100f),
                    new Vector2(0.40600f, -0.26500f),
                    new Vector2(0.71900f, -0.08100f),
                    new Vector2(0.34500f, -0.28100f),
                    new Vector2(-0.00500f, -0.30100f),
                    new Vector2(-0.14400f, -0.52400f),
                }
            },
            {
                // 连招第二段（踢腿）：手全程只是平衡用，握拳抬在胸前，
                // 落地起身后垂到身侧。ux 在胸前六帧稳定在 0.31~0.39，
                // 帧 6/7 手垂下来掉到 0.08~0.12。
                // 注意帧 5 在原始生成图里整体左偏 74px，已按背上水壶做水平对齐
                // （.workbuddy/tmp/align_frames.py），否则支撑脚会突然滑一下。
                "Attack02", new[]
                {
                    new Vector2(0.39371f, -0.17950f),
                    new Vector2(0.31644f, -0.11878f),
                    new Vector2(0.31878f, -0.11592f),
                    new Vector2(0.31663f, -0.12086f),
                    new Vector2(0.31607f, -0.11834f),
                    new Vector2(0.31246f, -0.11636f),
                    new Vector2(0.12489f, -0.53719f),
                    new Vector2(0.07537f, -0.54423f),
                }
            },
            {
                // 重击（蓄力砸地）：手从身侧抬到胸、过顶，再一路砸到地面。
                // 求解器改用「以背上蓝色水壶为参照，取所有非头部皮肤像素中离水壶最远的点」——
                // 旧规则（面积最小的皮肤块）在举拳过顶的帧会命中耳朵（HeavyAttack_03 实测
                // 掌心被算到 y=375，而拳头在 y=117），钳子贴到脸上。
                "HeavyAttack", new[]
                {
                    new Vector2(-0.0961f, -0.5205f),
                    new Vector2(0.3448f, -0.1615f),
                    new Vector2(0.3423f, -0.1592f),
                    new Vector2(0.2544f, 0.7918f),
                    new Vector2(0.2667f, 0.7685f),
                    new Vector2(0.5376f, -0.0985f),
                    new Vector2(0.3215f, -0.6411f),
                    new Vector2(0.3504f, -0.9462f),
                    new Vector2(0.3642f, -0.8825f),
                    new Vector2(0.4314f, -0.7129f),
                    new Vector2(0.0849f, -0.5766f),
                    new Vector2(-0.0961f, -0.5205f),
                }
            },
            {
                // 连招第三段（上勾拳）：起手手向后下、发力时划到身前、命中时伸到斜上前方
                // （ux 峰值 0.79 / uy 0.33，是全套动作里手伸得最远的一帧）。
                "Attack03", new[]
                {
                    new Vector2(-0.0961f, -0.5205f),
                    new Vector2(-0.2732f, -0.5801f),
                    new Vector2(0.0806f, -0.4480f),
                    new Vector2(0.3572f, -0.2417f),
                    new Vector2(0.2044f, -0.1657f),
                    new Vector2(0.7941f, 0.3306f),
                    new Vector2(0.4983f, -0.0689f),
                    new Vector2(-0.0138f, -0.5699f),
                    new Vector2(-0.0961f, -0.5205f),
                }
            },
            {
                // 空中下劈：先举过头顶（uy 0.91 为全程最高点），再一路劈到身前腰高。
                "AttackAir", new[]
                {
                    new Vector2(0.4392f, -0.0436f),
                    new Vector2(0.2502f, 0.9063f),
                    new Vector2(0.2409f, -0.1246f),
                    new Vector2(0.2521f, -0.3262f),
                    new Vector2(0.1137f, -0.4038f),
                    new Vector2(0.1863f, -0.4430f),
                }
            },
        };

        [MenuItem("StarEco/Setup/Player Animation")]
        public static void Run()
        {
            EnsureFolder(OutputRoot);
            EnsureFolder("Assets/Settings");
            EnsureFolder("Assets/Prefabs/Player");
            CleanupLegacyClips();

            var clips = BuildAllClips();
            var controller = BuildController(clips);
            var config = EnsureConfig();
            BuildPrefab(controller, config);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // 保险：删除重建 controller 的窗口期会让场景内 Player 实例的 Animator 引用被清空
            // （guid 虽已恢复，但内存里的实例引用已断）。重建后统一回绑并保存场景。
            RebindSceneAnimators(controller);
            Debug.Log("[PlayerAnimationSetup] 玩家动画管线搭建完成 ✓");
        }

        /// <summary>把场景中 Player 根节点的 Animator 重新指向刚生成的 controller 并保存场景。</summary>
        private static void RebindSceneAnimators(RuntimeAnimatorController controller)
        {
            int bound = 0;
            foreach (var anim in UnityEngine.Object.FindObjectsOfType<Animator>(true))
            {
                if (anim.gameObject.name != "Player") continue;
                if (anim.runtimeAnimatorController == controller) continue;
                anim.runtimeAnimatorController = controller;
                EditorUtility.SetDirty(anim);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(anim.gameObject.scene);
                bound++;
            }
            if (bound > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                Debug.Log($"[PlayerAnimationSetup] 已回绑 {bound} 处场景 Animator 引用");
            }
        }

        /// <summary>删除旧命名（Player_ 前缀）的 clip 资产，避免残留。</summary>
        private static void CleanupLegacyClips()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { OutputRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Player_"))
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"[PlayerAnimationSetup] 清理旧命名 clip：{name}");
                }
            }
        }

        // ===== 1. Sprite 导入 =====

        private static void EnsureSpriteImport(string pngPath)
        {
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null || (importer.textureType == TextureImporterType.Sprite &&
                                     importer.spriteImportMode == SpriteImportMode.Single))
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        // ===== 2. AnimationClip 生成 =====

        private static Dictionary<string, AnimationClip> BuildAllClips()
        {
            var clips = new Dictionary<string, AnimationClip>();

            foreach (var pair in AnimMap)
            {
                var folder = $"{FramesRoot}/{pair.Key}";
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Debug.LogWarning($"[PlayerAnimationSetup] 缺少动画目录：{folder}，跳过。");
                    continue;
                }

                var clip = BuildClip(pair.Key, pair.Value.clip, folder, pair.Value.loop, pair.Value.fps);
                if (clip != null)
                {
                    clips[pair.Key] = clip;
                }
            }

            return clips;
        }

        private static AnimationClip BuildClip(string animName, string clipName, string folder, bool loop, float fps)
        {
            float frameRate = fps > 0f ? fps : DefaultFrameRate;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            var pngPaths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(NaturalOrder)
                .ToList();

            if (pngPaths.Count == 0)
            {
                Debug.LogWarning($"[PlayerAnimationSetup] 目录 {folder} 内没有 png 帧，跳过。");
                return null;
            }

            foreach (var path in pngPaths)
            {
                EnsureSpriteImport(path);
            }
            AssetDatabase.Refresh();

            var keyframes = new ObjectReferenceKeyframe[pngPaths.Count];
            for (int i = 0; i < pngPaths.Count; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPaths[i]);
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / frameRate,
                    value = sprite
                };
            }

            var clip = new AnimationClip { frameRate = frameRate };
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            ApplyToolAnchorCurves(clip, clipName, frameRate);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var clipPath = $"{OutputRoot}/{clipName}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
            {
                // 覆盖重建时保留 Animation Event（Attack 开窗帧等由战斗系统手工打入，不能被冲掉）
                var preservedEvents = AnimationUtility.GetAnimationEvents(existing);
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                clip.name = clipName;
                if (preservedEvents != null && preservedEvents.Length > 0)
                {
                    AnimationUtility.SetAnimationEvents(clip, preservedEvents);
                }
            }
            else
            {
                AssetDatabase.CreateAsset(clip, clipPath);
                clip.name = clipName;
            }

            Debug.Log($"[PlayerAnimationSetup] 生成 {clipName}（{pngPaths.Count} 帧, {(loop ? "循环" : "单次")}）");
            return clip;
        }

        /// <summary>
        /// 工具绕 z 的旋转（度）。倒地时身体整体转了 90°，工具必须跟着转，
        /// 否则躺平后钳子仍然竖直朝下、像插进地里。没列出的动作一律 0
        /// （并且必须强制写 0，否则切状态时会残留上一个动作的旋转）。
        /// 数值 = 该帧躯干倾角的相反数（屏幕坐标下向身后倒 = 逆时针 = z 正）。
        /// </summary>
        private static readonly Dictionary<string, float[]> ToolRotations = new Dictionary<string, float[]>
        {
            { "Death", new[] { 0f, 3f, 7f, 13f, 22f, 36f, 52f, 68f, 82f, 90f } },
            // Attack 的 z 角由 .workbuddy/tmp/tool_preview.py 按「手臂方向相对 Idle 站姿」解出，
            // 第 6 帧（收招回胸前）检测到的臂向反了（会甩半圈），按前后帧插值改成 30。
            { "Attack", new[] { 5f, -11f, 50f, 70f, 84f, 67f, 30f, -4f } },
            // Attack02 踢腿时手只是握拳抬在胸前、不打出去，工具就该竖直握在手里，
            // 不需要跟着挥击甩。胸前六帧 15°，落地垂手两帧 0°（自然下垂）。
            { "Attack02", new[] { 15f, 15f, 15f, 15f, 15f, 15f, 0f, 0f } },
            // 以下三套的 z 角是逐帧目检定的：钳子精灵默认竖直向上、pivot 在底部手柄处，
            // 所以「手臂朝哪、钳子就转到哪」，0° = 竖直向上、-90° = 水平朝前、-180° = 朝下。
            // 举拳过顶保持 0（钳头朝天），下砸过程从 -110 一路转到 -160（钳头砸向地面）。
            { "HeavyAttack", new[] { 0f, -20f, -20f, 0f, -8f, -110f, -130f, -160f, -160f, -140f, 0f, 0f } },
            { "Attack03", new[] { 0f, 30f, 30f, -80f, -45f, -25f, -50f, 0f, 0f } },
            { "AttackAir", new[] { -10f, 0f, -80f, -120f, -100f, -30f } },
        };

        /// <summary>
        /// 把手中工具的锚点写进 clip。
        /// 有锚点数据的动作：写 x/y 位移 + z 旋转曲线 + 显示工具；
        /// 没有的：只写一条 m_IsActive=0，把 ToolAnchor 子物体藏掉，
        /// 否则那些帧里已画好的钳子会和独立工具重叠成两把。
        /// </summary>
        private static void ApplyToolAnchorCurves(AnimationClip clip, string clipName, float frameRate)
        {
            bool decoupled = ToolAnchors.TryGetValue(clipName, out var offsets)
                             && offsets != null && offsets.Length > 0;

            if (decoupled)
            {
                var curveX = new AnimationCurve();
                var curveY = new AnimationCurve();
                var curveZ = new AnimationCurve();
                ToolRotations.TryGetValue(clipName, out var rots);
                for (int i = 0; i < offsets.Length; i++)
                {
                    float t = i / frameRate;
                    curveX.AddKey(t, offsets[i].x);
                    curveY.AddKey(t, offsets[i].y);
                    curveZ.AddKey(t, (rots != null && i < rots.Length) ? rots[i] : 0f);
                }

                // 逐帧密集关键帧，必须给平滑切线，否则默认零切线会把呼吸走成一格一格的
                for (int i = 0; i < curveX.length; i++)
                {
                    curveX.SmoothTangents(i, 0f);
                    curveY.SmoothTangents(i, 0f);
                    curveZ.SmoothTangents(i, 0f);
                }

                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(ToolAnchorName, typeof(Transform), "m_LocalPosition.x"), curveX);
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(ToolAnchorName, typeof(Transform), "m_LocalPosition.y"), curveY);
                // 用局部欧拉角而不是 m_LocalRotation 四元数：一条曲线就能表达平面旋转
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(ToolAnchorName, typeof(Transform), "localEulerAnglesRaw.z"), curveZ);
            }

            // 显隐：解耦动作只在最后一帧为 1，其余恒 0（用常量曲线，保证切状态时值一定被写）
            float duration = clip.length > 0f ? clip.length : 0f;
            var active = new AnimationCurve();
            active.AddKey(0f, decoupled ? 1f : 0f);
            if (duration > 0f)
            {
                active.AddKey(duration, decoupled ? 1f : 0f);
            }

            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(ToolAnchorName, typeof(GameObject), "m_IsActive"), active);

            if (decoupled)
            {
                Debug.Log($"[PlayerAnimationSetup] {clipName} 已写入工具锚点曲线（{offsets.Length} 帧）");
            }
        }

        /// <summary>自然排序：Player_Run_02 排在 Player_Run_10 之前。</summary>
        private static string NaturalOrder(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return Regex.Replace(name, @"\d+", m => m.Value.PadLeft(6, '0'));
        }

        // ===== 3. AnimatorController 生成 =====

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            // 删除重建：历史重跑会在 .controller 里累积孤儿状态/转移块（含引用已删参数的死转移），
            // 重建保证文件只含当前图；同时恢复原 guid，保 prefab/场景引用不断。
            var oldGuid = AssetDatabase.AssetPathToGUID(ControllerPath);
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            if (!string.IsNullOrEmpty(oldGuid) && oldGuid != AssetDatabase.AssetPathToGUID(ControllerPath))
            {
                var metaPath = ControllerPath + ".meta";
                var meta = File.ReadAllText(metaPath);
                meta = new Regex(@"guid: \w+").Replace(meta, "guid: " + oldGuid, 1);
                File.WriteAllText(metaPath, meta);
                AssetDatabase.Refresh();
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                Debug.Log($"[PlayerAnimationSetup] controller 已重建并恢复 guid {oldGuid}");
            }

            // 参数（与 PlayerAnimParams 常量一一对应）
            controller.parameters = new[]
            {
                new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 0f },
                new AnimatorControllerParameter { name = "IsGrounded", type = AnimatorControllerParameterType.Bool, defaultBool = true },
                new AnimatorControllerParameter { name = "VelocityY", type = AnimatorControllerParameterType.Float, defaultFloat = 0f },
                new AnimatorControllerParameter { name = "Hurt", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Death", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Interact", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Dash", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Roll", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Attack2", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Attack3", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "AttackAir", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "HeavyAttack", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Victory", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Turn", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "WallJump", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "IsWallSliding", type = AnimatorControllerParameterType.Bool, defaultBool = false },
                new AnimatorControllerParameter { name = "IsLedgeGrab", type = AnimatorControllerParameterType.Bool, defaultBool = false },
                new AnimatorControllerParameter { name = "IsCrouching", type = AnimatorControllerParameterType.Bool, defaultBool = false },
                new AnimatorControllerParameter { name = "IsLongIdle", type = AnimatorControllerParameterType.Bool, defaultBool = false },
            };

            var root = controller.layers[0].stateMachine;
            root.states = new ChildAnimatorState[0];
            root.anyStateTransitions = new AnimatorStateTransition[0];
            root.entryTransitions = new AnimatorTransition[0];

            var idle = AddState(root, "Idle", GetClip(clips, "Player_Idle"), new Vector2(200, 0));
            var run = AddState(root, "Run", GetClip(clips, "Player_Run"), new Vector2(400, 0));
            var jump = AddState(root, "Jump", GetClip(clips, "Player_Jump"), new Vector2(400, 160));
            var fall = AddState(root, "Fall", GetClip(clips, "Player_Fall"), new Vector2(600, 160));
            var land = AddState(root, "Land", GetClip(clips, "Player_Land"), new Vector2(400, 320));
            var hurt = AddState(root, "Hurt", GetClip(clips, "Player_Hurt"), new Vector2(200, 480));
            var death = AddState(root, "Death", GetClip(clips, "Player_Die"), new Vector2(400, 480));
            root.defaultState = idle;

            // ---- 收尾补齐：一次性动作（trigger），播完回地面/空中 ----
            var interact = AddState(root, "Interact", GetClip(clips, "Player_Interact"), new Vector2(600, 0));
            var attack = AddState(root, "Attack", GetClip(clips, "Player_Attack01"), new Vector2(800, 0));
            var attack2 = AddState(root, "Attack02", GetClip(clips, "Player_Attack02"), new Vector2(1000, 0));
            var attack3 = AddState(root, "Attack03", GetClip(clips, "Player_Attack03"), new Vector2(1200, 0));
            var attackAir = AddState(root, "AttackAir", GetClip(clips, "Player_AttackAir"), new Vector2(800, 160));
            var heavy = AddState(root, "HeavyAttack", GetClip(clips, "Player_HeavyAttack"), new Vector2(1400, 0));
            var dash = AddState(root, "Dash", GetClip(clips, "Player_Dash"), new Vector2(600, 320));
            var roll = AddState(root, "Roll", GetClip(clips, "Player_Roll"), new Vector2(800, 320));
            var turn = AddState(root, "Turn", GetClip(clips, "Player_Turn"), new Vector2(1000, 320));
            var victory = AddState(root, "Victory", GetClip(clips, "Player_Victory"), new Vector2(1200, 320));
            var wallJump = AddState(root, "WallJump", GetClip(clips, "Player_WallJump"), new Vector2(600, 480));

            // ---- 持续型状态（bool 进出）----
            var wallSlide = AddState(root, "WallSlide", GetClip(clips, "Player_WallSlide"), new Vector2(800, 480));
            var ledgeGrab = AddState(root, "LedgeGrab", GetClip(clips, "Player_LedgeGrab"), new Vector2(1000, 480));
            var crouch = AddState(root, "Crouch", GetClip(clips, "Player_Crouch"), new Vector2(1200, 480));
            var longIdle = AddState(root, "Idle02", GetClip(clips, "Player_Idle02"), new Vector2(1400, 480));

            // ---- 地面移动循环 ----
            Link(idle, run, t => t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed"));
            Link(run, idle, t => t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed"));

            // ---- 起跳 / 离地 ----
            Link(idle, jump, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"));
            Link(run, jump, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"));
            Link(jump, fall, t => t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VelocityY"));

            // ---- 落地 ----
            Link(jump, land, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded"));
            Link(fall, land, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded"));

            // ---- Land 动画播完回待机；中途离地回空中 ----
            Link(land, idle, t =>
            {
                t.hasExitTime = true;
                t.exitTime = 0.9f;
            });
            Link(land, jump, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"));

            // ---- 收尾补齐接线 ----
            // 持续型状态（bool 由 Driver/状态机每帧写入，进出场即时响应）
            // 来源必须包含空中状态：WallSlide / LedgeGrab 本质是空中动作，
            // 只从 Idle/Run 接边的话这两个状态永远播不到（2026-10-28 实测确认）。
            foreach (var from in new[] { idle, run, jump, fall, longIdle })
            {
                Link(from, wallSlide, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsWallSliding"));
                Link(from, ledgeGrab, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsLedgeGrab"));
                Link(from, crouch, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsCrouching"));
            }

            // 持续型 -> 退出：先接「空中」再接「地面」，顺序不能反。
            // 条件解除时人可能还在空中（离墙、松手），若先判 Idle 会闪一帧站立。
            Link(wallSlide, fall, t =>
            {
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWallSliding");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");
            });
            Link(wallSlide, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWallSliding"));
            Link(ledgeGrab, fall, t =>
            {
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsLedgeGrab");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");
            });
            Link(ledgeGrab, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsLedgeGrab"));
            // 蹲伏：起身后若已离地（走下平台）直接进空中
            Link(crouch, jump, t =>
            {
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");
            });
            Link(crouch, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching"));
            Link(longIdle, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsLongIdle"));
            // 长待机被任何移动打断
            Link(longIdle, run, t => t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed"));
            Link(longIdle, jump, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"));

            // 墙跳：走 AnyState（蹬墙瞬间可能处于 WallSlide / Fall / Jump，逐个接边会漏）
            // 只保留「播完回到下落」这一条出边，进边统一在下面的一次性动作表里加。
            Link(wallJump, fall, t =>
            {
                t.hasExitTime = true;
                t.exitTime = 0.9f;
            });

            // 一次性动作：AnyState 进入（trigger），播完回地面/空中
            var oneShots = new[]
            {
                (state: interact, trigger: "Interact"), (state: attack, trigger: "Attack"),
                (state: attack2, trigger: "Attack2"), (state: attack3, trigger: "Attack3"),
                (state: attackAir, trigger: "AttackAir"), (state: heavy, trigger: "HeavyAttack"),
                (state: dash, trigger: "Dash"), (state: roll, trigger: "Roll"),
                (state: turn, trigger: "Turn"), (state: victory, trigger: "Victory"),
                (state: wallJump, trigger: "WallJump"),
            };
            foreach (var (state, trigger) in oneShots)
            {
                var any = root.AddAnyStateTransition(state);
                any.hasExitTime = false;
                any.duration = 0.02f;
                any.AddCondition(AnimatorConditionMode.If, 0, trigger);

                // 播完回地面或空中（Victory 循环播，由过关流程直接切场景）
                if (state != victory)
                {
                    // 顺序：先「仍在空中」，再「已落地」。反过来的话空中动作播完会闪一帧 Idle。
                    Link(state, jump, t =>
                    {
                        t.hasExitTime = true;
                        t.exitTime = 0.95f;
                        t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");
                    });
                    Link(state, idle, t =>
                    {
                        t.hasExitTime = true;
                        t.exitTime = 0.95f;
                    });
                }
            }

            // 连段链：Attack 结束窗口可无缝接 Attack02 -> Attack03（玩家按攻击键触发下一段）
            Link(attack, attack2, t => t.AddCondition(AnimatorConditionMode.If, 0, "Attack2"));
            Link(attack2, attack3, t => t.AddCondition(AnimatorConditionMode.If, 0, "Attack3"));

            // ---- Hurt：任意状态进入，播放完回 Idle ----
            // 只需一条 AnyState 转移。原先写成 foreach 逐状态添加，但循环体里没有用到
            // 循环变量，实际往 AnyState 里塞了 5 条一模一样的转移。
            // 同时必须关掉 canTransitionToSelf：否则 Hurt 播放期间每帧都能重新触发自己，
            // 连续受击会一直被打回第 0 帧（看起来像卡住）。
            var hurtAny = root.AddAnyStateTransition(hurt);
            hurtAny.hasExitTime = false;
            hurtAny.duration = 0.02f;
            hurtAny.canTransitionToSelf = false;
            hurtAny.AddCondition(AnimatorConditionMode.If, 0, "Hurt");

            Link(hurt, idle, t =>
            {
                t.hasExitTime = true;
                t.exitTime = 0.95f;
            });

            // ---- Death：任意状态进入，不退出（由重生逻辑复位） ----
            var deathAny = root.AddAnyStateTransition(death);
            deathAny.hasExitTime = false;
            deathAny.duration = 0.02f;
            deathAny.canTransitionToSelf = false;
            deathAny.AddCondition(AnimatorConditionMode.If, 0, "Death");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState AddState(AnimatorStateMachine root, string name, AnimationClip clip, Vector2 position)
        {
            var state = root.AddState(name, position);
            state.motion = clip;
            return state;
        }

        private static void Link(AnimatorState from, AnimatorState to, System.Action<AnimatorStateTransition> setup)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0.05f;
            t.interruptionSource = TransitionInterruptionSource.Source;
            setup?.Invoke(t);
        }

        private static AnimationClip GetClip(Dictionary<string, AnimationClip> clips, string key)
        {
            if (clips.TryGetValue(key, out var clip))
            {
                return clip;
            }

            Debug.LogError($"[PlayerAnimationSetup] 缺少 clip {key}，动画状态将为空。");
            return null;
        }

        // ===== 4. PlayerConfig 资产 =====

        private static StarEco.Player.PlayerConfig EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<StarEco.Player.PlayerConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<StarEco.Player.PlayerConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                Debug.Log($"[PlayerAnimationSetup] 已创建默认配置 {ConfigPath}");
            }

            return config;
        }

        // ===== 5. Player 预制体 =====

        private static void BuildPrefab(AnimatorController controller, StarEco.Player.PlayerConfig config)
        {
            // 用 Idle 第一帧做占位显示（外观由美术帧决定，尺寸由碰撞体决定）
            var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{OutputRoot}/Idle.anim");
            Sprite displaySprite = null;
            if (idleClip != null)
            {
                var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
                var keys = AnimationUtility.GetObjectReferenceCurve(idleClip, binding);
                if (keys != null && keys.Length > 0)
                {
                    displaySprite = keys[0].value as Sprite;
                }
            }

            var go = new GameObject("Player");
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = displaySprite;
            // 与 FixAllLevelSpriteSorting.GuessOrder 的兜底值保持一致。
            // 那个脚本给场景里所有分不清类别的 SpriteRenderer 打 15，Player 就是 15；
            // 如果预制体这边留 0，场景会存一个 15 的 override，而挂在 Player 下的
            // ToolAnchor 按「父 0 + 1」算出来是 1，进场景直接被角色整个盖住。
            sprite.sortingOrder = 15;

            var body = go.AddComponent<Rigidbody2D>();
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var collider = go.AddComponent<BoxCollider2D>();
            if (displaySprite != null)
            {
                // PPU=378 后角色视觉高约 2 世界单位（原 PPU=100 时是 8.33）。
                // Pivot = Center（.meta alignment:0）→ bounds 中心即 transform 原点；
                // size 取视觉 ×0.9 留宽容手感。
                Vector2 size = (Vector2)displaySprite.bounds.size * 0.9f;
                // 圆角 + 无摩擦材质：滑过方块/平台接缝时不被"啃住"（2026-10-20 用户选定方案）
                collider.size = size - Vector2.one * (BodyEdgeRadius * 2f);
                collider.edgeRadius = BodyEdgeRadius;
                collider.sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(BodyPhysicsMaterialPath);

                // offset 取 0（与视觉同心）时，物理底边比精灵脚底高 5% 精灵高，站立时精灵脚底
                // 陷进地面约 0.11u。下移 offset 让物理底边（含圆角半径 r 的向外膨胀）正好贴住
                // 精灵脚底 —— 与敌人碰撞体同一约定（2026-10-20 修复）。
                collider.offset = new Vector2(0f,
                    collider.size.y * 0.5f + BodyEdgeRadius - displaySprite.bounds.size.y * 0.5f);
            }

            go.AddComponent<Animator>().runtimeAnimatorController = controller;

            var player = go.AddComponent<StarEco.Player.PlayerController>();

            // 私有字段序列化注入（无需 public setter）
            var so = new SerializedObject(player);
            so.FindProperty("_config").objectReferenceValue = config;
            so.ApplyModifiedProperties();

            go.AddComponent<StarEco.Player.PlayerAnimatorDriver>();

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                // 覆盖：保留已有预制体资产身份
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Object.DestroyImmediate(go);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(go, PrefabPath, InteractionMode.AutomatedAction);
            }

            Debug.Log($"[PlayerAnimationSetup] 预制体已生成 {PrefabPath}");

            // 修复(2026-10-26)：上面是从零 new GameObject("Player") 重建预制体，
            // 只会带上 SpriteRenderer/Rigidbody2D/BoxCollider2D/Animator/PlayerController/
            // PlayerAnimatorDriver —— PlayerCombat、AttackHitbox、SprayController、PurifyTank
            // 会全部丢失。丢 PlayerCombat 的直接后果：PlayerStates 里 `Player.Combat != null`
            // 永远为 false，按攻击键毫无反应；HUDController.RefreshHearts 每帧 NullReferenceException。
            // 所以重建完立刻跑一次 PlayerPrefabSetup，把战斗/环保组件与判定盒子物体固化回去。
            StarEco.EditorTools.PlayerPrefabSetup.Run();
        }

        // ===== 工具 =====

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
