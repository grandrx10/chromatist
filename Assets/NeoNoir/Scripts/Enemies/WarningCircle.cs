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
    /// Builds its own meshes, so it needs no prefab. Coloured through a
    /// property block, never by editing the material. Whoever creates it
    /// destroys it; <see cref="DestroyWhen"/> covers an owner that might stop
    /// before getting the chance, such as an attack cut short by a death.
    ///
    /// Cheap enough for hundreds at once: the outline mesh is built once per
    /// size and shared by every circle that size, and the disc is one unit
    /// disc shared by all, scaled as it fills, so nothing is rebuilt from
    /// frame to frame. Circles given no material share one, too. A circle
    /// handed back with <see cref="Release"/> rather than destroyed is reused
    /// by the next <see cref="Create"/>, for attacks that lay many a second.
    /// </remarks>
    public sealed class WarningCircle : MonoBehaviour
    {
        private const int Segments = 48;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Shared by every circle: outlines by size, the unit disc, and the
        // material for circles given none.
        private static readonly Dictionary<Vector2Int, Mesh> outlines = new Dictionary<Vector2Int, Mesh>();
        private static Mesh unitDisc;
        private static Material defaultMaterial;

        // Released circles waiting to be reused.
        private static readonly Stack<WarningCircle> spare = new Stack<WarningCircle>();
        private static MaterialPropertyBlock block;

        private float radius;
        private float outlineWidth;
        private float fill = -1f;
        private Func<bool> expired;

        private Transform disc;
        private MeshRenderer discRenderer;
        private MeshFilter outlineFilter;
        private MeshRenderer outlineRenderer;

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
        /// floor. A null <paramref name="material"/> uses a shared NeoNoir/Flat one.
        /// </summary>
        public static WarningCircle Create(Vector3 position, float radius, float outlineWidth,
                                           Material material, Color colour, Transform parent = null)
        {
            WarningCircle circle = null;

            // Released circles in a scene that has since unloaded went with it.
            while (circle == null && spare.Count > 0)
            {
                circle = spare.Pop();
            }

            if (circle == null)
            {
                circle = new GameObject("WarningCircle").AddComponent<WarningCircle>();
            }

            circle.transform.SetParent(parent, false);
            circle.transform.position = position;
            circle.gameObject.SetActive(true);
            circle.Begin(radius, outlineWidth, material, colour);
            return circle;
        }

        /// <summary>Done with: hidden and kept for the next <see cref="Create"/>, rather than destroyed.</summary>
        public void Release()
        {
            expired = null;
            gameObject.SetActive(false);
            transform.SetParent(null, false);
            spare.Push(this);
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

            if (material == null)
            {
                material = DefaultMaterial();
            }

            // Built once; a reused circle only takes its new size and colour.
            if (outlineRenderer == null)
            {
                outlineRenderer = Paint(gameObject, out outlineFilter);

                GameObject discHost = new GameObject("Disc");
                disc = discHost.transform;
                disc.SetParent(transform, false);
                discRenderer = Paint(discHost, out MeshFilter discFilter);
                discFilter.sharedMesh = UnitDisc();
            }

            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, colour);

            outlineFilter.sharedMesh = Outline(radius, outlineWidth);
            outlineRenderer.sharedMaterial = material;
            outlineRenderer.SetPropertyBlock(block);
            discRenderer.sharedMaterial = material;
            discRenderer.SetPropertyBlock(block);

            fill = -1f;
            Fill = 0f;
        }

        private static MeshRenderer Paint(GameObject host, out MeshFilter filter)
        {
            filter = host.AddComponent<MeshFilter>();

            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
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
            // Inside the outline, so the two never overlap and flicker.
            float size = Mathf.Max(0f, radius - outlineWidth) * fill;
            bool shown = size > 0.0001f;

            discRenderer.enabled = shown;

            if (shown)
            {
                disc.localScale = new Vector3(size, 1f, size);
            }
        }

        // ---- shared meshes --------------------------------------------------

        private static Material DefaultMaterial()
        {
            if (defaultMaterial == null)
            {
                Shader flat = Shader.Find("NeoNoir/Flat");

                if (flat != null)
                {
                    defaultMaterial = new Material(flat) { name = "Runtime Warning Circle" };
                }
            }

            return defaultMaterial;
        }

        /// <summary>The outline for a circle this size, built the first time a circle that size is wanted.</summary>
        private static Mesh Outline(float circleRadius, float circleOutline)
        {
            // To the millimetre: near enough the same size is the same mesh.
            Vector2Int key = new Vector2Int(Mathf.RoundToInt(circleRadius * 1000f), Mathf.RoundToInt(circleOutline * 1000f));

            if (!outlines.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = Ring(Mathf.Max(0f, circleRadius - circleOutline), circleRadius, "Warning circle outline");
                outlines[key] = mesh;
            }

            return mesh;
        }

        private static Mesh UnitDisc()
        {
            if (unitDisc == null)
            {
                unitDisc = Ring(0f, 1f, "Warning circle disc");
            }

            return unitDisc;
        }

        private static Mesh Ring(float inner, float outer, string meshName)
        {
            List<Vector3> vertices = new List<Vector3>((Segments + 1) * 2);
            List<int> triangles = new List<int>(Segments * 12);

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices.Add(radial * inner);
                vertices.Add(radial * outer);
            }

            for (int i = 0; i < Segments; i++)
            {
                int at = i * 2;

                // Wound both ways, so it shows from either side.
                triangles.Add(at); triangles.Add(at + 2); triangles.Add(at + 1);
                triangles.Add(at + 1); triangles.Add(at + 2); triangles.Add(at + 3);
                triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
                triangles.Add(at + 1); triangles.Add(at + 3); triangles.Add(at + 2);
            }

            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
