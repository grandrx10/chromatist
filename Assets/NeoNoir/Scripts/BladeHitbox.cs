using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A blade's trigger volume. Damages each enemy it touches at most once
    /// per strike and fires the slash effect on contact.
    /// </summary>
    /// <remarks>
    /// Once per strike rather than once per touch: a spinning blade can pass in
    /// and out of the same enemy several times inside one cut, and each of
    /// those would otherwise count as a separate hit. The list of enemies
    /// already hit is cleared whenever the <see cref="Blade"/> begins a new
    /// strike, and nothing is hit at all unless the blade says it is
    /// currently dealing damage.
    ///
    /// The blade is moved by its transform, not by physics, so this adds a
    /// kinematic rigidbody if there is none. Unity only sends trigger messages
    /// when at least one side has a rigidbody, and this way enemies do not
    /// need one.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public sealed class BladeHitbox : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Leave empty to find it on this object or a parent.")]
        private Blade blade;

        [SerializeField]
        [Min(0f)]
        private float damage = 10f;

        [SerializeField]
        [Tooltip("Leave empty to use whichever one is in the scene. None at all just means no effect.")]
        private SlashEffect slashEffect;

        private readonly HashSet<EnemyHealth> hitThisStrike = new HashSet<EnemyHealth>();

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;

            if (!TryGetComponent(out Rigidbody body))
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;

            if (blade == null)
            {
                blade = GetComponentInParent<Blade>();
            }
        }

        private void OnEnable()
        {
            if (blade != null)
            {
                blade.HitWindowReset += ClearHits;
            }
        }

        private void OnDisable()
        {
            if (blade != null)
            {
                blade.HitWindowReset -= ClearHits;
            }

            ClearHits();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryHit(other);
        }

        // Stay as well as Enter: an enemy already overlapping the blade when a
        // new strike begins never re-enters, but should still be hit.
        private void OnTriggerStay(Collider other)
        {
            TryHit(other);
        }

        private void TryHit(Collider other)
        {
            // Brushing past an enemy while orbiting is not an attack.
            if (blade == null || !blade.IsDealingDamage)
            {
                return;
            }

            EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();

            if (enemy == null || enemy.IsDead || !hitThisStrike.Add(enemy))
            {
                return;
            }

            // Read before damaging: a killing blow destroys the enemy.
            Vector3 point = other.ClosestPointOnBounds(transform.position);

            enemy.TakeDamage(damage);

            SlashEffect effect = slashEffect != null ? slashEffect : SlashEffect.Current;

            if (effect != null)
            {
                effect.TriggerSlash(point, blade.HitVelocity);
            }
        }

        private void ClearHits()
        {
            hitThisStrike.Clear();
        }
    }
}
