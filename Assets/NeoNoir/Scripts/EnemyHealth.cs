using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
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

        /// <summary>
        /// Raised after a blow the enemy survived, when the weapon said where
        /// it came from: with the blow's blade, or null if it did not say,
        /// and the point the blow arrived from. For anything that cares which
        /// side the enemy was hit from.
        /// </summary>
        public event Action<EnemyHealth, Blade, Vector3> Struck;

        public float MaxHealth => maxHealth;

        public float CurrentHealth { get; private set; }

        public bool IsDead => CurrentHealth <= 0f;

        /// <summary>While true, every blow is ignored as if it never landed.</summary>
        public bool Invulnerable { get; set; }

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

        private void Awake()
        {
            CurrentHealth = maxHealth;
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
            active.Add(this);
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
            if (IsDead || Invulnerable || amount <= 0f)
            {
                return false;
            }

            amount *= DamageTakenMultiplier;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            Damaged?.Invoke(this, amount);
            AnyDamaged?.Invoke(this, amount);

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
