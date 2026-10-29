using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarEco.Framework
{
    /// <summary>
    /// 音效标识（M10）。与 Assets/Audio/Sfx/ 下的 WAV 文件名一一对应，
    /// AudioLibrarySetup 按约定自动回填，避免场景里到处挂 AudioClip 引用。
    /// </summary>
    public enum SfxId
    {
        UIClick,
        Jump,
        Land,
        Attack,
        Hurt,
        Death,
        EnemyHit,
        BossHit,
        Pickup,
        PurifyComplete,
        SprayStart,
        HeavyRelease,
        Rescue,
        PuzzleSolved,
        Checkpoint,
        Splash,
        LevelComplete,
        LevelFail,
        BreakPlant,
        BreakStone,
        Footstep,
        GrassRustle,
    }

    /// <summary>
    /// 游戏音频库（M10 R1）：SFX 表 + 分关 BGM，单一 ScriptableObject 资产
    /// （Assets/Resources/Audio/GameAudioLibrary.asset），AudioManager 运行时懒加载。
    /// 素材全部由 Tools/audio-pipeline/synthesize.py 程序化合成。
    /// </summary>
    [CreateAssetMenu(fileName = "GameAudioLibrary", menuName = "StarEco/Game Audio Library", order = 20)]
    public class GameAudioLibrary : ScriptableObject
    {
        [Serializable]
        public class SfxEntry
        {
            public SfxId Id;
            public AudioClip Clip;
        }

        [Tooltip("SFX 表：Id 与 Assets/Audio/Sfx/ 下的文件名对应")]
        public List<SfxEntry> Sfx = new List<SfxEntry>();

        [Tooltip("标题页 BGM（MainMenuPanel 播放）")]
        public AudioClip TitleBgm;

        [Tooltip("第一关 BGM（LevelIndex=1）")]
        public AudioClip Level01Bgm;

        [Tooltip("第二关 BGM（LevelIndex=2）")]
        public AudioClip Level02Bgm;

        [Tooltip("第三关 BGM（LevelIndex=3）")]
        public AudioClip Level03Bgm;

        /// <summary>按标识取 SFX；缺失返回 null（调用方静默降级）。</summary>
        public AudioClip GetSfx(SfxId id)
        {
            for (int i = 0; i < Sfx.Count; i++)
            {
                if (Sfx[i] != null && Sfx[i].Id == id)
                {
                    return Sfx[i].Clip;
                }
            }
            return null;
        }

        /// <summary>
        /// 按关卡索引取 BGM。约定：LevelIndex 1/2/3 对应三关；0 或越界回退 TitleBgm。
        /// </summary>
        public AudioClip GetBgm(int levelIndex)
        {
            switch (levelIndex)
            {
                case 1: return Level01Bgm;
                case 2: return Level02Bgm;
                case 3: return Level03Bgm;
                default: return TitleBgm;
            }
        }

        /// <summary>设置/更新一个 SFX 条目（AudioLibrarySetup 用，幂等）。</summary>
        public void SetSfx(SfxId id, AudioClip clip)
        {
            for (int i = 0; i < Sfx.Count; i++)
            {
                if (Sfx[i] != null && Sfx[i].Id == id)
                {
                    Sfx[i].Clip = clip;
                    return;
                }
            }
            Sfx.Add(new SfxEntry { Id = id, Clip = clip });
        }
    }
}
