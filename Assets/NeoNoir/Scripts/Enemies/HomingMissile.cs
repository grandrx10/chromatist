using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A homing missile: bursts out in its own direction, snaps round toward
    /// the player, then chases them with a slower turn they can outrun.
    /// </summary>
    /// <remarks>
    /// In order, from launch:
    ///   1. Scatter: flies straight along its launch direction at
    ///      <see cref="Settings.scatterSpeed"/> for
    ///      <see cref="Settings.scatterTime"/> seconds.
    ///   2. Lock: turns toward the player at
    ///      <see cref="Settings.lockTurnRate"/> for
    ///      <see cref="Settings.lockTime"/> seconds -- fast enough to swing
    ///      most of the way round -- while speeding up to
    ///      <see cref="Settings.speed"/>.
    ///   3. Track: keeps turning toward them, but only at
    ///      <see cref="Settings.trackTurnRate"/>, so a player who moves
    ///      sideways fast enough makes it miss and overshoot.
    /// It aims at the middle of the player's body, so it rises or dips to
    /// their height as it turns.
    ///
    /// Like <see cref="EnemyProjectile"/>, it moves by its transform and
    /// tests the path it covers each frame with a sphere cast, needing no
    /// rigidbody. A <see cref="ParryBarrier"/> is checked first and catches
    /// it harmlessly. The player it hits takes <see cref="Settings.damage"/>;
    /// anything else, a wall or the floor, makes it dissipate. Either way it
    /// goes in a small <see cref="EnergyBurst"/>, as it does at the end of
    /// <see cref="Settings.lifetime"/>. Colliders belonging to whoever fired
    /// it are ignored.
    ///
    /// Added at runtime by <see cref="SceptorAttackPattern"/>, to the art
    /// given or a plain glowing ball. Any colliders on the art are removed.
    /// Its trail is left behind when it goes, to shrink away rather than
    /// vanish; a trail already on the art is used as it is.
    /// </remarks>
    public sealed class HomingMissile : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Flight")]
            [Tooltip("Speed during the scatter.")]
            [Min(0f)] public float scatterSpeed = 10f;

            [Tooltip("Seconds it flies straight out before locking on.")]
            [Min(0f)] public float scatterTime = 0.15f;

            [Tooltip("Degrees per second it turns while locking on.")]
            [Min(0f)] public float lockTurnRate = 1200f;

            [Tooltip("Seconds of the fast turn.")]
            [Min(0f)] public float lockTime = 0.1f;

            [Tooltip("Degrees per second it turns while tracking, after the lock. Lower is easier to dodge.")]
            [Min(0f)] public float trackTurnRate = 150f;

            [Tooltip("Speed once locked on. Reached by the end of the lock.")]
            [Min(0f)] public float speed = 14f;

            [Tooltip("Seconds before it dissipates by itself.")]
            [Min(0.1f)] public float lifetime = 4f;

            [Header("Hits")]
            [Tooltip("Damage to the player it hits.")]
            [Min(0f)] public float damage = 8f;

            [Tooltip("Size of the missile for hits and parries.")]
            [Min(0.01f)] public float radius = 0.2f;

            [Tooltip("What stops the missile. Leave the Enemy layer out. Trigger colliders are always ignored.")]
            public LayerMask hitMask = ~0;

            [Header("Look")]
            [Tooltip("Size of the ball drawn when no missile art is given.")]
            [Min(0.01f)] public float size = 0.35f;

            [Tooltip("Colour of the ball and its trail. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Seconds of travel the trail covers. Longer is a longer tail.")]
            [Min(0.01f)] public float trailTime = 0.3f;

            [Tooltip("Width of the trail at the missile. It narrows to nothing at the tail.")]
            [Min(0.001f)] public float trailWidth = 0.3f;

            [Tooltip("Burst where it goes: on a hit, a wall, a parry, or running out of time.")]
            public EnergyBurst.Settings dissipate = new EnergyBurst.Settings
            {
                flashSize = 0.6f,
                flashTime = 0.12f,
                sparkCount = 6,
                sparkSize = 0.2f,
                sparkSpeed = new Vector2(3f, 6f),
                sparkLife = 0.25f,
            };
        }

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        private Settings settings;
        private Transform owner;
        private Transform target;
        private Collider targetBody;

        private Vector3 direction;
        private float currentSpeed;
        private float age;
        private TrailRenderer trail;
        private bool done;

        public Vector3 Velocity => direction * currentSpeed;

        /// <summary>
        /// Launches <paramref name="art"/> from where it stands along
        /// <paramref name="launchDirection"/>, to home in on
        /// <paramref name="homeOn"/>. <paramref name="firedBy"/>'s colliders
        /// are passed through. Null art makes a plain glowing ball.
        /// </summary>
        public static HomingMissile Launch(GameObject art, Vector3 position, Vector3 launchDirection,
                                           Transform homeOn, Transform firedBy, Settings settings)
        {
            if (art == null)
            {
                art = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                art.name = "HomingMissile";
                art.transform.localScale = Vector3.one * settings.size;

                MeshRenderer renderer = art.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.sharedMaterial = FlatMaterial.Shared;
                FlatMaterial.Tint(renderer, settings.colour, new MaterialPropertyBlock());
            }

            foreach (Collider part in art.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            art.transform.position = position;

            HomingMissile missile = art.AddComponent<HomingMissile>();
            missile.settings = settings;
            missile.owner = firedBy;
            missile.target = homeOn;
            missile.targetBody = homeOn != null ? homeOn.GetComponent<Collider>() : null;
            missile.direction = launchDirection.sqrMagnitude > 0.000001f ? launchDirection.normalized : Vector3.forward;
            missile.currentSpeed = settings.scatterSpeed;
            missile.transform.rotation = Quaternion.LookRotation(missile.direction);
            missile.trail = missile.GetComponentInChildren<TrailRenderer>();

            if (missile.trail == null)
            {
                missile.BuildTrail();
            }

            return missile;
        }

        private void BuildTrail()
        {
            GameObject host = new GameObject("Trail");
            host.transform.SetParent(transform, false);

            trail = host.AddComponent<TrailRenderer>();
            trail.time = settings.trailTime;
            trail.widthMultiplier = settings.trailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;

            if (trail.sharedMaterial != null)
            {
                FlatMaterial.Tint(trail, settings.colour, new MaterialPropertyBlock());
            }
        }

        private void Update()
        {
            if (done || settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= settings.lifetime)
            {
                Dissipate(transform.position);
                return;
            }

            Steer(deltaTime);

            Vector3 from = transform.position;
            Vector3 to = from + direction * (currentSpeed * deltaTime);

            if (ParryBarrier.TryCatch(from, to, settings.radius, Velocity))
            {
                Dissipate(from);
                return;
            }

            if (Cast(from, to, out RaycastHit hit))
            {
                PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(settings.damage, from);
                }

                Dissipate(from + direction * hit.distance);
                return;
            }

            transform.position = to;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        /// <summary>Turns and speeds up as the phase calls for.</summary>
        private void Steer(float deltaTime)
        {
            if (age < settings.scatterTime)
            {
                return;
            }

            bool locking = age < settings.scatterTime + settings.lockTime;
            float turnRate = locking ? settings.lockTurnRate : settings.trackTurnRate;

            if (target != null)
            {
                Vector3 aim = (targetBody != null ? targetBody.bounds.center : target.position) - transform.position;

                if (aim.sqrMagnitude > 0.0001f)
                {
                    direction = Vector3.RotateTowards(direction, aim.normalized,
                                                      turnRate * Mathf.Deg2Rad * deltaTime, 0f);
                }
            }

            // Up to full speed over the lock, so it leaves the turn at it.
            float rate = settings.lockTime > 0f
                ? Mathf.Abs(settings.speed - settings.scatterSpeed) / settings.lockTime
                : float.PositiveInfinity;
            currentSpeed = Mathf.MoveTowards(currentSpeed, settings.speed, rate * deltaTime);
        }

        /// <summary>The nearest thing the missile touches between the two points, its firer aside.</summary>
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
                if (owner != null && hits[i].collider.transform.IsChildOf(owner))
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

        private void Dissipate(Vector3 point)
        {
            done = true;
            EnergyBurst.Create(point, settings.dissipate);

            // Left in the world to shrink away; its points are already in
            // world space, so it stays where it was drawn.
            if (trail != null && trail.gameObject != gameObject)
            {
                trail.transform.SetParent(null, true);
                trail.emitting = false;
                Destroy(trail.gameObject, trail.time);
            }

            Destroy(gameObject);
        }
    }
}
