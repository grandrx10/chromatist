using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Makes a hammer glow while this lasts: its own materials tinted a
    /// colour, or swapped for a glow material. Put back exactly as they were
    /// when this is destroyed.
    /// </summary>
    /// <remarks>
    /// The tint goes through a property block, so no material is edited.
    /// <see cref="GlowIntensity"/> brightens it, for a charge-up.
    ///
    /// It eases in from each part's own colour over the fade-in time given
    /// to <see cref="On"/>, and <see cref="FadeOut"/> eases it back before it
    /// removes itself. Destroying it outright skips the fade.
    /// </remarks>
    public sealed class HammerGlow : MonoBehaviour
    {
        private Renderer[] renderers;
        private Material[][] originalMaterials;
        private MaterialPropertyBlock block;
        private Material glowMaterial;
        private Color glowColour;
        private float glowIntensity = 1f;

        // Each renderer's own colour, faded from and back to.
        private Color[] baseColours;

        // 0 is the hammer's own colours, 1 the full glow.
        private float fade;
        private float fadeTarget = 1f;
        private float fadeSpeed = float.PositiveInfinity;

        /// <summary>Multiplies the glow's brightness. 1 is as started.</summary>
        public float GlowIntensity
        {
            get => glowIntensity;
            set
            {
                glowIntensity = Mathf.Max(0f, value);
                ApplyGlow();
            }
        }

        /// <summary>
        /// Starts <paramref name="hammer"/> glowing until the returned glow is
        /// destroyed. With a <paramref name="material"/>, it replaces every
        /// material on the hammer and <paramref name="colour"/> is ignored;
        /// without one, the hammer's own materials are tinted
        /// <paramref name="colour"/>. Reaches full glow
        /// <paramref name="fadeIn"/> seconds from now.
        /// </summary>
        public static HammerGlow On(GameObject hammer, Material material, Color colour, float fadeIn = 0f)
        {
            // One still fading out from last time goes at once, so it cannot
            // put the materials back over this one later.
            foreach (HammerGlow old in hammer.GetComponents<HammerGlow>())
            {
                DestroyImmediate(old);
            }

            HammerGlow glow = hammer.AddComponent<HammerGlow>();
            glow.fadeSpeed = Speed(fadeIn);
            glow.Begin(material, colour);
            return glow;
        }

        /// <summary>
        /// Eases back to the hammer's own colours over <paramref name="seconds"/>,
        /// then puts its materials back and removes the glow.
        /// </summary>
        public void FadeOut(float seconds)
        {
            fadeTarget = 0f;
            fadeSpeed = Speed(seconds);
        }

        private static float Speed(float seconds)
        {
            return seconds > 0f ? 1f / seconds : float.PositiveInfinity;
        }

        private void Begin(Material material, Color colour)
        {
            glowMaterial = material;
            glowColour = colour;
            block = new MaterialPropertyBlock();

            renderers = GetComponentsInChildren<Renderer>();
            originalMaterials = new Material[renderers.Length][];
            baseColours = new Color[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                originalMaterials[i] = renderers[i].sharedMaterials;

                Material own = renderers[i].sharedMaterial;
                baseColours[i] = own != null && own.HasProperty(FlatMaterial.BaseColorId)
                    ? own.GetColor(FlatMaterial.BaseColorId)
                    : Color.white;

                if (material == null)
                {
                    continue;
                }

                Material[] swapped = new Material[originalMaterials[i].Length];

                for (int k = 0; k < swapped.Length; k++)
                {
                    swapped[k] = material;
                }

                renderers[i].sharedMaterials = swapped;
            }

            fade = float.IsPositiveInfinity(fadeSpeed) ? 1f : 0f;
            ApplyGlow();
        }

        private void Update()
        {
            if (fade != fadeTarget)
            {
                fade = Mathf.MoveTowards(fade, fadeTarget, fadeSpeed * Time.deltaTime);
                ApplyGlow();
            }

            if (fade <= 0f && fadeTarget <= 0f)
            {
                Destroy(this);
            }
        }

        /// <summary>
        /// Colours the hammer for the current intensity: the glow material's
        /// own colour scaled up, or the glow colour without one.
        /// </summary>
        private void ApplyGlow()
        {
            if (renderers == null)
            {
                return;
            }

            Color colour = glowColour;

            if (glowMaterial != null)
            {
                colour = glowMaterial.HasProperty(FlatMaterial.BaseColorId)
                    ? glowMaterial.GetColor(FlatMaterial.BaseColorId)
                    : Color.white;
            }

            Color scaled = colour * glowIntensity;
            scaled.a = colour.a;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    FlatMaterial.Tint(renderers[i], Color.LerpUnclamped(baseColours[i], scaled, fade), block);
                }
            }
        }

        private void OnDestroy()
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                renderers[i].sharedMaterials = originalMaterials[i];
                renderers[i].SetPropertyBlock(null);
            }
        }
    }
}
