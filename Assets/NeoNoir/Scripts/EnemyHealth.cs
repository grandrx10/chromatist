using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Hit points for an enemy. Takes damage, and dies at zero.
    /// </summary
    /// <remarks>
    /// Deliberately knows nothing about what hit it or how the hit looks.
    /// Weapons call <see cref="TakeDamage"/>; anything that wants to react --
    /// a health bar, a death animation, a score counter -- listens to
    /// <see cref="Damaged"/> and <see cref="Died"/>.
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

        /// <summary>Raised once, when health reaches zero.</summary>
        public event Action<EnemyHealth> Died;

        public float MaxHealth => maxHealth;

        public float CurrentHealth { get; private set; }

        public bool IsDead => CurrentHealth <= 0f;

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
        /// nothing happened -- already dead, or a non-positive amount.
        /// </summary>
        public bool TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return false;
            }

            amount *= DamageTakenMultiplier;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            Damaged?.Invoke(this, amount);

            if (IsDead)
            {
                Died?.Invoke(this);

                if (destroyOnDeath)
                {
                    Destroy(gameObject);
                }
            }

            return true;
        }
    }
}
