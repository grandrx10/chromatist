using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Burns something flat on the floor -- a carpet, a rug -- away from a
    /// point: a ragged front of embers spreads out across it, charring it
    /// just ahead and leaving nothing behind, and once the front has passed
    /// its farthest corner the object is destroyed.
    /// </summary>
    /// <remarks>
    /// Started with <see cref="Begin"/>, which adds this to the object. Each
    /// of its renderers' materials is swapped for a NeoNoir/Burn copy in the
    /// same colour, its _BaseColor, so it looks the same until the front
    /// reaches it. Its colliders switch off as the burn starts. Everything is
    /// measured flat, so the front spreads across the floor whatever the
    /// object's height.
    ///
    /// NeoNoir/Burn is found by name, so in a build it must be in the
    /// Always Included Shaders, or set <see cref="Settings.material"/> to a
    /// material made from it.
    /// </remarks>
    public sealed class BurnAway : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Seconds after Begin before the front sets out.")]
            [Min(0f)] public float delay = 0.1f;

            [Tooltip("How fast the front spreads, in units per second.")]
            [Min(0.01f)] public float speed = 6f;

            [Tooltip("Width of the glowing band at the front.")]
            [Min(0f)] public float emberWidth = 0.35f;

            [Tooltip("Width of the charred band ahead of the embers.")]
            [Min(0f)] public float charWidth = 0.9f;

            [Tooltip("Colour of the embers. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color emberColour = new Color(4f, 1.3f, 0.25f, 1f);

            [Tooltip("Colour of the char.")]
            public Color charColour = Color.black;

            [Tooltip("Size of the ragged front's wiggles: higher is finer.")]
            [Min(0.01f)] public float noiseScale = 0.6f;

            [Tooltip("How far the front strays either side of a clean circle.")]
            [Min(0f)] public float noiseAmount = 0.8f;

            [Tooltip("A material made from NeoNoir/Burn, copied for each part. Empty finds the shader by name.")]
            public Material material;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmberColorId = Shader.PropertyToID("_EmberColor");
        private static readonly int CharColorId = Shader.PropertyToID("_CharColor");
        private static readonly int EmberWidthId = Shader.PropertyToID("_EmberWidth");
        private static readonly int CharWidthId = Shader.PropertyToID("_CharWidth");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseAmountId = Shader.PropertyToID("_NoiseAmount");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int BurnCentreId = Shader.PropertyToID("_BurnCentre");
        private static readonly int BurnRadiusId = Shader.PropertyToID("_BurnRadius");

        private readonly List<Material> burning = new List<Material>();

        private Settings settings;
        private Vector3 origin;
        private float reach;
        private float age;

        /// <summary>Burns <paramref name="target"/> away, spreading out from <paramref name="from"/>.</summary>
        public static BurnAway Begin(GameObject target, Vector3 from, Settings settings)
        {
            if (target == null)
            {
                return null;
            }

            BurnAway burn = target.AddComponent<BurnAway>();
            burn.Ignite(from, settings);
            return burn;
        }

        private void Ignite(Vector3 from, Settings burnSettings)
        {
            settings = burnSettings;
            origin = from;

            Material template = settings.material;

            if (template == null)
            {
                Shader shader = Shader.Find("NeoNoir/Burn");

                if (shader == null)
                {
                    Debug.LogWarning("[BurnAway] NeoNoir/Burn shader not found, so it simply vanishes. Assign a burn "
                                     + "material, or add the shader to Always Included Shaders.", this);
                    Destroy(gameObject);
                    return;
                }

                template = new Material(shader);
            }

            foreach (Collider solid in GetComponentsInChildren<Collider>())
            {
                solid.enabled = false;
            }

            reach = 0f;

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                Material[] originals = part.sharedMaterials;
                Material[] swapped = new Material[originals.Length];

                for (int i = 0; i < originals.Length; i++)
                {
                    Material copy = new Material(template) { name = "Burning " + (originals[i] != null ? originals[i].name : "Part") };

                    if (originals[i] != null && originals[i].HasProperty(BaseColorId))
                    {
                        copy.SetColor(BaseColorId, originals[i].GetColor(BaseColorId));
                    }

                    if (originals[i] != null && originals[i].HasProperty(CullId))
                    {
                        copy.SetFloat(CullId, originals[i].GetFloat(CullId));
                    }

                    copy.SetColor(EmberColorId, settings.emberColour);
                    copy.SetColor(CharColorId, settings.charColour);
                    copy.SetFloat(EmberWidthId, settings.emberWidth);
                    copy.SetFloat(CharWidthId, settings.charWidth);
                    copy.SetFloat(NoiseScaleId, settings.noiseScale);
                    copy.SetFloat(NoiseAmountId, settings.noiseAmount);
                    copy.SetVector(BurnCentreId, origin);
                    copy.SetFloat(BurnRadiusId, -settings.noiseAmount - 0.01f);

                    swapped[i] = copy;
                    burning.Add(copy);
                }

                part.sharedMaterials = swapped;
                reach = Mathf.Max(reach, FarthestCorner(part.bounds, origin));
            }

            if (settings.material == null)
            {
                Destroy(template);
            }

            // Done once the front, embers, char and wiggles have all passed the far corner.
            reach += settings.noiseAmount + settings.emberWidth + settings.charWidth;
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            age += Time.deltaTime;
            float radius = (age - settings.delay) * settings.speed - settings.noiseAmount - 0.01f;

            foreach (Material material in burning)
            {
                material.SetFloat(BurnRadiusId, radius);
            }

            if (radius >= reach)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in burning)
            {
                Destroy(material);
            }

            burning.Clear();
        }

        /// <summary>How far, measured flat, the corner of <paramref name="bounds"/> farthest from <paramref name="from"/> is.</summary>
        private static float FarthestCorner(Bounds bounds, Vector3 from)
        {
            float x = Mathf.Max(Mathf.Abs(bounds.min.x - from.x), Mathf.Abs(bounds.max.x - from.x));
            float z = Mathf.Max(Mathf.Abs(bounds.min.z - from.z), Mathf.Abs(bounds.max.z - from.z));
            return Mathf.Sqrt(x * x + z * z);
        }
    }
}
