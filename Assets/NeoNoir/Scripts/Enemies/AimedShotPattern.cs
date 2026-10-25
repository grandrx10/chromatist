using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Winds up for a moment, then fires one projectile straight ahead from
    /// the muzzle.
    /// </summary>
    /// <remarks>
    /// The enemy keeps turning toward the player the whole time, at its own
    /// turn rate, and the shot goes where the model is facing when the windup
    /// ends -- not where the player is. A player who circles fast enough can
    /// outrun the aim.
    ///
    /// Speed, damage and lifetime belong to the projectile prefab, not to this
    /// attack.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Aimed Shot", fileName = "AimedShot")]
    public sealed class AimedShotPattern : AttackPattern
    {
        [SerializeField]
        private EnemyProjectile projectilePrefab;

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
            Vector3 direction = enemy.Model.forward;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            EnemyProjectile projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Launch(direction);
        }
    }
}
