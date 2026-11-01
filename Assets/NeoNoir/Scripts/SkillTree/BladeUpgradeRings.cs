using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// Rings on the Afterworld floor round a blade's stone, one for each
    /// upgrade it has: a white outline filled with the blade's colour.
    /// </summary>
    /// <remarks>
    /// Made by <see cref="PurchasableBlade"/>. The count comes from the
    /// blade's saved level in <see cref="BladeUnlocks"/>, so rings bought on
    /// an earlier visit are there when the scene loads. A new upgrade forms
    /// its ring by spreading out from the stone's edge to its place.
    ///
    /// The first ring sits just outside the stone; each one after sits
    /// further out.
    /// </remarks>
    public sealed class BladeUpgradeRings : MonoBehaviour
    {
        private const int Segments = 96;

        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Gap in metres between the edge of the stone and the first ring.")]
            [Min(0f)] public float gap = 0.3f;

            [Tooltip("Width of each ring, outline included, in metres.")]
            [Min(0.01f)] public float width = 0.22f;

            [Tooltip("Width of the white outline on each edge of a ring, in metres.")]
            [Min(0f)] public float outline = 0.04f;

            [Tooltip("Gap in metres between one ring and the next.")]
            [Min(0f)] public float spacing = 0.15f;

            [Tooltip("Colour of the outline. HDR: above 1 blooms.")]
            [ColorUsage(false, true)] public Color outlineColour = new Color(2.5f, 2.5f, 2.5f, 1f);

            [Tooltip("How bright the fill is: the blade's own colour times this. Above 1 blooms.")]
            [Min(0f)] public float fillBrightness = 1f;

            [Tooltip("Height above the floor lines the rings are drawn at, so they lie on top of them.")]
            [Min(0f)] public float lift = 0.01f;

            [Tooltip("Seconds after upgrading before the new ring starts to form.")]
            [Min(0f)] public float formDelay = 0.2f;

            [Tooltip("Seconds a new ring takes to spread out from the stone into place.")]
            [Min(0.01f)] public float formTime = 0.7f;
        }

        private readonly List<Transform> rings = new List<Transform>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Settings settings;
        private Color fill;
        private float stoneRadius;
        private MaterialPropertyBlock block;

        /// <summary>Makes the rings for <paramref name="blade"/>, with none showing yet.</summary>
        public static BladeUpgradeRings Create(PurchasableBlade blade, Settings settings, Color bladeColour)
        {
            Vector3 centre = blade.StoneTop;
            // Not parented to the blade, so its scale can't squash the rings.
            GameObject host = new GameObject($"UpgradeRings ({blade.name})");
            host.transform.position = new Vector3(centre.x, SkillTreeLines.Instance.FloorAt(blade) + settings.lift, centre.z);
            host.transform.rotation = Quaternion.identity;

            BladeUpgradeRings rings = host.AddComponent<BladeUpgradeRings>();
            rings.settings = settings;
            rings.fill = bladeColour * settings.fillBrightness;
            rings.fill.a = 1f;
            rings.stoneRadius = blade.StoneRadius;
            rings.block = new MaterialPropertyBlock();
            return rings;
        }

        /// <summary>How many rings there are.</summary>
        public int Count => rings.Count;

        /// <summary>Shows <paramref name="count"/> rings straight away, already in place.</summary>
        public void Show(int count)
        {
            while (rings.Count < count)
            {
                AddRing();
            }
        }

        /// <summary>Adds one more ring, spreading out from the stone into place.</summary>
        public void Form()
        {
            Transform ring = AddRing();
            StartCoroutine(Spread(ring, OuterRadius(rings.Count - 1)));
        }

        private float InnerRadius(int index)
        {
            return stoneRadius + settings.gap + index * (settings.width + settings.spacing);
        }

        private float OuterRadius(int index)
        {
            return InnerRadius(index) + settings.width;
        }

        private Transform AddRing()
        {
            int index = rings.Count;
            float inner = InnerRadius(index);
            float outer = OuterRadius(index);
            float edge = Mathf.Min(settings.outline, settings.width * 0.5f);

            GameObject ring = new GameObject($"Ring {index + 1}");
            ring.transform.SetParent(transform, false);

            // The white band underneath shows only at the edges, round the fill on top.
            MakeBand(ring.transform, "Outline", inner, outer, 0f, settings.outlineColour);

            if (outer - inner - edge * 2f > 0.0005f)
            {
                MakeBand(ring.transform, "Fill", inner + edge, outer - edge, 0.004f, fill);
            }

            rings.Add(ring.transform);
            return ring.transform;
        }

        private void MakeBand(Transform parent, string bandName, float inner, float outer, float height, Color colour)
        {
            List<Vector3> vertices = new List<Vector3>((Segments + 1) * 2);
            List<int> triangles = new List<int>(Segments * 6);

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(direction * outer);
                vertices.Add(direction * inner);
            }

            for (int k = 0; k + 3 < vertices.Count; k += 2)
            {
                triangles.Add(k); triangles.Add(k + 2); triangles.Add(k + 1);
                triangles.Add(k + 1); triangles.Add(k + 2); triangles.Add(k + 3);
            }

            Mesh mesh = new Mesh { name = $"Upgrade Ring {bandName}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            GameObject band = new GameObject(bandName);
            band.transform.SetParent(parent, false);
            band.transform.localPosition = Vector3.up * height;
            band.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = band.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.DoubleSided;
            FlatMaterial.Tint(renderer, colour, block);
        }

        private IEnumerator Spread(Transform ring, float radius)
        {
            float from = radius > 0.0001f ? Mathf.Clamp01(stoneRadius / radius) : 1f;
            ring.localScale = new Vector3(from, 1f, from);
            ring.gameObject.SetActive(false);

            if (settings.formDelay > 0f)
            {
                yield return new WaitForSeconds(settings.formDelay);
            }

            ring.gameObject.SetActive(true);

            for (float clock = 0f; clock < settings.formTime; clock += Time.deltaTime)
            {
                float t = Mathf.Clamp01(clock / settings.formTime);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                float scale = Mathf.Lerp(from, 1f, eased);
                ring.localScale = new Vector3(scale, 1f, scale);
                yield return null;
            }

            ring.localScale = Vector3.one;
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
