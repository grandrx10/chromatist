using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Test turret. Fires <see cref="EnemyProjectile"/>s at the player on a
    /// fixed interval, so the parry has something to catch until real enemies
    /// shoot.
    /// </summary>
    /// <remarks>
    /// Shots leave level with the middle of the target's collider and travel
    /// flat, so they always cross the player's body whatever height the
    /// launcher itself is placed at.
    /// </remarks>
    public sealed class EnemyProjectileLauncher : MonoBehaviour
    {
        [SerializeField]
        private EnemyProjectile projectilePrefab;

        [Header("Target")]
        [SerializeField]
        [Tooltip("What to shoot at. Empty finds the player automatically.")]
        private Transform target;

        [Header("Firing")]
        [SerializeField]
        [Tooltip("Seconds between volleys.")]
        [Min(0.05f)]
        private float interval = 1f;

        [SerializeField]
        [Tooltip("Bullets per volley, fanned evenly across the spread.")]
        [Range(1, 32)]
        private int bulletsPerVolley = 1;

        [SerializeField]
        [Tooltip("Total degrees a volley fans across.")]
        [Range(0f, 360f)]
        private float spread = 30f;

        [SerializeField]
        [Tooltip("Stop firing while the target is further than this. 0 always fires.")]
        [Min(0f)]
        private float range = 25f;

        private Collider targetCollider;
        private float cooldown;

        private void Update()
        {
            if (projectilePrefab == null || !ResolveTarget())
            {
                return;
            }

            cooldown -= Time.deltaTime;

            if (cooldown > 0f)
            {
                return;
            }

            cooldown = interval;

            Vector3 aimPoint = targetCollider != null ? targetCollider.bounds.center : target.position;
            Vector3 origin = new Vector3(transform.position.x, aimPoint.y, transform.position.z);
            Vector3 toTarget = aimPoint - origin;

            if (range > 0f && toTarget.sqrMagnitude > range * range)
            {
                return;
            }

            Vector3 aim = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;

            for (int i = 0; i < bulletsPerVolley; i++)
            {
                float angle = bulletsPerVolley > 1 ? Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (bulletsPerVolley - 1f)) : 0f;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * aim;

                EnemyProjectile projectile = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
                projectile.Launch(direction);
            }
        }

        private bool ResolveTarget()
        {
            if (target == null)
            {
                PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (player == null)
                {
                    return false;
                }

                target = player.transform;
                targetCollider = null;
            }

            if (targetCollider == null)
            {
                targetCollider = target.GetComponent<Collider>();
            }

            return true;
        }
    }
}
