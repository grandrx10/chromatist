using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// A quick run of automatic slashes on one enemy: each deals damage and
    /// fires the slash effect, a moment apart, cut at crossing angles.
    /// </summary>
    /// <remarks>
    /// Lives on its own short-lived object rather than on whatever started
    /// it, so the flurry plays out even if its source -- a mark, a dash --
    /// is gone before it finishes. Stops early if the enemy dies.
    /// </remarks>
    public sealed class SlashFlurry : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Slashes in the flurry.")]
            [Min(1)] public int strikes = 3;

            [Tooltip("Seconds between slashes.")]
            [Min(0f)] public float interval = 0.08f;

            [Tooltip("Size of each slash effect. 1 is a normal hit's.")]
            [Min(0.1f)] public float size = 1f;
        }

        private EnemyHealth target;
        private Collider targetCollider;
        private Settings settings;
        private float damage;
        private Color tint;
        private int remaining;
        private float wait;
        private float yaw;

        /// <summary>
        /// Starts a flurry on <paramref name="enemy"/>, each slash dealing
        /// <paramref name="damagePerStrike"/>. The first lands at once.
        /// <paramref name="effectTint"/> colours the slash effects; black
        /// leaves them the plain inversion.
        /// </summary>
        public static void Play(EnemyHealth enemy, float damagePerStrike, Settings settings, Color effectTint = default)
        {
            if (enemy == null || enemy.IsDead)
            {
                return;
            }

            SlashFlurry flurry = new GameObject("SlashFlurry").AddComponent<SlashFlurry>();
            flurry.target = enemy;
            flurry.targetCollider = enemy.GetComponentInChildren<Collider>();
            flurry.settings = settings;
            flurry.damage = damagePerStrike;
            flurry.tint = effectTint;
            flurry.remaining = settings.strikes;
            flurry.yaw = Random.Range(0f, 360f);
            flurry.Step(0f);
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void Step(float deltaTime)
        {
            wait -= deltaTime;

            while (remaining > 0 && wait <= 0f)
            {
                if (target == null || target.IsDead)
                {
                    remaining = 0;
                    break;
                }

                Strike();
                remaining--;
                wait += settings.interval;
            }

            if (remaining <= 0)
            {
                Destroy(gameObject);
            }
        }

        private void Strike()
        {
            Bounds bounds = targetCollider != null
                ? targetCollider.bounds
                : new Bounds(target.transform.position, Vector3.zero);

            // Spread over the strikes' half turn, so they cross rather than
            // stacking into one line.
            yaw += 180f / settings.strikes + Random.Range(-15f, 15f);
            Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            Vector3 jitter = Vector3.Scale(Random.insideUnitSphere, bounds.extents * 0.3f);
            Vector3 point = bounds.center + jitter;

            // Before damaging: a killing blow destroys the enemy.
            target.TakeDamage(damage);

            if (SlashEffect.Current != null)
            {
                SlashEffect.Current.TriggerSlash(point, direction, settings.size, tint);
            }
        }
    }
}
