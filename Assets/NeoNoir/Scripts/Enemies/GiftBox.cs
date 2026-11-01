using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A puppet that is only a gift box: dropped from the sky on strings that
    /// come loose when it lands, and, once broken, blowing up a moment later
    /// and hurting everyone nearby -- player, enemies and other boxes alike.
    /// </summary>
    /// <remarks>
    /// Each box picks a random size between <see cref="minSize"/> and
    /// <see cref="maxSize"/> as it is made, and scales by it. Its health, the
    /// blast's damage and the blast's radius all scale with that size: a
    /// size 1 box has <see cref="maxHealth"/>, and blows up for
    /// <see cref="maxDamage"/> over <see cref="maxRadius"/>; a size 0.5 box
    /// has half of each. Everyone in the radius takes the full damage.
    ///
    /// Dropping: one placed by hand starts hidden, and drops in once the
    /// player comes within the enemy's detection radius, unless
    /// <see cref="dropOnApproach"/> is off, when it simply sits where it is
    /// with no strings. One summoned through <see cref="PuppetSummoning"/>
    /// drops at once. It starts <see cref="dropHeight"/> above its spot and
    /// falls under real gravity, and the first time it comes to rest on
    /// something its <see cref="PuppetStrings"/> are cut.
    ///
    /// Breaking: any damage counts; enemy shots never hit enemies, so those
    /// leave it alone. With <see cref="detonateOnTouch"/>, so does the player
    /// so much as touching it, and so does any <see cref="SweepBeam"/> with
    /// Breaks Gift Boxes on, as the Puppeteer eyes' lasers have. At zero health it does not go off at once: for
    /// <see cref="fuseTime"/> it swells, wobbling in and out, with a warning
    /// circle filling out on the floor to show the blast's reach. Then it
    /// bursts: a ball of light out to the blast's reach that dissolves away,
    /// a shockwave ring, dust thrown out along the floor and a shower of
    /// confetti, and is gone.
    ///
    /// The blast hurts the player and shoves them away, hurts every enemy in
    /// reach -- an enemy it kills is flung away from the blast by its
    /// <see cref="DeathRagdoll"/> -- and breaks other boxes, which then go
    /// off after their own fuse. Loose physics bodies in reach are thrown
    /// clear. A Puppeteer eye in reach passes its damage on to the boss.
    ///
    /// Goes on the box's root, beside <see cref="Enemy"/> and a Rigidbody
    /// that is not kinematic, with solid, convex colliders. The enemy needs
    /// no attacks; it leaves the body to physics entirely.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GiftBox : MonoBehaviour
    {
        /// <summary>Contacts this close to straight up count as coming to rest on something.</summary>
        private const float LandingNormal = 0.5f;

        private static readonly Collider[] blastHits = new Collider[64];

        [Header("Size")]
        [SerializeField]
        [Tooltip("Pick a random size between the two below. Off keeps the scale the box was placed at as its size, so build the prefab at scale 1.")]
        private bool randomSize = true;

        [SerializeField]
        [Tooltip("Smallest size, as a multiple of the prefab's own scale.")]
        [Min(0.05f)]
        private float minSize = 0.5f;

        [SerializeField]
        [Tooltip("Largest size. A box this size has the full health, damage and radius below.")]
        [Min(0.05f)]
        private float maxSize = 1f;

        [SerializeField]
        [Tooltip("Health of a box at full size. Smaller boxes have proportionally less.")]
        [Min(1f)]
        private float maxHealth = 30f;

        [Header("Drop")]
        [SerializeField]
        [Tooltip("A box placed by hand starts hidden and drops in when the player comes near. Off, it just sits there. Summoned boxes always drop.")]
        private bool dropOnApproach = true;

        [SerializeField]
        [Tooltip("How far above its spot the box starts falling from.")]
        [Min(0f)]
        private float dropHeight = 10f;

        [Header("Fuse")]
        [SerializeField]
        [Tooltip("The player touching the box breaks it, lighting the fuse just as a blow would.")]
        private bool detonateOnTouch = true;

        [SerializeField]
        [Tooltip("Seconds between the box breaking and it blowing up.")]
        [Min(0f)]
        private float fuseTime = 0.3f;

        [SerializeField]
        [Tooltip("How much bigger the box swells by the end of the fuse. 0.3 is 30% bigger.")]
        [Min(0f)]
        private float swell = 0.35f;

        [SerializeField]
        [Tooltip("How far the box shudders while the fuse burns, as a share of its size.")]
        [Min(0f)]
        private float shudder = 0.06f;

        [SerializeField]
        [Tooltip("Times a second the box swells and shrinks back while the fuse burns.")]
        [Min(0f)]
        private float wobbleRate = 10f;

        [SerializeField]
        [Tooltip("How far each wobble swells and shrinks the box, as a share of its size. It grows toward the end of the fuse.")]
        [Min(0f)]
        private float wobble = 0.12f;

        [SerializeField]
        [Tooltip("How much the box squashes down as it swells out, and stretches up as it shrinks in, as a share of the wobble. 0 keeps it swelling evenly.")]
        [Range(0f, 1f)]
        private float squash = 0.6f;

        [SerializeField]
        [Tooltip("Material for the warning circle. Empty makes a NeoNoir/Flat one.")]
        private Material warningMaterial;

        [SerializeField]
        [Tooltip("Colour of the warning circle.")]
        private Color warningColour = new Color(1f, 0.25f, 0.2f, 0.6f);

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0.01f)]
        private float warningOutline = 0.12f;

        [SerializeField]
        [Tooltip("How far above the floor the warning circle sits, so it does not flicker into it.")]
        [Min(0f)]
        private float warningLift = 0.1f;

        [Header("Blast")]
        [SerializeField]
        [Tooltip("Damage of a full-size box's blast. Smaller boxes deal proportionally less.")]
        [Min(0f)]
        private float maxDamage = 50f;

        [SerializeField]
        [Tooltip("Radius of a full-size box's blast. Smaller boxes reach proportionally less far.")]
        [Min(0.1f)]
        private float maxRadius = 5f;

        [SerializeField]
        [Tooltip("How hard the player is shoved away, at the blast's centre. Falls off toward its edge.")]
        [Min(0f)]
        private float playerKnockback = 14f;

        [SerializeField]
        [Tooltip("Speed loose physics bodies, such as other boxes, are thrown at, at the blast's centre.")]
        [Min(0f)]
        private float bodyKnockback = 10f;

        [SerializeField]
        [Tooltip("Lift added to the shove, as if the blast were this far below its centre.")]
        [Min(0f)]
        private float upwardLift = 1f;

        [SerializeField]
        [Tooltip("What the blast reaches. Leave projectiles and triggers out.")]
        private LayerMask blastMask = ~0;

        [Header("Burst")]
        [SerializeField]
        [Tooltip("The ball of the explosion, bursting out to the blast's radius from the box's centre.")]
        private BlastSphere.Settings sphere = new BlastSphere.Settings();

        [SerializeField]
        [Tooltip("Dust puffs thrown out along the floor by a full-size blast. Smaller boxes throw fewer.")]
        [Min(0)]
        private int dustCount = 16;

        [SerializeField]
        [Tooltip("Slowest and fastest the dust is thrown outward, as a multiple of the blast's radius per second.")]
        private Vector2 dustSpeed = new Vector2(1.5f, 3f);

        [SerializeField]
        private DustTrail.Settings dust = new DustTrail.Settings
        {
            size = 0.6f,
            lifetime = 0.7f,
            rise = 1.2f,
        };

        [SerializeField]
        [Tooltip("Material for the shockwave ring. Empty uses NeoNoir/Flat.")]
        private Material ringMaterial;

        [SerializeField]
        private HammerImpact.Settings ring = new HammerImpact.Settings
        {
            ringWidth = 0.8f,
            ringTime = 0.35f,
            debrisCount = 0,
        };

        [SerializeField]
        [Tooltip("Material for the confetti. Empty uses NeoNoir/Flat, double-sided.")]
        private Material confettiMaterial;

        [SerializeField] private GiftConfetti.Settings confetti = new GiftConfetti.Settings();

        [SerializeField]
        [Tooltip("Camera shake of a full-size blast. Smaller boxes shake less. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.35f;

        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> hiddenColliders = new List<Collider>();

        private Enemy enemy;
        private EnemyHealth health;
        private Rigidbody body;
        private PuppetStrings strings;
        private Vector3 placedScale;

        private bool dropping;
        private bool landed;
        private bool hidden;

        // Raining down, from RainDown.
        private bool breakOnLanding;
        private float rainHeight;
        private Vector3 rainFloor;
        private WarningCircle landingCircle;

        /// <summary>This box's size, from 0.05 up; 1 is the prefab's own scale.</summary>
        public float Size { get; private set; } = 1f;

        /// <summary>What this box's blast deals.</summary>
        public float Damage => maxDamage * Size / Mathf.Max(0.05f, maxSize);

        /// <summary>How far this box's blast reaches.</summary>
        public float Radius => maxRadius * Size / Mathf.Max(0.05f, maxSize);

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            health = GetComponent<EnemyHealth>();
            body = GetComponent<Rigidbody>();
            strings = GetComponent<PuppetStrings>();

            enemy.PhysicsDriven = true;
            health.DestroyOnDeath = false;

            // Before anything measures it: a summon is placed by its body.
            // Placed at its size by hand: the scale it was given is its size.
            if (randomSize)
            {
                float smallest = Mathf.Min(minSize, maxSize);
                Size = Random.Range(smallest, Mathf.Max(minSize, maxSize));
                transform.localScale *= Size;
            }
            else
            {
                Size = transform.localScale.x;
            }

            placedScale = transform.localScale;

            health.SetMaxHealth(maxHealth * Size / Mathf.Max(0.05f, maxSize));
        }

        private void OnEnable()
        {
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
        }

        private void Start()
        {
            if (dropping)
            {
                return;
            }

            if (dropOnApproach)
            {
                Hide();
                StartCoroutine(WaitForPlayer());
            }
            else if (strings != null)
            {
                // Sitting, not hanging.
                strings.enabled = false;
            }
        }

        private IEnumerator WaitForPlayer()
        {
            while (!dropping && !enemy.PlayerInRange())
            {
                yield return null;
            }

            DropNow();
        }

        /// <summary>
        /// Drops the box now, from <see cref="dropHeight"/> above where it
        /// stands. Does nothing once it has dropped.
        /// </summary>
        public void DropNow()
        {
            Drop(dropHeight);
        }

        /// <summary>
        /// Whether this box's blast sets off other boxes, and other boxes'
        /// blasts set it off. A blast between two boxes only counts if both
        /// take part. On for every box but those rained down without it.
        /// </summary>
        public bool Chains { get; private set; } = true;

        /// <summary>
        /// Drops the box from <paramref name="height"/> above where it
        /// stands, as a present raining down: a warning circle fills on the
        /// floor below as it falls, and it breaks the moment it lands, its
        /// fuse lit. It sets off from <paramref name="speed"/> straight down,
        /// gravity speeding it on from there. With <paramref name="chains"/>
        /// off, it keeps out of chain reactions with other boxes. A
        /// <paramref name="fullRadius"/> of 0 or more replaces the blast radius
        /// a full-size box has (smaller boxes still reach proportionally less),
        /// and a <paramref name="fuse"/> of 0 or more replaces the fuse time;
        /// negative keeps the prefab's. For <see cref="EncorePattern"/>.
        /// </summary>
        public void RainDown(float height, float speed = 0f, bool chains = true, float fullRadius = -1f, float fuse = -1f)
        {
            if (dropping || health.IsDead)
            {
                return;
            }

            // This box's own, in place of the prefab's: a full-size box's
            // blast radius, which still scales with its size, and its fuse.
            if (fullRadius >= 0f)
            {
                maxRadius = Mathf.Max(0.1f, fullRadius);
            }

            if (fuse >= 0f)
            {
                fuseTime = fuse;
            }

            Chains = chains;
            breakOnLanding = true;
            rainHeight = Mathf.Max(0.01f, height);
            rainFloor = FloorBelow() + Vector3.up * warningLift;
            landingCircle = WarningCircle.Create(rainFloor, Radius, warningOutline, warningMaterial, warningColour);
            landingCircle.Fill = 0f;

            Drop(height);
            body.linearVelocity = Vector3.down * Mathf.Max(0f, speed);
        }

        private void Drop(float height)
        {
            if (dropping || health.IsDead)
            {
                return;
            }

            dropping = true;
            Show();

            Vector3 top = transform.position + Vector3.up * height;
            transform.position = top;
            body.position = top;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Breaks the box outright, lighting its fuse just as a blow that
        /// emptied its health would. For things that set it off without
        /// striking it: the player's touch, a Puppeteer eye's laser. Does
        /// nothing while it is hidden or once it is broken.
        /// </summary>
        public void Break()
        {
            if (hidden || health.IsDead)
            {
                return;
            }

            // Through linked damage, which shows no damage number: nothing
            // struck it.
            health.TakeLinkedDamage(health.CurrentHealth);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (detonateOnTouch && collision.collider.GetComponentInParent<PlayerHealth>() != null)
            {
                Break();
            }

            if (!dropping || landed)
            {
                return;
            }

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y >= LandingNormal)
                {
                    landed = true;

                    if (strings != null)
                    {
                        strings.CutAll();
                    }

                    if (breakOnLanding)
                    {
                        ClearLandingCircle();
                        Break();
                    }

                    return;
                }
            }
        }

        private void Hide()
        {
            hidden = true;
            health.Invulnerable = true;
            body.isKinematic = true;

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
            if (!hidden)
            {
                return;
            }

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

        private void OnDied(EnemyHealth died)
        {
            ClearLandingCircle();
            StartCoroutine(Fuse());
        }

        // The landing warning of a present raining down fills as it falls.
        private void Update()
        {
            if (landingCircle == null)
            {
                return;
            }

            float above = health.BodyBounds().min.y - rainFloor.y;
            landingCircle.Fill = 1f - Mathf.Clamp01(above / rainHeight);
        }

        private void ClearLandingCircle()
        {
            if (landingCircle != null)
            {
                Destroy(landingCircle.gameObject);
                landingCircle = null;
            }
        }

        private void OnDestroy()
        {
            ClearLandingCircle();
        }

        // ---- fuse -----------------------------------------------------------

        private IEnumerator Fuse()
        {
            WarningCircle circle = WarningCircle.Create(FloorBelow() + Vector3.up * warningLift, Radius, warningOutline, warningMaterial, warningColour);
            circle.Fill = 0f;

            Transform model = enemy.Model;
            Vector3 modelRest = model.localPosition;
            float clock = 0f;

            while (clock < fuseTime)
            {
                yield return null;
                clock += Time.deltaTime;
                float t = Mathf.Clamp01(clock / fuseTime);

                circle.transform.position = FloorBelow() + Vector3.up * warningLift;
                circle.Fill = t;

                // Swells faster toward the end, wobbling in and out about
                // that, harder as it goes: squashing down as it swells out
                // and stretching up as it shrinks in, like something about
                // to burst.
                float grown = 1f + swell * t * t;
                float pulse = Mathf.Sin(clock * wobbleRate * 2f * Mathf.PI) * wobble * (0.4f + 0.6f * t);
                Vector3 shape = new Vector3(1f + pulse, 1f - pulse * squash, 1f + pulse);
                transform.localScale = Vector3.Scale(placedScale, shape) * grown;

                if (model != transform)
                {
                    model.localPosition = modelRest + Random.insideUnitSphere * (shudder * t);
                }
            }

            Destroy(circle.gameObject);
            Explode();
        }

        /// <summary>The floor under the box, or its own feet if there is none in reach.</summary>
        private Vector3 FloorBelow()
        {
            Bounds bounds = health.BodyBounds();
            Vector3 from = bounds.center;

            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, bounds.extents.y + 20f,
                                                          ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                {
                    return hit.point;
                }
            }

            return new Vector3(from.x, bounds.min.y, from.z);
        }

        // ---- blast ----------------------------------------------------------

        private void Explode()
        {
            Vector3 centre = health.BodyBounds().center;
            Vector3 floor = FloorBelow();
            float radius = Radius;
            float damage = Damage;
            float share = Size / Mathf.Max(0.05f, maxSize);

            // Gone at once, so nothing hits it on the way, and the blast
            // does not find its own body.
            foreach (Collider part in GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            int count = Physics.OverlapSphereNonAlloc(centre, radius, blastHits, blastMask, QueryTriggerInteraction.Ignore);

            HashSet<EnemyHealth> struck = new HashSet<EnemyHealth> { health };
            HashSet<Rigidbody> thrown = new HashSet<Rigidbody> { body };
            PlayerHealth playerHit = null;

            for (int i = 0; i < count; i++)
            {
                Collider hit = blastHits[i];

                PlayerHealth player = hit.GetComponentInParent<PlayerHealth>();

                if (player != null)
                {
                    if (playerHit == null)
                    {
                        playerHit = player;
                        player.TakeDamage(damage, centre);

                        PlayerMotor motor = player.GetComponent<PlayerMotor>();

                        if (motor != null)
                        {
                            motor.AddExplosionForce(playerKnockback, centre, radius, upwardLift);
                        }
                    }

                    continue;
                }

                // Boxes that keep out of chain reactions neither set others
                // off nor are set off: not hurt, not thrown.
                GiftBox otherBox = hit.GetComponentInParent<GiftBox>();

                if (otherBox != null && otherBox != this && (!Chains || !otherBox.Chains))
                {
                    continue;
                }

                EnemyHealth target = EnemyHealth.Of(hit);

                if (target != null && struck.Add(target) && !target.IsDead)
                {
                    DeathRagdoll ragdoll = target.GetComponent<DeathRagdoll>();

                    if (ragdoll != null)
                    {
                        ragdoll.BlastFrom(centre);
                    }

                    target.TakeDamage(damage, null, centre);

                    if (ragdoll != null)
                    {
                        ragdoll.ClearBlast();
                    }
                }

                Rigidbody loose = hit.attachedRigidbody;

                if (loose != null && !loose.isKinematic && thrown.Add(loose))
                {
                    loose.AddExplosionForce(bodyKnockback, centre, radius, upwardLift, ForceMode.VelocityChange);
                }
            }

            BlastSphere.Create(centre, radius, sphere);
            ThrowDust(floor, radius, share);

            HammerImpact.Settings burst = ring;
            burst.ringRadius = radius;
            HammerImpact.Create(floor, burst, ringMaterial != null ? ringMaterial : FlatMaterial.Shared);
            GiftConfetti.Burst(centre, confetti, confettiMaterial, share);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength * share, shakeDuration);
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Dust thrown out along the floor from <paramref name="floor"/>,
        /// evenly round with a little scatter, rising as it goes. The cloud
        /// is its own object, so it outlives the box.
        /// </summary>
        private void ThrowDust(Vector3 floor, float radius, float share)
        {
            int count = Mathf.RoundToInt(dustCount * share);

            if (count <= 0)
            {
                return;
            }

            DustTrail.Cloud cloud = DustTrail.Cloud.Create("GiftDust");
            float slowest = Mathf.Min(dustSpeed.x, dustSpeed.y);
            float fastest = Mathf.Max(dustSpeed.x, dustSpeed.y);

            for (int i = 0; i < count; i++)
            {
                float angle = (i + Random.Range(-0.4f, 0.4f)) * 360f / count;
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float speed = radius * Random.Range(slowest, fastest);

                cloud.Emit(floor + outward * (radius * 0.15f) + Vector3.up * (dust.size * 0.3f),
                           outward * speed + Vector3.up * (dust.rise * Random.Range(0.6f, 1.2f)),
                           dust);
            }

            cloud.Release();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, maxRadius * transform.lossyScale.x);
        }
    }
}
