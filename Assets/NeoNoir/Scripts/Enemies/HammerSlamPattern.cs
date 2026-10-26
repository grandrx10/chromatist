using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Charges at the player, then slams a hammer into the floor and sends a
    /// ripple of earth cubes running out along it.
    /// </summary>
    /// <remarks>
    /// Two parts. The charge plays the attack's own animation and follows its
    /// movement, which should be Chase; it does no damage. It ends as soon
    /// as the player is within <see cref="slamRadius"/>, or after
    /// <see cref="maxChargeTime"/> seconds either way.
    ///
    /// The slam plays <see cref="slamAnimation"/> and keeps chasing at
    /// <see cref="slamSpeedMultiplier"/> of the speed. <see cref="impactDelay"/>
    /// seconds in, the hammer lands: an <see cref="HammerImpact"/> bursts on
    /// the floor in front of the enemy, the camera shakes, and an
    /// <see cref="EarthRipple"/> starts there. The ripple runs along the way
    /// the model faced at that moment and never turns after, so it can be
    /// sidestepped.
    ///
    /// The ripple lives on its own once started, like a fired projectile, so
    /// it finishes even if the enemy dies. Killing the enemy before the
    /// hammer lands cancels it.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Hammer Slam", fileName = "HammerSlam")]
    public sealed class HammerSlamPattern : AttackPattern
    {
        [Header("Charge")]
        [SerializeField]
        [Tooltip("The charge ends and the slam starts once the player is this close.")]
        [Min(0f)]
        private float slamRadius = 7f;

        [SerializeField]
        [Tooltip("The slam starts after this many seconds of charging, however far away the player is.")]
        [Min(0f)]
        private float maxChargeTime = 2f;

        [Header("Slam")]
        [SerializeField]
        [Tooltip("Animator state played for the slam. The charge plays the Attack Animation above.")]
        private string slamAnimation = "GuardAttackHammerP2";

        [SerializeField]
        [Tooltip("Walking speed during the slam, as a fraction of the movement speed above.")]
        [Min(0f)]
        private float slamSpeedMultiplier = 0.5f;

        [SerializeField]
        [Tooltip("Seconds from the start of the slam animation to the hammer hitting the floor.")]
        [Min(0f)]
        private float impactDelay = 1f;

        [SerializeField]
        [Tooltip("Seconds after the hammer lands before the attack counts as over. Lets the animation finish.")]
        [Min(0f)]
        private float recovery = 0.5f;

        [Header("Impact")]
        [SerializeField]
        [Tooltip("How far in front of the enemy the hammer lands. The first cube rises here.")]
        [Min(0f)]
        private float impactDistance = 2.5f;

        [SerializeField]
        [Tooltip("What counts as floor when finding the height the cubes rise out of. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [SerializeField]
        [Tooltip("Material for the impact burst, e.g. NeoNoir/Flat in the colour you want.")]
        private Material impactMaterial;

        [SerializeField] private HammerImpact.Settings impact = new HammerImpact.Settings();

        [SerializeField]
        [Tooltip("How far the camera shakes when the hammer lands. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.4f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.35f;

        [Header("Ripple")]
        [SerializeField] private EarthRipple.Settings ripple = new EarthRipple.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            float charged = 0f;

            while (charged < maxChargeTime && !PlayerInSlamRange(enemy))
            {
                yield return null;
                charged += Time.deltaTime;
            }

            enemy.MovementSpeedMultiplier = slamSpeedMultiplier;
            enemy.PlayAnimation(slamAnimation);

            if (impactDelay > 0f)
            {
                yield return new WaitForSeconds(impactDelay);
            }

            Impact(enemy);

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private bool PlayerInSlamRange(Enemy enemy)
        {
            if (!enemy.HasTarget)
            {
                return false;
            }

            Vector3 toTarget = enemy.Target.position - enemy.transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude <= slamRadius * slamRadius;
        }

        private void Impact(Enemy enemy)
        {
            Transform model = enemy.Model;

            Vector3 forward = model.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            Vector3 point = model.position + forward * impactDistance;

            // Measured once, at the hammer, and used for the whole ripple:
            // it runs along one flat floor.
            point.y = Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f,
                                      groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : enemy.transform.position.y;

            HammerImpact.Create(point, impact, impactMaterial);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            EarthRipple.Create(point, forward, ripple);
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.Chase;
            Movement.speed = 5f;
            Movement.stopDistance = 1.5f;
        }
    }
}
