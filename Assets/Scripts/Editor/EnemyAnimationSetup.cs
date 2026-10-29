using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 一键搭建敌人动画管线：动画 clip → AnimatorController → Prefab。
    /// 覆盖 Enemy01/02/03 + Tower01 + EnemyProjectile。
    /// 菜单：StarEco → Setup → Enemy Animation（幂等，可重复运行）
    /// </summary>
    public static class EnemyAnimationSetup
    {
        private const float DefaultFps = 12f;

        [MenuItem("StarEco/Setup/Enemy Animation")]
        public static void Run()
        {
            // 体量不再硬编码世界单位：PPU 从 100 改为 378 后角色视觉缩小 3.78倍，
            // 旧的绝对尺寸全部失效（碰撞体曾达视觉体量的 3.0~3.4 倍）。
            // 现统一从精灵实际 bounds 推导，下列向量只表达「相对碰撞体的比例」，
            // 与 PPU / 素材尺寸解耦。
            BuildUnit("Enemy01", "Assets/Art/Art/Enemies/Enemy01/Frames", "Assets/Animations/Enemy01",
                "Assets/Prefabs/Enemies/Enemy01.prefab", "Assets/Settings/Enemy01Config.asset",
                "StarEco.EnemyAI.Enemy01Controller", false, false, false,
                new Vector2(0.556f, 1f), new Vector2(0.611f, 1f), 0.611f);
            BuildUnit("Enemy02", "Assets/Art/Art/Enemies/Enemy02/Frames", "Assets/Animations/Enemy02",
                "Assets/Prefabs/Enemies/Enemy02.prefab", "Assets/Settings/Enemy02Config.asset",
                "StarEco.EnemyAI.Enemy02Controller", false, true, false,
                null, null, 0.778f);
            BuildUnit("Enemy03", "Assets/Art/Art/Enemies/Enemy03/Frames", "Assets/Animations/Enemy03",
                "Assets/Prefabs/Enemies/Enemy03.prefab", "Assets/Settings/Enemy03Config.asset",
                "StarEco.EnemyAI.Enemy03Controller", true, false, false,
                new Vector2(0.571f, 0.889f), new Vector2(0.643f, 0f), 0f);
            BuildUnit("Tower01", "Assets/Art/Art/Towers/Tower01/Frames", "Assets/Animations/Tower01",
                "Assets/Prefabs/Towers/Tower01.prefab", "Assets/Settings/Tower01Config.asset",
                "StarEco.EnemyAI.Tower01Controller", false, true, true,
                null, null, 0.625f);
            BuildProjectilePrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EnemyAnimationSetup] 敌人动画管线搭建完成 ✓");
        }

        /// <param name="hitboxSizeRatio">攻击判定盒相对碰撞体的尺寸比例，null 表示该单位无近战判定。</param>
        /// <param name="hitboxOffsetRatio">攻击判定盒沿 +X 的偏移，相对碰撞体宽度。</param>
        /// <param name="firePointOffsetRatio">远程发射点沿 +X 的偏移，相对碰撞体宽度；0 表示无发射点。</param>
        private static void BuildUnit(string name, string framesRoot, string outputRoot,
            string prefabPath, string configPath, string controllerType,
            bool hasSpawn, bool hasFirePoint, bool isStatic,
            Vector2? hitboxSizeRatio, Vector2? hitboxOffsetRatio, float firePointOffsetRatio)
        {
            EnsureFolder(outputRoot);
            EnsureFolder(Path.GetDirectoryName(prefabPath));

            var clips = BuildAllClips(name, framesRoot, outputRoot);
            var controller = BuildController(name, outputRoot, clips, hasSpawn, isStatic);
            BuildPrefab(name, prefabPath, configPath, controllerType, controller, hasFirePoint, isStatic,
                hitboxSizeRatio, hitboxOffsetRatio, firePointOffsetRatio);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var folder = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }

        // ===== 1. AnimationClip 生成 =====

        private static Dictionary<string, AnimationClip> BuildAllClips(string unitName, string framesRoot, string outputRoot)
        {
            var clips = new Dictionary<string, AnimationClip>();
            if (!AssetDatabase.IsValidFolder(framesRoot))
            {
                Debug.LogWarning($"[EnemySetup] 缺少帧图目录：{framesRoot}");
                return clips;
            }

            var subDirs = AssetDatabase.GetSubFolders(framesRoot);
            foreach (var dir in subDirs)
            {
                var clipName = Path.GetFileName(dir); // 如 Enemy01_Idle
                var shortName = clipName.Substring(clipName.LastIndexOf('_') + 1); // 如 Idle
                bool loop = shortName == "Idle" || shortName == "Move" || shortName == "Chase";
                var clip = BuildClip(clipName, shortName, dir, outputRoot, loop, DefaultFps);
                if (clip != null) clips[shortName] = clip;
            }
            return clips;
        }

        private static AnimationClip BuildClip(string animName, string clipName, string folder, string outputRoot, bool loop, float fps)
        {
            var pngPaths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(NaturalOrder)
                .ToList();

            if (pngPaths.Count == 0) return null;

            foreach (var p in pngPaths) EnsureSpriteImport(p);
            AssetDatabase.Refresh();

            var keyframes = new ObjectReferenceKeyframe[pngPaths.Count];
            for (int i = 0; i < pngPaths.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / fps,
                    value = AssetDatabase.LoadAssetAtPath<Sprite>(pngPaths[i])
                };
            }

            var clip = new AnimationClip { frameRate = fps };
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var clipPath = $"{outputRoot}/{clipName}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
            {
                var preservedEvents = AnimationUtility.GetAnimationEvents(existing);
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                clip.name = clipName;
                if (preservedEvents != null && preservedEvents.Length > 0)
                    AnimationUtility.SetAnimationEvents(clip, preservedEvents);
            }
            else
            {
                AssetDatabase.CreateAsset(clip, clipPath);
                clip.name = clipName;
            }
            return clip;
        }

        private static string NaturalOrder(string path) =>
            Regex.Replace(Path.GetFileNameWithoutExtension(path), @"\d+", m => m.Value.PadLeft(6, '0'));

        private static void EnsureSpriteImport(string pngPath)
        {
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null || (importer.textureType == TextureImporterType.Sprite &&
                                     importer.spriteImportMode == SpriteImportMode.Single)) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        // ===== 2. AnimatorController 生成 =====

        private static AnimatorController BuildController(string unitName, string outputRoot,
            Dictionary<string, AnimationClip> clips, bool hasSpawn, bool isStatic)
        {
            var controllerPath = $"{outputRoot}/{unitName}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                AssetDatabase.DeleteAsset(controllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.parameters = new[]
            {
                new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 0f },
                new AnimatorControllerParameter { name = "IsMove", type = AnimatorControllerParameterType.Bool, defaultBool = false },
                new AnimatorControllerParameter { name = "IsAiming", type = AnimatorControllerParameterType.Bool, defaultBool = false },
                new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Fire", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Hurt", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Die", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Spawn", type = AnimatorControllerParameterType.Trigger },
            };

            var root = controller.layers[0].stateMachine;
            root.states = new ChildAnimatorState[0];
            root.anyStateTransitions = new AnimatorStateTransition[0];
            root.entryTransitions = new AnimatorTransition[0];

            AnimatorState idle = null, move = null, attack = null, hurt = null, die = null, spawn = null, aim = null, fire = null;
            float x = 200, y = 0;

            if (hasSpawn && clips.TryGetValue("Spawn", out var spawnClip))
            {
                spawn = AddState(root, "Spawn", spawnClip, new Vector2(x, y)); y += 150;
            }
            if (clips.TryGetValue("Idle", out var idleClip))
                idle = AddState(root, "Idle", idleClip, new Vector2(x, y)); y += 150;
            if (clips.TryGetValue("Move", out var moveClip))
                move = AddState(root, "Move", moveClip, new Vector2(x + 250, y - 150));
            if (clips.TryGetValue("Chase", out var chaseClip))
                move = AddState(root, "Chase", chaseClip, new Vector2(x + 250, y - 150));
            if (clips.TryGetValue("Attack", out var attackClip))
                attack = AddState(root, "Attack", attackClip, new Vector2(x + 500, y - 150));
            if (clips.TryGetValue("Aim", out var aimClip))
                aim = AddState(root, "Aim", aimClip, new Vector2(x + 250, y - 150));
            if (clips.TryGetValue("Fire", out var fireClip))
                fire = AddState(root, "Fire", fireClip, new Vector2(x + 500, y - 150));
            if (clips.TryGetValue("Hurt", out var hurtClip))
                hurt = AddState(root, "Hurt", hurtClip, new Vector2(x, y));
            if (clips.TryGetValue("Die", out var dieClip))
                die = AddState(root, "Die", dieClip, new Vector2(x + 250, y));
            if (clips.TryGetValue("Disable", out var disableClip))
                die = AddState(root, "Disable", disableClip, new Vector2(x + 250, y));

            root.defaultState = spawn ?? idle;

            // Idle ↔ Move/Chase (IsMove)
            if (idle != null && move != null)
            {
                Link(idle, move, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsMove"));
                Link(move, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMove"));
            }
            // Idle ↔ Aim (IsAiming) — Tower01
            if (idle != null && aim != null)
            {
                Link(idle, aim, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsAiming"));
                Link(aim, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAiming"));
            }
            // Spawn → Idle (exitTime)
            if (spawn != null && idle != null)
                Link(spawn, idle, t => { t.hasExitTime = true; t.exitTime = 0.95f; });

            // AnyState → Attack/Hurt/Die/Fire (trigger)
            foreach (var (state, trigger) in new[] {
                (attack, "Attack"), (fire, "Fire"), (hurt, "Hurt"), (die, "Die")
            })
            {
                if (state == null) continue;
                var any = root.AddAnyStateTransition(state);
                any.hasExitTime = false;
                any.duration = 0.02f;
                any.AddCondition(AnimatorConditionMode.If, 0, trigger);
            }

            // Attack/Fire/Hurt → 回 Idle (exitTime)
            foreach (var state in new[] { attack, fire, hurt })
            {
                if (state == null || idle == null) continue;
                Link(state, idle, t => { t.hasExitTime = true; t.exitTime = 0.95f; });
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState AddState(AnimatorStateMachine root, string name, AnimationClip clip, Vector2 pos)
        {
            var state = root.AddState(name, pos);
            state.motion = clip;
            return state;
        }

        private static void Link(AnimatorState from, AnimatorState to, System.Action<AnimatorStateTransition> setup)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0.05f;
            setup?.Invoke(t);
        }

        // ===== 3. Prefab 生成 =====

        private static void BuildPrefab(string unitName, string prefabPath, string configPath,
            string controllerType, AnimatorController controller, bool hasFirePoint, bool isStatic,
            Vector2? hitboxSizeRatio, Vector2? hitboxOffsetRatio, float firePointOffsetRatio)
        {
            var go = new GameObject(unitName);
            int enemyLayer = UnityEngine.LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) go.layer = enemyLayer;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = FindFirstSprite(unitName);
            var anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;

            // 控制器组件
            var ctrlType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(controllerType))
                .FirstOrDefault(t => t != null);
            Component ctrl = null;
            if (ctrlType != null) ctrl = go.AddComponent(ctrlType);
            go.AddComponent<StarEco.EnemyAI.EnemyAnimatorDriver>();

            // Config 赋值
            var config = AssetDatabase.LoadAssetAtPath<StarEco.EnemyAI.EnemyConfig>(configPath);
            if (config != null && ctrl != null)
            {
                var configField = ctrl.GetType().GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (configField != null) configField.SetValue(ctrl, config);
            }

            // Rigidbody2D + BoxCollider2D —— 碰撞体一律从精灵视觉 bounds 推导（×0.9），
            // 不再使用硬编码世界单位。Sprite Pivot = Center（.meta alignment:0），
            // 故 sprite.bounds 中心即 transform 原点。
            // 但 ×0.9 后碰撞体比精灵矮 10%，若 offset 仍取 0（同心），
            // 物理落底时碰撞体底边贴地、精灵脚底会陷进地面 5% 精灵高。
            // 故 offset.y 取「碰撞体半高 − 精灵半高」，把碰撞体底边对齐精灵脚底。
            Vector2 bounds = new Vector2(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);
            Vector2 colliderSize = bounds * 0.9f;
            Vector2 colliderOffset = new Vector2(0f, (colliderSize.y - bounds.y) * 0.5f);
            float hitboxOffset = hitboxOffsetRatio.HasValue ? colliderSize.x * hitboxOffsetRatio.Value.x : 0f;
            Vector2? hitboxSize = hitboxSizeRatio.HasValue ? colliderSize * hitboxSizeRatio.Value : (Vector2?)null;
            float firePointOffset = firePointOffsetRatio * colliderSize.x;

            if (!isStatic)
            {
                var rb = go.AddComponent<Rigidbody2D>();
                rb.freezeRotation = true;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = colliderSize;
                col.offset = colliderOffset;
            }
            else
            {
                // 静态单位（Tower01）也必须有 Rigidbody2D，否则它与玩家 AttackHitbox 之间
                // 不会产生任何 trigger 事件 —— 玩家的近战判定将永远打不到它。
                // 用 Kinematic 而非省略：不模拟动力学、不被推动，但保留 trigger 事件收发能力。
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.gravityScale = 0f;
                rb.freezeRotation = true;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = colliderSize;
                col.offset = colliderOffset;
            }

            // AttackHitbox 子物体（近战单位）
            if (hitboxSize.HasValue)
            {
                var hitboxGo = new GameObject("AttackHitbox");
                hitboxGo.transform.SetParent(go.transform, false);
                // Center pivot：碰撞体与视觉同中心，判定盒沿 +X 前伸，y 与身体中心一致
                hitboxGo.transform.localPosition = new Vector3(hitboxOffset, 0f, 0f);
                var hitboxCol = hitboxGo.AddComponent<BoxCollider2D>();
                hitboxCol.isTrigger = true;
                hitboxCol.size = hitboxSize.Value;
                var hb = hitboxGo.AddComponent<StarEco.EnemyAI.EnemyAttackHitbox>();
                var hflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                hb.GetType().GetField("_localOffset", hflags)?.SetValue(hb, new Vector2(hitboxOffset, 0f));
            }

            // FirePoint 子物体
            if (hasFirePoint)
            {
                var fireGo = new GameObject("FirePoint");
                fireGo.transform.SetParent(go.transform, false);
                // Center pivot：与身体中心同高，弹幕不会从脚底射出
                fireGo.transform.localPosition = new Vector3(firePointOffset, 0f, 0f);
            }

            // 保存 Prefab
            var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            // 保存后补齐序列化引用（_sourcePrefab / _firePoint / _projectilePrefab）
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            if (ctrlType != null)
            {
                var savedCtrl = saved.GetComponent(ctrlType);
                if (savedCtrl != null)
                {
                    ctrlType.GetField("_sourcePrefab", flags)?.SetValue(savedCtrl, savedCtrl);
                    if (hasFirePoint)
                    {
                        var fp = saved.transform.Find("FirePoint");
                        ctrlType.GetField("_firePoint", flags)?.SetValue(savedCtrl, fp);
                        var proj = AssetDatabase.LoadAssetAtPath<StarEco.EnemyAI.EnemyProjectile>(
                            "Assets/Prefabs/Projectiles/EnemyProjectile.prefab");
                        ctrlType.GetField("_projectilePrefab", flags)?.SetValue(savedCtrl, proj);
                    }
                    EditorUtility.SetDirty(saved);
                }
            }
            Debug.Log($"[EnemySetup] 生成 {unitName} Prefab");
        }

        private static void BuildProjectilePrefab()
        {
            EnsureFolder("Assets/Prefabs/Projectiles");
            var go = new GameObject("EnemyProjectile");
            int projLayer = UnityEngine.LayerMask.NameToLayer("Projectile");
            if (projLayer >= 0) go.layer = projLayer;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Art/FX/Projectiles/proj_toxin_sting.png");
            sr.color = Color.white;
            sr.sortingOrder = 20; // 高于草垛(10)/装饰树与堵塞物(15)，弹道不被前景遮住
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.22f; // 判定略小于精灵，留出擦弹空间
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Prefabs/Projectiles/ProjectileTrail.mat");
            trail.time = 0.22f;
            trail.startWidth = 0.24f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            trail.textureMode = LineTextureMode.Stretch;
            trail.sortingOrder = 19;
            go.AddComponent<StarEco.EnemyAI.EnemyProjectile>();
            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Projectiles/EnemyProjectile.prefab");
            Object.DestroyImmediate(go);
            Debug.Log("[EnemySetup] 生成 EnemyProjectile Prefab");
        }

        private static Sprite FindFirstSprite(string unitName)
        {
            var root = unitName == "Tower01"
                ? $"Assets/Art/Art/Towers/{unitName}/Frames/{unitName}_Idle"
                : $"Assets/Art/Art/Enemies/{unitName}/Frames/{unitName}_Idle";
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
