using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A charge-up warning: rings on the floor closing in on an enemy, the
    /// last arriving the moment the charge is spent.
    /// </summary>
    /// <remarks>
    /// Follows the enemy across the floor without being parented to it, so
    /// it never spins with a spinning model. Each ring starts wide and thin
    /// and closes in, thickening, so the eye is pulled toward the enemy as
    /// the moment arrives. The rings are staggered so they read as a pulse
    /// rather than one shape shrinking. Nothing fades: they just close in to
    /// nothing, so every pixel stays full colour.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab.
    /// </remarks>
    public sealed class ChargeRing : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Rings that close in over one charge-up, one after another.")]
            [Min(1)] public int rings = 3;

            [Tooltip("Radius each ring starts at.")]
            [Min(0.1f)] public float startRadius = 5f;

            [Tooltip("Radius each ring closes in to, about the enemy's footprint.")]
            [Min(0f)] public float endRadius = 1f;

            [Tooltip("Thickness of a ring as it starts out.")]
            [Min(0.01f)] public float startWidth = 0.1f;

            [Tooltip("Thickness of a ring as it arrives.")]
            [Min(0.01f)] public float endWidth = 0.45f;

            [Tooltip("Height above the floor the rings are drawn at.")]
            [Range(0f, 1f)] public float floorOffset = 0.06f;

            [Tooltip("Colour of the rings. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f);

            [Tooltip("Material for the rings. Empty uses NeoNoir/Flat in the colour above.")]
            public Material material;
        }

        private const int Segments = 48;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Settings settings;
        private Transform follow;
        private float floor;
        private float duration;
        private float age;
        private Mesh mesh;

        /// <summary>
        /// Starts closing in on <paramref name="target"/> at floor height
        /// <paramref name="floorHeight"/>, arriving after
        /// <paramref name="time"/> seconds.
        /// </summary>
        public static ChargeRing Create(Transform target, float floorHeight, float time, Settings settings)
        {
            GameObject host = new GameObject("ChargeRing");
            ChargeRing ring = host.AddComponent<ChargeRing>();
            ring.Begin(target, floorHeight, time, settings);
            return ring;
        }

        private void Begin(Transform target, float floorHeight, float time, Settings ringSettings)
        {
            settings = ringSettings;
            follow = target;
            floor = floorHeight;
            duration = Mathf.Max(0.01f, time);

            mesh = new Mesh { name = "Charge ring" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            Material material = settings.material != null ? settings.material : FlatMaterial.DoubleSided;
            meshRenderer.sharedMaterial = material;

            if (settings.material == null && material != null)
            {
                FlatMaterial.Tint(meshRenderer, settings.colour, new MaterialPropertyBlock());
            }

            Follow();
            Build();
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;

            if (age >= duration || follow == null)
            {
                Destroy(gameObject);
                return;
            }

            Follow();
            Build();
        }

        private void Follow()
        {
            Vector3 position = follow.position;
            position.y = floor + settings.floorOffset;
            transform.SetPositionAndRotation(position, Quaternion.identity);
        }

        /// <summary>
        /// Every ring, each starting a little after the last and all arriving
        /// by the end: the first takes the whole charge-up, the last only the
        /// final stretch.
        /// </summary>
        private void Build()
        {
            vertices.Clear();
            triangles.Clear();

            float t = age / duration;

            for (int r = 0; r < settings.rings; r++)
            {
                float start = settings.rings > 1 ? 0.5f * r / (settings.rings - 1f) : 0f;
                float progress = Mathf.InverseLerp(start, 1f, t);

                if (progress <= 0f)
                {
                    continue;
                }

                // Slow at first, rushing in at the end.
                float eased = progress * progress;
                float outer = Mathf.Lerp(settings.startRadius, settings.endRadius, eased);
                float width = Mathf.Lerp(settings.startWidth, settings.endWidth, eased);
                AddRing(Mathf.Max(0f, outer - width), outer);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddRing(float inner, float outer)
        {
            int first = vertices.Count;

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices.Add(radial * inner);
                vertices.Add(radial * outer);
            }

            for (int i = 0; i < Segments; i++)
            {
                int at = first + i * 2;
                triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
            }
        }

        private void OnDestroy()
        {
            Destroy(mesh);
        }
    }
}
