using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>Something that can turn aside blows meant for an enemy, set as its <see cref="EnemyHealth.Guard"/>.</summary>
    public interface IBlowGuard
    {
        /// <summary>
        /// Whether the blow is turned aside. <paramref name="at"/> is where it
        /// landed, if the weapon knows. Shows the parry when it returns true.
        /// </summary>
        bool TryParry(Vector3? at, Blade source);
    }

    /// <summary>
    /// Hit points for an enemy. Takes damage, and dies at zero.
    /// </summary
    /// <remarks>
    /// Knows nothing about how a hit looks. Weapons call
    /// <see cref="TakeDamage"/>; anything that wants to react -- a health
    /// bar, a death animation, a score counter -- listens to
    /// <see cref="Damaged"/> and <see cref="Died"/>.
    ///
    /// The one thing it does know is which blade a blow came from, when the
    /// weapon says: the killing blow's blade is told about the kill, after
    /// <see cref="Died"/>, so it can play its own kill effect.
    /// </remarks>
    public sealed class EnemyHealth : MonoBehaviour
    {
        [SerializeField]
        [Min(1f)]
        private float maxHealth = 100f;

        [SerializeField]
        [Tooltip("Destroy the object at zero health. Turn off to leave death to whatever listens to Died.")]
        private bool destroyOnDeath = true;

        private static readonly List<EnemyHealth> active = new List<EnemyHealth>();

        /// <summary>Every enabled enemy, so systems that decorate enemies need not search the scene.</summary>
        public static IReadOnlyList<EnemyHealth> Active => active;

        /// <summary>Raised after damage is applied, with the amount dealt.</summary>
        public event Action<EnemyHealth, float> Damaged;

        /// <summary>
        /// <see cref="Damaged"/> for every enemy at once, so something like
        /// damage numbers can hear all of them without subscribing to each.
        /// </summary>
        public static event Action<EnemyHealth, float> AnyDamaged;

        /// <summary>Raised once, when health reaches zero.</summary>
        public event Action<EnemyHealth> Died;

        /// <summary>Raised after health is restored, with the amount actually restored.</summary>
        public event Action<EnemyHealth, float> Healed;

        /// <summary>
        /// Raised after a blow the enemy survived, when the weapon said where
        /// it came from: with the blow's blade, or null if it did not say,
        /// and the point the blow arrived from. For anything that cares which
        /// side the enemy was hit from.
        /// </summary>
        public event Action<EnemyHealth, Blade, Vector3> Struck;

        public float MaxHealth => maxHealth;

        /// <summary>Whether the object is destroyed at zero health. Something that outlives its death, like an <see cref="Altar"/>, turns this off.</summary>
        public bool DestroyOnDeath
        {
            get => destroyOnDeath;
            set => destroyOnDeath = value;
        }

        public float CurrentHealth { get; private set; }

        public bool IsDead => CurrentHealth <= 0f;

        /// <summary>While true, every blow is ignored as if it never landed.</summary>
        public bool Invulnerable { get; set; }

        /// <summary>
        /// True for something hittable that is not really an enemy, such as a
        /// <see cref="ThrownColumn"/>: blades leave it out of the marks they
        /// paint on enemies, like Rose's arc.
        /// </summary>
        public bool Unmarked { get; set; }

        /// <summary>
        /// While set, every blow is offered to it first, and one it parries
        /// does nothing. Weapons with hit effects of their own call
        /// <see cref="Parry"/> before striking, to skip them too.
        /// </summary>
        public IBlowGuard Guard { get; set; }

        /// <summary>
        /// While true, no blow can kill: one that would leaves the enemy on
        /// <see cref="DeathFloor"/> instead and raises
        /// <see cref="DeathPrevented"/>. A <see cref="RunicShield"/> turns
        /// this on while its generators stand.
        /// </summary>
        public bool CannotDie { get; set; }

        /// <summary>
        /// Health no blow can take the enemy below, or 0 for none. Set by
        /// <see cref="Enemy"/> so a boss cannot skip a phase, or die, before
        /// the phase it is in has played an attack. Unlike
        /// <see cref="CannotDie"/>, holding here raises nothing.
        /// </summary>
        public float MinimumHealth { get; set; }

        /// <summary>
        /// While true the enemy is not there to be struck at all: every blow
        /// is ignored, as with <see cref="Invulnerable"/>, and it is left out
        /// of <see cref="Active"/>, so marks and homing never pick it out.
        /// Only <see cref="TakeLinkedDamage"/> reaches it. For a boss that
        /// fights through stand-ins, as the Puppeteer does through its
        /// <see cref="PuppeteerEye"/>s.
        /// </summary>
        public bool Intangible
        {
            get => intangible;
            set
            {
                if (intangible == value)
                {
                    return;
                }

                intangible = value;

                if (!isActiveAndEnabled)
                {
                    return;
                }

                if (value)
                {
                    active.Remove(this);
                }
                else
                {
                    active.Add(this);
                }
            }
        }

        /// <summary>The health a blow stops at while <see cref="CannotDie"/>.</summary>
        public const float DeathFloor = 1f;

        /// <summary>Raised when a blow that would have killed is stopped by <see cref="CannotDie"/>.</summary>
        public event Action<EnemyHealth> DeathPrevented;

        /// <summary>
        /// Another health that blows landing on this object's colliders count
        /// against instead, or null. An armed <see cref="Altar"/> points its
        /// own at its core's, so the whole altar can be struck. Followed by
        /// <see cref="Of"/>.
        /// </summary>
        public EnemyHealth RedirectTo { get; set; }

        /// <summary>
        /// The health a blow on <paramref name="hit"/> lands on: the nearest
        /// one on it or above it, following <see cref="RedirectTo"/>. Null if
        /// the collider belongs to no enemy.
        /// </summary>
        public static EnemyHealth Of(Collider hit)
        {
            EnemyHealth health = hit.GetComponentInParent<EnemyHealth>();

            // Bounded, in case two redirects point at each other.
            for (int i = 0; i < 4 && health != null && health.RedirectTo != null; i++)
            {
                health = health.RedirectTo;
            }

            return health;
        }

        /// <summary>
        /// What incoming damage is multiplied by right now: the active
        /// vulnerability's multiplier, or 1 when there is none.
        /// </summary>
        public float DamageTakenMultiplier => VulnerabilityRemaining > 0f ? vulnerability : 1f;

        /// <summary>Seconds left on the active vulnerability, 0 when there is none.</summary>
        public float VulnerabilityRemaining { get; private set; }

        /// <summary>Raised when a vulnerability takes hold or is refreshed, with its multiplier and duration.</summary>
        public event Action<EnemyHealth, float, float> VulnerabilityApplied;

        private float vulnerability = 1f;
        private bool intangible;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        /// <summary>
        /// Sets the health pool and fills it. For something given health at
        /// runtime, like an <see cref="ArcaneOrb"/>, with no inspector to set it in.
        /// </summary>
        public void SetMaxHealth(float value)
        {
            maxHealth = Mathf.Max(1f, value);
            CurrentHealth = maxHealth;
        }

        /// <summary>
        /// Restores up to <paramref name="amount"/> health, never past the
        /// maximum. Returns the amount actually restored: nothing if dead,
        /// already full, or a non-positive amount.
        /// </summary>
        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return 0f;
            }

            float restored = Mathf.Min(amount, maxHealth - CurrentHealth);

            if (restored <= 0f)
            {
                return 0f;
            }

            CurrentHealth += restored;
            Healed?.Invoke(this, restored);
            return restored;
        }

        private void Update()
        {
            if (VulnerabilityRemaining > 0f)
            {
                VulnerabilityRemaining = Mathf.Max(0f, VulnerabilityRemaining - Time.deltaTime);
            }
        }

        /// <summary>
        /// Makes the enemy take <paramref name="multiplier"/> times damage, from
        /// every source, for <paramref name="duration"/> seconds.
        /// </summary>
        /// <remarks>
        /// Vulnerabilities do not stack. One at least as strong as the active
        /// one replaces it, timer and all; a weaker one is ignored while a
        /// stronger one is still running, rather than cutting it short.
        /// Returns whether it took hold.
        /// </remarks>
        public bool ApplyVulnerability(float multiplier, float duration)
        {
            if (IsDead || duration <= 0f)
            {
                return false;
            }

            if (VulnerabilityRemaining > 0f && multiplier < vulnerability)
            {
                return false;
            }

            vulnerability = multiplier;
            VulnerabilityRemaining = duration;
            VulnerabilityApplied?.Invoke(this, multiplier, duration);
            return true;
        }

        /// <summary>
        /// Bounds of everything solid on the enemy: all of its colliders
        /// together, triggers left out, so a small child such as a feet
        /// trigger does not stand in for the body. Falls back to a point at
        /// the pivot if it has no solid collider.
        /// </summary>
        public Bounds BodyBounds()
        {
            bool found = false;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);

            foreach (Collider part in GetComponentsInChildren<Collider>())
            {
                if (part.isTrigger || !part.enabled)
                {
                    continue;
                }

                if (found)
                {
                    bounds.Encapsulate(part.bounds);
                }
                else
                {
                    bounds = part.bounds;
                    found = true;
                }
            }

            return bounds;
        }

        private void OnEnable()
        {
            if (!intangible)
            {
                active.Add(this);
            }
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        /// <summary>
        /// Deals damage, scaled by any active vulnerability. Returns false if
        /// nothing happened -- already dead, invulnerable, or a non-positive amount.
        /// <paramref name="source"/> is the blade the blow came from, directly
        /// or through anything it made; if the blow kills, that blade is told.
        /// <paramref name="from"/> is the point the blow arrived from, if the
        /// weapon has one; a blow the enemy survives is then reported through
        /// <see cref="Struck"/>.
        /// </summary>
        public bool TakeDamage(float amount, Blade source = null, Vector3? from = null)
        {
            if (Intangible || Parry(from, source))
            {
                return false;
            }

            return ApplyDamage(amount * DamageTakenMultiplier, source, from, true);
        }

        /// <summary>
        /// Offers a blow to the <see cref="Guard"/>. True if it was parried:
        /// the blow should then do nothing at all, its effects included.
        /// <paramref name="at"/> is where it landed, if the weapon knows.
        /// </summary>
        public bool Parry(Vector3? at, Blade source = null)
        {
            return Guard != null && !IsDead && !Invulnerable && !Intangible && Guard.TryParry(at, source);
        }

        /// <summary>
        /// Deals damage passed on from something linked to this enemy, such
        /// as a <see cref="PuppeteerEye"/>. Reaches it even while
        /// <see cref="Intangible"/>, but not while <see cref="Invulnerable"/>.
        /// Taken exactly as given: the stand-in already had its own
        /// vulnerability applied. Raises <see cref="Damaged"/> but not
        /// <see cref="AnyDamaged"/>, since the stand-in already showed the hit.
        /// </summary>
        public bool TakeLinkedDamage(float amount, Blade source = null)
        {
            return ApplyDamage(amount, source, null, false);
        }

        private bool ApplyDamage(float amount, Blade source, Vector3? from, bool announce)
        {
            if (IsDead || Invulnerable || amount <= 0f)
            {
                return false;
            }

            // Held at the minimum: only what takes it down to there.
            if (MinimumHealth > 0f && CurrentHealth - amount < MinimumHealth)
            {
                amount = Mathf.Max(0f, CurrentHealth - MinimumHealth);
            }

            bool prevented = CannotDie && CurrentHealth - amount <= 0f;

            if (prevented)
            {
                // Only what takes it down to the floor; nothing once there.
                float floor = Mathf.Min(CurrentHealth, DeathFloor);
                amount = CurrentHealth - floor;
                CurrentHealth = floor;
            }
            else
            {
                CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            }

            if (amount > 0f)
            {
                Damaged?.Invoke(this, amount);

                if (announce)
                {
                    AnyDamaged?.Invoke(this, amount);
                }
            }

            if (prevented)
            {
                DeathPrevented?.Invoke(this);
            }

            if (IsDead)
            {
                Died?.Invoke(this);

                // Before the destroy, so the body is still there to play on.
                if (source != null)
                {
                    source.ReportKill(this);
                }

                if (destroyOnDeath)
                {
                    Destroy(gameObject);
                }
            }
            else if (from.HasValue)
            {
                // Last, after everything above has settled: a listener may
                // deal damage of its own in response, and that must not land
                // in the middle of this blow.
                Struck?.Invoke(this, source, from.Value);
            }

            return true;
        }
    }
}
