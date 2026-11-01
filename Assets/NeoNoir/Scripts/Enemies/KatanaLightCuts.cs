using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The katana's stored-up cuts let loose: thin lines of light shoot out
    /// across the floor from the boss, each curving its own way, and a
    /// moment later a wall of light flows out along each one, from the boss
    /// to its tip. A wall hurts the player only where its light is.
    /// </summary>
    /// <remarks>
    /// Made by <see cref="KatanaParryPattern"/> with <see cref="Release"/>.
    /// Runs on its own object and removes itself once every wall has run
    /// out; <see cref="Done"/> says when.
    ///
    /// Each curve is an arc on the floor, <see cref="Settings.length"/>
    /// long, starting <see cref="Settings.startOffset"/> out from the boss's
    /// middle and bending by a random angle in <see cref="Settings.bend"/>
    /// to either side. Each is aimed to pass through its own random spot
    /// within <see cref="Settings.aimRadius"/> of the player, measured flat,
    /// so the cuts close in on them from every which way. A curve too tightly
    /// bent to reach its spot is bent less until it does, and failing that
    /// runs straight at it. With no player, the curves head out at random,
    /// each in its own slice of the circle so no side is left bare.
    ///
    /// Each line shoots out over <see cref="Settings.lineGrowTime"/>, the
    /// lines a little apart, up to <see cref="Settings.lineStagger"/>.
    /// <see cref="Settings.wallDelay"/> after a line starts, its wall sets
    /// off, at <see cref="Settings.wallSpeed"/>: a stretch
    /// <see cref="Settings.wallLength"/> long, tallest at its leading edge,
    /// passing right through to the tip and out. The lines stay whole until
    /// every wall is through, then all fade out together over
    /// <see cref="Settings.lineFadeTime"/>. Each wall hurts the player once, the
    /// first time they are within <see cref="Settings.hitRadius"/> of its
    /// stretch, measured flat; walls hurt separately, so crossing several
    /// at once hurts several times.
    ///
    /// Everything stays where it was drawn, on the floor at the boss's feet
    /// -- or, during a run, moves on with the <see cref="ChaseFrame"/>, so it
    /// keeps its place round the boss and the player as they run.
    /// If the boss dies, the light goes at once and nothing more is hurt.
    /// </remarks>
    public sealed class KatanaLightCuts : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Curves")]
            [Tooltip("Length of each curve along the floor.")]
            [Min(0.1f)] public float length = 30f;

            [Tooltip("How far out from the boss's middle each curve begins.")]
            [Min(0f)] public float startOffset = 1f;

            [Tooltip("Least and most degrees a curve turns over its length, to one side or the other at random.")]
            public Vector2 bend = new Vector2(15f, 120f);

            [Tooltip("Each curve passes through a random spot within this far of the player, measured flat.")]
            [Min(0f)] public float aimRadius = 5f;

            [Tooltip("Height of the lines and walls above the boss's feet, so they sit just over the floor.")]
            public float groundOffset = 0.03f;

            [Tooltip("Points along each curve. More draws a smoother bend.")]
            [Range(4, 128)] public int segments = 48;

            [Header("Lines")]
            [Tooltip("Width of each line on the floor.")]
            [Min(0.01f)] public float lineWidth = 0.12f;

            [Tooltip("Colour of the lines.")]
            public Color lineColour = new Color(0.75f, 0.95f, 1f);

            [Tooltip("Colour intensity of the lines. Above 1 blooms.")]
            [Min(0f)] public float lineIntensity = 2.5f;

            [Tooltip("Seconds each line takes to shoot out to its full length.")]
            [Min(0.01f)] public float lineGrowTime = 0.2f;

            [Tooltip("Lines set off up to this many seconds apart, at random, so they don't all shoot out as one.")]
            [Min(0f)] public float lineStagger = 0.15f;

            [Tooltip("Seconds the lines take to fade out, all together, once every wall has passed right through.")]
            [Min(0.01f)] public float lineFadeTime = 0.3f;

            [Header("Walls")]
            [Tooltip("Seconds from a line starting to its wall setting off.")]
            [Min(0f)] public float wallDelay = 1f;

            [Tooltip("How fast each wall flows out along its curve, in units per second.")]
            [Min(0.1f)] public float wallSpeed = 40f;

            [Tooltip("Length of the stretch of light flowing along the curve. Only this stretch hurts.")]
            [Min(0.1f)] public float wallLength = 4f;

            [Tooltip("Height of the wall at its leading edge. It slopes down to nothing at the back.")]
            [Min(0.01f)] public float wallHeight = 2.5f;

            [Tooltip("Colour of the walls.")]
            public Color wallColour = new Color(0.85f, 0.97f, 1f);

            [Tooltip("Colour intensity of the walls. Above 1 blooms.")]
            [Min(0f)] public float wallIntensity = 5f;

            [Header("Hurting")]
            [Tooltip("Damage a wall does to the player. Each wall hurts at most once.")]
            [Min(0f)] public float damage = 10f;

            [Tooltip("A player this close to a wall's stretch of light, measured flat, is hit.")]
            [Min(0f)] public float hitRadius = 0.6f;

            [Header("Effects")]
            [Tooltip("How far the camera jolts as the walls set off. 0 for none.")]
            [Min(0f)] public float shakeStrength = 0.15f;

            [Min(0.01f)] public float shakeDuration = 0.25f;
        }

        private sealed class Curve
        {
            public Vector3[] points;
            public float step;
            public float start;
            public bool hit;
        }

        private readonly List<Curve> curves = new List<Curve>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Enemy owner;
        private Settings settings;
        private float clock;
        private float fadeClock;
        private bool shaken;

        private Mesh lineMesh;
        private Mesh wallMesh;

        /// <summary>True once every wall has run out to its tip, or the boss has died.</summary>
        public bool Done { get; private set; }

        /// <summary>
        /// Lets loose <paramref name="count"/> cuts from <paramref name="boss"/>,
        /// on the floor at its feet.
        /// </summary>
        public static KatanaLightCuts Release(Enemy boss, int count, Settings settings)
        {
            KatanaLightCuts cuts = new GameObject("KatanaLightCuts").AddComponent<KatanaLightCuts>();
            cuts.owner = boss;
            cuts.settings = settings;
            cuts.Build(boss.transform.position, Mathf.Max(1, count), boss.HasTarget ? boss.TargetPoint : (Vector3?)null);
            return cuts;
        }

        private void Build(Vector3 origin, int count, Vector3? player)
        {
            origin.y += settings.groundOffset;

            int segments = Mathf.Max(4, settings.segments);
            float step = settings.length / segments;
            float slice = 360f / count;
            float turn = UnityEngine.Random.Range(0f, 360f);

            for (int i = 0; i < count; i++)
            {
                float bend = UnityEngine.Random.Range(Mathf.Min(settings.bend.x, settings.bend.y), Mathf.Max(settings.bend.x, settings.bend.y));
                bend *= UnityEngine.Random.value < 0.5f ? -1f : 1f;

                float heading = turn + (i + UnityEngine.Random.value) * slice;
                Vector3 start = origin;

                if (player.HasValue)
                {
                    Vector2 spread = UnityEngine.Random.insideUnitCircle * settings.aimRadius;
                    Vector3 spot = new Vector3(player.Value.x + spread.x, origin.y, player.Value.z + spread.y);
                    heading = Flat(spot - origin);
                    start = origin + Quaternion.Euler(0f, heading, 0f) * Vector3.forward * settings.startOffset;
                    heading = AimThrough(start, spot, heading, ref bend);
                }

                // An arc: the heading turns evenly along the length.
                Vector3[] points = new Vector3[segments + 1];
                Vector3 at = player.HasValue ? start : origin + Quaternion.Euler(0f, heading, 0f) * Vector3.forward * settings.startOffset;
                points[0] = at;

                for (int k = 1; k <= segments; k++)
                {
                    float middle = heading + bend * ((k - 0.5f) / segments);
                    at += Quaternion.Euler(0f, middle, 0f) * Vector3.forward * step;
                    points[k] = at;
                }

                curves.Add(new Curve
                {
                    points = points,
                    step = step,
                    start = UnityEngine.Random.Range(0f, settings.lineStagger),
                });
            }

            lineMesh = MakeLayer("Lines", settings.lineColour, settings.lineIntensity);
            wallMesh = MakeLayer("Walls", settings.wallColour, settings.wallIntensity);
        }

        /// <summary>
        /// The heading a curve setting off from <paramref name="start"/> needs
        /// to pass through <paramref name="spot"/>, bending by
        /// <paramref name="bend"/> degrees over its length. Lessens the bend
        /// when it is too tight to get there; straight at it when nothing will.
        /// </summary>
        private float AimThrough(Vector3 start, Vector3 spot, float straight, ref float bend)
        {
            Vector3 toward = spot - start;
            toward.y = 0f;
            float distance = toward.magnitude;

            if (distance < 0.01f || distance >= settings.length)
            {
                bend = 0f;
                return straight;
            }

            float chordHeading = Flat(toward);

            for (int tries = 0; tries < 6; tries++, bend *= 0.6f)
            {
                // Bending evenly, k radians per unit, the chord to a point s
                // along is 2 sin(ks/2) / k long, pointing ks/2 round from the start.
                float k = Mathf.Abs(bend) * Mathf.Deg2Rad / settings.length;

                if (k < 0.0001f)
                {
                    break;
                }

                float half = distance * k * 0.5f;

                if (half > 1f)
                {
                    continue;
                }

                float along = 2f * Mathf.Asin(half) / k;

                if (along <= settings.length)
                {
                    return chordHeading - bend * along / (2f * settings.length);
                }
            }

            bend = 0f;
            return chordHeading;
        }

        /// <summary>The heading, in degrees about up, that <paramref name="direction"/> points flat along.</summary>
        private static float Flat(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private Mesh MakeLayer(string layerName, Color colour, float intensity)
        {
            GameObject host = new GameObject(layerName);
            host.transform.SetParent(transform, false);

            Mesh mesh = new Mesh { name = $"Katana light cut {layerName.ToLowerInvariant()}" };
            mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = host.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sharedMaterial = FlatMaterial.DoubleSided;

            Color bright = colour * intensity;
            bright.a = colour.a;
            FlatMaterial.Tint(meshRenderer, bright, new MaterialPropertyBlock());
            return mesh;
        }

        private void Update()
        {
            if (owner == null || owner.Health == null || owner.Health.IsDead)
            {
                Done = true;
                Destroy(gameObject);
                return;
            }

            clock += Time.deltaTime;

            // Carried on with a run, so the cuts keep up with the player.
            Vector3 ride = ChaseFrame.Step(Time.deltaTime);

            if (ride != Vector3.zero)
            {
                foreach (Curve curve in curves)
                {
                    for (int k = 0; k < curve.points.Length; k++)
                    {
                        curve.points[k] += ride;
                    }
                }
            }

            bool running = false;
            PlayerHealth player = owner.HasTarget ? owner.Target.GetComponentInParent<PlayerHealth>() : null;

            vertices.Clear();
            triangles.Clear();

            foreach (Curve curve in curves)
            {
                float front = WallFront(curve);
                float tail = front - settings.wallLength;

                if (tail < settings.length)
                {
                    running = true;
                }

                if (front <= 0f || tail >= settings.length)
                {
                    continue;
                }

                if (!shaken)
                {
                    shaken = true;
                    TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
                }

                AddWall(curve, front);

                if (!curve.hit && player != null && Touches(curve, Mathf.Max(0f, tail), Mathf.Min(front, settings.length), owner.TargetPoint, out Vector3 near))
                {
                    curve.hit = true;
                    player.TakeDamage(settings.damage, near);
                }
            }

            Fill(wallMesh);

            // The lines stay whole until every wall has passed right through,
            // then all thin away together.
            if (!running)
            {
                fadeClock += Time.deltaTime;
            }

            float width = 1f - fadeClock / settings.lineFadeTime;

            vertices.Clear();
            triangles.Clear();

            if (width > 0f)
            {
                foreach (Curve curve in curves)
                {
                    float age = clock - curve.start;
                    float grown = Mathf.Clamp01(age / settings.lineGrowTime);
                    grown = 1f - (1f - grown) * (1f - grown);

                    if (age > 0f)
                    {
                        AddLine(curve, grown * settings.length, width);
                    }
                }
            }

            Fill(lineMesh);

            if (width <= 0f)
            {
                Done = true;
                Destroy(gameObject);
            }
        }

        /// <summary>How far along its curve the wall's leading edge has flowed. 0 or less before it sets off.</summary>
        private float WallFront(Curve curve)
        {
            return (clock - curve.start - settings.wallDelay) * settings.wallSpeed;
        }

        /// <summary>A line from the boss out to <paramref name="to"/> along the curve, at <paramref name="width"/> of its full width.</summary>
        private void AddLine(Curve curve, float to, float width)
        {
            if (to <= 0f)
            {
                return;
            }

            float half = settings.lineWidth * 0.5f * width;

            Walk(curve, 0f, to, (point, along, distance) =>
            {
                Vector3 side = Vector3.Cross(Vector3.up, along) * half;
                vertices.Add(point - side);
                vertices.Add(point + side);
            });
        }

        private void AddWall(Curve curve, float front)
        {
            float tail = front - settings.wallLength;

            Walk(curve, Mathf.Max(0f, tail), Mathf.Min(front, settings.length), (point, along, distance) =>
            {
                // Tallest at the leading edge, sloping to nothing at the back.
                float rise = Mathf.Clamp01((distance - tail) / settings.wallLength);
                vertices.Add(point);
                vertices.Add(point + Vector3.up * (settings.wallHeight * rise));
            });
        }

        /// <summary>
        /// Lays a strip along <paramref name="curve"/> from <paramref name="from"/>
        /// to <paramref name="to"/>, with <paramref name="edge"/> adding the two
        /// vertices across it at each point: the point, the way the curve runs
        /// there, and how far along it is.
        /// </summary>
        private void Walk(Curve curve, float from, float to, Action<Vector3, Vector3, float> edge)
        {
            int first = vertices.Count;
            int last = curve.points.Length - 1;
            int a = Mathf.Clamp(Mathf.CeilToInt(from / curve.step), 0, last);
            int b = Mathf.Clamp(Mathf.FloorToInt(to / curve.step), 0, last);

            edge(PointAt(curve, from, out Vector3 along), along, from);

            for (int k = a; k <= b; k++)
            {
                float distance = k * curve.step;

                if (distance > from && distance < to)
                {
                    edge(PointAt(curve, distance, out along), along, distance);
                }
            }

            edge(PointAt(curve, to, out along), along, to);

            for (int at = first; at + 3 < vertices.Count; at += 2)
            {
                triangles.Add(at);
                triangles.Add(at + 2);
                triangles.Add(at + 1);
                triangles.Add(at + 1);
                triangles.Add(at + 2);
                triangles.Add(at + 3);
            }
        }

        private static Vector3 PointAt(Curve curve, float distance, out Vector3 along)
        {
            int last = curve.points.Length - 1;
            float at = Mathf.Clamp(distance / curve.step, 0f, last);
            int k = Mathf.Min(Mathf.FloorToInt(at), last - 1);

            Vector3 a = curve.points[k];
            Vector3 b = curve.points[k + 1];
            along = (b - a).normalized;
            return Vector3.Lerp(a, b, at - k);
        }

        /// <summary>Whether <paramref name="target"/> is within the hit radius, flat, of the curve between two distances along it.</summary>
        private bool Touches(Curve curve, float from, float to, Vector3 target, out Vector3 near)
        {
            near = target;

            if (to <= from)
            {
                return false;
            }

            Vector2 flat = new Vector2(target.x, target.z);
            float reach = settings.hitRadius * settings.hitRadius;
            float distance = from;

            while (true)
            {
                float next = Mathf.Min(to, (Mathf.Floor(distance / curve.step) + 1f) * curve.step);
                Vector3 a = PointAt(curve, distance, out _);
                Vector3 b = PointAt(curve, next, out _);

                Vector2 a2 = new Vector2(a.x, a.z);
                Vector2 ab = new Vector2(b.x, b.z) - a2;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(flat - a2, ab) / ab.sqrMagnitude) : 0f;

                if ((a2 + ab * t - flat).sqrMagnitude <= reach)
                {
                    near = Vector3.Lerp(a, b, t);
                    return true;
                }

                if (next >= to)
                {
                    return false;
                }

                distance = next;
            }
        }

        private void Fill(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (lineMesh != null)
            {
                Destroy(lineMesh);
            }

            if (wallMesh != null)
            {
                Destroy(wallMesh);
            }
        }
    }
}
