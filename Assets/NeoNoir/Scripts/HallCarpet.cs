using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A rectangular carpet with the banner's insignia laid along it: a line
    /// down the middle with the cross-and-triangle glyphs on it, facing in
    /// turn, and a scrolled border along each long edge, broken under each
    /// glyph with its ends rolled in. Built in code; drop it on an empty
    /// object.
    /// </summary>
    /// <remarks>
    /// Stretch it with the Scale tool: at scale 1 it is a metre square, and
    /// its length runs along X and width along Z. The pattern is measured in
    /// metres and undoes the scale, so stretching only spreads the glyphs out
    /// and lengthens the lines, never the glyphs or scrolls themselves.
    ///
    /// The pivot is the middle of the carpet's underside. Colours are set on
    /// materials of its own, as <see cref="HallBanner"/> does.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HallCarpet : MonoBehaviour
    {
        // The glyph's proportions, from the banner's: where the cross sits
        // between tip and bar, and the bar's half-height, as fractions of
        // the glyph's length.
        private const float CrossAlong = 0.47f;
        private const float BarHalf = 0.376f;

        // The scrolls: how far round each rolled end turns, how tight it
        // ends, and how far a spiral's centre sits from the break it faces.
        private const float ScrollTurns = 1.15f;
        private const float ScrollInnerRadius = 0.25f;
        private const float ScrollGap = 1.75f;
        private const int ScrollPointsPerTurn = 32;

        [Header("Shape")]
        [SerializeField, Min(0.001f)] private float thickness = 0.02f;

        [Header("Pattern")]
        [SerializeField, Min(0)]
        [Tooltip("Glyphs along the carpet, spaced evenly.")]
        private int glyphCount = 3;

        [SerializeField, Min(0.05f)]
        [Tooltip("Length of each glyph from tip to bar, in metres.")]
        private float glyphSize = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Radius of the scrolls on the border, in metres.")]
        private float scrollSize = 0.1f;

        [SerializeField, Min(0f)]
        [Tooltip("How far the border lines sit in from the long edges, in metres.")]
        private float borderInset = 0.15f;

        [SerializeField, Min(0.005f)]
        [Tooltip("Thickness of the pattern's lines, in metres.")]
        private float lineWidth = 0.04f;

        [Header("Colour")]
        [SerializeField, ColorUsage(false, true)] private Color clothColour = new Color(0.55f, 0.03f, 0.04f);

        [SerializeField, ColorUsage(false, true)]
        [Tooltip("The pattern.")]
        private Color trimColour = Color.black;

        [SerializeField]
        [Tooltip("Copied, not edited. NeoNoir/Flat if empty.")]
        private Material clothMaterial;

        [SerializeField]
        [Tooltip("Copied, not edited. NeoNoir/Flat if empty.")]
        private Material trimMaterial;

        // Built in metres, then divided by the scale when set on the mesh.
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> clothTriangles = new List<int>();
        private readonly List<int> trimTriangles = new List<int>();

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Material ownCloth;
        private Material ownTrim;

        private Vector3 builtScale;
        private bool dirty = true;

        private void OnEnable()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            dirty = true;
        }

        private void OnValidate()
        {
            dirty = true;
        }

        private void OnDisable()
        {
            Release(ref mesh);
            Release(ref ownCloth);
            Release(ref ownTrim);
        }

        private void Update()
        {
            if (dirty || transform.lossyScale != builtScale)
            {
                dirty = false;
                Rebuild();
            }
        }

        private void Rebuild()
        {
            Vector3 scale = transform.lossyScale;
            builtScale = scale;

            if (Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f || Mathf.Abs(scale.z) < 0.0001f)
            {
                return;
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "Hall Carpet", hideFlags = HideFlags.DontSave };
            }

            float length = Mathf.Abs(scale.x);
            float width = Mathf.Abs(scale.z);

            vertices.Clear();
            clothTriangles.Clear();
            trimTriangles.Clear();

            AddBox(new Vector3(0f, thickness * 0.5f, 0f), new Vector3(length, thickness, width));
            BuildPattern(length, width);

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i];
                vertices[i] = new Vector3(v.x / scale.x, v.y / scale.y, v.z / scale.z);
            }

            mesh.Clear();
            mesh.subMeshCount = 2;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(clothTriangles, 0);
            mesh.SetTriangles(trimTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;

            ownCloth = Recolour(ownCloth, clothMaterial, clothColour, "Carpet Cloth");
            ownTrim = Recolour(ownTrim, trimMaterial, trimColour, "Carpet Trim");
            meshRenderer.sharedMaterials = new[] { ownCloth, ownTrim };
        }

        /// <summary>
        /// The pattern in (along, across) metres from the centre of the top.
        /// </summary>
        private void BuildPattern(float length, float width)
        {
            float halfLength = length * 0.5f;
            float[] glyphAt = new float[glyphCount];

            for (int i = 0; i < glyphCount; i++)
            {
                glyphAt[i] = ((i + 0.5f) / glyphCount - 0.5f) * length;
            }

            AddStroke(new List<Vector2> { new Vector2(-halfLength, 0f), new Vector2(halfLength, 0f) });

            for (int i = 0; i < glyphCount; i++)
            {
                // Each faces the one before, as the banner's pair face each other.
                AddGlyph(glyphAt[i], i % 2 == 0 ? 1f : -1f);
            }

            float borderAcross = width * 0.5f - borderInset;
            AddBorder(glyphAt, halfLength, borderAcross, -1f);
            AddBorder(glyphAt, halfLength, -borderAcross, 1f);
        }

        /// <summary>
        /// One line, cross to corner to corner and back through the cross,
        /// tip first on the side <paramref name="facing"/> points away from.
        /// </summary>
        private void AddGlyph(float centre, float facing)
        {
            float tip = centre - facing * glyphSize * 0.5f;
            float cross = tip + facing * glyphSize * CrossAlong;
            float bar = centre + facing * glyphSize * 0.5f;
            float barHalf = glyphSize * BarHalf;

            // The arms carry on past the cross along the lines from the bar's corners.
            float tipHalf = barHalf * (tip - cross) / (bar - cross);

            AddStroke(new List<Vector2>
            {
                new Vector2(tip, tipHalf),
                new Vector2(cross, 0f),
                new Vector2(bar, barHalf),
                new Vector2(bar, -barHalf),
                new Vector2(cross, 0f),
                new Vector2(tip, -tipHalf),
            });
        }

        /// <summary>
        /// A line along the carpet at <paramref name="across"/>, broken under
        /// each glyph, both ends at each break rolled toward
        /// <paramref name="inward"/>.
        /// </summary>
        private void AddBorder(float[] glyphAt, float halfLength, float across, float inward)
        {
            float gap = scrollSize * ScrollGap;

            for (int k = 0; k <= glyphAt.Length; k++)
            {
                bool rolledStart = k > 0;
                bool rolledEnd = k < glyphAt.Length;
                float start = rolledStart ? glyphAt[k - 1] + gap : -halfLength;
                float end = rolledEnd ? glyphAt[k] - gap : halfLength;

                if (end < start)
                {
                    continue;
                }

                List<Vector2> path = new List<Vector2>();

                if (rolledStart)
                {
                    List<Vector2> scroll = Scroll(new Vector2(start, across), -1f, inward);
                    scroll.Reverse();
                    path.AddRange(scroll);
                }
                else
                {
                    path.Add(new Vector2(start, across));
                }

                if (rolledEnd)
                {
                    path.AddRange(Scroll(new Vector2(end, across), 1f, inward));
                }
                else
                {
                    path.Add(new Vector2(end, across));
                }

                AddStroke(path);
            }
        }

        /// <summary>
        /// A spiral from <paramref name="end"/>, carrying on along
        /// <paramref name="along"/> then curling round toward
        /// <paramref name="inward"/> and back, tightening as it goes.
        /// </summary>
        private List<Vector2> Scroll(Vector2 end, float along, float inward)
        {
            List<Vector2> points = new List<Vector2>();
            Vector2 centre = end + new Vector2(0f, inward * scrollSize);
            int count = Mathf.CeilToInt(ScrollTurns * ScrollPointsPerTurn);
            float total = ScrollTurns * Mathf.PI * 2f;

            for (int i = 0; i <= count; i++)
            {
                float t = (float)i / count;
                float angle = t * total;
                float radius = scrollSize * Mathf.Lerp(1f, ScrollInnerRadius, t);
                points.Add(centre + radius * new Vector2(along * Mathf.Sin(angle), -inward * Mathf.Cos(angle)));
            }

            return points;
        }

        /// <summary>
        /// A mitred line through <paramref name="points"/> (in along, across),
        /// laid just above the top of the carpet.
        /// </summary>
        private void AddStroke(List<Vector2> points)
        {
            float halfWidth = lineWidth * 0.5f;
            float lift = thickness + 0.003f;
            int first = vertices.Count;

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 before = i > 0 ? (points[i] - points[i - 1]).normalized : Vector2.zero;
                Vector2 after = i < points.Count - 1 ? (points[i + 1] - points[i]).normalized : Vector2.zero;
                Vector2 direction = (before + after).normalized;

                if (direction == Vector2.zero)
                {
                    // Doubles straight back on itself; any side will do.
                    direction = before;
                }

                Vector2 side = new Vector2(-direction.y, direction.x);

                // Widen at corners so the line keeps its thickness round them.
                float miter = 1f;
                Vector2 reference = after != Vector2.zero ? after : before;
                float dot = Vector2.Dot(side, new Vector2(-reference.y, reference.x));

                if (dot > 0.01f)
                {
                    miter = Mathf.Min(1f / dot, 3f);
                }

                Vector2 left = points[i] + side * (halfWidth * miter);
                Vector2 right = points[i] - side * (halfWidth * miter);
                vertices.Add(new Vector3(left.x, lift, left.y));
                vertices.Add(new Vector3(right.x, lift, right.y));
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                int a = first + i * 2;
                AddQuad(trimTriangles, a, a + 2, a + 3, a + 1);
            }
        }

        private void AddBox(Vector3 centre, Vector3 size)
        {
            Vector3 h = size * 0.5f;

            // Each face: its normal, then two directions across it whose
            // cross product is that normal, so it winds facing outward.
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

            foreach (Vector3 normal in normals)
            {
                Vector3 a = new Vector3(normal.y, normal.z, normal.x);
                Vector3 b = Vector3.Cross(normal, a);
                int first = vertices.Count;

                vertices.Add(centre + Vector3.Scale(normal - a - b, h));
                vertices.Add(centre + Vector3.Scale(normal - a + b, h));
                vertices.Add(centre + Vector3.Scale(normal + a + b, h));
                vertices.Add(centre + Vector3.Scale(normal + a - b, h));

                AddQuad(clothTriangles, first, first + 3, first + 2, first + 1);
            }
        }

        /// <summary>
        /// Two triangles through four corners in order round the edge. It
        /// faces the way Cross(b - a, c - a) points.
        /// </summary>
        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        private static Material Recolour(Material own, Material source, Color colour, string materialName)
        {
            if (own == null)
            {
                Shader flat = Shader.Find("NeoNoir/Flat");

                if (source != null)
                {
                    own = new Material(source);
                }
                else if (flat != null)
                {
                    own = new Material(flat);
                }
                else
                {
                    return null;
                }

                own.name = materialName;
                own.hideFlags = HideFlags.DontSave;
            }

            own.SetColor(FlatMaterial.BaseColorId, colour);
            return own;
        }

        private static void Release<T>(ref T thing) where T : Object
        {
            if (thing == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(thing);
            }
            else
            {
                DestroyImmediate(thing);
            }

            thing = null;
        }
    }
}
