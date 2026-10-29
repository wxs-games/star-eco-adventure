using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StarEco.Combat
{
    /// <summary>
    /// 受击闪白：把 SpriteRenderer 的材质临时换成 <c>StarEco/SpriteFlash</c>，
    /// 由片元着色器把颜色插值到白色，再线性回落，结束后还原本材质。
    ///
    /// 不用「把 color 调到 1 倍以上」的写法：SpriteRenderer.color 写入的是 Color32
    /// 顶点色，超过 1 会被裁掉，实测画面亮度完全不变。
    /// </summary>
    public static class HitFlash
    {
        private static HitFlashRunner _runner;

        /// <summary>播放一次闪白；同一渲染器重复调用会打断上一次并重新起闪。</summary>
        public static void Play(SpriteRenderer sprite)
        {
            if (sprite == null)
            {
                return;
            }

            if (_runner == null)
            {
                var go = new GameObject("HitFlashRunner", typeof(HitFlashRunner));
                Object.DontDestroyOnLoad(go);
                _runner = go.GetComponent<HitFlashRunner>();
            }

            _runner.Play(sprite);
        }
    }

    /// <summary>闪白协程宿主：由 <see cref="HitFlash"/> 按需创建，全局唯一。</summary>
    public class HitFlashRunner : MonoBehaviour
    {
        /// <summary>闪白总时长（秒）。</summary>
        public const float Duration = 0.12f;

        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        private Material _flashMaterial;
        private MaterialPropertyBlock _block;
        private readonly Dictionary<SpriteRenderer, Coroutine> _running =
            new Dictionary<SpriteRenderer, Coroutine>();
        private readonly Dictionary<SpriteRenderer, Material> _originalMaterials =
            new Dictionary<SpriteRenderer, Material>();

        private void Awake()
        {
            _block = new MaterialPropertyBlock();

            var shader = Resources.Load<Shader>("Shaders/SpriteFlash");
            if (shader == null)
            {
                shader = Shader.Find("StarEco/SpriteFlash");
            }

            if (shader == null)
            {
                Debug.LogWarning("[HitFlash] 找不到 StarEco/SpriteFlash 着色器，受击闪白停用。");
                return;
            }

            _flashMaterial = new Material(shader)
            {
                name = "SpriteFlash (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            _flashMaterial.SetColor("_FlashColor", Color.white);
        }

        public void Play(SpriteRenderer sprite)
        {
            if (_flashMaterial == null || sprite == null)
            {
                return;
            }

            if (_running.TryGetValue(sprite, out var running) && running != null)
            {
                StopCoroutine(running);   // 连击时重新起闪，原材质基准保持不变
            }
            else
            {
                _originalMaterials[sprite] = sprite.sharedMaterial;
            }

            sprite.sharedMaterial = _flashMaterial;
            _running[sprite] = StartCoroutine(Flash(sprite));
        }

        private IEnumerator Flash(SpriteRenderer sprite)
        {
            float elapsed = 0f;

            while (elapsed < Duration && sprite != null)
            {
                elapsed += Time.deltaTime;
                ApplyAmount(sprite, 1f - Mathf.Clamp01(elapsed / Duration));
                yield return null;
            }

            if (sprite != null)
            {
                ApplyAmount(sprite, 0f);
                if (_originalMaterials.TryGetValue(sprite, out var original) && original != null)
                {
                    sprite.sharedMaterial = original;
                }
            }

            _running.Remove(sprite);
            _originalMaterials.Remove(sprite);
        }

        private void ApplyAmount(SpriteRenderer sprite, float amount)
        {
            sprite.GetPropertyBlock(_block);
            _block.SetFloat(FlashAmountId, amount);
            sprite.SetPropertyBlock(_block);
        }
    }
}
