using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A cloth banner hanging from a pole, with the kingdom's insignia on its
    /// front: a staff with a cross-and-triangle above and the same mirrored
    /// below. Built in code, so it needs no model or texture; drop it on an
    /// empty object.
    /// </summary>
    /// <remarks>
    /// The pivot is the middle of the pole and the banner hangs down from it,
    /// its insignia facing the object's forward (blue arrow). In play it sways
    /// in a slow draft, the bottom moving most; the wave's timing comes from
    /// where the banner stands, so a row of them down a hallway ripples in
    /// sequence rather than in step. It is drawn in edit mode too, still, so
    /// it can be placed by eye.
    ///
    /// Colours are set on materials of its own, copied from the ones given
    /// (or NeoNoir/Flat), rather than through a property block, which
    /// <see cref="OccluderFader"/> uses and clears.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HallBanner : MonoBehaviour
    {
        // The insignia, traced from the sketch and tidied to be symmetric.
        // u runs 0 to 1 across the banner, v 0 to 1 down from the pole to the
        // lowest point of the hem.
        private const float UpperTipV = 0.075f;
        private const float UpperCrossV = 0.235f;
        private const float UpperBarV = 0.415f;
        private const float LowerBarV = 0.515f;
        private const float LowerCrossV = 0.695f;
        private const float LowerTipV = 0.855f;
        private const float BarHalfU = 0.32f;
        private const float StaffTopV = 0.045f;
        private const float StaffBottomV = 0.885f;

        // The hem: two notches cut up into the bottom, leaving three points.
        // x is u; y is how far down that point hangs, 1 the lowest and 0 a
        // notch's full hemDepth higher.
        private static readonly Vector2[] HemPoints =
        {
            new Vector2(0f, 1f),
            new Vector2(0.32f, 0f),
            new Vector2(0.5f, 0.75f),
            new Vector2(0.68f, 0f),
            new Vector2(1f, 1f),
        };

        [Header("Shape")]
        [SerializeField, Min(0.1f)] private float width = 1.6f;
        [SerializeField, Min(0.1f)] private float height = 4f;

        [SerializeField, Range(0f, 0.4f)]
        [Tooltip("How far up the notches in the hem cut, as a fraction of the height.")]
        private float hemDepth = 0.12f;

        [SerializeField, Range(0.005f, 0.1f)]
        [Tooltip("Thickness of the insignia's lines, as a fraction of the width.")]
        private float lineWidth = 0.035f;

        [SerializeField, Min(2)]
        [Tooltip("Rows the cloth is cut into. More bends the sway more smoothly.")]
        private int rows = 24;

        [SerializeField, Min(1)] private int columns = 8;

        [Header("Pole")]
        [SerializeField] private bool showPole = true;
        [SerializeField, Min(0f)] private float poleOverhang = 0.2f;
        [SerializeField, Min(0.01f)] private float poleThickness = 0.07f;

        [Header("Colour")]
        [SerializeField, ColorUsage(false, true)] private Color clothColour = new Color(0.55f, 0.03f, 0.04f);

        [SerializeField, ColorUsage(false, true)]
        [Tooltip("The insignia and the pole.")]
        private Color trimColour = Color.black;

        [SerializeField]
        [Tooltip("Copied, not edited. NeoNoir/Flat if empty.")]
        private Material clothMaterial;

        [SerializeField]
        [Tooltip("Copied, not edited. NeoNoir/Flat if empty.")]
        private Material trimMaterial;

        [Header("Sway")]
        [SerializeField, Min(0f)]
        [Tooltip("How far the bottom of the banner swings, in metres. 0 hangs still.")]
        private float swayAmplitude = 0.12f;

        [SerializeField, Min(0f)] private float swaySpeed = 1.1f;

        [SerializeField, Min(0f)]
        [Tooltip("Waves running down the cloth at once.")]
        private float swayWaves = 0.6f;

        private readonly List<Vector3> restVertices = new List<Vector3>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> clothTriangles = new List<int>();
        private readonly List<int> trimTriangles = new List<int>();

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Material ownCloth;
        private Material ownTrim;

        // Vertices before this index sway; the pole after it stays put.
        private int swayingCount;
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
            if (dirty)
            {
                dirty = false;
                Rebuild();
            }

            if (Application.isPlaying && swayAmplitude > 0f && meshRenderer.isVisible)
            {
                Sway(Time.time);
            }
        }

        private void Rebuild()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Hall Banner", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }

            restVertices.Clear();
            clothTriangles.Clear();
            trimTriangles.Clear();

            BuildCloth();
            BuildInsignia();
            swayingCount = restVertices.Count;

            if (showPole)
            {
                BuildPole();
            }

            vertices.Clear();
            vertices.AddRange(restVertices);

            mesh.Clear();
            mesh.subMeshCount = 2;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(clothTriangles, 0);
            mesh.SetTriangles(trimTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Room for the sway, so it is not culled mid-swing.
            Bounds bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, 0f, swayAmplitude * 3f));
            mesh.bounds = bounds;

            GetComponent<MeshFilter>().sharedMesh = mesh;

            ownCloth = Recolour(ownCloth, clothMaterial, clothColour, "Banner Cloth");
            ownTrim = Recolour(ownTrim, trimMaterial, trimColour, "Banner Trim");
            meshRenderer.sharedMaterials = new[] { ownCloth, ownTrim };
        }

        /// <summary>
        /// A grid, front and back, whose columns are stretched down to the hem
        /// under them. The hem's corners are columns of their own so they stay
        /// sharp.
        /// </summary>
        private void BuildCloth()
        {
            List<float> us = new List<float>();

            for (int i = 0; i <= columns; i++)
            {
                us.Add((float)i / columns);
            }

            foreach (Vector2 point in HemPoints)
            {
                if (!us.Exists(u => Mathf.Abs(u - point.x) < 0.001f))
                {
                    us.Add(point.x);
                }
            }

            us.Sort();

            int across = us.Count;
            int down = rows + 1;

            for (int side = 0; side < 2; side++)
            {
                int first = restVertices.Count;

                foreach (float u in us)
                {
                    float bottom = HemV(u);

                    for (int j = 0; j < down; j++)
                    {
                        restVertices.Add(ToLocal(u, bottom * j / rows, 0f));
                    }
                }

                for (int i = 0; i < across - 1; i++)
                {
                    for (int j = 0; j < rows; j++)
                    {
                        int topLeft = first + i * down + j;
                        int topRight = topLeft + down;
                        int bottomLeft = topLeft + 1;
                        int bottomRight = topRight + 1;

                        if (side == 0)
                        {
                            AddQuad(clothTriangles, topLeft, bottomLeft, bottomRight, topRight);
                        }
                        else
                        {
                            AddQuad(clothTriangles, topLeft, topRight, bottomRight, bottomLeft);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Each glyph is one line, cross to corner to corner and back through
        /// the cross, so the triangle's corners are mitred rather than two
        /// ends overlapping.
        /// </summary>
        private void BuildInsignia()
        {
            AddStroke(new[] { new Vector2(0.5f, StaffTopV), new Vector2(0.5f, StaffBottomV) });
            AddGlyph(UpperTipV, UpperCrossV, UpperBarV);
            AddGlyph(LowerTipV, LowerCrossV, LowerBarV);
        }

        private void AddGlyph(float tipV, float crossV, float barV)
        {
            // The arms carry on past the cross along the lines from the bar's corners.
            float tipHalfU = BarHalfU * (tipV - crossV) / (barV - crossV);

            AddStroke(new[]
            {
                new Vector2(0.5f + tipHalfU, tipV),
                new Vector2(0.5f, crossV),
                new Vector2(0.5f + BarHalfU, barV),
                new Vector2(0.5f - BarHalfU, barV),
                new Vector2(0.5f, crossV),
                new Vector2(0.5f - tipHalfU, tipV),
            });
        }

        /// <summary>
        /// A mitred line through <paramref name="points"/> (in u, v), laid just
        /// in front of the cloth and cut into short pieces so it bends with it.
        /// </summary>
        private void AddStroke(Vector2[] points)
        {
            List<Vector3> path = new List<Vector3>();
            float step = height / rows;

            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 from = ToLocal(points[i].x, points[i].y, 0f);
                Vector3 to = ToLocal(points[i + 1].x, points[i + 1].y, 0f);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / step));

                for (int k = 0; k < pieces; k++)
                {
                    path.Add(Vector3.Lerp(from, to, (float)k / pieces));
                }
            }

            path.Add(ToLocal(points[points.Length - 1].x, points[points.Length - 1].y, 0f));

            float halfWidth = lineWidth * width * 0.5f;
            Vector3 lift = Vector3.forward * Mathf.Max(0.004f, halfWidth * 0.2f);
            int first = restVertices.Count;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 before = i > 0 ? (path[i] - path[i - 1]).normalized : Vector3.zero;
                Vector3 after = i < path.Count - 1 ? (path[i + 1] - path[i]).normalized : Vector3.zero;
                Vector3 direction = (before + after).normalized;

                if (direction == Vector3.zero)
                {
                    // Doubles straight back on itself; any side will do.
                    direction = before;
                }

                Vector3 side = new Vector3(-direction.y, direction.x, 0f);

                // Widen at corners so the line keeps its thickness round them.
                float miter = 1f;
                Vector3 reference = after != Vector3.zero ? after : before;
                float dot = Vector3.Dot(side, new Vector3(-reference.y, reference.x, 0f));

                if (dot > 0.01f)
                {
                    miter = Mathf.Min(1f / dot, 3f);
                }

                restVertices.Add(path[i] + side * (halfWidth * miter) + lift);
                restVertices.Add(path[i] - side * (halfWidth * miter) + lift);
            }

            for (int i = 0; i < path.Count - 1; i++)
            {
                int a = first + i * 2;
                AddQuad(trimTriangles, a, a + 1, a + 3, a + 2);
            }
        }

        private void BuildPole()
        {
            float half = width * 0.5f + poleOverhang;
            Vector3 thickness = Vector3.one * poleThickness;

            AddBox(Vector3.zero, new Vector3(half * 2f, poleThickness, poleThickness));
            AddBox(new Vector3(-half, 0f, 0f), thickness * 2f);
            AddBox(new Vector3(half, 0f, 0f), thickness * 2f);
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
                int first = restVertices.Count;

                restVertices.Add(centre + Vector3.Scale(normal - a - b, h));
                restVertices.Add(centre + Vector3.Scale(normal - a + b, h));
                restVertices.Add(centre + Vector3.Scale(normal + a + b, h));
                restVertices.Add(centre + Vector3.Scale(normal + a - b, h));

                AddQuad(trimTriangles, first, first + 3, first + 2, first + 1);
            }
        }

        /// <summary>
        /// Pushes the cloth in and out along its facing, nothing at the pole
        /// and most at the hem, in a wave running downward.
        /// </summary>
        private void Sway(float time)
        {
            Vector3 position = transform.position;
            float phase = (position.x * 0.7f + position.z * 0.45f) * 0.8f;
            float omega = swaySpeed * Mathf.PI * 2f * 0.25f;

            for (int i = 0; i < swayingCount; i++)
            {
                Vector3 rest = restVertices[i];
                float depth = Mathf.Clamp01(-rest.y / height);
                float wave = Mathf.Sin(time * omega - depth * swayWaves * Mathf.PI * 2f + phase);
                float drift = Mathf.Sin(time * omega * 0.37f + phase * 1.3f + rest.x * 0.6f) * 0.35f;

                rest.z += (wave + drift) * swayAmplitude * depth * depth;
                vertices[i] = rest;
            }

            mesh.SetVertices(vertices);
            mesh.RecalculateNormals();
        }

        private float HemV(float u)
        {
            for (int i = 0; i < HemPoints.Length - 1; i++)
            {
                Vector2 a = HemPoints[i];
                Vector2 b = HemPoints[i + 1];

                if (u <= b.x)
                {
                    float drop = Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.x, b.x, u));
                    return 1f - hemDepth * (1f - drop);
                }
            }

            return 1f;
        }

        private Vector3 ToLocal(float u, float v, float z)
        {
            return new Vector3((u - 0.5f) * width, -v * height, z);
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
