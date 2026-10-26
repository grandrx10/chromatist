using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A warning circle on the floor: the outline at full size, and a disc
    /// inside it that fills out from the middle as <see cref="Fill"/> goes
    /// from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Builds its own mesh, so it needs no prefab. Coloured through a
    /// property block, never by editing the material. Whoever creates it
    /// destroys it; <see cref="DestroyWhen"/> covers an owner that might stop
    /// before getting the chance, such as an attack cut short by a death.
    /// </remarks>
    public sealed class WarningCircle : MonoBehaviour
    {
        private const int Segments = 48;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private float radius;
        private float outlineWidth;
        private float fill = -1f;
        private Func<bool> expired;

        private Mesh mesh;
        private Material ownMaterial;

        /// <summary>How far the disc has filled the circle, 0 to 1.</summary>
        public float Fill
        {
            get => fill;
            set
            {
                value = Mathf.Clamp01(value);

                if (value != fill)
                {
                    fill = value;
                    Draw();
                }
            }
        }

        /// <summary>
        /// Draws an empty circle at <paramref name="position"/>, flat on the
        /// floor. A null <paramref name="material"/> makes a NeoNoir/Flat one.
        /// </summary>
        public static WarningCircle Create(Vector3 position, float radius, float outlineWidth,
                                           Material material, Color colour, Transform parent = null)
        {
            GameObject host = new GameObject("WarningCircle");
            host.transform.SetParent(parent, false);
            host.transform.position = position;

            WarningCircle circle = host.AddComponent<WarningCircle>();
            circle.Begin(radius, outlineWidth, material, colour);
            return circle;
        }

        /// <summary>Destroys the circle by itself the first frame <paramref name="condition"/> is true.</summary>
        public void DestroyWhen(Func<bool> condition)
        {
            expired = condition;
        }

        private void Begin(float circleRadius, float circleOutline, Material material, Color colour)
        {
            radius = circleRadius;
            outlineWidth = circleOutline;

            mesh = new Mesh { name = "Warning circle" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (material == null)
            {
                Shader flat = Shader.Find("NeoNoir/Flat");

                if (flat != null)
                {
                    ownMaterial = new Material(flat);
                    material = ownMaterial;
                }
            }

            renderer.sharedMaterial = material;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            renderer.SetPropertyBlock(block);

            Fill = 0f;
        }

        private void Update()
        {
            if (expired != null && expired())
            {
                Destroy(gameObject);
            }
        }

        private void Draw()
        {
            vertices.Clear();
            triangles.Clear();

            AddRing(Mathf.Max(0f, radius - outlineWidth), radius);

            // Inside the outline, so the two never overlap and flicker.
            float disc = Mathf.Max(0f, radius - outlineWidth) * fill;

            if (disc > 0.0001f)
            {
                AddRing(0f, disc);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddRing(float inner, float outer)
        {
            if (outer - inner <= 0f)
            {
                return;
            }

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

                // Wound both ways, so it shows from either side.
                triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
                triangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
            }
        }

        private void OnDestroy()
        {
            Destroy(mesh);

            if (ownMaterial != null)
            {
                Destroy(ownMaterial);
            }
        }
    }
}
