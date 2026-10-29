using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using StarEco.Framework;

namespace StarEco.EditorTools
{
    /// <summary>
    /// M10 音频库一键装配：生成/更新 Assets/Resources/Audio/GameAudioLibrary.asset，
    /// 按文件名约定自动回填 SFX 与 BGM（Assets/Audio/Sfx/&lt;Id&gt;.wav、Assets/Resources/Audio/Bgm/&lt;Name&gt;.wav）。
    /// 可重复执行；素材缺失只警告不失败（运行时静默降级）。
    /// 菜单：StarEco → Setup → GameAudioLibrary
    /// </summary>
    public static class AudioLibrarySetup
    {
        private const string SfxDir = "Assets/Audio/Sfx";
        private const string BgmDir = "Assets/Resources/Audio/Bgm";
        private const string LibraryPath = "Assets/Resources/Audio/GameAudioLibrary.asset";

        [MenuItem("StarEco/Setup/GameAudioLibrary", priority = 40)]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Resources/Audio");

            var lib = AssetDatabase.LoadAssetAtPath<GameAudioLibrary>(LibraryPath);
            bool created = lib == null;
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<GameAudioLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            int bound = 0;
            int missing = 0;

            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Path.Combine(SfxDir, id + ".wav"));
                if (clip != null)
                {
                    lib.SetSfx(id, clip);
                    bound++;
                }
                else
                {
                    missing++;
                    Debug.LogWarning($"[AudioLibrarySetup] 缺少 SFX 素材：{SfxDir}/{id}.wav（可运行 Tools/audio-pipeline/synthesize.py 生成）");
                }
            }

            lib.TitleBgm = BindBgm("Title");
            lib.Level01Bgm = BindBgm("Level01");
            lib.Level02Bgm = BindBgm("Level02");
            lib.Level03Bgm = BindBgm("Level03");
            int bgmBound = 0;
            if (lib.TitleBgm != null) bgmBound++;
            if (lib.Level01Bgm != null) bgmBound++;
            if (lib.Level02Bgm != null) bgmBound++;
            if (lib.Level03Bgm != null) bgmBound++;

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AudioLibrarySetup] {(created ? "已创建" : "已更新")} {LibraryPath}：SFX {bound}/{Enum.GetValues(typeof(SfxId)).Length}，BGM {bgmBound}/4，缺失 {missing} 个 SFX（缺失项运行时静默降级）。");
        }

        private static AudioClip BindBgm(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Path.Combine(BgmDir, name + ".wav"));
            if (clip == null)
            {
                Debug.LogWarning($"[AudioLibrarySetup] 缺少 BGM 素材：{BgmDir}/{name}.wav（可运行 Tools/audio-pipeline/synthesize.py 生成）");
            }
            return clip;
        }
    }
}
