using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A glowing rope binding a weapon to Luna: from the middle of one to the
    /// middle of the other, sagging a little, with glints running along it
    /// into her.
    /// </summary>
    /// <remarks>
    /// Drawn with the NeoNoir/String shader, its fade pushed to the very end
    /// so the rope glows evenly along its length. Each end is the middle of
    /// everything drawn under that end's transform, worked out every frame,
    /// so an end can be handed from one object to another -- a captive blade
    /// to the boss that replaces it -- with <see cref="To"/>.
    ///
    /// <see cref="Reach"/> grows it out from Luna, for when it first binds.
    /// <see cref="Tautness"/> straightens it and <see cref="Strain"/> makes it
    /// shudder, for a weapon pulling against it. <see cref="Snap"/> breaks
    /// it at the weapon's end: a burst there, and the loose rope whips back
    /// into Luna, fading, and is gone.
    ///
    /// Its own object, not a child of either end, so it survives either
    /// being swapped out.
    /// </remarks>
    public sealed class WeaponChain : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Thickness of the rope.")]
            [Min(0.001f)] public float width = 0.07f;

            [Tooltip("Brightness the rope's colour is multiplied by. Above 1 blooms.")]
            [Min(0f)] public float intensity = 2.5f;

            [Tooltip("Brightness of the glints running along it.")]
            [Min(0f)] public float glintIntensity = 6f;

            [Tooltip("How far it sags at its middle when slack, as a fraction of its length.")]
            [Range(0f, 0.5f)] public float sag = 0.12f;

            [Tooltip("Points along it. More makes a smoother sag.")]
            [Range(2, 64)] public int points = 20;

            [Tooltip("Seconds the broken rope takes to whip back into Luna and fade.")]
            [Min(0.01f)] public float snapTime = 0.35f;

            [Tooltip("Material for the rope. Empty uses NeoNoir/String.")]
            public Material material;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlintColorId = Shader.PropertyToID("_GlintColor");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int LengthId = Shader.PropertyToID("_Length");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        private static Material sharedMaterial;

        private Settings settings;
        private LineRenderer line;
        private MaterialPropertyBlock block;
        private Vector3[] positions;

        private Transform to;
        private Renderer[] fromRenderers;
        private Renderer[] toRenderers;
        private Vector3 lastTo;

        private bool snapped;
        private float snapAge;
        private float snapReach;
        private float seed;

        /// <summary>Luna's end.</summary>
        public Transform From { get; private set; }

        /// <summary>The weapon's end. Settable, to hand the end on to whatever replaces the weapon.</summary>
        public Transform To
        {
            get => to;
            set
            {
                to = value;
                toRenderers = value != null ? value.GetComponentsInChildren<Renderer>() : null;
            }
        }

        /// <summary>How far the rope reaches from Luna toward the weapon, 0 to 1.</summary>
        public float Reach { get; set; } = 1f;

        /// <summary>0 hangs with its full sag, 1 pulls it straight.</summary>
        public float Tautness { get; set; }

        /// <summary>How far, in units, it shudders sideways, for a weapon straining against it.</summary>
        public float Strain { get; set; }

        /// <summary>True once <see cref="Snap"/> has been called.</summary>
        public bool Snapped => snapped;

        /// <summary>Binds <paramref name="weapon"/> to <paramref name="luna"/> with a rope in <paramref name="colour"/>.</summary>
        public static WeaponChain Create(Transform luna, Transform weapon, Color colour, Settings settings)
        {
            WeaponChain chain = new GameObject("Weapon Chain").AddComponent<WeaponChain>();
            chain.Build(luna, weapon, colour, settings);
            return chain;
        }

        private void Build(Transform luna, Transform weapon, Color colour, Settings chainSettings)
        {
            settings = chainSettings;
            From = luna;
            fromRenderers = luna.GetComponentsInChildren<Renderer>();
            To = weapon;
            seed = UnityEngine.Random.value * 100f;

            positions = new Vector3[Mathf.Max(2, settings.points)];
            block = new MaterialPropertyBlock();

            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = positions.Length;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = settings.width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = settings.material != null ? settings.material : SharedMaterial();

            Color glow = colour * settings.intensity;
            glow.a = 1f;
            Color glint = colour * settings.glintIntensity;
            glint.a = 1f;

            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, glow);
            block.SetColor(GlintColorId, glint);
            block.SetFloat(FadeStartId, 0.999f);
            block.SetFloat(AlphaId, 1f);
            line.SetPropertyBlock(block);

            lastTo = weapon != null ? weapon.position : luna.position;
            Draw();
        }

        /// <summary>
        /// Breaks the rope at the weapon's end: a burst there in its colour,
        /// and the loose rope whips back into Luna and is gone.
        /// </summary>
        public void Snap(EnergyBurst.Settings burst)
        {
            if (snapped)
            {
                return;
            }

            snapped = true;
            snapAge = 0f;
            snapReach = Reach;

            if (burst != null)
            {
                EnergyBurst.Create(EndPoint(), burst);
            }
        }

        private void LateUpdate()
        {
            if (From == null)
            {
                Destroy(gameObject);
                return;
            }

            if (snapped)
            {
                snapAge += Time.deltaTime;
                float t = Mathf.Clamp01(snapAge / settings.snapTime);

                // Fast at first, as a released rope recoils.
                Reach = snapReach * (1f - t) * (1f - t);
                Tautness = 0f;
                Strain = 0f;

                line.GetPropertyBlock(block);
                block.SetFloat(AlphaId, 1f - t);
                line.SetPropertyBlock(block);

                if (t >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            Draw();
        }

        private void Draw()
        {
            Vector3 luna = Middle(From, fromRenderers);
            Vector3 weapon = EndPoint();
            Vector3 end = Vector3.Lerp(luna, weapon, Mathf.Clamp01(Reach));

            Vector3 span = end - luna;
            float length = span.magnitude;
            float sag = settings.sag * length * (1f - Mathf.Clamp01(Tautness));

            // Shudder across the rope, square to it and to the view down.
            Vector3 across = Vector3.Cross(span, Vector3.up);
            across = across.sqrMagnitude > 0.0001f ? across.normalized : Vector3.right;

            int count = positions.Length;

            // Drawn from the weapon to Luna, so the glints run into her.
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float bow = 4f * t * (1f - t);
                Vector3 point = Vector3.Lerp(end, luna, t) + Vector3.down * (sag * bow);

                if (Strain > 0f)
                {
                    point += across * (Strain * bow * (Mathf.PerlinNoise(seed + i * 0.7f, Time.time * 18f) * 2f - 1f));
                }

                positions[i] = point;
            }

            line.SetPositions(positions);

            line.GetPropertyBlock(block);
            block.SetFloat(LengthId, Mathf.Max(0.01f, length));
            line.SetPropertyBlock(block);
        }

        /// <summary>The middle of the weapon's end, or where it last was if it is gone.</summary>
        private Vector3 EndPoint()
        {
            if (to != null)
            {
                lastTo = Middle(to, toRenderers);
            }

            return lastTo;
        }

        /// <summary>The middle of everything drawn under <paramref name="end"/>, or its pivot if nothing is.</summary>
        private static Vector3 Middle(Transform end, Renderer[] renderers)
        {
            bool any = false;
            Bounds bounds = default;

            if (renderers != null)
            {
                foreach (Renderer part in renderers)
                {
                    if (part == null || !part.enabled || !part.gameObject.activeInHierarchy || part is LineRenderer || part is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    if (any)
                    {
                        bounds.Encapsulate(part.bounds);
                    }
                    else
                    {
                        bounds = part.bounds;
                        any = true;
                    }
                }
            }

            return any ? bounds.center : end.position;
        }

        private static Material SharedMaterial()
        {
            if (sharedMaterial == null)
            {
                Shader shader = Shader.Find("NeoNoir/String");

                if (shader == null)
                {
                    Debug.LogWarning("[WeaponChain] NeoNoir/String shader not found. Assign a rope material, "
                                     + "or add the shader to Always Included Shaders.");
                    return null;
                }

                sharedMaterial = new Material(shader) { name = "Runtime Weapon Chain" };
            }

            return sharedMaterial;
        }
    }
}
