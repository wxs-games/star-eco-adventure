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
    /// Boss01 动画与预制体装配（M8 R8）：
    /// 帧目录 → 9 个 clip（Idle/Move/AttackA/AttackB/Hurt/Die/Enrage/Special/Charge）
    /// → Boss01.controller → Boss01.prefab。Charge 为专用蓄力帧（Boss01_Charge，8 帧循环）。
    /// 新增加 Boss 专属参数 AttackB / Special / Enrage / Charge；AttackA 复用通用 Attack 触发器。
    /// 菜单：StarEco → Setup → Boss01 Animation（幂等，可重复运行）
    /// </summary>
    public static class BossAnimationSetup
    {
        private const float DefaultFps = 12f;
        private const string BossName = "Boss01";
        private const string FramesRoot = "Assets/Art/Art/Boss/Boss01/Frames";
        private const string OutputRoot = "Assets/Animations/Boss01";
        private const string PrefabPath = "Assets/Prefabs/Boss/Boss01.prefab";
        private const string ConfigPath = "Assets/Settings/Boss/Boss01.asset";

        [MenuItem("StarEco/Setup/Boss01 Animation")]
        public static void Run()
        {
            EnsureFolder(OutputRoot);
            EnsureFolder(Path.GetDirectoryName(PrefabPath));
            EnsureFolder("Assets/Settings/Boss");

            var log = new System.Text.StringBuilder();

            var clips = BuildAllClips(log);
            log.AppendLine($"已生成 clip {clips.Count} 个：{string.Join(", ", clips.Keys)}");

            var controller = BuildController(clips, log);
            var config = EnsureConfig(log);
            BuildPrefab(controller, config, log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BossAnimationSetup] Boss01 动画管线搭建完成 ✓\n" + log);
        }

        // ===== 1. Clips =====

        private static Dictionary<string, AnimationClip> BuildAllClips(System.Text.StringBuilder log)
        {
            var clips = new Dictionary<string, AnimationClip>();
            if (!AssetDatabase.IsValidFolder(FramesRoot))
            {
                Debug.LogError($"[BossAnimationSetup] 缺少帧目录：{FramesRoot}");
                return clips;
            }

            foreach (var dir in AssetDatabase.GetSubFolders(FramesRoot))
            {
                string dirName = Path.GetFileName(dir);            // Boss01_Idle
                string shortName = dirName.Substring(dirName.LastIndexOf('_') + 1);   // Idle
                bool loop = shortName == "Idle" || shortName == "Move";

                var clip = BuildClip(dir, shortName, loop);
                if (clip != null)
                {
                    clips[shortName] = clip;
                }
            }

            // 蓄力 clip（Charge）：专用帧目录 Boss01_Charge（8 帧：核心鼓胀 → 绿雾外溢 → 回落），
            // 循环播放 1.1s 作为远程技能 B / 特殊招的长前摇提示（与 Boss01.asset 前摇对齐）。
            var charge = BuildChargeClip();
            if (charge != null)
            {
                clips["Charge"] = charge;
            }

            return clips;
        }

        // ===== 1.1 蓄力 clip（专用帧目录 Boss01_Charge，8 帧） =====

        private const string ChargeSourceAction = "Charge";
        private const float ChargeLengthSeconds = 1.1f;

        private static AnimationClip BuildChargeClip()
        {
            var sprites = LoadActionSprites(ChargeSourceAction);
            if (sprites.Count < 2)
            {
                Debug.LogWarning($"[BossAnimationSetup] {ChargeSourceAction} 帧不足 2 张，跳过 Charge 蓄力 clip。");
                return null;
            }

            // 8 帧依次播完 + 末尾补回首帧：末帧决定 clip 长度 = 1.1s，循环时首尾无缝。
            int n = sprites.Count;
            var keyframes = new ObjectReferenceKeyframe[n + 1];
            for (int i = 0; i < n; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i * (ChargeLengthSeconds / n),
                    value = sprites[i]
                };
            }
            keyframes[n] = new ObjectReferenceKeyframe
            {
                time = ChargeLengthSeconds,
                value = sprites[0]
            };

            var clip = new AnimationClip { frameRate = DefaultFps, name = "Charge" };
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;             // 前摇期间持续循环，直到 OnWindupEnd 切攻击动作
            settings.stopTime = ChargeLengthSeconds;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string clipPath = $"{OutputRoot}/Charge.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                clip.name = "Charge";
            }
            else
            {
                AssetDatabase.CreateAsset(clip, clipPath);
            }

            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            return clip;
        }

        /// <summary>按自然序读取某个动作帧目录下的全部 Sprite。</summary>
        private static List<Sprite> LoadActionSprites(string actionName)
        {
            string folder = $"{FramesRoot}/{BossName}_{actionName}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return new List<Sprite>();
            }

            return AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(NaturalOrder)
                .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(s => s != null)
                .ToList();
        }

        private static AnimationClip BuildClip(string folder, string clipName, bool loop)
        {
            var pngPaths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(NaturalOrder)
                .ToList();

            if (pngPaths.Count == 0)
            {
                return null;
            }

            var keyframes = new ObjectReferenceKeyframe[pngPaths.Count];
            for (int i = 0; i < pngPaths.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / DefaultFps,
                    value = AssetDatabase.LoadAssetAtPath<Sprite>(pngPaths[i])
                };
            }

            var clip = new AnimationClip { frameRate = DefaultFps };
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string clipPath = $"{OutputRoot}/{clipName}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
            {
                var preserved = AnimationUtility.GetAnimationEvents(existing);
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                clip.name = clipName;
                if (preserved != null && preserved.Length > 0)
                {
                    AnimationUtility.SetAnimationEvents(clip, preserved);
                }
            }
            else
            {
                AssetDatabase.CreateAsset(clip, clipPath);
                clip.name = clipName;
            }

            // 事件帧（沿用 M4"事件帧为权威"）：
            // Die 末端挂 OnDieEnd（死亡广播/回收依赖它，缺了会导致 Boss 死了不推进目标链）；
            // AttackA / AttackB 开窗（OnAttackWindowOpen/Close）由近战判定盒消费。
            var events = AnimationUtility.GetAnimationEvents(clip);
            if (events == null || events.Length == 0)
            {
                float length = clip.length;
                switch (clipName)
                {
                    case "Die":
                        events = new[]
                        {
                            new AnimationEvent
                            {
                                time = length > 0.1f ? length * 0.85f : 0.45f,
                                functionName = "OnDieEnd"
                            }
                        };
                        break;
                    case "AttackA":
                    case "AttackB":
                        events = new[]
                        {
                            new AnimationEvent { time = length * 0.25f, functionName = "OnAttackWindowOpen" },
                            new AnimationEvent { time = length * 0.60f, functionName = "OnAttackWindowClose" }
                        };
                        break;
                    default:
                        events = new AnimationEvent[0];
                        break;
                }

                AnimationUtility.SetAnimationEvents(clip, events);
            }

            return clip;
        }

        // ===== 2. Controller =====

        private static AnimatorController BuildController(
            Dictionary<string, AnimationClip> clips, System.Text.StringBuilder log)
        {
            string controllerPath = $"{OutputRoot}/{BossName}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.parameters = new[]
            {
                new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 0f },
                new AnimatorControllerParameter { name = "IsMove", type = AnimatorControllerParameterType.Bool, defaultBool = false },
                new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Charge", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "AttackB", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Special", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Enrage", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Hurt", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "Die", type = AnimatorControllerParameterType.Trigger }
            };

            var root = controller.layers[0].stateMachine;
            root.states = new ChildAnimatorState[0];
            root.anyStateTransitions = new AnimatorStateTransition[0];
            root.entryTransitions = new AnimatorTransition[0];

            AnimatorState idle = null, move = null, attackA = null, attackB = null,
                special = null, enrage = null, hurt = null, die = null, charge = null;

            idle = TryAdd(root, clips, "Idle", new Vector2(250f, 0f));
            move = TryAdd(root, clips, "Move", new Vector2(500f, -120f));
            charge = TryAdd(root, clips, "Charge", new Vector2(500f, 160f));
            attackA = TryAdd(root, clips, "AttackA", new Vector2(750f, 0f));
            attackB = TryAdd(root, clips, "AttackB", new Vector2(750f, 160f));
            special = TryAdd(root, clips, "Special", new Vector2(750f, 320f));
            enrage = TryAdd(root, clips, "Enrage", new Vector2(750f, 480f));
            hurt = TryAdd(root, clips, "Hurt", new Vector2(250f, 320f));
            die = TryAdd(root, clips, "Die", new Vector2(250f, 480f));

            root.defaultState = idle ?? move;

            if (idle != null && move != null)
            {
                Link(idle, move, t => t.AddCondition(AnimatorConditionMode.If, 0, "IsMove"));
                Link(move, idle, t => t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMove"));
            }

            // 触发器：Any State → 各动作
            foreach (var pair in new[]
            {
                (attackA, "Attack"), (charge, "Charge"), (attackB, "AttackB"), (special, "Special"),
                (enrage, "Enrage"), (hurt, "Hurt"), (die, "Die")
            })
            {
                if (pair.Item1 == null)
                {
                    continue;
                }

                var any = root.AddAnyStateTransition(pair.Item1);
                any.hasExitTime = false;
                any.duration = 0.02f;
                any.AddCondition(AnimatorConditionMode.If, 0, pair.Item2);
            }

            // 动作播完回 Idle / Move
            if (idle != null)
            {
                foreach (var state in new[] { attackA, attackB, special, enrage, hurt })
                {
                    if (state != null)
                    {
                        Link(state, idle, t => { t.hasExitTime = true; t.exitTime = 0.95f; });
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            log.AppendLine($"已生成 controller：{controllerPath}（参数 9 个，含 Charge/AttackB/Special/Enrage）");
            return controller;
        }

        private static AnimatorState TryAdd(AnimatorStateMachine root,
            Dictionary<string, AnimationClip> clips, string name, Vector2 pos)
        {
            if (!clips.TryGetValue(name, out var clip))
            {
                Debug.LogWarning($"[BossAnimationSetup] 缺少 {name} 帧目录，跳过该状态。");
                return null;
            }

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

        // ===== 3. Config + Prefab =====

        private static StarEco.Boss.BossConfig EnsureConfig(System.Text.StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<StarEco.Boss.BossConfig>(ConfigPath);
            if (config != null)
            {
                log.AppendLine("Boss01.asset 已存在，沿用（需改数值请在 Inspector 编辑）");
                return config;
            }

            config = ScriptableObject.CreateInstance<StarEco.Boss.BossConfig>();
            config.EnemyTypeId = "Boss01";
            config.MaxHP = 80;
            config.MoveSpeed = 2.2f;
            config.AttackDamage = 2;
            config.AttackRange = 2.2f;
            config.AttackInterval = 2.0f;
            config.AttackKnockback = 6f;
            config.HurtStunSeconds = 0.25f;
            config.PerceptionRadius = 20f;
            config.ProjectileSpeed = 7f;
            config.ProjectileMaxRange = 14f;
            config.ProjectilePrewarm = 10;
            AssetDatabase.CreateAsset(config, ConfigPath);
            log.AppendLine("已创建 Boss01.asset（HP 30 / 阈值 60%·30% / 喷雾倍率 1.5）");
            return config;
        }

        private static void BuildPrefab(AnimatorController controller,
            StarEco.Boss.BossConfig config, System.Text.StringBuilder log)
        {
            var sprite = FindFirstSprite();
            GameObject root;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(PrefabPath);
            }

            root = new GameObject(BossName);
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 3;

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            var body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.freezeRotation = true;

            // 碰撞体从精灵视觉 bounds 推导（PPU=378 后旧的 3.6×4.2 绝对值已失效）。
            // Pivot = Center（.meta alignment:0），bounds 中心即 transform 原点。
            // ×0.9 后碰撞体比精灵矮 10%，offset 取 0（同心）会让 Boss 落地时精灵脚底陷进地面，
            // 故 offset.y 取「碰撞体半高 − 精灵半高」，把碰撞体底边对齐精灵脚底。
            // 注意：不能用 sprite.bounds 推导 —— 那是「画布」尺寸，含四周透明留白。
            // 2026-10-26 Boss01 画布从 831×798 加宽到 1216×798（防止挥砍/倒地这类横向大动作
            // 被裁掉本体），若继续用 bounds，碰撞体会跟着胖 46%（实测 1.98 → 2.90），
            // 把纯美术改动变成玩法改动。这里固定「角色本体基准 831×798」与画布解耦：
            // 以后画布再怎么加宽，碰撞体/射击点/判定盒的尺寸都不受影响。
            const float ColliderRefW = 831f, ColliderRefH = 798f;
            float ppuRef = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 378f;
            Vector2 bossBounds = new Vector2(ColliderRefW / ppuRef, ColliderRefH / ppuRef);
            Vector2 bossColliderSize = bossBounds * 0.9f;
            Vector2 bossColliderOffset = new Vector2(0f, (bossColliderSize.y - bossBounds.y) * 0.5f);

            var col = root.AddComponent<BoxCollider2D>();
            col.size = bossColliderSize;
            col.offset = bossColliderOffset;

            root.AddComponent<StarEco.EnemyAI.EnemyAnimatorDriver>();

            var boss = root.AddComponent<StarEco.Boss.BossController>();
            var bossSo = new SerializedObject(boss);
            bossSo.FindProperty("_config").objectReferenceValue = config;
            bossSo.FindProperty("_firePointOffset").floatValue = bossColliderSize.x * 0.5f;
            var projectile = AssetDatabase.LoadAssetAtPath<StarEco.EnemyAI.EnemyProjectile>(
                "Assets/Prefabs/Projectiles/EnemyProjectile.prefab");
            if (projectile != null)
            {
                bossSo.FindProperty("_projectilePrefab").objectReferenceValue = projectile;
            }
            bossSo.ApplyModifiedPropertiesWithoutUndo();

            // 近战判定盒（复用 M4 EnemyAttackHitbox，事件帧开窗）
            var hitboxGo = new GameObject("AttackHitbox");
            hitboxGo.transform.SetParent(root.transform, false);
            // Center pivot：判定盒与身体中心同高
            hitboxGo.transform.localPosition = new Vector3(bossColliderSize.x * 0.44f, 0f, 0f);
            var hitboxCol = hitboxGo.AddComponent<BoxCollider2D>();
            hitboxCol.size = bossColliderSize * new Vector2(0.72f, 0.67f);
            hitboxCol.isTrigger = true;
            hitboxCol.enabled = false;
            var hitbox = hitboxGo.AddComponent<StarEco.EnemyAI.EnemyAttackHitbox>();
            var hitboxSo = new SerializedObject(hitbox);
            hitboxSo.FindProperty("_localOffset").vector2Value = new Vector2(bossColliderSize.x * 0.44f, 0f);
            hitboxSo.ApplyModifiedPropertiesWithoutUndo();

            // 视觉体量放大：Boss 与 Player 同为 PPU=378，Boss 原图仅比玩家略大，
            // 需localScale 2.2 拉开压迫感。碰撞体/判定盒为局部单位，会同步放大，
            // 因此它们在 scale 之前按 精灵 bounds×0.9 推导即自动与视觉重合。
            root.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
            root.layer = LayerMask.NameToLayer("Enemy");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            log.AppendLine($"已创建预制体 {PrefabPath}（BossController + Animator + 近战判定盒）");
        }

        private static Sprite FindFirstSprite()
        {
            var guids = AssetDatabase.FindAssets("t:Sprite",
                new[] { "Assets/Art/Art/Boss/Boss01/Frames/Boss01_Idle" });
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        private static string NaturalOrder(string path) =>
            Regex.Replace(Path.GetFileNameWithoutExtension(path), @"\d+", m => m.Value.PadLeft(6, '0'));

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
