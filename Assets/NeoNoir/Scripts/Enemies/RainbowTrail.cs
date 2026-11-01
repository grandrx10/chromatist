using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A line laid on the floor wherever the boss goes: a white outline round
    /// a rainbow of colour bands running its length.
    /// </summary>
    /// <remarks>
    /// Goes on the boss's root. A point is laid on the floor under the boss
    /// each time it has moved <see cref="spacing"/>, and the line runs
    /// through them to wherever the boss is now. It stays where it was laid,
    /// so a boss on the move draws a long streak behind it. Each point lasts
    /// <see cref="lifetime"/>; over its last <see cref="shrinkTime"/> the line
    /// there narrows to nothing, so the tail end draws in after the boss.
    ///
    /// Across the line: <see cref="outlineWidth"/> of white each side, and
    /// the rest split into <see cref="bands"/> stripes, red through violet.
    /// <see cref="hueSpeed"/> sends the colours rolling across the stripes.
    ///
    /// Nothing fades: everything shown is full colour, so the spot colour
    /// threshold never crushes a half-faded edge. Drawn on the shared runtime
    /// NeoNoir/Flat material, one material slot per colour, each tinted
    /// through its own property block, so it needs no material or prefab.
    ///
    /// Stops laying points when the boss dies or <see cref="Emitting"/> is
    /// turned off; what is laid draws in as usual.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RainbowTrail : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Lay the line from the start. Turn off to start with Emitting off, for something else to turn on.")]
        private bool emitOnStart = true;

        [Header("Shape")]
        [SerializeField]
        [Tooltip("Width of the whole line, outline included.")]
        [Min(0.01f)]
        private float width = 1.2f;

        [SerializeField]
        [Tooltip("Width of the white outline along each side.")]
        [Min(0f)]
        private float outlineWidth = 0.12f;

        [SerializeField]
        [Tooltip("Colour stripes across the inside of the line.")]
        [Range(1, 12)]
        private int bands = 6;

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
        private float spacing = 0.4f;

        [SerializeField]
        [Tooltip("Seconds each point lasts.")]
        [Min(0.01f)]
        private float lifetime = 2.5f;

        [SerializeField]
        [Tooltip("Seconds at the end of each point's life over which the line there narrows to nothing.")]
        [Min(0.01f)]
        private float shrinkTime = 0.6f;

        [Header("Colour")]
        [SerializeField]
        [Tooltip("Colour of the outline.")]
        private Color outlineColour = Color.white;

        [SerializeField]
        [Tooltip("Colour intensity of the outline. Above 1 blooms.")]
        [Min(0f)]
        private float outlineIntensity = 1.5f;

        [SerializeField]
        [Tooltip("Colour intensity of the stripes. Above 1 blooms.")]
        [Min(0f)]
        private float bandIntensity = 1.5f;

        [SerializeField]
        [Tooltip("How far round the rainbow the stripes go, across the line. 1 is red through violet.")]
        [Range(0.1f, 1f)]
        private float hueSpan = 0.83f;

        [SerializeField]
        [Tooltip("Turns of the rainbow per second the colours roll across the stripes. 0 holds them still.")]
        private float hueSpeed = 0.3f;

        private struct Point
        {
            public Vector3 position;
            public float time;
        }

        private readonly List<Point> points = new List<Point>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int>[] triangles = new List<int>[13];
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly List<float> halfWidths = new List<float>();

        private EnemyHealth health;
        private GameObject host;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;

        /// <summary>Whether the boss is laying line right now.</summary>
        public bool Emitting { get; set; }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            Emitting = emitOnStart;

            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = new List<int>();
            }
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

            Vector3 here = laying ? FloorUnder(transform.position) : Vector3.zero;

            if (laying && (points.Count == 0 || Flat(here - points[points.Count - 1].position).sqrMagnitude >= spacing * spacing))
            {
                points.Add(new Point { position = here, time = now });
            }

            Draw(laying, here, now);
        }

        private Vector3 FloorUnder(Vector3 position)
        {
            Vector3 floor = Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 4f, floorMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : position;

            return floor + Vector3.up * floorOffset;
        }

        /// <summary>Rebuilds the line through the points, on to <paramref name="head"/> while still laying.</summary>
        private void Draw(bool laying, Vector3 head, float now)
        {
            path.Clear();
            halfWidths.Clear();

            foreach (Point point in points)
            {
                path.Add(point.position);
                halfWidths.Add(width * 0.5f * Mathf.Clamp01((lifetime - (now - point.time)) / shrinkTime));
            }

            if (laying && points.Count > 0 && Flat(head - path[path.Count - 1]).sqrMagnitude > 0.0001f)
            {
                path.Add(head);
                halfWidths.Add(width * 0.5f);
            }

            if (path.Count < 2)
            {
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                }

                return;
            }

            Build();

            vertices.Clear();

            foreach (List<int> list in triangles)
            {
                list.Clear();
            }

            int stripes = bands;
            float outline = Mathf.Min(outlineWidth, width * 0.5f);

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 side = Side(i);
                float half = halfWidths[i];
                float scale = half / (width * 0.5f);
                float inner = half - outline * scale;

                // Outline: the full width, underneath. Stripes on top of it,
                // raised a hair so they never fight it for the same pixels.
                vertices.Add(path[i] - side * half);
                vertices.Add(path[i] + side * half);

                Vector3 raised = path[i] + Vector3.up * 0.005f;

                for (int b = 0; b <= stripes; b++)
                {
                    vertices.Add(raised + side * Mathf.Lerp(-inner, inner, b / (float)stripes));
                }
            }

            int stride = 2 + stripes + 1;

            for (int i = 0; i < path.Count - 1; i++)
            {
                int at = i * stride;
                int next = at + stride;

                AddQuad(triangles[0], at, at + 1, next, next + 1);

                for (int b = 0; b < stripes; b++)
                {
                    AddQuad(triangles[1 + b], at + 2 + b, at + 3 + b, next + 2 + b, next + 3 + b);
                }
            }

            mesh.Clear();
            mesh.subMeshCount = 1 + stripes;
            mesh.SetVertices(vertices);

            for (int s = 0; s <= stripes; s++)
            {
                mesh.SetTriangles(triangles[s], s);
            }

            mesh.RecalculateBounds();
            Paint(now);
            meshRenderer.enabled = true;
        }

        /// <summary>Across the line at point <paramref name="i"/>, flat: square to the way it runs there.</summary>
        private Vector3 Side(int i)
        {
            Vector3 before = path[Mathf.Max(0, i - 1)];
            Vector3 after = path[Mathf.Min(path.Count - 1, i + 1)];
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

        private void Paint(float now)
        {
            int stripes = bands;
            Material flat = FlatMaterial.DoubleSided;

            if (meshRenderer.sharedMaterials.Length != 1 + stripes)
            {
                Material[] slots = new Material[1 + stripes];

                for (int s = 0; s < slots.Length; s++)
                {
                    slots[s] = flat;
                }

                meshRenderer.sharedMaterials = slots;
            }

            SetColour(0, outlineColour * outlineIntensity, outlineColour.a);

            float roll = now * hueSpeed;

            for (int b = 0; b < stripes; b++)
            {
                float across = stripes > 1 ? b / (float)(stripes - 1) : 0f;
                float hue = Mathf.Repeat(across * hueSpan + roll, 1f);
                SetColour(1 + b, Color.HSVToRGB(hue, 1f, 1f) * bandIntensity, 1f);
            }
        }

        private void SetColour(int slot, Color colour, float alpha)
        {
            colour.a = alpha;
            block.Clear();
            block.SetColor(FlatMaterial.BaseColorId, colour);
            meshRenderer.SetPropertyBlock(block, slot);
        }

        private void Build()
        {
            if (host != null)
            {
                return;
            }

            // Its own object, in world space: the line stays where it was
            // laid, whatever the boss does.
            host = new GameObject($"{name} Rainbow Trail");

            mesh = new Mesh { name = "Rainbow trail" };
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
