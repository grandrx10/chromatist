using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Lowers a puppet enemy from the sky on its strings, holds it limp for a
    /// moment after it lands, then lets it loose as an ordinary enemy.
    /// </summary>
    /// <remarks>
    /// A puppet placed in the scene by hand starts hidden where it stands:
    /// not drawn, no colliders, invulnerable, its strings put away. It stays
    /// that way until the player comes within the enemy's detection radius,
    /// then drops in. One the puppeteer spawns with <see cref="Spawn"/>, or
    /// is told to <see cref="DescendNow"/>, drops in at once instead.
    ///
    /// It starts <see cref="dropHeight"/> above where it was placed and is
    /// lowered over <see cref="descentTime"/>, fast at first and settling
    /// gently onto the floor. From the moment it appears it can be hurt, and
    /// killed. It plays <see cref="puppetIdleAnimation"/> all the way down
    /// and for <see cref="inactiveTime"/> after landing, held dormant so it
    /// neither turns, moves nor attacks. Then it is woken straight away,
    /// wherever the player is, and fights like any other enemy.
    ///
    /// It falls kinematic, so gravity and the enemy's own movement leave it
    /// alone on the way down; the body is put back as it was on landing.
    ///
    /// Goes on the enemy's root, beside <see cref="Enemy"/>. Pairs with
    /// <see cref="PuppetStrings"/>, which it hides while the puppet is.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    public sealed class PuppetDescent : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Animator state played while lowered and while limp after landing.")]
        private string puppetIdleAnimation = "PuppetIdle";

        [SerializeField]
        [Tooltip("How far above its landing spot the puppet starts.")]
        [Min(0.5f)]
        private float dropHeight = 10f;

        [SerializeField]
        [Tooltip("Seconds it takes to be lowered to the floor.")]
        [Min(0.05f)]
        private float descentTime = 1.2f;

        [SerializeField]
        [Tooltip("Seconds it hangs limp after landing before it wakes. It can be hurt meanwhile.")]
        [Min(0f)]
        private float inactiveTime = 0.5f;

        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> hiddenColliders = new List<Collider>();

        private Enemy enemy;
        private EnemyHealth health;
        private Rigidbody body;
        private PuppetStrings strings;
        private bool triggered;
        private bool hidden;
        private bool lifting;
        private bool handedOver;

        /// <summary>How fast a lifted puppet picks up speed, in units per second per second.</summary>
        private const float LiftAcceleration = 30f;

        /// <summary>Fastest a lifted puppet is hauled up, in units per second.</summary>
        private const float LiftMaxSpeed = 40f;

        /// <summary>Seconds after which a lifted puppet is removed even if somehow still in view.</summary>
        private const float LiftTimeout = 4f;

        private static readonly Plane[] frustum = new Plane[6];

        /// <summary>True while the puppet is hidden, waiting to drop in.</summary>
        public bool IsHidden => hidden;

        /// <summary>True once it has woken and is fighting.</summary>
        public bool IsDone { get; private set; }

        /// <summary>
        /// While true, the puppet stays limp after landing, however long that
        /// is, instead of waking once <see cref="inactiveTime"/> is up. For a
        /// cutscene, such as <see cref="PuppeteerIntro"/>'s dialogue, to hold
        /// it until the fight begins.
        /// </summary>
        public bool HoldAfterLanding { get; set; }

        /// <summary>
        /// Bounds of its solid colliders, as <see cref="EnemyHealth.BodyBounds"/>
        /// gives, but measured even while hidden, when they are switched off
        /// and would read as nothing. For placing a puppet before it drops in.
        /// </summary>
        public Bounds BodyBounds()
        {
            if (!hidden)
            {
                return health.BodyBounds();
            }

            // On only long enough to measure: physics never steps meanwhile.
            foreach (Collider part in hiddenColliders)
            {
                part.enabled = true;
            }

            Physics.SyncTransforms();
            Bounds bounds = health.BodyBounds();

            foreach (Collider part in hiddenColliders)
            {
                part.enabled = false;
            }

            return bounds;
        }

        /// <summary>
        /// Spawns <paramref name="prefab"/> to land at
        /// <paramref name="position"/>, dropping in at once. For the
        /// puppeteer to place its puppets with.
        /// </summary>
        public static Enemy Spawn(Enemy prefab, Vector3 position, Quaternion rotation)
        {
            Enemy unit = Instantiate(prefab, position, rotation);
            PuppetDescent descent = unit.GetComponent<PuppetDescent>();

            if (descent != null)
            {
                descent.DescendNow();
            }
            else
            {
                Debug.LogWarning($"[PuppetDescent] {prefab.name} has no PuppetDescent; it is spawned standing.", prefab);
            }

            return unit;
        }

        /// <summary>
        /// Hauls the puppet back up on its strings and away, limp, harming no
        /// one and unharmed, and removes it once out of sight. One still
        /// hidden, waiting to drop, is simply removed. For a phase change.
        /// </summary>
        public void LiftAway()
        {
            if (lifting || health.IsDead)
            {
                return;
            }

            lifting = true;
            StopAllCoroutines();

            if (hidden)
            {
                Destroy(gameObject);
                return;
            }

            // Its brain off stops any attack partway, and all movement.
            enemy.enabled = false;
            health.Invulnerable = true;

            if (body != null)
            {
                body.isKinematic = true;
            }

            foreach (Collider part in GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            StartPuppetIdle();
            StartCoroutine(Lift());
        }

        private IEnumerator Lift()
        {
            Renderer[] parts = GetComponentsInChildren<Renderer>();
            float speed = 0f;
            float clock = 0f;

            while (clock < LiftTimeout)
            {
                yield return null;

                float deltaTime = Time.deltaTime;
                clock += deltaTime;
                speed = Mathf.Min(LiftMaxSpeed, speed + LiftAcceleration * deltaTime);
                transform.position += Vector3.up * (speed * deltaTime);

                if (!InView(parts))
                {
                    break;
                }
            }

            Destroy(gameObject);
        }

        /// <summary>Whether any of <paramref name="parts"/> can still be seen. True without a camera, leaving it to the timeout.</summary>
        private static bool InView(Renderer[] parts)
        {
            Camera view = Camera.main;

            if (view == null)
            {
                return true;
            }

            GeometryUtility.CalculateFrustumPlanes(view, frustum);

            foreach (Renderer part in parts)
            {
                if (part != null && part.enabled && GeometryUtility.TestPlanesAABB(frustum, part.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Drops the puppet in now, without waiting for the player to come near.</summary>
        public void DescendNow()
        {
            triggered = true;
        }

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            health = GetComponent<EnemyHealth>();
            body = GetComponent<Rigidbody>();
            strings = GetComponent<PuppetStrings>();

            // Before its brain first checks for waking, a frame from now.
            enemy.StayDormant = true;
            Hide();
        }

        /// <summary>
        /// Hands the puppet over to something else that places and moves it
        /// itself, such as an <see cref="EyeRing"/>: it is shown at once, never
        /// dropped in by this, and left dormant.
        /// </summary>
        public void HandOver()
        {
            handedOver = true;
            StopAllCoroutines();

            if (hidden)
            {
                Show();
            }
        }

        private void Start()
        {
            if (!handedOver)
            {
                StartCoroutine(Run());
            }
        }

        private IEnumerator Run()
        {
            while (!triggered)
            {
                if (enemy.PlayerInRange())
                {
                    triggered = true;
                    break;
                }

                yield return null;
            }

            Vector3 landing = transform.position;
            Vector3 top = landing + Vector3.up * dropHeight;

            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                body.isKinematic = true;
                body.position = top;
            }

            transform.position = top;
            Show();
            StartPuppetIdle();

            // Lowered fast at first, settling gently onto the floor.
            float clock = 0f;

            while (clock < descentTime)
            {
                yield return null;

                if (health.IsDead)
                {
                    Release(wasKinematic);
                    yield break;
                }

                clock += Time.deltaTime;
                float left = 1f - Mathf.Clamp01(clock / descentTime);
                transform.position = landing + Vector3.up * (dropHeight * left * left);
            }

            transform.position = landing;
            Release(wasKinematic);

            float limp = 0f;

            while (limp < inactiveTime || HoldAfterLanding)
            {
                yield return null;

                if (health.IsDead)
                {
                    yield break;
                }

                limp += Time.deltaTime;
            }

            enemy.StayDormant = false;
            enemy.Wake();
            enemy.PlayIdleAnimation();
            IsDone = true;
        }

        private void Release(bool wasKinematic)
        {
            if (body != null)
            {
                body.isKinematic = wasKinematic;

                if (!wasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }
        }

        /// <summary>
        /// Switches off every part drawn or touchable, remembering which were
        /// on, so parts already off stay off when it shows again.
        /// </summary>
        private void Hide()
        {
            hidden = true;
            health.Invulnerable = true;

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    hiddenRenderers.Add(part);
                }
            }

            foreach (Collider part in GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    hiddenColliders.Add(part);
                }
            }

            if (strings != null)
            {
                strings.enabled = false;
            }
        }

        private void Show()
        {
            hidden = false;
            health.Invulnerable = false;

            foreach (Renderer part in hiddenRenderers)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            foreach (Collider part in hiddenColliders)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            hiddenRenderers.Clear();
            hiddenColliders.Clear();

            if (strings != null)
            {
                strings.enabled = true;
            }
        }

        /// <summary>
        /// Puts the puppet straight into its limp idle from the first frame
        /// it is seen. A blend asked for on the frame an animator wakes can be
        /// lost to the idle its enemy asks for in the same frame, so this
        /// starts the state outright and applies it at once instead.
        /// </summary>
        private void StartPuppetIdle()
        {
            Animator animator = GetComponentInChildren<Animator>();

            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(puppetIdleAnimation))
            {
                return;
            }

            int hash = Animator.StringToHash(puppetIdleAnimation);

            if (!animator.HasState(0, hash))
            {
                Debug.LogWarning($"[PuppetDescent] {name}'s animator has no state called \"{puppetIdleAnimation}\".", this);
                return;
            }

            animator.Play(hash, 0, 0f);
            animator.Update(0f);
        }
    }
}
