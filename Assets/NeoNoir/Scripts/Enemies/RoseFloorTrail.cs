using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The glowing pink line Rose's tip cuts into the floor as she slides,
    /// shedding petals as it goes. Each stretch of it holds for
    /// <see cref="Settings.linger"/> seconds, hurting the player who stands
    /// on it, then thins away.
    /// </summary>
    /// <remarks>
    /// Fed a point at a time by <see cref="Emit"/>; a point is only kept once
    /// the tip has moved <see cref="Settings.spacing"/> from the last, so
    /// standing still adds nothing. The line is a flat ribbon on the floor,
    /// rebuilt every frame: each point keeps its full width until it is
    /// <see cref="Settings.linger"/> old, then narrows to nothing over
    /// <see cref="Settings.fadeTime"/>. Only the holding part hurts.
    ///
    /// A player within <see cref="Settings.radius"/> of the holding part,
    /// measured flat, takes <see cref="Settings.damagePerSecond"/> in ticks
    /// <see cref="Settings.tickInterval"/> apart, the first the moment they
    /// step on. Once its enemy dies the line hurts no more, and is gone when
    /// the last of it has faded.
    ///
    /// Petals are only for show, as on the player's dash.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab.
    /// </remarks>
    public sealed class RoseFloorTrail : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Seconds each stretch holds, hurting, before it starts to fade.")]
            [Min(0f)] public float linger = 3f;

            [Tooltip("Seconds a stretch takes to thin away once it has held. It no longer hurts meanwhile.")]
            [Min(0.01f)] public float fadeTime = 0.4f;

            [Tooltip("Damage a second to the player standing on it.")]
            [Min(0f)] public float damagePerSecond = 10f;

            [Tooltip("Seconds between ticks of damage. Each tick deals Damage Per Second times this.")]
            [Min(0.02f)] public float tickInterval = 0.5f;

            [Tooltip("How close to the line, measured flat, the player is hurt.")]
            [Min(0.01f)] public float radius = 0.6f;

            [Tooltip("Thickness of the line drawn on the floor.")]
            [Min(0.01f)] public float width = 0.35f;

            [Tooltip("Height above the floor the line is drawn at.")]
            [Range(0f, 0.5f)] public float floorOffset = 0.04f;

            [Tooltip("Distance the tip moves between one point of the line and the next.")]
            [Min(0.02f)] public float spacing = 0.15f;

            [Tooltip("Colour of the line. Keep it strongly saturated, or the spot colour threshold crushes it. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(2f, 0.44f, 0.9f);

            [Tooltip("Material for the line. Empty uses NeoNoir/Flat in the colour above.")]
            public Material material;

            [Tooltip("Height above the floor petals are shed from.")]
            public float petalHeight = 0.3f;

            public RosePetal.Settings petals = new RosePetal.Settings();
        }

        private struct Point
        {
            public Vector3 Position;
            public float Born;
        }

        private readonly List<Point> points = new List<Point>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Settings settings;
        private Material petalMaterial;
        private EnemyHealth owner;
        private PlayerHealth player;
        private Mesh mesh;

        private float tickWait;
        private float petalGap;

        /// <summary>
        /// Starts an empty line for <paramref name="enemy"/>, hurting the
        /// player it is fighting. Petals take <paramref name="petalMaterial"/>;
        /// without one, none are shed.
        /// </summary>
        public static RoseFloorTrail Create(Enemy enemy, Settings settings, Material petalMaterial)
        {
            GameObject host = new GameObject("RoseFloorTrail");
            RoseFloorTrail trail = host.AddComponent<RoseFloorTrail>();
            trail.Begin(enemy, settings, petalMaterial);
            return trail;
        }

        private void Begin(Enemy enemy, Settings trailSettings, Material petals)
        {
            settings = trailSettings;
            petalMaterial = petals;
            owner = enemy.Health;

            if (enemy.HasTarget)
            {
                player = enemy.Target.GetComponentInParent<PlayerHealth>();
            }

            mesh = new Mesh { name = "Rose floor trail" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            Material material = settings.material != null ? settings.material : FlatMaterial.DoubleSided;
            meshRenderer.sharedMaterial = material;

            if (settings.material == null && material != null)
            {
                FlatMaterial.Tint(meshRenderer, settings.colour, new MaterialPropertyBlock());
            }
        }

        /// <summary>
        /// The tip is at <paramref name="floorPoint"/> this frame, heading
        /// along <paramref name="direction"/>. Call every frame the line is
        /// being cut.
        /// </summary>
        public void Emit(Vector3 floorPoint, Vector3 direction)
        {
            if (points.Count == 0)
            {
                AddPoint(floorPoint);
                return;
            }

            Vector3 last = points[points.Count - 1].Position;
            float length = Vector3.Distance(last, floorPoint);

            if (length < settings.spacing)
            {
                return;
            }

            ShedPetals(last, floorPoint, direction);
            AddPoint(floorPoint);
        }

        private void AddPoint(Vector3 position)
        {
            points.Add(new Point { Position = position, Born = Time.time });
        }

        /// <summary>
        /// Drops petals at even spacing along the stretch, carrying the
        /// leftover distance on, as the player's dash does.
        /// </summary>
        private void ShedPetals(Vector3 from, Vector3 to, Vector3 direction)
        {
            if (petalMaterial == null)
            {
                return;
            }

            float length = Vector3.Distance(from, to);
            Vector3 lift = Vector3.up * settings.petalHeight;
            float along = petalGap;

            while (along <= length)
            {
                RosePetal.Spawn(Vector3.Lerp(from, to, along / length) + lift, direction, settings.petals, petalMaterial);
                along += settings.petals.spacing;
            }

            petalGap = along - length;
        }

        private void Update()
        {
            float now = Time.time;
            float life = settings.linger + settings.fadeTime;

            // Dropped from the old end once fully faded.
            while (points.Count > 0 && now - points[0].Born >= life)
            {
                points.RemoveAt(0);
            }

            bool ownerAlive = owner != null && !owner.IsDead;

            if (!ownerAlive && points.Count == 0)
            {
                Destroy(gameObject);
                return;
            }

            tickWait -= Time.deltaTime;

            if (!ownerAlive || player == null || player.IsDead || tickWait > 0f)
            {
                return;
            }

            if (Touching(player.transform.position, now, out Vector3 closest))
            {
                player.TakeDamage(settings.damagePerSecond * settings.tickInterval, closest);
                tickWait = settings.tickInterval;
            }
        }

        /// <summary>
        /// Whether <paramref name="position"/> is within the radius of any
        /// stretch still holding, measured flat, and the nearest point on it.
        /// </summary>
        private bool Touching(Vector3 position, float now, out Vector3 closest)
        {
            closest = position;
            float radiusSquared = settings.radius * settings.radius;
            Vector2 flat = new Vector2(position.x, position.z);

            for (int i = 0; i < points.Count; i++)
            {
                if (now - points[i].Born >= settings.linger)
                {
                    continue;
                }

                // A lone holding point, or the stretch to the next one.
                Vector3 a = points[i].Position;
                Vector3 b = i + 1 < points.Count ? points[i + 1].Position : a;
                Vector2 a2 = new Vector2(a.x, a.z);
                Vector2 ab = new Vector2(b.x, b.z) - a2;

                float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(flat - a2, ab) / ab.sqrMagnitude) : 0f;

                if ((a2 + ab * t - flat).sqrMagnitude <= radiusSquared)
                {
                    closest = Vector3.Lerp(a, b, t);
                    return true;
                }
            }

            return false;
        }

        private void LateUpdate()
        {
            Build(Time.time);
        }

        /// <summary>A flat ribbon through every point, each as wide as its age allows.</summary>
        private void Build(float now)
        {
            vertices.Clear();
            triangles.Clear();

            int count = points.Count;

            if (count >= 2)
            {
                Vector3 lift = Vector3.up * settings.floorOffset;

                for (int i = 0; i < count; i++)
                {
                    // Across the line, from its neighbours either side.
                    Vector3 before = points[Mathf.Max(i - 1, 0)].Position;
                    Vector3 after = points[Mathf.Min(i + 1, count - 1)].Position;
                    Vector3 along = after - before;
                    along.y = 0f;

                    Vector3 across = along.sqrMagnitude > 0.000001f
                        ? Vector3.Cross(Vector3.up, along.normalized)
                        : Vector3.right;

                    float age = now - points[i].Born;
                    float scale = age <= settings.linger ? 1f : 1f - (age - settings.linger) / settings.fadeTime;
                    float half = settings.width * 0.5f * Mathf.Clamp01(scale);

                    Vector3 centre = points[i].Position + lift;
                    vertices.Add(centre - across * half);
                    vertices.Add(centre + across * half);

                    if (i > 0)
                    {
                        int v = i * 2;
                        triangles.Add(v - 2);
                        triangles.Add(v - 1);
                        triangles.Add(v);
                        triangles.Add(v - 1);
                        triangles.Add(v + 1);
                        triangles.Add(v);
                    }
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
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
