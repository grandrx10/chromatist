using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A line laid on the floor wherever the boss walks, whose colour runs
    /// round the rainbow as it goes. Each stretch keeps the colour it was laid
    /// in.
    /// </summary>
    /// <remarks>
    /// Goes on the boss's root. A point is laid on the floor under the boss
    /// each time it has moved <see cref="spacing"/>, and the line runs
    /// through them. It stays where it was laid, so a boss on the move draws a
    /// long streak behind it.
    ///
    /// The colour being laid turns <see cref="huePerMetre"/> of the way round
    /// the rainbow for every metre the boss walks, so it only changes while it
    /// moves. Each stretch of line takes the colour being laid when it was
    /// put down and never changes after. The rainbow is cut into
    /// <see cref="hueSteps"/> colours; more steps reads as a smoother sweep.
    ///
    /// Each point lasts <see cref="lifetime"/>; over its last
    /// <see cref="shrinkTime"/> the line there narrows to nothing, so the
    /// oldest end draws in after the boss. It narrows rather than turning
    /// see-through so the spot colour threshold never crushes a half-faded
    /// edge.
    ///
    /// Drawn on the shared runtime NeoNoir/Flat material, one material slot
    /// per colour, each tinted through its own property block, so it needs no
    /// material or prefab.
    ///
    /// Stops laying points when the boss dies or <see cref="Emitting"/> is
    /// turned off; what is laid draws in as usual.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ChromaticTrail : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Lay the line from the start. Turn off to start with Emitting off, for something else to turn on.")]
        private bool emitOnStart = true;

        [Header("Shape")]
        [SerializeField]
        [Tooltip("Width of the line.")]
        [Min(0.01f)]
        private float width = 1f;

        [SerializeField]
        [Tooltip("Height above the floor the line is drawn at.")]
        [Range(0f, 0.5f)]
        private float floorOffset = 0.03f;

        [SerializeField]
        [Tooltip("Layers the line is laid on. Without floor under the boss, it is laid at the boss's pivot.")]
        private LayerMask floorMask = 1;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Distance the boss moves between points laid.")]
        [Min(0.05f)]
        private float spacing = 0.25f;

        [SerializeField]
        [Tooltip("Seconds each point lasts.")]
        [Min(0.01f)]
        private float lifetime = 3f;

        [SerializeField]
        [Tooltip("Seconds at the end of each point's life over which the line there narrows to nothing.")]
        [Min(0.01f)]
        private float shrinkTime = 1f;

        [Header("Colour")]
        [SerializeField]
        [Tooltip("Turns of the rainbow per metre walked. 0.1 goes all the way round every 10 metres.")]
        [Min(0f)]
        private float huePerMetre = 0.1f;

        [SerializeField]
        [Tooltip("Colours the rainbow is cut into. 3 is just red, green and blue.")]
        [Range(3, 48)]
        private int hueSteps = 24;

        [SerializeField]
        [Tooltip("Where round the rainbow the line starts. 0 is red.")]
        [Range(0f, 1f)]
        private float startHue = 0f;

        [SerializeField]
        [Tooltip("Colour intensity. Above 1 blooms.")]
        [Min(0f)]
        private float intensity = 1.5f;

        private struct Point
        {
            public Vector3 position;
            public float time;
            public int step;
        }

        private readonly List<Point> points = new List<Point>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<List<int>> triangles = new List<List<int>>();

        private EnemyHealth health;
        private GameObject host;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private float walked;

        /// <summary>Whether the boss is laying line right now.</summary>
        public bool Emitting { get; set; }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            Emitting = emitOnStart;
        }

        private void OnEnable()
        {
            if (host != null)
            {
                host.SetActive(true);
            }
        }

        private void OnDisable()
        {
            if (host != null)
            {
                host.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (host != null)
            {
                Destroy(host);
            }

            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private void LateUpdate()
        {
            bool laying = Emitting && (health == null || !health.IsDead);
            float now = Time.time;

            while (points.Count > 0 && now - points[0].time >= lifetime)
            {
                points.RemoveAt(0);
            }

            if (laying)
            {
                Vector3 here = FloorUnder(transform.position);

                if (points.Count == 0)
                {
                    points.Add(new Point { position = here, time = now, step = StepAt(walked) });
                }
                else
                {
                    float moved = Flat(here - points[points.Count - 1].position).magnitude;

                    if (moved >= spacing)
                    {
                        walked += moved;
                        points.Add(new Point { position = here, time = now, step = StepAt(walked) });
                    }
                }
            }

            Draw(now);
        }

        /// <summary>The colour step being laid once the boss has walked <paramref name="distance"/>.</summary>
        private int StepAt(float distance)
        {
            float hue = Mathf.Repeat(startHue + distance * huePerMetre, 1f);
            return Mathf.Min(Mathf.FloorToInt(hue * hueSteps), hueSteps - 1);
        }

        private Vector3 FloorUnder(Vector3 position)
        {
            Vector3 floor = Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 4f, floorMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : position;

            return floor + Vector3.up * floorOffset;
        }

        private void Draw(float now)
        {
            if (points.Count < 2)
            {
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                }

                return;
            }

            Build();

            while (triangles.Count < hueSteps)
            {
                triangles.Add(new List<int>());
            }

            vertices.Clear();

            foreach (List<int> list in triangles)
            {
                list.Clear();
            }

            // Each stretch is its own quad, not sharing corners with the next,
            // so it can sit in its own colour's slot. Neighbours meet on the
            // same corner positions, so there are no gaps.
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i].position;
                Vector3 b = points[i + 1].position;
                Vector3 sideA = Side(i) * HalfWidth(points[i], now);
                Vector3 sideB = Side(i + 1) * HalfWidth(points[i + 1], now);

                int at = vertices.Count;
                vertices.Add(a - sideA);
                vertices.Add(a + sideA);
                vertices.Add(b - sideB);
                vertices.Add(b + sideB);

                // The stretch takes the colour of the point that finished it:
                // the colour being laid when it was put down.
                AddQuad(triangles[points[i + 1].step], at, at + 1, at + 2, at + 3);
            }

            mesh.Clear();
            mesh.subMeshCount = hueSteps;
            mesh.SetVertices(vertices);

            for (int s = 0; s < hueSteps; s++)
            {
                mesh.SetTriangles(triangles[s], s);
            }

            mesh.RecalculateBounds();
            Paint();
            meshRenderer.enabled = true;
        }

        private float HalfWidth(Point point, float now)
        {
            return width * 0.5f * Mathf.Clamp01((lifetime - (now - point.time)) / shrinkTime);
        }

        /// <summary>Across the line at point <paramref name="i"/>, flat: square to the way it runs there.</summary>
        private Vector3 Side(int i)
        {
            Vector3 before = points[Mathf.Max(0, i - 1)].position;
            Vector3 after = points[Mathf.Min(points.Count - 1, i + 1)].position;
            Vector3 along = Flat(after - before);

            if (along.sqrMagnitude < 0.000001f)
            {
                along = Vector3.forward;
            }

            return Vector3.Cross(Vector3.up, along.normalized);
        }

        /// <summary>A quad; the material draws both faces, so the winding never matters.</summary>
        private static void AddQuad(List<int> list, int a, int b, int c, int d)
        {
            list.Add(a); list.Add(c); list.Add(b);
            list.Add(b); list.Add(c); list.Add(d);
        }

        private void Paint()
        {
            if (meshRenderer.sharedMaterials.Length == hueSteps)
            {
                return;
            }

            Material flat = FlatMaterial.DoubleSided;
            Material[] slots = new Material[hueSteps];

            for (int s = 0; s < slots.Length; s++)
            {
                slots[s] = flat;
            }

            meshRenderer.sharedMaterials = slots;

            for (int s = 0; s < hueSteps; s++)
            {
                Color colour = Color.HSVToRGB(s / (float)hueSteps, 1f, 1f) * intensity;
                colour.a = 1f;
                block.Clear();
                block.SetColor(FlatMaterial.BaseColorId, colour);
                meshRenderer.SetPropertyBlock(block, s);
            }
        }

        private void Build()
        {
            if (host != null)
            {
                return;
            }

            // Its own object, in world space: the line stays where it was
            // laid, whatever the boss does.
            host = new GameObject($"{name} Chromatic Trail");

            mesh = new Mesh { name = "Chromatic trail" };
            mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = host.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            block = new MaterialPropertyBlock();
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }
    }
}
