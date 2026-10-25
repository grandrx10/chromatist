using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of Scarlett's claw marks: a wide, ragged tear raked sideways across
    /// a point, as if something had clawed at the screen, cutting everything
    /// it opens over.
    /// </summary>
    /// <remarks>
    /// The shape is built once at spawn and only revealed over time:
    ///
    ///   spine      a shallow arc across the target, bowed away from the
    ///              player, thrown sideways at a few kinks so it runs in
    ///              sharp zigzags rather than a smooth curve.
    ///   width      fullest in the middle, drawn out to needle points at both
    ///              ends.
    ///   edges      each edge torn independently with irregularly spaced
    ///              teeth and bites, so the two sides never match, plus a few
    ///              long splinters swept back against the direction of the
    ///              rake, like fibres dragged out by the claw.
    ///
    /// The tear opens from one end to the other at a fixed speed, with a
    /// pointed head, sweeping damage along the spine as it goes. It then holds
    /// open for a moment and closes up, narrowing to nothing.
    ///
    /// The middle of the tear always lies on the target. The bow curves the
    /// ends back toward the player rather than pushing the middle away, so a
    /// strongly bowed tear still lands where it was aimed.
    ///
    /// The mesh lies flat at the cut height, then every vertex is pulled
    /// toward the camera by the same factor. That leaves its outline on screen
    /// exactly where it was, but lifts it above the scene, so it reads as a
    /// mark across the picture rather than something sunk into enemies.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab.
    /// </remarks>
    public sealed class ScarlettClaw : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Min(0f)] public float damage = 10f;

            [Tooltip("Left to right distance the claw rakes across, centred on its target.")]
            [Min(0.1f)] public float span = 5.5f;

            [Tooltip("Units per second the tear opens across.")]
            [Min(0.5f)] public float speed = 40f;

            [Tooltip("Width of the tear at its widest.")]
            [Min(0.01f)] public float thickness = 0.8f;

            [Tooltip("How far the ends of the tear curve back toward the player. The middle stays on the target.")]
            public float bow = 0.5f;

            [Tooltip("Sharp changes of direction along the tear. Too many, or too large, and a wide tear folds over itself.")]
            [Range(0, 10)] public int kinks = 3;

            [Tooltip("How far each kink throws the tear sideways.")]
            [Min(0f)] public float kinkSize = 0.22f;

            [Tooltip("How torn the edges are. 0 is smooth.")]
            [Range(0f, 1f)] public float raggedness = 0.7f;

            [Tooltip("Long shards torn out of the edges, swept back against the rake.")]
            [Range(0, 32)] public int splinters = 7;

            [Tooltip("How far a splinter sticks out, in multiples of the tear's width there.")]
            [Min(1f)] public float splinterLength = 2.2f;

            [Tooltip("Seconds the tear stays fully open once drawn.")]
            [Min(0f)] public float hold = 0.08f;

            [Tooltip("Seconds the mark takes to close up and vanish after it has been held.")]
            [Min(0.01f)] public float fadeTime = 0.5f;

            [Tooltip("Width of the outline round the tear. 0 for none.")]
            [Min(0f)] public float outlineWidth = 0.06f;

            [Tooltip("Distance the mark is lifted toward the camera, so it draws over the scene. Does not move it on screen.")]
            [Min(0f)] public float lift = 4f;
        }

        // Points along the spine. Enough that each serration is small next to
        // the tear even on the largest settings.
        private const int Samples = 72;

        // Fraction of the span over which the opening head comes to a point.
        private const float HeadTaper = 0.12f;

        private enum Phase { Opening, Holding, Fading }

        private readonly HashSet<EnemyHealth> hit = new HashSet<EnemyHealth>();

        private readonly Vector3[] spine = new Vector3[Samples];
        private readonly Vector3[] side = new Vector3[Samples];
        private readonly Vector3[] tangent = new Vector3[Samples];
        private readonly float[] distances = new float[Samples];
        private readonly float[] leftEdge = new float[Samples];
        private readonly float[] rightEdge = new float[Samples];

        // Per sample: 0 for none, otherwise which side a splinter leaves from
        // (+1 left, -1 right) and how long it is, in multiples of the width.
        private readonly float[] splinterSide = new float[Samples];
        private readonly float[] splinterLength = new float[Samples];

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Color> colours = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();

        private readonly List<Vector3> leftLine = new List<Vector3>();
        private readonly List<Vector3> rightLine = new List<Vector3>();
        private readonly List<Vector3> outline = new List<Vector3>();

        // Scratch for AppendOutline. Shared, since meshes are only ever built
        // on the main thread, one at a time.
        private static readonly List<Vector3> OutlineOffsets = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Settings settings;
        private Action<float> onDealt;
        private Vector3 direction;
        private Color colour;
        private Material material;
        private ScarlettBlood.Settings blood;
        private Vector3 source;
        private Vector3 cutPoint;
        private Mesh mesh;

        private Phase phase;
        private float clock;
        private float travelled;
        private int segment;

        private float Length => distances[Samples - 1];

        /// <summary>
        /// Rakes a claw across <paramref name="centre"/>, travelling along
        /// <paramref name="across"/>. <paramref name="away"/> is the direction
        /// from the player to the centre; the tear bows that way. Both are
        /// flattened. <paramref name="onDealt"/> is told the health taken off
        /// each frame anything is hit, for lifesteal. Each enemy hit spews
        /// <paramref name="blood"/> out of its far side, away from
        /// <paramref name="source"/> (the player), drawn with the same material.
        /// </summary>
        public static ScarlettClaw Spawn(Vector3 centre, Vector3 across, Vector3 away, Vector3 source,
                                         Settings settings, ScarlettBlood.Settings blood, Material material,
                                         Color colour, Action<float> onDealt)
        {
            GameObject host = new GameObject("ScarlettClaw");
            ScarlettClaw claw = host.AddComponent<ScarlettClaw>();
            claw.Launch(centre, across, away, source, settings, blood, material, colour, onDealt);
            return claw;
        }

        private void Launch(Vector3 centre, Vector3 across, Vector3 away, Vector3 launchSource,
                            Settings launchSettings, ScarlettBlood.Settings launchBlood, Material launchMaterial,
                            Color launchColour, Action<float> dealt)
        {
            settings = launchSettings;
            source = launchSource;
            blood = launchBlood;
            material = launchMaterial;
            onDealt = dealt;
            colour = launchColour;

            across.y = 0f;
            away.y = 0f;
            across = across.sqrMagnitude > 0.000001f ? across.normalized : Vector3.right;
            away = away.sqrMagnitude > 0.000001f ? away.normalized : Vector3.Cross(across, Vector3.up);
            direction = across;

            BuildSpine(centre, across, away);
            BuildEdges();

            mesh = new Mesh { name = "ScarlettClaw" };
            mesh.MarkDynamic();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            phase = Phase.Opening;
        }

        private void BuildSpine(Vector3 centre, Vector3 across, Vector3 away)
        {
            // Kinks sit exactly on samples, so each one is a real corner
            // rather than being rounded off between two samples.
            float[] kinkOffsets = new float[Samples];
            int kinkCount = settings.kinks;

            if (kinkCount > 0)
            {
                int[] at = new int[kinkCount + 2];
                float[] offset = new float[kinkCount + 2];
                float sign = UnityEngine.Random.value < 0.5f ? 1f : -1f;

                at[0] = 0;
                at[kinkCount + 1] = Samples - 1;

                for (int k = 1; k <= kinkCount; k++)
                {
                    float even = k / (float)(kinkCount + 1);
                    float jitter = UnityEngine.Random.Range(-0.3f, 0.3f) / (kinkCount + 1);
                    at[k] = Mathf.Clamp(Mathf.RoundToInt((even + jitter) * (Samples - 1)), at[k - 1] + 1, Samples - 2);

                    float u = at[k] / (float)(Samples - 1);
                    offset[k] = sign * settings.kinkSize * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u))) * UnityEngine.Random.Range(0.5f, 1f);
                    sign = -sign;
                }

                for (int k = 0; k <= kinkCount; k++)
                {
                    for (int i = at[k]; i <= at[k + 1]; i++)
                    {
                        float t = (i - at[k]) / (float)Mathf.Max(1, at[k + 1] - at[k]);
                        kinkOffsets[i] = Mathf.Lerp(offset[k], offset[k + 1], t);
                    }
                }
            }

            for (int i = 0; i < Samples; i++)
            {
                float centred = i / (float)(Samples - 1) * 2f - 1f;

                spine[i] = centre
                           + across * (centred * settings.span * 0.5f)
                           + away * (-settings.bow * centred * centred + kinkOffsets[i]);

                distances[i] = i == 0 ? 0f : distances[i - 1] + Vector3.Distance(spine[i - 1], spine[i]);
            }

            for (int i = 0; i < Samples; i++)
            {
                Vector3 along = spine[Mathf.Min(i + 1, Samples - 1)] - spine[Mathf.Max(i - 1, 0)];
                along.y = 0f;
                tangent[i] = along.normalized;
                side[i] = Vector3.Cross(Vector3.up, tangent[i]);
            }
        }

        private void BuildEdges()
        {
            TearEdge(leftEdge);
            TearEdge(rightEdge);

            // Splinters: a single vertex pushed far out makes a thin spike.
            // Kept off the very ends, where the tear is too narrow to show one.
            int first = Mathf.RoundToInt(Samples * 0.15f);
            int last = Mathf.RoundToInt(Samples * 0.85f);

            for (int s = 0; s < settings.splinters; s++)
            {
                int i = UnityEngine.Random.Range(first, last);
                splinterSide[i] = UnityEngine.Random.value < 0.5f ? 1f : -1f;
                splinterLength[i] = settings.splinterLength * UnityEngine.Random.Range(0.7f, 1.2f);
            }
        }

        /// <summary>
        /// Teeth and bites at irregular gaps along an otherwise clean edge.
        /// Irregular is what makes it read as torn; notching every point
        /// evenly reads as fur.
        /// </summary>
        private void TearEdge(float[] edge)
        {
            float r = settings.raggedness;

            for (int i = 0; i < Samples; i++)
            {
                edge[i] = 1f;
            }

            for (int i = UnityEngine.Random.Range(1, 5); i < Samples - 1; i += UnityEngine.Random.Range(2, 7))
            {
                edge[i] = UnityEngine.Random.value < 0.55f
                    ? 1f + r * UnityEngine.Random.Range(0.25f, 0.6f)
                    : 1f - r * UnityEngine.Random.Range(0.4f, 0.8f);
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            switch (phase)
            {
                case Phase.Opening:
                    Open(Mathf.Min(Length, travelled + settings.speed * deltaTime));

                    if (travelled >= Length - 0.0001f)
                    {
                        phase = Phase.Holding;
                        clock = 0f;
                    }

                    break;

                case Phase.Holding:
                    clock += deltaTime;

                    if (clock >= settings.hold)
                    {
                        phase = Phase.Fading;
                        clock = 0f;
                    }

                    break;

                case Phase.Fading:
                    clock += deltaTime;

                    if (clock >= settings.fadeTime)
                    {
                        Destroy(gameObject);
                        return;
                    }

                    break;
            }

            Rebuild();
        }

        /// <summary>Advances the head to <paramref name="target"/> along the spine, cutting corner to corner.</summary>
        private void Open(float target)
        {
            float radius = Mathf.Max(0.3f, settings.thickness * 0.5f);
            Vector3 start = SpineAt(travelled);
            float dealt = 0f;

            // Scarlett's hits spew blood rather than drawing the usual slash
            // streak, so the streak is turned off and Bleed draws the blood.
            while (segment < Samples - 1 && distances[segment + 1] <= target)
            {
                Vector3 corner = spine[segment + 1];
                cutPoint = corner;
                dealt += BladeDamage.Sweep(start, corner, radius, settings.damage, direction, hit,
                                           Bleed, colour, false);
                start = corner;
                segment++;
            }

            travelled = target;
            cutPoint = SpineAt(travelled);
            dealt += BladeDamage.Sweep(start, cutPoint, radius, settings.damage, direction, hit,
                                       Bleed, colour, false);

            if (dealt > 0f)
            {
                onDealt?.Invoke(dealt);
            }
        }

        /// <summary>
        /// Spews blood out of the centre of an enemy just cut, away from the
        /// player, so it reads as blown out through the body.
        /// </summary>
        private void Bleed(EnemyHealth enemy, Vector3 from)
        {
            if (blood == null || blood.droplets <= 0)
            {
                return;
            }

            // The enemy's own centre, not the cut height: the claw cuts at
            // the player's middle, which on a taller enemy is down by its feet.
            // A killing blow destroys the enemy at the end of this frame, not
            // now, so its colliders can still be read here.
            Vector3 wound = enemy.BodyBounds().center;

            Vector3 away = wound - source;
            away.y = 0f;

            if (away.sqrMagnitude < 0.0001f)
            {
                away = direction;
            }

            ScarlettBlood.Spawn(wound, away, blood, material);
        }


        private Vector3 SpineAt(float distance)
        {
            int i = Mathf.Min(segment, Samples - 2);

            while (i < Samples - 2 && distances[i + 1] < distance)
            {
                i++;
            }

            float span = distances[i + 1] - distances[i];
            float t = span > 0.000001f ? (distance - distances[i]) / span : 1f;
            return Vector3.Lerp(spine[i], spine[i + 1], Mathf.Clamp01(t));
        }

        private void Rebuild()
        {
            vertices.Clear();
            normals.Clear();
            colours.Clear();
            uvs.Clear();
            triangles.Clear();
            leftLine.Clear();
            rightLine.Clear();

            // Closing narrows the whole tear at once, easing in so it stays
            // open a beat before it seals shut.
            float closing = phase == Phase.Fading ? Mathf.Clamp01(clock / settings.fadeTime) : 0f;
            float open = 1f - closing * closing * (3f - 2f * closing);

            float headLength = Mathf.Max(0.0001f, settings.span * HeadTaper);

            for (int i = 0; i < Samples && distances[i] <= travelled; i++)
            {
                float u = distances[i] / Length;
                // Clamped: at the far end sin(pi) comes out a hair below zero
                // in float, and a fractional power of a negative is NaN.
                float profile = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), 1.3f);
                float head = Mathf.Clamp01((travelled - distances[i]) / headLength);
                float half = settings.thickness * 0.5f * profile * head * open;

                Vector3 left = spine[i] + side[i] * (half * leftEdge[i]);
                Vector3 right = spine[i] - side[i] * (half * rightEdge[i]);

                if (splinterSide[i] != 0f)
                {
                    // Out to the side and swept back against the rake.
                    float reach = half * splinterLength[i];
                    Vector3 shard = spine[i] + side[i] * (splinterSide[i] * reach) - tangent[i] * (reach * 0.8f);

                    if (splinterSide[i] > 0f)
                    {
                        left = shard;
                    }
                    else
                    {
                        right = shard;
                    }
                }

                AddPair(left, right);
                leftLine.Add(left);
                rightLine.Add(right);
            }

            // The head itself, always a point.
            if (phase == Phase.Opening)
            {
                Vector3 tip = SpineAt(travelled);
                AddPair(tip, tip);
                leftLine.Add(tip);
                rightLine.Add(tip);
            }

            // Thins with the tear as it closes, so no white line is left
            // standing once the window itself has sealed.
            AddOutline(settings.outlineWidth * open);

            Lift();

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colours);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddPair(Vector3 left, Vector3 right)
        {
            int index = vertices.Count;

            vertices.Add(left);
            vertices.Add(right);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            colours.Add(colour);
            colours.Add(colour);

            // u = 0 marks the window; the outline strip is u = 1.
            // v runs across the tear, 0 on the left edge and 1 on the right.
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));

            if (index < 2)
            {
                return;
            }

            AddQuad(index - 2, index - 1, index, index + 1);
        }

        /// <summary>
        /// A strip of constant width round the outside of the tear, flagged
        /// for the outline colour. The tear writes no depth, so the outline
        /// pass cannot draw this for it.
        /// </summary>
        private void AddOutline(float width)
        {
            if (width <= 0f)
            {
                return;
            }

            // One closed loop: down the left edge, back up the right. The two
            // edges meet at the ends, so drop the repeated points there, or
            // their zero-length sides would have no direction to push out in.
            outline.Clear();
            outline.AddRange(leftLine);

            for (int i = rightLine.Count - 1; i >= 0; i--)
            {
                outline.Add(rightLine[i]);
            }

            for (int i = outline.Count - 1; i > 0; i--)
            {
                if ((outline[i] - outline[i - 1]).sqrMagnitude < 0.0000001f)
                {
                    outline.RemoveAt(i);
                }
            }

            AppendOutline(outline, width, vertices, normals, colours, uvs, triangles);
        }

        /// <summary>
        /// Appends a strip of constant <paramref name="width"/> round the
        /// outside of the closed, flat <paramref name="loop"/>, flagged
        /// uv.x = 1 for NeoNoir/Tear's outline colour. For marks drawn with
        /// that shader, which writes no depth, so the outline pass cannot
        /// draw their outline for them. <paramref name="colours"/> may be null.
        /// </summary>
        public static void AppendOutline(List<Vector3> loop, float width, List<Vector3> vertices,
                                         List<Vector3> normals, List<Color> colours, List<Vector2> uvs,
                                         List<int> triangles)
        {
            if (width <= 0f)
            {
                return;
            }

            // A closing point equal to the first adds a zero-length side with
            // no direction to push out in.
            if (loop.Count > 1 && (loop[0] - loop[loop.Count - 1]).sqrMagnitude < 0.0000001f)
            {
                loop.RemoveAt(loop.Count - 1);
            }

            int count = loop.Count;

            if (count < 3)
            {
                return;
            }

            // Which way round the loop runs decides which side of each edge
            // is outside, so take it from the signed area.
            float area = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = loop[i];
                Vector3 q = loop[(i + 1) % count];
                area += p.x * q.z - q.x * p.z;
            }

            float outward = area >= 0f ? 1f : -1f;

            OutlineOffsets.Clear();

            for (int i = 0; i < count; i++)
            {
                Vector3 before = EdgeNormal(loop[(i - 1 + count) % count], loop[i], outward);
                Vector3 after = EdgeNormal(loop[i], loop[(i + 1) % count], outward);
                Vector3 normal = before + after;

                // Averaged rather than mitred: a mitre at a splinter's tip
                // would shoot the outline out far past the point.
                OutlineOffsets.Add(normal.sqrMagnitude > 0.000001f ? normal.normalized * width : after * width);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int index = vertices.Count;

                vertices.Add(loop[i]);
                vertices.Add(loop[i] + OutlineOffsets[i]);
                vertices.Add(loop[next]);
                vertices.Add(loop[next] + OutlineOffsets[next]);

                for (int v = 0; v < 4; v++)
                {
                    normals.Add(Vector3.up);
                    colours?.Add(Color.white);
                    uvs.Add(new Vector2(1f, 0f));
                }

                AddQuad(triangles, index, index + 1, index + 2, index + 3);
            }
        }

        private static Vector3 EdgeNormal(Vector3 from, Vector3 to, float outward)
        {
            Vector3 edge = to - from;
            Vector3 normal = new Vector3(edge.z, 0f, -edge.x) * outward;
            return normal.sqrMagnitude > 0.0000001f ? normal.normalized : Vector3.zero;
        }

        private void AddQuad(int a, int b, int c, int d)
        {
            AddQuad(triangles, a, b, c, d);
        }

        /// <summary>
        /// Two triangles across a quad whose corners run a-b along one side
        /// and c-d along the other, in both windings, so it shows whichever
        /// way the camera looks at it. The material culls one of each pair.
        /// </summary>
        public static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(b); triangles.Add(d); triangles.Add(c);
        }

        private void Lift()
        {
            LiftTowardCamera(vertices, spine[Samples / 2], settings.lift);
        }

        /// <summary>
        /// Pulls every vertex toward the camera by the same factor, chosen so
        /// <paramref name="anchor"/> comes <paramref name="lift"/> closer. That
        /// leaves the shape on screen untouched but draws it over the scene.
        /// </summary>
        public static void LiftTowardCamera(List<Vector3> vertices, Vector3 anchor, float lift)
        {
            Camera camera = Camera.main;

            if (camera == null || lift <= 0f || vertices.Count == 0)
            {
                return;
            }

            Vector3 eye = camera.transform.position;
            float distance = Vector3.Distance(eye, anchor);

            // Never pulled past the near plane, however close the camera is.
            float lifted = Mathf.Max(camera.nearClipPlane * 2f, distance - lift);
            float scale = lifted / Mathf.Max(0.0001f, distance);

            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = eye + (vertices[i] - eye) * scale;
            }
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
