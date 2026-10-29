using System.Text;
using UnityEditor;
using UnityEngine;

namespace StarEco.EditorTools
{
    /// <summary>
    /// Player 预制体战斗/环保组件固化（T17 依赖）。
    /// 把战斗、喷雾、近战判定等组件统一固化到 Player.prefab。
    /// </summary>
    public static class PlayerPrefabSetup
    {
        private const string PrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const string SprayHitboxName = "SprayHitbox";
        private const string AttackHitboxName = "AttackHitbox";

        // ---- 武器解耦：钳子不再烘焙进角色帧，改为 ToolAnchor 子物体 + 动画曲线驱动 ----
        private const string ToolAnchorName = "ToolAnchor";
        private const string ToolSpritePath = "Assets/Art/Art/Player/Tools/Player_Tool_Pliers.png";
        // 待机第 0 帧的右手锚点（朝右基准，单位：世界单位）。逐帧值由 Idle.anim 的曲线覆盖。
        private static readonly Vector3 ToolRestPosition = new Vector3(-0.05291f, -0.48280f, 0f);
        // 钳子原图是横握的，挂到下垂的手里需要翻过来；185° 与 art_gen 里合成预览一致。
        private const float ToolRestAngle = 185f;

        [MenuItem("StarEco/Setup/Player Prefab")]
        public static void Run()
        {
            var log = new StringBuilder();

            var combatConfig = AssetDatabase.LoadAssetAtPath<StarEco.Combat.CombatConfig>("Assets/Settings/CombatConfig.asset");
            var ecoConfig = AssetDatabase.LoadAssetAtPath<StarEco.Eco.EcoConfig>("Assets/Settings/EcoConfig.asset");
            var fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/fx_purify.prefab");
            var fxOneShot = fxPrefab != null ? fxPrefab.GetComponent<StarEco.Eco.FxOneShot>() : null;

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                // 玩家必须在 Player 层：敌人 EnemyConfig.PlayerLayer 掩码（256 = Player 层）同时用于
                // 感知查询（OverlapCircle）与近战命中盒的层校验。玩家留在 Default 层会让怪物
                // 完全感知不到玩家、近战判定也被层校验拦掉，等于敌人没有任何攻击力。
                root.layer = LayerMask.NameToLayer("Player");

                var combat = root.GetComponent<StarEco.Combat.PlayerCombat>();
                if (combat == null)
                {
                    combat = root.AddComponent<StarEco.Combat.PlayerCombat>();
                    log.AppendLine("已添加 PlayerCombat");
                }

                var tank = root.GetComponent<StarEco.Eco.PurifyTank>();
                if (tank == null)
                {
                    tank = root.AddComponent<StarEco.Eco.PurifyTank>();
                    log.AppendLine("已添加 PurifyTank");
                }

                var bodyCol = root.GetComponent<BoxCollider2D>();
                // 身体碰撞体带 edgeRadius 时物理半宽 = size.x/2 + r（bounds 不含 r），判定盒按物理半宽摆放
                float halfW = bodyCol != null ? bodyCol.size.x * 0.5f + bodyCol.edgeRadius : 0.8f;

                // 近战判定盒：盒体从身体右缘向前铺开，而非从身体中心外推。
                // 半宽 1.2，故中心置于 halfW + 0.35，盒左缘正好贴住身体右缘（无空隙），
                // 盒右缘伸到 halfW + 1.55 ≈ 2.4 单位处，覆盖一段挥击距离。
                const float AttackHalfW = 1.2f;
                float attackCenterX = halfW + AttackHalfW * 0.3f;

                var sprayHitboxGo = root.transform.Find(SprayHitboxName);
                if (sprayHitboxGo == null)
                {
                    var child = new GameObject(SprayHitboxName);
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3(halfW + 0.55f, 0f, 0f);
                    child.layer = root.layer;

                    var col = child.AddComponent<BoxCollider2D>();
                    col.isTrigger = true;
                    col.size = new Vector2(1.4f, 1.2f);
                    child.AddComponent<StarEco.Eco.SprayHitbox>();

                    sprayHitboxGo = child.transform;
                    log.AppendLine("已创建 SprayHitbox 子物体（size 1.4x1.2）");
                }
                else
                {
                    log.AppendLine("SprayHitbox 子物体已存在");
                }

                var attackHitboxGo = root.transform.Find(AttackHitboxName);
                if (attackHitboxGo == null)
                {
                    var child = new GameObject(AttackHitboxName);
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3(attackCenterX, 0.05f, 0f);
                    child.layer = root.layer;

                    var col = child.AddComponent<BoxCollider2D>();
                    col.isTrigger = true;
                    col.size = new Vector2(AttackHalfW * 2f, 1.5f);
                    col.enabled = false;

                    attackHitboxGo = child.transform;
                    log.AppendLine($"已创建 AttackHitbox 子物体（size {AttackHalfW * 2f}x1.5，中心 x={attackCenterX:F2}）");
                }
                else
                {
                    attackHitboxGo.localPosition = new Vector3(attackCenterX, 0.05f, 0f);
                    attackHitboxGo.gameObject.layer = root.layer;
                    var col = attackHitboxGo.GetComponent<BoxCollider2D>() ?? attackHitboxGo.gameObject.AddComponent<BoxCollider2D>();
                    col.isTrigger = true;
                    col.size = new Vector2(AttackHalfW * 2f, 1.5f);
                    col.enabled = false;
                    log.AppendLine($"AttackHitbox 子物体已校正（中心 x={attackCenterX:F2}）");
                }

                var attackHitbox = root.GetComponent<StarEco.Combat.AttackHitbox>();
                if (attackHitbox == null)
                {
                    attackHitbox = root.AddComponent<StarEco.Combat.AttackHitbox>();
                    log.AppendLine("已添加 AttackHitbox 组件");
                }

                var spray = root.GetComponent<StarEco.Eco.SprayController>();
                if (spray == null)
                {
                    spray = root.AddComponent<StarEco.Eco.SprayController>();
                    log.AppendLine("已添加 SprayController");
                }

                EnsureToolAnchor(root, log);

                Inject(combat, "_config", combatConfig);
                Inject(tank, "_config", ecoConfig);
                Inject(attackHitbox, "_hitbox", attackHitboxGo.GetComponent<Collider2D>());

                var spraySo = new SerializedObject(spray);
                SetIfPresent(spraySo, "_config", ecoConfig);
                SetIfPresent(spraySo, "_fxPurifyPrefab", fxOneShot);
                SetIfPresent(spraySo, "_hitbox", sprayHitboxGo.GetComponent<StarEco.Eco.SprayHitbox>());
                spraySo.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerPrefabSetup] Player 预制体组件固化完成 ✓\n" + log);
        }

        /// <summary>
        /// 固化手中工具锚点。幂等：已有则校正参数，没有才创建。
        /// 工具是 Player 的子物体，不带碰撞体，纯视觉；位置由 AnimationClip 的
        /// ToolAnchor.m_LocalPosition 曲线逐帧驱动（Idle.anim 已含该曲线）。
        /// </summary>
        private static void EnsureToolAnchor(GameObject root, StringBuilder log)
        {
            var body = root.GetComponent<SpriteRenderer>();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ToolSpritePath);
            if (sprite == null)
            {
                Debug.LogWarning($"[PlayerPrefabSetup] 找不到工具精灵 {ToolSpritePath}，跳过 ToolAnchor");
                return;
            }

            var anchorTr = root.transform.Find(ToolAnchorName);
            bool created = false;
            if (anchorTr == null)
            {
                var go = new GameObject(ToolAnchorName);
                anchorTr = go.transform;
                anchorTr.SetParent(root.transform, false);
                created = true;
            }

            anchorTr.gameObject.layer = root.layer;
            anchorTr.localPosition = ToolRestPosition;
            anchorTr.localRotation = Quaternion.Euler(0f, 0f, ToolRestAngle);
            anchorTr.localScale = Vector3.one;

            var sr = anchorTr.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                sr = anchorTr.gameObject.AddComponent<SpriteRenderer>();
            }

            sr.sprite = sprite;
            if (body != null)
            {
                sr.sortingLayerID = body.sortingLayerID;
                // 比角色高 1 层：钳子垂在腿前面，若排在角色之后会被腿切掉一块。
                // 手压在工具上的层次由美术帧本身承担（身体帧带手，手在钳子上缘之外）。
                sr.sortingOrder = body.sortingOrder + 1;
            }

            sr.color = Color.white;
            sr.flipX = false;

            var follower = anchorTr.GetComponent<StarEco.Player.PlayerToolAnchor>();
            if (follower == null)
            {
                follower = anchorTr.gameObject.AddComponent<StarEco.Player.PlayerToolAnchor>();
            }

            Inject(follower, "_body", body);
            Inject(follower, "_tool", sr);

            log.AppendLine(created
                ? $"已创建 {ToolAnchorName} 子物体（{sprite.rect.width}x{sprite.rect.height}px, PPU 378）"
                : $"{ToolAnchorName} 子物体已存在，参数已校正");
        }

        private static void Inject(Component target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SetIfPresent(so, field, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetIfPresent(SerializedObject so, string field, Object value)
        {
            var prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[PlayerPrefabSetup] 字段 {field} 不存在（脚本已变更？）");
            }
        }
    }
}
