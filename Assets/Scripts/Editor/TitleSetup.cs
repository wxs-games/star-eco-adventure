using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarEco.UI;

namespace StarEco.EditorTools
{
    /// <summary>
    /// 主菜单场景一键搭建（M7 T21）：生成 Assets/Scenes/Title.unity。
    /// 场景内容：UIManager（根节点，MonoSingleton 常驻）+ MainMenuPanel + 相机 + 简单氛围背景。
    /// Build Settings：Title = index 0（社区主流结构：标题场景在最前）。
    /// 菜单：StarEco → Setup → Title（幂等：场景已存在则跳过）
    /// </summary>
    public static class TitleSetup
    {
        private const string ScenePath = "Assets/Scenes/Title.unity";

        [MenuItem("StarEco/Setup/Title")]
        public static void Run()
        {
            var log = new StringBuilder();

            EnsureFolder("Assets/Scenes");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                log.AppendLine("Title.unity 已存在，跳过场景创建（需要重建请先删除该场景）");
                AssetDatabase.SaveAssets();
                Debug.Log("[TitleSetup] 完成 ✓\n" + log);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // —— 相机（主菜单只需要一台相机，无游戏逻辑） ——
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = 5.5f;
            cam.backgroundColor = new Color(0.06f, 0.10f, 0.09f, 1f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            // —— UIManager：必须挂根节点 ——
            // MonoSingleton 只对根节点做 DontDestroyOnLoad。UIManager 跨 Title/Level 两场景常驻，
            // 因此这里必须挂根节点（与 LevelManager 的非根节点要求正好相反，M6 教训）。
            var uiRootGo = new GameObject("UIManager");
            uiRootGo.AddComponent<UIManager>();

            // —— 主菜单面板 ——
            var menuGo = new GameObject("MainMenu");
            menuGo.AddComponent<MainMenuPanel>();

            // —— 其他面板预挂（Title 场景内可开设置/关卡选择/图鉴） ——
            var settingsGo = new GameObject("Settings");
            settingsGo.AddComponent<SettingsPanel>();

            var levelSelectGo = new GameObject("LevelSelect");
            levelSelectGo.AddComponent<LevelSelectPanel>();

            var codexGo = new GameObject("Codex");
            codexGo.AddComponent<CodexPanel>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine($"已创建场景 {ScenePath}（UIManager + MainMenu + Settings/LevelSelect/Codex）");

            EnsureTitleFirstInBuildSettings(ScenePath, log);

            AssetDatabase.SaveAssets();
            Debug.Log("[TitleSetup] 完成 ✓\n" + log);
        }

        /// <summary>Build Settings 顺序：Title = index 0，Level01 = index 1。</summary>
        private static void EnsureTitleFirstInBuildSettings(string titlePath, StringBuilder log)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            scenes.RemoveAll(s => s.path == titlePath);

            int levelIndex = -1;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == "Assets/Scenes/Level01.unity")
                {
                    levelIndex = i;
                    break;
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(titlePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            log.AppendLine($"Build Settings：Title=index 0，Level01=index {(levelIndex >= 0 ? levelIndex + 1 : 1)}");
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
