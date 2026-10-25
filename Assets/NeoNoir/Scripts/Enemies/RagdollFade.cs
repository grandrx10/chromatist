using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Waits, then dissolves a body away and removes it. Added to each
    /// ragdoll by <see cref="DeathRagdoll"/>, since the enemy that spawned it
    /// is gone by then.
    /// </summary>
    /// <remarks>
    /// Dissolves through _Fade, the same dither the blades use, so a material
    /// without it cannot fade: those parts simply switch off at the halfway
    /// point instead.
    /// </remarks>
    public sealed class RagdollFade : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private float delay;
        private float duration;
        private float age;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;

        /// <summary>Starts the countdown: <paramref name="wait"/> seconds whole, then <paramref name="fadeTime"/> seconds dissolving.</summary>
        public void Begin(float wait, float fadeTime)
        {
            delay = Mathf.Max(0f, wait);
            duration = Mathf.Max(0.01f, fadeTime);
            age = 0f;

            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (renderers == null)
            {
                return;
            }

            age += Time.deltaTime;

            if (age < delay)
            {
                return;
            }

            float visibility = 1f - Mathf.Clamp01((age - delay) / duration);

            if (visibility <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            foreach (Renderer part in renderers)
            {
                if (part == null)
                {
                    continue;
                }

                Material material = part.sharedMaterial;

                if (material == null || !material.HasProperty(FadeId))
                {
                    part.enabled = visibility >= 0.5f;
                    continue;
                }

                part.GetPropertyBlock(block);
                block.SetFloat(FadeId, visibility);
                part.SetPropertyBlock(block);
            }
        }
    }
}
