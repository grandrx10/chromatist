using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Turns the enemy's model to face the player, then fires one projectile
    /// straight ahead from the muzzle.
    /// </summary>
    /// <remarks>
    /// The shot goes where the model is facing, not where the player is. If
    /// the turn runs out of time before it lines up, the shot still fires
    /// along whatever the model is pointing at, so a player who circles fast
    /// enough can outrun the aim.
    ///
    /// Speed, damage and lifetime belong to the projectile prefab, not to this
    /// attack.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Aimed Shot", fileName = "AimedShot")]
    public sealed class AimedShotPattern : AttackPattern
    {
        [SerializeField]
        private EnemyProjectile projectilePrefab;

        [Header("Aim")]
        [SerializeField]
        [Tooltip("Degrees per second the model turns toward the player.")]
        [Min(0f)]
        private float turnSpeed = 360f;

        [SerializeField]
        [Tooltip("Fire once the model is within this many degrees of facing the player.")]
        [Range(0f, 45f)]
        private float aimTolerance = 3f;

        [SerializeField]
        [Tooltip("Most seconds spent turning. When it runs out, the shot fires wherever the model is facing.")]
        [Min(0f)]
        private float maxTurnTime = 1f;

        public override IEnumerator Perform(Enemy enemy)
        {
            float elapsed = 0f;

            while (enemy.HasTarget && elapsed < maxTurnTime)
            {
                if (enemy.TurnModelTowardTarget(turnSpeed * Time.deltaTime) <= aimTolerance)
                {
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Fire(enemy);
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
