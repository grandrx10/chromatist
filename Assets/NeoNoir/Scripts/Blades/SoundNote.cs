using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of Blue's music notes in flight: drifts in a straight line,
    /// bobbing and rocking as it goes, until it hits an enemy or a wall.
    /// </summary>
    /// <remarks>
    /// Added to the note prefab's instance when it is fired, so the prefab
    /// itself needs nothing but its look.
    ///
    /// Moved by hand and swept with a sphere cast each frame, not by physics,
    /// the same as Lavender's arrows: what stops it is any collider on an
    /// enemy, and any collider on one of the obstacle layers. The bob is
    /// purely visual and applied to the model only; the sweep runs along the
    /// straight line of flight, so a note never dodges what it was aimed at.
    ///
    /// No slash effect on a hit. The note pops and the banjo is told, through
    /// the callback, so it can mark the enemy.
    ///
    /// Notes are modelled flat, so by default they turn to face the camera,
    /// like a sprite. <see cref="Settings.modelRotation"/> corrects a model
    /// that was not built facing -Z.
    /// </remarks>
    public sealed class SoundNote : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Min(0f)] public float damage = 10f;

            [Tooltip("Units per second. Deliberately slow.")]
            [Min(0.5f)] public float speed = 9f;

            [Tooltip("Furthest the note flies before it fades away.")]
            [Min(0.5f)] public float range = 18f;

            [Tooltip("Radius of the note's hit sweep. A little fatter than the note makes it forgiving.")]
            [Min(0.01f)] public float radius = 0.3f;

            [Tooltip("Uniform scale applied to the note prefab.")]
            [Min(0.01f)] public float scale = 1f;

            [Tooltip("Height the note bobs up and down by as it flies.")]
            [Min(0f)] public float bobHeight = 0.12f;

            [Tooltip("Bobs per second.")]
            [Min(0f)] public float bobRate = 2.5f;

            [Tooltip("Degrees the note rocks side to side as it flies.")]
            [Range(0f, 45f)] public float rock = 15f;

            [Tooltip("Seconds the note takes to pop when it hits something or runs out of range.")]
            [Min(0.01f)] public float popTime = 0.12f;

            [Tooltip("Turn notes to face the camera. Off leaves them facing their direction of flight.")]
            public bool faceCamera = true;

            [Tooltip("Degrees to turn the note model by, if it does not face -Z (toward the viewer) as modelled.")]
            public Vector3 modelRotation;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private Settings settings;
        private LayerMask obstacles;
        private Action<EnemyHealth, Vector3> onHit;
        private Blade source;

        private Vector3 position;
        private Vector3 direction;
        private Vector3 baseScale;
        private float travelled;
        private float age;
        private float phase;

        private bool stopped;
        private float popClock;

        /// <summary>
        /// Fires the note from <paramref name="origin"/> along
        /// <paramref name="flightDirection"/>. <paramref name="hit"/> is called
        /// after the note has dealt its damage to an enemy that survived it.
        /// <paramref name="shooter"/> is the blade that played it, told if it
        /// kills.
        /// </summary>
        public void Launch(Vector3 origin, Vector3 flightDirection, Settings shot, LayerMask obstacleLayers,
                           Action<EnemyHealth, Vector3> hit, Blade shooter = null)
        {
            settings = shot;
            obstacles = obstacleLayers;
            onHit = hit;
            source = shooter;

            position = origin;
            direction = flightDirection.normalized;
            baseScale = transform.localScale * settings.scale;
            phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            foreach (Collider part in GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>())
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            Pose();
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (stopped)
            {
                popClock += deltaTime;

                if (popClock >= settings.popTime)
                {
                    Destroy(gameObject);
                    return;
                }

                Pose();
                return;
            }

            float step = Mathf.Min(settings.speed * deltaTime, settings.range - travelled);

            if (step <= 0f)
            {
                Stop();
                return;
            }

            if (TryHit(step, out RaycastHit hit, out EnemyHealth enemy))
            {
                position += direction * hit.distance;
                travelled += hit.distance;

                if (enemy != null)
                {
                    Strike(enemy, hit);
                }

                Stop();
                return;
            }

            position += direction * step;
            travelled += step;

            Pose();
        }

        private void Stop()
        {
            stopped = true;
            popClock = 0f;
            Pose();
        }

        /// <summary>
        /// Places the model: on the line of flight plus its bob, rocking, and
        /// while popping, swelling briefly then shrinking away to nothing.
        /// </summary>
        private void Pose()
        {
            float bob = Mathf.Sin(phase + age * settings.bobRate * Mathf.PI * 2f);
            float rock = Mathf.Sin(phase + age * settings.bobRate * Mathf.PI) * settings.rock;

            Quaternion facing;
            Camera camera = Camera.main;

            if (settings.faceCamera && camera != null)
            {
                facing = camera.transform.rotation;
            }
            else
            {
                facing = Quaternion.LookRotation(direction, Vector3.up);
            }

            // Rocks about the axis toward the viewer, so it tilts like a
            // sprite rather than turning edge on.
            facing = facing * Quaternion.Euler(0f, 0f, rock) * Quaternion.Euler(settings.modelRotation);

            float size = 1f;

            if (stopped)
            {
                float t = Mathf.Clamp01(popClock / settings.popTime);
                size = t < 0.3f ? Mathf.Lerp(1f, 1.35f, t / 0.3f) : Mathf.Lerp(1.35f, 0f, (t - 0.3f) / 0.7f);
            }

            transform.SetPositionAndRotation(position + Vector3.up * (bob * settings.bobHeight), facing);
            transform.localScale = baseScale * size;
        }

        /// <summary>The nearest thing along this step that stops the note, if any.</summary>
        private bool TryHit(float step, out RaycastHit nearest, out EnemyHealth enemy)
        {
            nearest = default;
            enemy = null;

            int count = Physics.SphereCastNonAlloc(position, settings.radius, direction, Hits, step,
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
                bool alive = struck != null && !struck.IsDead;
                bool blocks = alive || (obstacles.value & (1 << candidate.collider.gameObject.layer)) != 0;

                if (!blocks)
                {
                    continue;
                }

                best = candidate.distance;
                nearest = candidate;
                enemy = alive ? struck : null;
            }

            return best < float.MaxValue;
        }

        private void Strike(EnemyHealth enemy, RaycastHit hit)
        {
            // A sweep that starts already overlapping reports no point.
            Vector3 point = hit.distance > 0f ? hit.point : position;

            enemy.TakeDamage(settings.damage, source, position);

            // Damage first: a killing note has nothing left to mark.
            if (!enemy.IsDead)
            {
                onHit?.Invoke(enemy, point);
            }
        }
    }
}
