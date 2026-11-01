using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The ball of an explosion: a sphere that bursts out to the blast's
    /// radius from its centre, then dissolves away.
    /// </summary>
    /// <remarks>
    /// Swells to full size over <see cref="Settings.growTime"/>, fast at
    /// first and easing in, and dissolves over <see cref="Settings.fadeTime"/>
    /// through NeoNoir/Flat's dithered _Fade, starting as it reaches full
    /// size. A dither rather than transparency, so every pixel drawn stays
    /// full colour for the spot colour threshold. A material without _Fade
    /// cannot dissolve and simply vanishes at the end instead.
    ///
    /// Outlined by a second sphere, <see cref="Settings.outlineWidth"/>
    /// bigger and drawn inside out, so only its rim shows round the ball.
    /// Its own outline rather than the edge pass's, which a dithering
    /// surface would break up.
    ///
    /// Purely visual. Spawned at runtime and built from primitives, so it
    /// needs no prefab.
    /// </remarks>
    public sealed class BlastSphere : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Seconds the sphere takes to burst out to full size.")]
            [Min(0.01f)] public float growTime = 0.12f;

            [Tooltip("Seconds it takes to dissolve once full size.")]
            [Min(0.01f)] public float fadeTime = 0.3f;

            [Tooltip("Colour of the sphere. Above 1 blooms. Keep it strongly saturated, or white.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Width of the outline round the sphere at full size, in units. 0 draws none.")]
            [Min(0f)] public float outlineWidth = 0.15f;

            [Tooltip("Colour of the outline. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color outlineColour = new Color(2f, 2f, 2f, 1f);

            [Tooltip("Material for the sphere. Empty uses NeoNoir/Flat in the colour above.")]
            public Material material;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private Settings settings;
        private MeshRenderer sphere;
        private MeshRenderer outline;
        private MaterialPropertyBlock block;
        private bool canFade;
        private float size;
        private float age;

        /// <summary>Bursts a sphere at <paramref name="centre"/>, out to <paramref name="radius"/>.</summary>
        public static BlastSphere Create(Vector3 centre, float radius, Settings settings)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(piece.GetComponent<Collider>());
            piece.name = "BlastSphere";
            piece.transform.position = centre;

            BlastSphere blast = piece.AddComponent<BlastSphere>();
            blast.Begin(radius, settings);
            return blast;
        }

        private void Begin(float radius, Settings sphereSettings)
        {
            settings = sphereSettings;
            size = radius * 2f;
            block = new MaterialPropertyBlock();

            sphere = GetComponent<MeshRenderer>();
            Prepare(sphere);
            sphere.sharedMaterial = settings.material != null ? settings.material : FlatMaterial.Shared;

            if (settings.material == null && sphere.sharedMaterial != null)
            {
                FlatMaterial.Tint(sphere, settings.colour, block);
            }

            canFade = sphere.sharedMaterial != null && sphere.sharedMaterial.HasProperty(FadeId);

            if (settings.outlineWidth > 0f && FlatMaterial.Inverted != null)
            {
                GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(shell.GetComponent<Collider>());
                shell.name = "Outline";
                shell.transform.SetParent(transform, false);

                // As a share of the ball, so the rim keeps its width as the
                // ball grows. Set again every frame.
                outline = shell.GetComponent<MeshRenderer>();
                Prepare(outline);
                outline.sharedMaterial = FlatMaterial.Inverted;
                FlatMaterial.Tint(outline, settings.outlineColour, new MaterialPropertyBlock());
            }

            transform.localScale = Vector3.zero;
        }

        private static void Prepare(MeshRenderer piece)
        {
            piece.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.receiveShadows = false;
        }

        private void Update()
        {
            age += Time.deltaTime;

            float grown = Mathf.Clamp01(age / settings.growTime);
            float faded = Mathf.Clamp01((age - settings.growTime) / settings.fadeTime);

            if (faded >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // Out fast, easing in to full size.
            float ease = 1f - (1f - grown) * (1f - grown);
            float across = size * ease;
            transform.localScale = Vector3.one * across;

            if (outline != null)
            {
                float rim = across > 0.0001f ? 1f + settings.outlineWidth * 2f * ease / across : 1f;
                outline.transform.localScale = Vector3.one * rim;
            }

            if (canFade)
            {
                Fade(sphere, 1f - faded);

                if (outline != null)
                {
                    Fade(outline, 1f - faded);
                }
            }
        }

        private void Fade(Renderer piece, float visibility)
        {
            piece.GetPropertyBlock(block);
            block.SetFloat(FadeId, visibility);
            piece.SetPropertyBlock(block);
        }
    }
}
