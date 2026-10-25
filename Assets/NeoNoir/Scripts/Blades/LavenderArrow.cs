using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of Lavender's arrows in flight: travels in a straight line until
    /// it hits an enemy or a wall, and marks any enemy it hits with a
    /// vulnerability.
    /// </summary>
    /// <remarks>
    /// Added to the arrow prefab's instance when it is loosed, so the prefab
    /// itself needs nothing but its look.
    ///
    /// Moved by hand and swept with a sphere cast each frame, not by physics:
    /// an arrow moves much further per frame than it is thick, and a trigger
    /// or rigidbody would pass straight through thin enemies.
    ///
    /// What stops it: any collider on an enemy, and any collider on one of
    /// the obstacle layers (Default unless the bow says otherwise). Anything
    /// else -- the player, triggers, other layers -- it flies straight past.
    ///
    /// On an enemy it deals its damage first and applies its vulnerability
    /// after, so the arrow that marks an enemy is not itself boosted by the
    /// mark.
    ///
    /// Trailed by a streak of lavender with a white core, tapering from the
    /// back of the arrow to a point. Arrows fly dead straight, so the streak
    /// is simply drawn back along the line of flight rather than recorded.
    /// When the arrow stops, the arrow disappears but the streak draws in
    /// after it into the point of impact, so a hit never cuts it off.
    /// </remarks>
    public sealed class LavenderArrow : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Min(0f)] public float damage = 8f;

            [Tooltip("Units per second.")]
            [Min(0.5f)] public float speed = 30f;

            [Tooltip("Furthest the arrow flies before it disappears.")]
            [Min(0.5f)] public float range = 30f;

            [Tooltip("Radius of the arrow's hit sweep. A little fatter than the arrow makes it forgiving.")]
            [Min(0.01f)] public float radius = 0.15f;

            [Tooltip("Damage multiplier the enemy takes, from every source, while the debuff lasts.")]
            [Min(1f)] public float vulnerability = 1.25f;

            [Tooltip("Seconds the debuff lasts.")]
            [Min(0f)] public float vulnerabilityDuration = 2f;
        }

        [Serializable]
        public sealed class Trail
        {
            [Tooltip("Length of the streak behind the arrow. 0 for none.")]
            [Min(0f)] public float length = 2.5f;

            [Tooltip("Width of the streak where it leaves the arrow.")]
            [Min(0.01f)] public float width = 0.14f;

            [Tooltip("Width of the white core, as a fraction of the streak's.")]
            [Range(0f, 1f)] public float core = 0.35f;

            [Tooltip("Seconds the streak takes to draw in after the arrow hits something.")]
            [Min(0.01f)] public float fadeTime = 0.12f;
        }

        // Pieces along the streak, so its taper reads as a curve.
        private const int TrailSegments = 8;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private Settings settings;
        private Trail trail;
        private LayerMask obstacles;
        private LavenderBubbles.Settings bubbles;
        private Material bubbleMaterial;
        private Color hitColour;
        private Vector3 direction;
        private float tailOffset;
        private float travelled;

        private bool stopped;
        private float fadeClock;

        private GameObject trailObject;
        private Mesh trailMesh;
        private readonly System.Collections.Generic.List<Vector3> vertices = new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<Vector2> uvs = new System.Collections.Generic.List<Vector2>();
        private readonly System.Collections.Generic.List<int> triangles = new System.Collections.Generic.List<int>();

        /// <summary>
        /// Looses the arrow from where it is, along <paramref name="flightDirection"/>.
        /// Anything on <paramref name="obstacleLayers"/> stops it, as does any enemy.
        /// <paramref name="tail"/> is how far behind its pivot the arrow's back
        /// end is, where the trail starts.
        /// </summary>
        public void Launch(Vector3 flightDirection, Settings shot, Trail streak, float tail, LayerMask obstacleLayers,
                           LavenderBubbles.Settings debuffBubbles, Material effectMaterial, Color colour)
        {
            settings = shot;
            trail = streak;
            tailOffset = tail;
            obstacles = obstacleLayers;
            bubbles = debuffBubbles;
            bubbleMaterial = effectMaterial;
            hitColour = colour;
            direction = flightDirection.normalized;
            travelled = 0f;

            BuildTrail(effectMaterial);
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            if (stopped)
            {
                // Hidden, but kept until the streak has drawn in after it.
                fadeClock += Time.deltaTime;

                if (trail == null || fadeClock >= trail.fadeTime)
                {
                    Destroy(gameObject);
                    return;
                }

                UpdateTrail();
                return;
            }

            float step = Mathf.Min(settings.speed * Time.deltaTime, settings.range - travelled);

            if (step <= 0f)
            {
                Stop();
                return;
            }

            if (TryHit(step, out RaycastHit hit, out EnemyHealth enemy))
            {
                transform.position += direction * hit.distance;
                travelled += hit.distance;

                if (enemy != null)
                {
                    Strike(enemy, hit);
                }

                Stop();
                return;
            }

            transform.position += direction * step;
            travelled += step;

            UpdateTrail();
        }

        /// <summary>Ends the flight: the arrow vanishes, and its trail draws in after it.</summary>
        private void Stop()
        {
            stopped = true;
            fadeClock = 0f;

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                part.enabled = false;
            }

            UpdateTrail();
        }

        // ---- trail --------------------------------------------------------

        private void BuildTrail(Material material)
        {
            if (trail == null || trail.length <= 0f)
            {
                return;
            }

            // Its own object, unparented, so the arrow's scale never touches
            // it and hiding the arrow leaves it showing.
            trailObject = new GameObject("ArrowTrail");

            trailMesh = new Mesh { name = "ArrowTrail" };
            trailMesh.MarkDynamic();

            trailObject.AddComponent<MeshFilter>().sharedMesh = trailMesh;

            MeshRenderer trailRenderer = trailObject.AddComponent<MeshRenderer>();
            trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trailRenderer.receiveShadows = false;

            if (material != null)
            {
                trailRenderer.sharedMaterial = material;
            }

            UpdateTrail();
        }

        /// <summary>
        /// Rebuilds the streak back along the line of flight from the back of
        /// the arrow: never longer than the arrow has flown, so it grows out
        /// of the bow, and shrinking toward the front once the arrow stops.
        /// </summary>
        private void UpdateTrail()
        {
            if (trailMesh == null)
            {
                return;
            }

            vertices.Clear();
            uvs.Clear();
            triangles.Clear();

            float length = Mathf.Min(trail.length, travelled);

            if (stopped)
            {
                float closing = Mathf.Clamp01(fadeClock / trail.fadeTime);
                length *= 1f - closing * closing;
            }

            Camera camera = Camera.main;

            if (length > 0.0001f && camera != null)
            {
                Vector3 front = transform.position - direction * tailOffset;
                Vector3 back = front - direction * length;
                Vector3 side = Vector3.Cross(direction, -camera.transform.forward);
                side = side.sqrMagnitude > 0.000001f ? side.normalized : Vector3.Cross(direction, Vector3.up).normalized;

                // Lavender first, then the white core over it.
                AddStreak(back, front, side, trail.width, 2f);
                AddStreak(back, front, side, trail.width * trail.core, 1f);
            }

            trailMesh.Clear();
            trailMesh.SetVertices(vertices);
            trailMesh.SetUVs(0, uvs);
            trailMesh.SetTriangles(triangles, 0);
            trailMesh.RecalculateBounds();
        }

        /// <summary>
        /// A ribbon from a point at <paramref name="back"/> widening to
        /// <paramref name="width"/> at <paramref name="front"/>. Its shade comes
        /// from NeoNoir/Tear by <paramref name="kind"/>: 2 the solid colour,
        /// 1 the outline colour.
        /// </summary>
        private void AddStreak(Vector3 back, Vector3 front, Vector3 side, float width, float kind)
        {
            if (width <= 0f)
            {
                return;
            }

            int first = vertices.Count;

            for (int i = 0; i <= TrailSegments; i++)
            {
                float t = i / (float)TrailSegments;

                // Swells quickly out of the point, then holds near full width
                // up to the arrow.
                float half = width * 0.5f * Mathf.Sqrt(t);
                Vector3 centre = Vector3.Lerp(back, front, t);

                vertices.Add(centre + side * half);
                vertices.Add(centre - side * half);
                uvs.Add(new Vector2(kind, 0f));
                uvs.Add(new Vector2(kind, 0f));
            }

            for (int i = 0; i < TrailSegments; i++)
            {
                int at = first + i * 2;
                ScarlettClaw.AddQuad(triangles, at, at + 1, at + 2, at + 3);
            }
        }

        private void OnDestroy()
        {
            if (trailObject != null)
            {
                Destroy(trailObject);
            }

            if (trailMesh != null)
            {
                Destroy(trailMesh);
            }
        }

        /// <summary>The nearest thing along this step that stops the arrow, if any.</summary>
        private bool TryHit(float step, out RaycastHit nearest, out EnemyHealth enemy)
        {
            nearest = default;
            enemy = null;

            int count = Physics.SphereCastNonAlloc(transform.position, settings.radius, direction, Hits, step,
                                                   ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = Hits[i];

                if (candidate.distance >= best)
                {
                    continue;
                }

                EnemyHealth struck = candidate.collider.GetComponentInParent<EnemyHealth>();
                bool blocks = struck != null && !struck.IsDead
                              || (obstacles.value & (1 << candidate.collider.gameObject.layer)) != 0;

                if (!blocks)
                {
                    continue;
                }

                best = candidate.distance;
                nearest = candidate;
                enemy = struck != null && !struck.IsDead ? struck : null;
            }

            return best < float.MaxValue;
        }

        private void Strike(EnemyHealth enemy, RaycastHit hit)
        {
            // A sweep that starts already overlapping reports no point.
            Vector3 point = hit.distance > 0f ? hit.point : transform.position;

            enemy.TakeDamage(settings.damage, from: transform.position);

            if (SlashEffect.Current != null)
            {
                SlashEffect.Current.TriggerSlash(point, direction, 1f, hitColour);
            }

            // Damage first: a killing arrow has nothing left to mark.
            if (!enemy.IsDead && enemy.ApplyVulnerability(settings.vulnerability, settings.vulnerabilityDuration))
            {
                LavenderBubbles.Show(enemy, bubbles, bubbleMaterial);
            }
        }
    }
}
