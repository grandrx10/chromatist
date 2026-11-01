using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A column cut free by the Katana: held aloft by the boss, then thrown.
    /// It has health of its own, and the player can cut it apart before it
    /// reaches them.
    /// </summary>
    /// <remarks>
    /// Made by <see cref="KatanaColumnThrowPattern"/> from what
    /// <see cref="CuttableColumn.CutOff"/> hands back. While held, the attack
    /// poses it every frame through <see cref="Hold"/>. Once
    /// <see cref="Throw"/>n it flies straight on by itself, rolling about its
    /// length.
    ///
    /// It is on the Enemy layer, with an <see cref="EnemyHealth"/>, so the
    /// player's blades hit it and homing ones seek it out. It passes through
    /// the boss and the player. If it touches the player it hurts them once
    /// and breaks. At zero health, out of time, or left held by an attack
    /// that has stopped, it is cut to pieces with the Katana's own
    /// <see cref="KatanaCut"/>, which keep its speed.
    /// </remarks>
    public sealed class ThrownColumn : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Damage the column takes before it is cut apart.")]
            [Min(1f)] public float health = 40f;

            [Tooltip("Damage to the player if it reaches them.")]
            [Min(0f)] public float contactDamage = 20f;

            [Tooltip("How far past the column's own thickness it reaches the player.")]
            [Min(0f)] public float contactPadding = 0.2f;

            [Tooltip("Seconds it flies before breaking apart by itself.")]
            [Min(0.1f)] public float maxFlightTime = 3f;

            [Tooltip("Degrees per second it rolls about its length in flight.")]
            public float roll = 360f;

            [Tooltip("How far below the column it can still be hit, so shots flying low across the floor catch it while it floats overhead. 0 leaves only the column itself.")]
            [Min(0f)] public float hitReachDown = 4f;

            [Tooltip("Width of that reach, across.")]
            [Min(0.05f)] public float hitReachWidth = 1.2f;
        }

        private static readonly Collider[] touching = new Collider[16];

        private Settings settings;
        private KatanaCut.Settings cut;
        private EnemyHealth health;
        private BoxCollider box;
        private Vector3 velocity;
        private bool thrown;
        private float flightClock;
        private float lastHeld;
        private bool broken;

        private Vector3 cutScale;
        private bool shrinking;
        private Vector3 shrinkFrom;
        private Vector3 shrinkTo;
        private float shrinkTime;
        private float shrinkClock;

        private TrailRenderer trail;

        private GameObject reach;
        private BoxCollider reachBox;

        /// <summary>Its length along its local Y axis, as cut.</summary>
        public float Length => box.size.y;

        /// <summary>True once thrown.</summary>
        public bool Thrown => thrown;

        /// <summary>True once it has broken apart, by any means.</summary>
        public bool Broken => broken;

        /// <summary>
        /// Turns <paramref name="piece"/>, a freshly cut column top, into a
        /// thrown column belonging to <paramref name="owner"/>, held where it is.
        /// </summary>
        public static ThrownColumn Create(GameObject piece, Enemy owner, Settings settings, KatanaCut.Settings cut)
        {
            int layer = LayerMask.NameToLayer("Enemy");

            if (layer >= 0)
            {
                piece.layer = layer;
            }

            ThrownColumn column = piece.AddComponent<ThrownColumn>();
            column.Begin(owner, settings, cut);
            return column;
        }

        private void Begin(Enemy owner, Settings columnSettings, KatanaCut.Settings cutSettings)
        {
            settings = columnSettings;
            cut = cutSettings;
            lastHeld = Time.time;
            cutScale = transform.localScale;

            MeshFilter filter = GetComponent<MeshFilter>();
            box = gameObject.AddComponent<BoxCollider>();

            if (filter != null && filter.sharedMesh != null)
            {
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }

            // Moved by its transform; a kinematic body keeps the physics
            // engine from treating the collider as a static one.
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            health = gameObject.AddComponent<EnemyHealth>();
            health.SetMaxHealth(settings.health);
            health.DestroyOnDeath = false;
            health.Unmarked = true;
            health.Died += OnDied;

            if (settings.hitReachDown > 0f)
            {
                BuildReach();
            }

            if (owner != null)
            {
                IgnoreCollisions(owner.transform);

                if (owner.HasTarget)
                {
                    IgnoreCollisions(owner.Target.root);
                }
            }
        }

        /// <summary>
        /// An invisible upright box hanging from the column down toward the
        /// floor. Anything striking it strikes the column: it carries a
        /// health of its own that hands every blow on, and is otherwise not
        /// there, so homing and marks never pick it out. Kept apart from the
        /// column, so it stays upright and full size however the column
        /// tumbles and shrinks.
        /// </summary>
        private void BuildReach()
        {
            reach = new GameObject($"{name} (hit reach)");
            reach.layer = gameObject.layer;

            reachBox = reach.AddComponent<BoxCollider>();

            Rigidbody body = reach.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            EnemyHealth handOn = reach.AddComponent<EnemyHealth>();
            handOn.Intangible = true;
            handOn.Unmarked = true;
            handOn.DestroyOnDeath = false;
            handOn.RedirectTo = health;

            Physics.IgnoreCollision(box, reachBox);
            PlaceReach();
        }

        /// <summary>Sizes the reach to hang from the column as it is now.</summary>
        private void PlaceReach()
        {
            if (reach == null)
            {
                return;
            }

            Bounds body = box.bounds;
            float top = body.max.y;
            float bottom = body.min.y - settings.hitReachDown;
            float width = Mathf.Max(settings.hitReachWidth, Mathf.Max(body.size.x, body.size.z));

            reach.transform.SetPositionAndRotation(new Vector3(body.center.x, (top + bottom) * 0.5f, body.center.z), Quaternion.identity);
            reachBox.size = new Vector3(width, top - bottom, width);
        }

        private void LateUpdate()
        {
            // After whatever moved the column this frame, held or flying.
            PlaceReach();
        }

        private void IgnoreCollisions(Transform other)
        {
            foreach (Collider part in other.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(box, part);

                if (reachBox != null)
                {
                    Physics.IgnoreCollision(reachBox, part);
                }
            }
        }

        /// <summary>
        /// Poses the column while it is held. <paramref name="heldVelocity"/>
        /// is how fast it is being carried, which its pieces keep should it be
        /// cut apart now.
        /// </summary>
        public void Hold(Vector3 position, Quaternion rotation, Vector3 heldVelocity)
        {
            if (broken || thrown)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            velocity = heldVelocity;
            lastHeld = Time.time;
        }

        /// <summary>Lets it go at <paramref name="throwVelocity"/>; it flies straight on from here.</summary>
        public void Throw(Vector3 throwVelocity)
        {
            if (broken)
            {
                return;
            }

            thrown = true;
            velocity = throwVelocity;
            flightClock = 0f;
        }

        /// <summary>
        /// Shrinks it, from its size now, to <paramref name="scale"/> of its
        /// size as cut, over <paramref name="seconds"/>.
        /// </summary>
        public void ShrinkTo(float scale, float seconds)
        {
            shrinkFrom = transform.localScale;
            shrinkTo = cutScale * Mathf.Max(0.001f, scale);
            shrinkTime = Mathf.Max(0f, seconds);
            shrinkClock = 0f;
            shrinking = true;
        }

        /// <summary>
        /// Gives it a thin trail of <paramref name="colour"/>,
        /// <paramref name="width"/> wide at the chunk and tapering away over
        /// <paramref name="time"/> seconds behind it.
        /// </summary>
        public void AddTrail(Color colour, float width, float time)
        {
            Material material = FlatMaterial.DoubleSided;

            if (material == null || width <= 0f || time <= 0f)
            {
                return;
            }

            GameObject host = new GameObject("Trail");
            host.transform.SetParent(transform, false);

            trail = host.AddComponent<TrailRenderer>();
            trail.time = time;
            trail.widthMultiplier = width;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = material;
            FlatMaterial.Tint(trail, colour, new MaterialPropertyBlock());
        }

        private void Update()
        {
            if (broken)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (shrinking)
            {
                shrinkClock += deltaTime;
                float t = shrinkTime > 0f ? Mathf.Clamp01(shrinkClock / shrinkTime) : 1f;
                transform.localScale = Vector3.Lerp(shrinkFrom, shrinkTo, t * t * (3f - 2f * t));
                shrinking = t < 1f;
            }

            if (!thrown)
            {
                // Its attack stopped holding it -- the boss died, or the run ended.
                if (Time.time - lastHeld > 0.25f)
                {
                    Break(transform.position - velocity);
                }

                return;
            }

            flightClock += deltaTime;
            transform.position += velocity * deltaTime;

            // Rolled about its length, which stays along its flight.
            transform.Rotate(Vector3.up, settings.roll * deltaTime, Space.Self);

            if (Touch())
            {
                return;
            }

            if (flightClock >= settings.maxFlightTime)
            {
                Break(transform.position - velocity);
            }
        }

        /// <summary>Hurts the player and breaks if it has reached them.</summary>
        private bool Touch()
        {
            // Measured at its size now, shrunk or not.
            Vector3 scale = transform.lossyScale;
            Vector3 half = transform.up * (box.size.y * scale.y * 0.5f);
            Vector3 centre = transform.TransformPoint(box.center);
            float radius = Mathf.Max(box.size.x * scale.x, box.size.z * scale.z) * 0.5f + settings.contactPadding;

            int count = Physics.OverlapCapsuleNonAlloc(centre - half, centre + half, radius, touching,
                                                       ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth player = touching[i].GetComponentInParent<PlayerHealth>();

                if (player == null)
                {
                    continue;
                }

                // Dashed through, unhurt: it flies on.
                if (settings.contactDamage > 0f && player.TakeDamage(settings.contactDamage, centre) <= 0f)
                {
                    return false;
                }

                // Shattered against them, so the pieces fly on past.
                Break(centre - velocity);
                return true;
            }

            return false;
        }

        private void OnDied(EnemyHealth died)
        {
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
            Break(player != null ? player.transform.position : transform.position - velocity);
        }

        /// <summary>Cuts it to pieces as a blow from <paramref name="from"/>, and it is gone.</summary>
        public void Break(Vector3 from)
        {
            if (broken)
            {
                return;
            }

            broken = true;

            // Left behind to fade out where it is, rather than vanishing.
            if (trail != null)
            {
                trail.transform.SetParent(null, true);
                trail.emitting = false;
                Destroy(trail.gameObject, trail.time);
            }

            KatanaCut.Cut(gameObject, cut, from, velocity);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }

            if (reach != null)
            {
                Destroy(reach);
            }
        }
    }
}
