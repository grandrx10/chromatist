using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Hit points for the player. Takes damage, heals, and dies at zero.
    /// </summary>
    /// <remarks>
    /// The player's counterpart to <see cref="EnemyHealth"/>, and just as
    /// ignorant of what hurt or healed it. Anything that wants to react -- a
    /// health bar, a hurt flash, a game over screen -- listens to the events.
    ///
    /// Death only raises <see cref="Died"/>. What dying means for the player is
    /// a game decision, not a health one.
    /// </remarks>
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField]
        [Min(1f)]
        private float maxHealth = 100f;

        // Keyed by whoever applied each one, so a source can only ever
        // remove its own and two sources stack by multiplying.
        private readonly Dictionary<object, float> damageTakenModifiers = new Dictionary<object, float>();

        /// <summary>Raised after damage is applied, with the amount actually lost.</summary>
        public event Action<PlayerHealth, float> Damaged;

        /// <summary>
        /// <see cref="Damaged"/> for every player at once, with where the hit
        /// came from when the damage said, so effects can hear it without a
        /// reference to the player.
        /// </summary>
        public static event Action<PlayerHealth, float, Vector3?> AnyDamaged;

        /// <summary>Raised after healing is applied, with the amount actually restored.</summary>
        public event Action<PlayerHealth, float> Healed;

        /// <summary>Raised once, when health reaches zero.</summary>
        public event Action<PlayerHealth> Died;

        public float MaxHealth => maxHealth;

        public float CurrentHealth { get; private set; }

        /// <summary>Current health as a fraction of max, 0 to 1.</summary>
        public float Fraction => CurrentHealth / maxHealth;

        public bool IsDead => CurrentHealth <= 0f;

        /// <summary>What incoming damage is multiplied by: every modifier applied, multiplied together. 1 with none.</summary>
        public float DamageTakenMultiplier
        {
            get
            {
                float multiplier = 1f;

                foreach (float modifier in damageTakenModifiers.Values)
                {
                    multiplier *= modifier;
                }

                return multiplier;
            }
        }

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        /// <summary>
        /// Multiplies incoming damage by <paramref name="multiplier"/> until
        /// <see cref="RemoveDamageTakenModifier"/> is called with the same
        /// <paramref name="source"/>: 0.5 halves it. Applying again from the
        /// same source replaces its modifier rather than stacking.
        /// </summary>
        public void SetDamageTakenModifier(object source, float multiplier)
        {
            damageTakenModifiers[source] = Mathf.Max(0f, multiplier);
        }

        public void RemoveDamageTakenModifier(object source)
        {
            damageTakenModifiers.Remove(source);
        }

        /// <summary>
        /// Deals damage, scaled by <see cref="DamageTakenMultiplier"/>.
        /// Returns the amount actually lost, which is less than asked when it
        /// runs out, and 0 when already dead. <paramref name="source"/> is
        /// where the hit came from, for effects; leave it out when there is no
        /// such place.
        /// </summary>
        public float TakeDamage(float amount, Vector3? source = null)
        {
            amount *= DamageTakenMultiplier;

            if (IsDead || amount <= 0f)
            {
                return 0f;
            }

            float lost = Mathf.Min(amount, CurrentHealth);
            CurrentHealth -= lost;
            Damaged?.Invoke(this, lost);
            AnyDamaged?.Invoke(this, lost, source);

            if (IsDead)
            {
                Died?.Invoke(this);
            }

            return lost;
        }

        /// <summary>
        /// Restores health, up to max. Returns the amount actually restored.
        /// The dead cannot be healed.
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

        // Nothing hurts the player yet, so these are the only way to see
        // lifesteal and anything else that reacts to missing health.
        [ContextMenu("Debug/Take 25 Damage")]
        private void DebugTakeDamage()
        {
            TakeDamage(25f);
            Debug.Log($"[PlayerHealth] {CurrentHealth:0.#} / {maxHealth:0.#}", this);
        }

        [ContextMenu("Debug/Heal To Full")]
        private void DebugHealToFull()
        {
            Heal(maxHealth);
            Debug.Log($"[PlayerHealth] {CurrentHealth:0.#} / {maxHealth:0.#}", this);
        }
    }
}
