using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Winds up for a moment, then fires one or more projectiles from the
    /// muzzle, fanned evenly across the frontal spread.
    /// </summary>
    /// <remarks>
    /// The enemy keeps turning toward the player the whole time, at its own
    /// turn rate, and the shot goes where the model is facing when the windup
    /// ends -- not where the player is. A player who circles fast enough can
    /// outrun the aim.
    ///
    /// The fan is centred on that facing. Below 360 degrees the outer shots
    /// sit exactly on the edges of the spread; at 360 they are spaced evenly
    /// around the full circle so the first and last don't overlap.
    ///
    /// Speed, damage and lifetime belong to the projectile prefab, not to this
    /// attack.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Aimed Shot", fileName = "AimedShot")]
    public sealed class AimedShotPattern : AttackPattern
    {
        [SerializeField]
        private EnemyProjectile projectilePrefab;

        [Header("Spread")]
        [SerializeField]
        [Tooltip("Projectiles per shot, fanned evenly across the spread.")]
        [Range(1, 32)]
        private int projectileCount = 1;

        [SerializeField]
        [Tooltip("Total degrees the shot fans across, centred on the enemy's facing. Ignored with a single projectile.")]
        [Range(0f, 360f)]
        private float spread;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Seconds from the start of the attack animation to the shot.")]
        [Min(0f)]
        private float windup = 1f;

        [SerializeField]
        [Tooltip("Seconds after the shot before the attack counts as over and the enemy goes back to idle. Lets the animation finish.")]
        [Min(0f)]
        private float recovery = 0.5f;

        public override IEnumerator Perform(Enemy enemy)
        {
            if (windup > 0f)
            {
                yield return new WaitForSeconds(windup);
            }

            if (enemy.HasTarget)
            {
                Fire(enemy);
            }

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private void Fire(Enemy enemy)
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[{name}] No projectile prefab assigned.", this);
                return;
            }

            Transform muzzle = enemy.Muzzle;

            // Flat, along the model's facing, so the shot travels level at
            // the muzzle's height.
            Vector3 aim = enemy.Model.forward;
            aim.y = 0f;
            aim = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector3.forward;

            // A full circle divides by the count so the ends don't land on
            // the same spot; anything narrower divides by the gaps between
            // shots so the outer ones sit on the edges.
            bool fullCircle = spread >= 360f;
            float step = projectileCount > 1 ? spread / (fullCircle ? projectileCount : projectileCount - 1) : 0f;
            float start = fullCircle ? 0f : -step * (projectileCount - 1) * 0.5f;

            for (int i = 0; i < projectileCount; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(start + step * i, Vector3.up) * aim;

                EnemyProjectile projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(direction, Vector3.up));
                projectile.Launch(direction);
            }
        }
    }
}
