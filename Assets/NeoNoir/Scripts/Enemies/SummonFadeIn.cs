using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Dissolves a freshly summoned enemy into being, then lets it loose.
    /// The reverse of <see cref="RagdollFade"/>.
    /// </summary>
    /// <remarks>
    /// While it fades in the enemy is harmless and untouchable: held dormant,
    /// so it neither moves nor attacks, and invulnerable, so blows pass it by.
    /// Once fully solid it is woken straight away, wherever the player is,
    /// and this component removes itself.
    ///
    /// Dissolves through _Fade, the same dither as a dying body, so a part
    /// whose material lacks it cannot fade: it simply switches on at the
    /// halfway point instead. Parts already switched off are left alone.
    /// </remarks>
    public sealed class SummonFadeIn : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<bool> canFade = new List<bool>();

        private Enemy enemy;
        private MaterialPropertyBlock block;
        private float duration;
        private float age;

        /// <summary>Starts <paramref name="unit"/> invisible and fades it in over <paramref name="fadeTime"/> seconds.</summary>
        public static SummonFadeIn Begin(Enemy unit, float fadeTime)
        {
            SummonFadeIn fade = unit.gameObject.AddComponent<SummonFadeIn>();
            fade.Setup(unit, fadeTime);
            return fade;
        }

        private void Setup(Enemy unit, float fadeTime)
        {
            enemy = unit;
            duration = Mathf.Max(0.01f, fadeTime);
            age = 0f;
            block = new MaterialPropertyBlock();

            enemy.StayDormant = true;
            enemy.Health.Invulnerable = true;

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled)
                {
                    continue;
                }

                bool fades = true;

                foreach (Material material in part.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(FadeId))
                    {
                        fades = false;
                        break;
                    }
                }

                renderers.Add(part);
                canFade.Add(fades);
            }

            // Before it is ever drawn, so it never flashes in whole.
            Apply(0f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float visibility = Mathf.Clamp01(age / duration);
            Apply(visibility);

            if (visibility >= 1f)
            {
                enemy.Health.Invulnerable = false;
                enemy.StayDormant = false;
                enemy.Wake();
                Destroy(this);
            }
        }

        private void Apply(float visibility)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer part = renderers[i];

                if (part == null)
                {
                    continue;
                }

                if (!canFade[i])
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
