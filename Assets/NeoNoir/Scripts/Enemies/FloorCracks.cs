using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A spider web of glowing cracks across the floor round a point, the
    /// look of <see cref="FloorWeb"/>'s cuts without cutting anything: the
    /// cracks light up spreading out from the middle, glow a while, then
    /// fade away.
    /// </summary>
    /// <remarks>
    /// The web is laid out as <see cref="FloorWeb"/>'s is:
    /// <see cref="Settings.spokes"/> straight cracks running out from the
    /// middle, unevenly spaced, crossed by rings of straight cracks
    /// <see cref="Settings.ringSpacing"/> apart, each meeting each spoke a
    /// little nearer or further than its even spot. It stops at the radius
    /// it is made with.
    ///
    /// Drawn flat on the floor, one mesh per ring of the web -- the ring and
    /// the stretch of each spoke leading out to it -- so each ring lights as
    /// the spread reaches it. Brightness comes from the colour, above 1 to
    /// bloom; the fading is NeoNoir/Flat's dithered _Fade, since nothing here
    /// is see-through. The floor is expected to be flat. It touches nothing
    /// and hurts no one.
    ///
    /// Spawned at runtime with no prefab of its own. Removes itself once
    /// faded.
    /// </remarks>
    public sealed class FloorCracks : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Web")]
            [Tooltip("Cracks running straight out from the middle.")]
            [Min(3)] public int spokes = 14;

            [Tooltip("How unevenly the spokes are spaced round, as a part of the even gap between them.")]
            [Range(0f, 0.45f)] public float spokeJitter = 0.25f;

            [Tooltip("Distance from the middle to the first ring.")]
            [Min(0.2f)] public float firstRing = 1.5f;

            [Tooltip("Distance between rings.")]
            [Min(0.3f)] public float ringSpacing = 2.2f;

            [Tooltip("How unevenly each ring meets each spoke, as a part of the ring spacing.")]
            [Range(0f, 0.45f)] public float ringJitter = 0.25f;

            [Header("Look")]
            [Tooltip("Colour of the cracks. Above 1 blooms; keep it strongly saturated, or the threshold turns it white.")]
            [ColorUsage(false, true)] public Color colour = new Color(2.5f, 0.12f, 0.08f, 1f);

            [Tooltip("Width of the cracks.")]
            [Min(0.01f)] public float lineWidth = 0.15f;

            [Tooltip("Width of the outline round each crack, either side. 0 for none.")]
            [Min(0f)] public float outlineWidth = 0.05f;

            [ColorUsage(false, true)] public Color outlineColour = new Color(2f, 2f, 2f, 1f);

            [Tooltip("Lift off the floor, so the cracks never flicker into it.")]
            [Range(0f, 0.2f)] public float floorOffset = 0.04f;

            [Header("Timing")]
            [Tooltip("Speed the cracks light up, spreading out from the middle.")]
            [Min(0.1f)] public float spreadSpeed = 30f;

            [Tooltip("Seconds each ring takes to fade in once the spread reaches it.")]
            [Min(0.01f)] public float fadeInTime = 0.15f;

            [Tooltip("Seconds the whole web glows once fully lit, before it starts to fade.")]
            [Min(0f)] public float holdTime = 0.8f;

            [Tooltip("Seconds the web takes to fade away.")]
            [Min(0.01f)] public float fadeOutTime = 1f;
        }

        /// <summary>One ring of the web: its cracks, their outline, and when the spread reaches it.</summary>
        private sealed class Band
        {
            public MeshRenderer lines;
            public MeshRenderer outline;
            public float delay;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Band> bands = new List<Band>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Settings settings;
        private MaterialPropertyBlock block;
        private float age;
        private float lastDelay;

        /// <summary>Spreads a web of cracks <paramref name="radius"/> out round <paramref name="floorPoint"/>, a point on the floor.</summary>
        public static FloorCracks Create(Vector3 floorPoint, float radius, Settings settings)
        {
            GameObject host = new GameObject("FloorCracks");
            host.transform.position = floorPoint;

            FloorCracks cracks = host.AddComponent<FloorCracks>();
            cracks.Begin(radius, settings);
            return cracks;
        }

        private void Begin(float radius, Settings crackSettings)
        {
            settings = crackSettings;
            block = new MaterialPropertyBlock();

            int spokes = settings.spokes;
            float step = Mathf.PI * 2f / spokes;
            float turn = UnityEngine.Random.Range(0f, step);
            Vector2[] directions = new Vector2[spokes];

            for (int i = 0; i < spokes; i++)
            {
                float angle = turn + (i + UnityEngine.Random.Range(-settings.spokeJitter, settings.spokeJitter)) * step;
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            // Every ring that fits; the last is pulled in to the radius.
            int rings = Mathf.Max(1, Mathf.FloorToInt((radius - settings.firstRing) / settings.ringSpacing) + 1);
            Vector2[] previous = new Vector2[spokes];
            float[] previousRadii = new float[spokes];

            for (int k = 1; k <= rings; k++)
            {
                float even = settings.firstRing + (k - 1) * settings.ringSpacing;
                Vector2[] knots = new Vector2[spokes];
                float[] radii = new float[spokes];

                for (int i = 0; i < spokes; i++)
                {
                    float wobble = k == 1 ? 0f : UnityEngine.Random.Range(-settings.ringJitter, settings.ringJitter) * settings.ringSpacing;
                    radii[i] = Mathf.Min(radius, Mathf.Max(previousRadii[i] + 0.2f, even + wobble));
                    knots[i] = directions[i] * radii[i];
                }

                // This ring, and each spoke's stretch out to it.
                List<Vector2> cuts = new List<Vector2>();

                for (int i = 0; i < spokes; i++)
                {
                    cuts.Add(previous[i]);
                    cuts.Add(knots[i]);
                    cuts.Add(knots[i]);
                    cuts.Add(knots[(i + 1) % spokes]);
                }

                AddBand(cuts, (even - settings.ringSpacing * 0.5f) / settings.spreadSpeed);
                previous = knots;
                previousRadii = radii;
            }

            Paint(0f);
        }

        private void AddBand(List<Vector2> cuts, float delay)
        {
            Band band = new Band { delay = Mathf.Max(0f, delay) };
            lastDelay = Mathf.Max(lastDelay, band.delay);

            band.lines = MakeRenderer("Cracks", Lines(cuts, settings.lineWidth, settings.floorOffset));

            // Under the cracks, so they draw over it.
            if (settings.outlineWidth > 0f)
            {
                band.outline = MakeRenderer("Outline", Lines(cuts, settings.lineWidth + settings.outlineWidth * 2f, settings.floorOffset * 0.5f));
            }

            bands.Add(band);
        }

        private MeshRenderer MakeRenderer(string partName, Mesh mesh)
        {
            meshes.Add(mesh);

            GameObject part = new GameObject(partName);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sharedMaterial = FlatMaterial.DoubleSided;
            return meshRenderer;
        }

        private void Update()
        {
            age += Time.deltaTime;

            if (age >= lastDelay + settings.fadeInTime + settings.holdTime + settings.fadeOutTime)
            {
                Destroy(gameObject);
                return;
            }

            Paint(age);
        }

        /// <summary>Shows each ring as far as the spread has lit it, the whole web dimmed as it fades.</summary>
        private void Paint(float clock)
        {
            float fadeStart = lastDelay + settings.fadeInTime + settings.holdTime;
            float fadeOut = 1f - Mathf.Clamp01((clock - fadeStart) / settings.fadeOutTime);

            Color lines = settings.colour;
            Color outline = settings.outlineColour;
            lines.a = 1f;
            outline.a = 1f;

            foreach (Band band in bands)
            {
                float shown = Smooth((clock - band.delay) / settings.fadeInTime) * fadeOut;
                Show(band.lines, lines, shown);

                if (band.outline != null)
                {
                    Show(band.outline, outline, shown);
                }
            }
        }

        private void Show(MeshRenderer target, Color colour, float fade)
        {
            target.enabled = fade > 0f;

            if (fade > 0f)
            {
                block.SetColor(FlatMaterial.BaseColorId, colour);
                block.SetFloat(FadeId, fade);
                target.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// A flat strip <paramref name="width"/> wide along each pair of points
        /// in <paramref name="cuts"/>, <paramref name="lift"/> up, facing up.
        /// </summary>
        private static Mesh Lines(List<Vector2> cuts, float width, float lift)
        {
            List<Vector3> vertices = new List<Vector3>(cuts.Count * 2);
            List<int> triangles = new List<int>(cuts.Count * 3);

            for (int c = 0; c < cuts.Count; c += 2)
            {
                Vector2 a = cuts[c];
                Vector2 b = cuts[c + 1];

                if ((b - a).sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                Vector2 along = (b - a).normalized;

                // Run on past each end by half a width, so corners close.
                a -= along * (width * 0.5f);
                b += along * (width * 0.5f);
                Vector2 side = new Vector2(-along.y, along.x) * (width * 0.5f);

                int first = vertices.Count;
                vertices.Add(new Vector3(a.x - side.x, lift, a.y - side.y));
                vertices.Add(new Vector3(a.x + side.x, lift, a.y + side.y));
                vertices.Add(new Vector3(b.x + side.x, lift, b.y + side.y));
                vertices.Add(new Vector3(b.x - side.x, lift, b.y - side.y));

                // Facing up.
                bool flip = Vector3.Cross(vertices[first + 1] - vertices[first], vertices[first + 2] - vertices[first]).y < 0f;
                triangles.AddRange(flip
                    ? new[] { first, first + 2, first + 1, first, first + 3, first + 2 }
                    : new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            Mesh mesh = new Mesh { name = "Floor cracks" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in meshes)
            {
                Destroy(mesh);
            }
        }
    }
}
