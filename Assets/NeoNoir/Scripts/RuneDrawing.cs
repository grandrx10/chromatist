using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Glowing lines drawn flat in an object's local XZ plane that write
    /// themselves in, stroke by stroke, as <see cref="Progress"/> goes from
    /// 0 to 1. What <see cref="RuneCircle"/> and <see cref="RuneArc"/> are
    /// drawn with.
    /// </summary>
    /// <remarks>
    /// Each stroke is given the part of the progress during which it is
    /// written; before it, it is not there, and after it, it is whole. The
    /// mesh is built on the host object, so it needs no prefab, and is
    /// coloured through a property block, never by editing the material.
    /// Call <see cref="Release"/> from the host's OnDestroy.
    /// </remarks>
    public sealed class RuneDrawing
    {
        private sealed class Stroke
        {
            public List<Vector3> points;
            public float[] distances;
            public bool closed;
            public float halfWidth;
            public float start;
            public float end;

            public float Length => distances[distances.Length - 1];
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Stroke> strokes = new List<Stroke>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Vector3> partial = new List<Vector3>();

        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly Material ownMaterial;

        private float progress = -1f;

        /// <summary>
        /// Draws on <paramref name="host"/>, which must not have a mesh of its
        /// own. A null <paramref name="material"/> makes a NeoNoir/Flat one.
        /// </summary>
        public RuneDrawing(GameObject host, Material material)
        {
            mesh = new Mesh { name = "Runes" };
            mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            renderer = host.AddComponent<MeshRenderer>();
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
        }

        /// <summary>How much is written, 0 to 1.</summary>
        public float Progress
        {
            get => Mathf.Max(0f, progress);
            set
            {
                value = Mathf.Clamp01(value);

                if (value != progress)
                {
                    progress = value;
                    Rebuild();
                }
            }
        }

        /// <summary>HDR: above 1 blooms.</summary>
        public void SetColour(Color colour)
        {
            colour.a = 1f;

            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetColor(ColorId, colour);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Dissolves the lines away, 1 solid to 0 gone. A dithered stipple
        /// rather than transparency, which this game's two-tone look cannot
        /// show; NeoNoir/Flat has it, other shaders may not.
        /// </summary>
        public void SetFade(float fade)
        {
            renderer.GetPropertyBlock(block);
            block.SetFloat(FadeId, Mathf.Clamp01(fade));
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Adds a line through <paramref name="points"/>, in local space,
        /// written while the progress goes from <paramref name="start"/> to
        /// <paramref name="end"/>. Only X and Z are used.
        /// </summary>
        public void AddStroke(List<Vector3> points, bool closed, float width, float start, float end)
        {
            float[] distances = new float[points.Count];

            for (int i = 1; i < points.Count; i++)
            {
                distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            }

            strokes.Add(new Stroke
            {
                points = points,
                distances = distances,
                closed = closed,
                halfWidth = width * 0.5f,
                start = start,
                end = end,
            });

            progress = -1f;
        }

        /// <summary>
        /// Adds a rune from <see cref="RuneGlyphs"/>, centred on
        /// <paramref name="centre"/>, <paramref name="size"/> metres tall,
        /// with its top toward <paramref name="up"/> (flat, in local space).
        /// Its lines are written one after another while the progress goes
        /// from <paramref name="start"/> to <paramref name="end"/>, each given
        /// time by its length, so the pen moves at one speed.
        /// </summary>
        public void AddRune(List<List<Vector2>> rune, Vector3 centre, Vector3 up, float size, float width,
                           float start, float end)
        {
            up.y = 0f;
            up.Normalize();
            Vector3 right = new Vector3(up.z, 0f, -up.x);
            float half = size * 0.5f;

            float total = 0f;
            foreach (List<Vector2> line in rune)
            {
                total += RuneGlyphs.PathLength(line);
            }

            float written = 0f;

            foreach (List<Vector2> line in rune)
            {
                List<Vector3> points = new List<Vector3>(line.Count);

                foreach (Vector2 point in line)
                {
                    points.Add(centre + (right * point.x + up * point.y) * half);
                }

                float from = Mathf.Lerp(start, end, written / total);
                written += RuneGlyphs.PathLength(line);
                float to = Mathf.Lerp(start, end, written / total);

                bool closed = (line[0] - line[line.Count - 1]).sqrMagnitude < 0.0001f;
                AddStroke(points, closed, width, from, to);
            }
        }

        /// <summary>
        /// The rotation that turns a drawing to face the main camera, its
        /// top toward the camera's up, for drawings hanging in the air.
        /// </summary>
        public static Quaternion FacingCamera()
        {
            // Drawn flat with the top toward local forward: lie the drawing
            // across the camera's view.
            Camera view = Camera.main;
            return view != null
                ? Quaternion.LookRotation(view.transform.up, -view.transform.forward)
                : Quaternion.identity;
        }

        /// <summary>Frees the mesh and any material made for it.</summary>
        public void Release()
        {
            Object.Destroy(mesh);

            if (ownMaterial != null)
            {
                Object.Destroy(ownMaterial);
            }
        }

        private void Rebuild()
        {
            vertices.Clear();
            triangles.Clear();

            foreach (Stroke stroke in strokes)
            {
                float t = stroke.end > stroke.start
                    ? Mathf.InverseLerp(stroke.start, stroke.end, progress)
                    : progress >= stroke.end ? 1f : 0f;

                if (t <= 0f)
                {
                    continue;
                }

                Trim(stroke, t * stroke.Length);
                AddRibbon(partial, stroke.closed && t >= 1f, stroke.halfWidth);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Fills <see cref="partial"/> with the stroke's first <paramref name="length"/> metres.</summary>
        private void Trim(Stroke stroke, float length)
        {
            partial.Clear();
            partial.Add(stroke.points[0]);

            for (int i = 1; i < stroke.points.Count; i++)
            {
                Vector3 point = stroke.points[i];
                bool last = stroke.distances[i] > length;

                if (last)
                {
                    float segment = stroke.distances[i] - stroke.distances[i - 1];
                    float t = segment > 0f ? (length - stroke.distances[i - 1]) / segment : 1f;
                    point = Vector3.Lerp(stroke.points[i - 1], point, t);
                }

                // Skips points on top of the last, which have no direction.
                if ((point - partial[partial.Count - 1]).sqrMagnitude > 0.000001f)
                {
                    partial.Add(point);
                }

                if (last)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// A flat strip along <paramref name="points"/>, mitred at the
        /// corners. A closed one's first and last points are the same, and
        /// it is mitred across that join too.
        /// </summary>
        private void AddRibbon(List<Vector3> points, bool closed, float halfWidth)
        {
            int count = points.Count;

            if (count < 2)
            {
                return;
            }

            int first = vertices.Count;

            for (int i = 0; i < count; i++)
            {
                Vector3 incoming = i > 0
                    ? points[i] - points[i - 1]
                    : closed ? points[0] - points[count - 2] : points[1] - points[0];

                Vector3 outgoing = i < count - 1
                    ? points[i + 1] - points[i]
                    : closed ? points[1] - points[0] : incoming;

                incoming.Normalize();
                outgoing.Normalize();

                Vector3 tangent = incoming + outgoing;
                tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : outgoing;

                Vector3 side = new Vector3(-tangent.z, 0f, tangent.x);
                Vector3 segmentSide = new Vector3(-outgoing.z, 0f, outgoing.x);

                // Wider at a corner so the line keeps its width through it,
                // capped so a sharp corner does not spike.
                float miter = halfWidth / Mathf.Max(0.35f, Vector3.Dot(side, segmentSide));

                vertices.Add(points[i] + side * miter);
                vertices.Add(points[i] - side * miter);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int at = first + i * 2;

                // Wound both ways, so it shows from either side.
                triangles.Add(at); triangles.Add(at + 2); triangles.Add(at + 1);
                triangles.Add(at + 1); triangles.Add(at + 2); triangles.Add(at + 3);
                triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
                triangles.Add(at + 1); triangles.Add(at + 3); triangles.Add(at + 2);
            }
        }
    }
}
