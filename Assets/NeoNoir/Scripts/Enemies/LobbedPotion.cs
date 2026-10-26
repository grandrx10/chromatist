using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A thrown potion: arcs from the hand to a spot on the floor, hurting
    /// the player it hits on the way, and shatters into a
    /// <see cref="PotionPool"/> wherever it comes down.
    /// </summary>
    /// <remarks>
    /// Added at runtime to whatever art the potion should look like, by
    /// <see cref="PotionLobPattern"/>, so the prefab stays pure art. Any
    /// colliders on it are removed: like <see cref="EnemyProjectile"/>, it
    /// moves by its transform and tests the path it covers each frame with a
    /// sphere cast.
    ///
    /// The arc is fixed from the throw: it does not track the player. It
    /// takes <see cref="Settings.flightTime"/> seconds whatever the distance,
    /// peaking <see cref="Settings.arcHeight"/> above the straight line from
    /// hand to landing spot.
    ///
    /// It shatters on the first thing it touches -- the player, a wall, the
    /// floor -- or on reaching the landing spot, and the pool spreads on the
    /// floor beneath. The player it hits takes <see cref="Settings.damage"/>,
    /// and the pool too if they stay in it. A <see cref="ParryBarrier"/>
    /// catches it before anything else: it shatters harmlessly in the air,
    /// with no pool.
    ///
    /// Colliders belonging to the thrower are ignored, so it never shatters
    /// on the hand that let it go.
    /// </remarks>
    public sealed class LobbedPotion : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Seconds from the hand to the landing spot, whatever the distance.")]
            [Min(0.05f)] public float flightTime = 1f;

            [Tooltip("How far the arc rises above the straight line from hand to landing spot, at its peak.")]
            [Min(0f)] public float arcHeight = 4f;

            [Tooltip("Damage to the player the potion itself hits. The pool's damage is separate.")]
            [Min(0f)] public float damage = 12f;

            [Tooltip("Size of the potion for hits and parries. Match it roughly to the model.")]
            [Min(0.01f)] public float radius = 0.4f;

            [Tooltip("Degrees per second the potion tumbles in flight.")]
            public float tumbleSpeed = 540f;

            [Tooltip("What the potion shatters on. Leave the Enemy layer out. Trigger colliders are always ignored.")]
            public LayerMask hitMask = ~0;

            [Tooltip("What counts as floor for the pool beneath a potion that shatters on the player or a wall.")]
            public LayerMask groundMask = 1;
        }

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        private Settings settings;
        private PotionPool.Settings pool;
        private PotionShatter.Settings shatter;
        private Transform thrower;

        private Vector3 start;
        private Vector3 end;
        private float age;
        private Vector3 tumbleAxis;
        private bool done;

        /// <summary>
        /// Throws <paramref name="art"/> from where it stands to
        /// <paramref name="landing"/>, a point on the floor.
        /// <paramref name="owner"/> is the thrower, whose colliders it passes
        /// through.
        /// </summary>
        public static LobbedPotion Throw(GameObject art, Vector3 landing, Transform owner,
                                         Settings settings, PotionPool.Settings pool, PotionShatter.Settings shatter)
        {
            foreach (Collider part in art.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            LobbedPotion potion = art.AddComponent<LobbedPotion>();
            potion.settings = settings;
            potion.pool = pool;
            potion.shatter = shatter;
            potion.thrower = owner;
            potion.start = art.transform.position;
            potion.end = landing;
            potion.tumbleAxis = UnityEngine.Random.onUnitSphere;
            return potion;
        }

        /// <summary>Where the arc is <paramref name="t"/> of the way along, 0 to 1.</summary>
        private Vector3 PointAt(float t)
        {
            return Vector3.Lerp(start, end, t) + Vector3.up * (settings.arcHeight * 4f * t * (1f - t));
        }

        private void Update()
        {
            if (done || settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 from = transform.position;

            age += deltaTime;
            float t = Mathf.Clamp01(age / settings.flightTime);
            Vector3 to = PointAt(t);
            Vector3 velocity = deltaTime > 0f ? (to - from) / deltaTime : Vector3.zero;

            if (ParryBarrier.TryCatch(from, to, settings.radius, velocity))
            {
                Shatter(from, false);
                return;
            }

            if (Cast(from, to, out RaycastHit hit))
            {
                PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(settings.damage, from);
                }

                Vector3 point = from + (to - from).normalized * hit.distance;
                Shatter(point, true);
                return;
            }

            transform.position = to;
            transform.Rotate(tumbleAxis, settings.tumbleSpeed * deltaTime, Space.World);

            if (t >= 1f)
            {
                Shatter(end, true);
            }
        }

        /// <summary>The nearest thing the potion touches between the two points, the thrower aside.</summary>
        private bool Cast(Vector3 from, Vector3 to, out RaycastHit nearest)
        {
            nearest = default;
            Vector3 path = to - from;
            float distance = path.magnitude;

            if (distance <= 0f)
            {
                return false;
            }

            int count = Physics.SphereCastNonAlloc(from, settings.radius, path / distance, hits, distance,
                                                   settings.hitMask, QueryTriggerInteraction.Ignore);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                // Overlapping at the start of the cast: the hand it is still
                // leaving, or something it is already inside. Neither is new.
                if (hits[i].distance <= 0f)
                {
                    continue;
                }

                if (thrower != null && hits[i].collider.transform.IsChildOf(thrower))
                {
                    continue;
                }

                if (!found || hits[i].distance < nearest.distance)
                {
                    nearest = hits[i];
                    found = true;
                }
            }

            return found;
        }

        private void Shatter(Vector3 point, bool spill)
        {
            done = true;
            Vector3 floor = FloorBelow(point);

            PotionShatter.Create(point, floor.y, shatter, pool.colour);

            if (spill)
            {
                PotionPool.Create(floor, pool);
            }

            Destroy(gameObject);
        }

        private Vector3 FloorBelow(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 20f,
                                settings.groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            // Nothing below: the landing spot's own height.
            return new Vector3(point.x, end.y, point.z);
        }

        private void OnDrawGizmos()
        {
            if (settings == null || done)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Vector3 last = start;

            for (int i = 1; i <= 16; i++)
            {
                Vector3 next = PointAt(i / 16f);
                Gizmos.DrawLine(last, next);
                last = next;
            }
        }
    }
}
