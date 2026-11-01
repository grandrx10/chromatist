using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// The glowing lines along the Afterworld floor joining each blade to the
    /// blades it unlocks. Nothing is drawn until a blade is activated; then a
    /// line grows out of its stone to every blade that requires it.
    /// </summary>
    /// <remarks>
    /// Laid out from the <see cref="PurchasableBlade.Requires"/> lists alone,
    /// so there is nothing to place or draw by hand: one line per
    /// prerequisite, from the prerequisite's stone to the blade's.
    /// Prerequisites may be the blade PREFABS rather than the blades in the
    /// scene, so each is matched to the scene blade with the same
    /// <see cref="BladeDefinition"/>.
    ///
    /// Every line bows the same way (<see cref="bend"/>), so lines leaving a
    /// stone together curl round it like a pinwheel, and none of them ever
    /// turns back on itself. Each is a flat strip tapering to a point at both
    /// ends; while it grows, its tip is a brighter comet that fades into the
    /// line once it reaches the far stone.
    ///
    /// Lines whose blade was activated on an earlier visit load already
    /// drawn.
    ///
    /// Made on first use by <see cref="PurchasableBlade"/>. To restyle the
    /// lines, put this component on an object in the Afterworld and change
    /// the fields -- that one is used instead.
    /// </remarks>
    public sealed class SkillTreeLines : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField]
        [Tooltip("Width of the line at its widest, in metres.")]
        [Min(0.005f)]
        private float width = 0.12f;

        [SerializeField]
        [Tooltip("How far each line bows out to the side, as a fraction of its length. 0 is straight; negative bows the other way. Every line bows the same way.")]
        [Range(-0.6f, 0.6f)]
        private float bend = 0.22f;

        [SerializeField]
        [Tooltip("Metres over which each end tapers to a point.")]
        [Min(0f)]
        private float taper = 0.6f;

        [SerializeField]
        [Tooltip("Gap in metres left between each end of a line and the edge of its stone.")]
        [Min(0f)]
        private float stoneGap = 0.25f;

        [SerializeField]
        [Tooltip("Height of the lines relative to each PurchasableBlade object's position. Negative lowers them; set it so they sit just on the floor. Read when the scene starts.")]
        private float elevation = -1.5f;

        [SerializeField]
        [Tooltip("Metres between the points a line is built from. Smaller is smoother.")]
        [Min(0.02f)]
        private float resolution = 0.08f;

        [Header("Look")]
        [SerializeField]
        [Tooltip("HDR: above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color colour = new Color(2.5f, 2.5f, 2.5f, 1f);

        [SerializeField]
        [Tooltip("Colour of the growing tip. HDR.")]
        [ColorUsage(false, true)]
        private Color headColour = new Color(8f, 8f, 8f, 1f);

        [SerializeField]
        [Tooltip("Length of the bright growing tip, in metres.")]
        [Min(0f)]
        private float headLength = 1f;

        [SerializeField]
        [Tooltip("Width of the tip relative to the line.")]
        [Min(0f)]
        private float headWidth = 1.8f;

        [Header("Growing")]
        [SerializeField]
        [Tooltip("Seconds after a blade is activated before its lines start to grow, so they follow the stone lighting up.")]
        [Min(0f)]
        private float delay = 0.6f;

        [SerializeField]
        [Tooltip("Metres a second the lines grow.")]
        [Min(0.1f)]
        private float speed = 5f;

        [SerializeField]
        [Tooltip("Seconds the bright tip takes to fade into the line once it arrives.")]
        [Min(0.01f)]
        private float settleTime = 0.5f;

        private sealed class Line
        {
            public PurchasableBlade from;
            public readonly List<Vector3> points = new List<Vector3>();
            public readonly List<float> distances = new List<float>();
            public float start;
            public float end;
            public Mesh body;
            public Mesh head;
            public MeshRenderer headRenderer;
        }

        private static SkillTreeLines instance;

        private readonly List<Line> lines = new List<Line>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private MaterialPropertyBlock block;

        /// <summary>The lines in the scene, made on first use if there are none.</summary>
        public static SkillTreeLines Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<SkillTreeLines>(FindObjectsInactive.Include);
                }

                if (instance == null)
                {
                    instance = new GameObject("SkillTreeLines").AddComponent<SkillTreeLines>();
                }

                return instance;
            }
        }

        /// <summary>Height of the floor at <paramref name="blade"/>, where the lines run. Floor effects use it too.</summary>
        public float FloorAt(PurchasableBlade blade)
        {
            return blade.transform.position.y + elevation;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            BladeUnlocks.BladeAcquired += OnBladeAcquired;
        }

        private void OnDisable()
        {
            BladeUnlocks.BladeAcquired -= OnBladeAcquired;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            foreach (Line line in lines)
            {
                Destroy(line.body);
                Destroy(line.head);
            }
        }

        private void Start()
        {
            PurchasableBlade[] blades = FindObjectsByType<PurchasableBlade>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Dictionary<BladeDefinition, PurchasableBlade> byBlade = new Dictionary<BladeDefinition, PurchasableBlade>();

            foreach (PurchasableBlade blade in blades)
            {
                if (blade.Blade != null)
                {
                    byBlade[blade.Blade] = blade;
                }
            }

            foreach (PurchasableBlade to in blades)
            {
                foreach (PurchasableBlade prerequisite in to.Requires)
                {
                    if (prerequisite == null || prerequisite.Blade == null
                        || !byBlade.TryGetValue(prerequisite.Blade, out PurchasableBlade from) || from == to)
                    {
                        continue;
                    }

                    Line line = Lay(from, to);

                    // Activated on an earlier visit: already drawn.
                    if (from.IsAcquired)
                    {
                        Build(line.body, line, line.start, line.end, width, false);
                    }
                }
            }
        }

        private void OnBladeAcquired(BladeDefinition blade)
        {
            foreach (Line line in lines)
            {
                if (line.from.Blade == blade)
                {
                    StartCoroutine(Grow(line));
                }
            }
        }

        private IEnumerator Grow(Line line)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            float tip = line.start;
            line.headRenderer.enabled = true;

            while (tip < line.end)
            {
                tip = Mathf.Min(line.end, tip + speed * Time.deltaTime);
                Build(line.body, line, line.start, tip, width, false);
                Build(line.head, line, Mathf.Max(line.start, tip - headLength), tip, width * headWidth, true);
                yield return null;
            }

            // Arrived: the tip shrinks back into the line.
            float clock = 0f;

            while (clock < settleTime)
            {
                clock += Time.deltaTime;
                float left = 1f - Mathf.Clamp01(clock / settleTime);
                float length = headLength * left;
                Build(line.head, line, Mathf.Max(line.start, line.end - length), line.end, width * Mathf.Lerp(1f, headWidth, left) * left, true);
                yield return null;
            }

            line.headRenderer.enabled = false;
        }

        /// <summary>Works out the curve from one stone to the other, and makes its meshes.</summary>
        private Line Lay(PurchasableBlade from, PurchasableBlade to)
        {
            Line line = new Line { from = from };

            Vector3 a = from.StoneTop;
            Vector3 b = to.StoneTop;
            a.y = FloorAt(from);
            b.y = FloorAt(to);

            // A quadratic curve bowing to one side never turns back on
            // itself, and with every line sharing the side, they all curl the
            // same way.
            Vector3 along = b - a;
            along.y = 0f;
            float straight = along.magnitude;
            Vector3 side = straight > 0.0001f ? Vector3.Cross(Vector3.up, along / straight) : Vector3.right;
            Vector3 control = (a + b) * 0.5f + side * (bend * straight * 2f);

            int count = Mathf.Max(8, Mathf.CeilToInt(straight * (1f + Mathf.Abs(bend) * 2f) / resolution));
            float distance = 0f;

            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                float u = 1f - t;
                Vector3 point = u * u * a + 2f * u * t * control + t * t * b;

                if (i > 0)
                {
                    distance += Vector3.Distance(line.points[i - 1], point);
                }

                line.points.Add(point);
                line.distances.Add(distance);
            }

            // Starts and stops at the stones' edges, not their middles.
            line.start = Mathf.Min(from.StoneRadius + stoneGap, distance * 0.45f);
            line.end = Mathf.Max(line.start, distance - Mathf.Min(to.StoneRadius + stoneGap, distance * 0.45f));

            GameObject host = new GameObject($"Line {from.name} -> {to.name}");
            host.transform.SetParent(transform, false);

            line.body = MakeMesh(host, "Body", colour, out _);
            line.head = MakeMesh(host, "Head", headColour, out line.headRenderer);
            line.headRenderer.enabled = false;

            lines.Add(line);
            return line;
        }

        private Mesh MakeMesh(GameObject parent, string meshName, Color tint, out MeshRenderer renderer)
        {
            GameObject host = new GameObject(meshName);
            host.transform.SetParent(parent.transform, false);

            // The tip sits a hair above the body, so it always draws on top.
            if (meshName == "Head")
            {
                host.transform.localPosition = Vector3.up * 0.003f;
            }

            Mesh mesh = new Mesh { name = meshName };
            mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            renderer = host.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.DoubleSided;
            FlatMaterial.Tint(renderer, tint, block);
            return mesh;
        }

        /// <summary>
        /// Builds the part of <paramref name="line"/> from distance
        /// <paramref name="from"/> to <paramref name="to"/> into
        /// <paramref name="mesh"/>, as a flat strip <paramref name="stripWidth"/>
        /// wide tapering to points at both of its ends. A comet also thins
        /// toward its tail.
        /// </summary>
        private void Build(Mesh mesh, Line line, float from, float to, float stripWidth, bool comet)
        {
            vertices.Clear();
            normals.Clear();
            triangles.Clear();

            if (to - from > 0.001f)
            {
                List<float> distances = line.distances;
                float tapered = Mathf.Min(taper, (to - from) * 0.5f);

                int i = 0;
                while (i < distances.Count - 1 && distances[i + 1] <= from)
                {
                    i++;
                }

                AddPair(line, i, from, from, to, stripWidth, tapered, comet);

                for (int j = i + 1; j < distances.Count && distances[j] < to; j++)
                {
                    if (distances[j] > from)
                    {
                        AddPair(line, j - 1, distances[j], from, to, stripWidth, tapered, comet);
                    }
                }

                int last = i;
                while (last < distances.Count - 2 && distances[last + 1] < to)
                {
                    last++;
                }

                AddPair(line, last, to, from, to, stripWidth, tapered, comet);

                for (int k = 0; k + 3 < vertices.Count; k += 2)
                {
                    triangles.Add(k); triangles.Add(k + 2); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + 2); triangles.Add(k + 3);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Adds the two edge vertices at distance <paramref name="at"/>, which lies on segment <paramref name="segment"/>.</summary>
        private void AddPair(Line line, int segment, float at, float from, float to, float stripWidth, float tapered, bool comet)
        {
            List<Vector3> points = line.points;
            List<float> distances = line.distances;

            int next = Mathf.Min(segment + 1, points.Count - 1);
            float length = distances[next] - distances[segment];
            float t = length > 0f ? Mathf.Clamp01((at - distances[segment]) / length) : 0f;
            Vector3 point = Vector3.Lerp(points[segment], points[next], t);

            Vector3 tangent = points[next] - points[segment];
            tangent.y = 0f;
            tangent = tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.forward;
            Vector3 side = new Vector3(-tangent.z, 0f, tangent.x);

            // Rounded points: the square root gives a nib rather than a spike.
            float half = stripWidth * 0.5f;

            if (tapered > 0f)
            {
                half *= Mathf.Sqrt(Mathf.Clamp01((at - from) / tapered)) * Mathf.Sqrt(Mathf.Clamp01((to - at) / tapered));
            }

            if (comet && to > from)
            {
                half *= Mathf.Sqrt(Mathf.Clamp01((at - from) / (to - from)));
            }

            vertices.Add(point + side * half);
            vertices.Add(point - side * half);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
        }
    }
}
