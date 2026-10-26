using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Plants the hammer, then charges in a straight line at where the player
    /// stood, faster and faster, until it hits a wall or the player. Then it
    /// slams: earth ripples run out in every direction around it.
    /// </summary>
    /// <remarks>
    /// Three parts.
    ///
    /// The wind-up plays the attack's own animation for <see cref="windupTime"/>
    /// seconds, standing still and turning to track the player as usual.
    ///
    /// The charge aims at the player's position the moment the wind-up ends,
    /// and never steers after: it runs dead straight, through the spot the
    /// player stood on and on, until something stops it. It starts at
    /// <see cref="startSpeed"/> and gains <see cref="acceleration"/> every
    /// second, up to <see cref="maxSpeed"/>. The model faces the way it runs.
    ///
    /// It stops on the first of:
    ///   a wall    a sphere cast ahead finds something solid on
    ///             <see cref="wallMask"/> that is not the floor, or the
    ///             enemy is stalled against something. A
    ///             <see cref="HammerImpact"/> bursts on the floor where it
    ///             hit and the camera shakes.
    ///   the player  within <see cref="playerHitDistance"/>. No damage of
    ///             its own -- the slam is the damage.
    ///   time      <see cref="maxChargeTime"/> seconds of charging, in case
    ///             there is no wall to hit.
    ///   a parry   the front of the charge, <see cref="parryReach"/> from
    ///             the pivot, meets a guarding <see cref="ParryBarrier"/>.
    ///             Checked before the player, so a parry always wins.
    ///
    /// A parried charge does not slam. A <see cref="HammerImpact"/> bursts
    /// where it met the barrier, the camera shakes, and the enemy is thrown
    /// straight back the way it came at <see cref="knockbackSpeed"/>, sliding
    /// to a stop over <see cref="knockbackTime"/> seconds, still facing the
    /// player. Then it is stunned as usual.
    ///
    /// Whichever it was, the slam is immediate: <see cref="waveCount"/>
    /// <see cref="EarthRipple"/>s start around the enemy, evenly spaced, the
    /// first along the charge. Each runs on its own once started, like a
    /// fired projectile. Then the enemy stands in its idle animation for
    /// <see cref="stunTime"/> seconds, looking where it ran, not at the player.
    ///
    /// The hammer glows <see cref="glowColour"/> the way it does in the
    /// whirl: it starts at the tail of the wind-up, reaching full glow just
    /// as the charge starts, and fades out once the charge stops.
    ///
    /// The enemy needs a non-kinematic Rigidbody for walls to stop it; without
    /// one it still stops when the cast finds the wall, but nothing physically
    /// holds it back.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Charge Slam", fileName = "ChargeSlam")]
    public sealed class ChargeSlamPattern : AttackPattern
    {
        /// <summary>Covering less than this fraction of the asked-for distance over one stall window counts as stalled against a wall.</summary>
        private const float StallFraction = 0.25f;

        /// <summary>
        /// Seconds of charging each stall check looks back over. Several
        /// physics steps long: a frame between two steps shows no movement at
        /// all, and must not read as a stall.
        /// </summary>
        private const float StallWindow = 0.15f;

        [Header("Wind-up")]
        [SerializeField]
        [Tooltip("Seconds the Attack Animation above (planting the hammer) plays before the charge.")]
        [Min(0f)]
        private float windupTime = 1f;

        [Header("Charge")]
        [SerializeField]
        [Tooltip("Animator state played while charging. Empty keeps the wind-up animation going.")]
        private string chargeAnimation = "";

        [SerializeField]
        [Tooltip("Speed the charge starts at, in units per second.")]
        [Min(0f)]
        private float startSpeed = 4f;

        [SerializeField]
        [Tooltip("Speed gained every second of the charge.")]
        [Min(0f)]
        private float acceleration = 12f;

        [SerializeField]
        [Tooltip("The charge never goes faster than this.")]
        [Min(0f)]
        private float maxSpeed = 25f;

        [SerializeField]
        [Tooltip("The charge gives up and slams after this many seconds, in case it never meets a wall.")]
        [Min(0.1f)]
        private float maxChargeTime = 4f;

        [SerializeField]
        [Tooltip("The charge stops and slams once the player is this close, measured flat from the enemy's pivot.")]
        [Min(0f)]
        private float playerHitDistance = 1.8f;

        [Header("Hammer glow")]
        [SerializeField]
        [Tooltip("Name of the hammer object somewhere under the enemy. Empty, or not found, skips the glow.")]
        private string hammerName = "HammerBig";

        [SerializeField]
        [Tooltip("Colour the hammer glows during the charge, tinting its own materials. Above 1 blooms. Ignored if a glow material is set.")]
        [ColorUsage(false, true)]
        private Color glowColour = new Color(3f, 0.1f, 0.05f);

        [SerializeField]
        [Tooltip("Optional. Replaces the hammer's materials during the charge instead of tinting them. Its own materials come back afterwards.")]
        private Material glowMaterial;

        [SerializeField]
        [Tooltip("Seconds the hammer takes to reach its full glow. It starts this long before the charge, so it is at full glow as the charge begins.")]
        [Min(0f)]
        private float glowFadeIn = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the glow takes to fade once the charge stops.")]
        [Min(0f)]
        private float glowFadeOut = 0.4f;

        [Header("Walls")]
        [SerializeField]
        [Tooltip("What the charge crashes into. Anything facing upward on it counts as floor and is ignored.")]
        private LayerMask wallMask = 1;

        [SerializeField]
        [Tooltip("Radius of the sphere cast ahead of the enemy. About the width of its body.")]
        [Min(0.05f)]
        private float castRadius = 0.8f;

        [SerializeField]
        [Tooltip("Height above the enemy's pivot the cast runs at. High enough to clear kerbs and steps.")]
        [Min(0f)]
        private float castHeight = 1f;

        [SerializeField]
        [Tooltip("Material for the wall impact burst, e.g. NeoNoir/Flat in the colour you want.")]
        private Material impactMaterial;

        [SerializeField] private HammerImpact.Settings impact = new HammerImpact.Settings();

        [SerializeField]
        [Tooltip("How far the camera shakes when the charge hits a wall. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.4f;

        [Header("Parry")]
        [SerializeField]
        [Tooltip("How far ahead of the enemy's pivot a barrier stops the charge. Keep it above the player hit distance, or the charge reaches the player first.")]
        [Min(0f)]
        private float parryReach = 2.2f;

        [SerializeField]
        [Tooltip("Speed the enemy is thrown back at when the charge is parried.")]
        [Min(0f)]
        private float knockbackSpeed = 18f;

        [SerializeField]
        [Tooltip("Seconds the enemy slides back after a parry, slowing to a stop.")]
        [Min(0f)]
        private float knockbackTime = 0.5f;

        [SerializeField]
        [Tooltip("Material for the burst where the charge meets the barrier. Empty uses the wall impact material.")]
        private Material parryImpactMaterial;

        [SerializeField] private HammerImpact.Settings parryImpact = new HammerImpact.Settings();

        [SerializeField]
        [Tooltip("How far the camera shakes on a parry. 0 disables it.")]
        [Min(0f)]
        private float parryShakeStrength = 0.8f;

        [SerializeField]
        [Tooltip("Seconds the parry camera shake takes to die away.")]
        [Min(0.01f)]
        private float parryShakeDuration = 0.4f;

        [Header("Slam")]
        [SerializeField]
        [Tooltip("Ripples sent out around the enemy, evenly spaced.")]
        [Min(1)]
        private int waveCount = 8;

        [SerializeField]
        [Tooltip("How far from the enemy each ripple's first cube rises.")]
        [Min(0f)]
        private float waveStartDistance = 1.5f;

        [SerializeField]
        [Tooltip("What counts as floor when finding the height the cubes rise out of. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [SerializeField] private EarthRipple.Settings ripple = new EarthRipple.Settings();

        [Header("Dust")]
        [SerializeField] private DustTrail.Settings dust = new DustTrail.Settings();

        [Header("Recovery")]
        [SerializeField]
        [Tooltip("Seconds the enemy stands idle after the slam before the attack counts as over.")]
        [Min(0f)]
        private float stunTime = 2f;

        private enum Stop
        {
            Wall,
            Player,
            Timeout,
            Parried,
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            HammerGlow glow = null;

            // Should the enemy die mid-charge, which stops this outright, the
            // glow goes with it.
            System.Action<EnemyHealth> dropGlow = _ =>
            {
                if (glow != null)
                {
                    Destroy(glow);
                }
            };

            enemy.Health.Died += dropGlow;

            // The glow builds over the end of the wind-up, full as the charge starts.
            float lead = Mathf.Min(glowFadeIn, windupTime);

            if (windupTime - lead > 0f)
            {
                yield return new WaitForSeconds(windupTime - lead);
            }

            glow = StartGlow(enemy);

            if (lead > 0f)
            {
                yield return new WaitForSeconds(lead);
            }

            Vector3 direction = Flat(enemy.TargetPoint - enemy.transform.position);
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Flat(enemy.Model.forward).normalized;

            enemy.FacingOverride = direction;
            enemy.PlayAnimation(chargeAnimation);

            float speed = startSpeed;
            float charged = 0f;
            Stop stop = Stop.Timeout;
            Vector3 contact = Vector3.zero;

            Vector3 windowStart = enemy.transform.position;
            float windowExpected = 0f;
            float windowTime = 0f;

            DustTrail trail = DustTrail.On(enemy.gameObject, dust);

            while (charged < maxChargeTime)
            {
                enemy.VelocityOverride = direction * speed;
                trail.KeepEmitting();
                yield return null;

                float deltaTime = Time.deltaTime;
                charged += deltaTime;

                if (Parried(enemy, direction, speed, deltaTime, out contact))
                {
                    stop = Stop.Parried;
                    break;
                }

                if (PlayerHit(enemy))
                {
                    stop = Stop.Player;
                    break;
                }

                if (WallProbe.Cast(enemy, direction, speed * deltaTime, castRadius, castHeight, wallMask,
                                   out contact, out _))
                {
                    stop = Stop.Wall;
                    break;
                }

                // Pressed against something the cast missed, like a low
                // ledge: barely moving though it is asking to run.
                windowExpected += speed * deltaTime;
                windowTime += deltaTime;

                if (windowTime >= StallWindow)
                {
                    float covered = Vector3.Dot(enemy.transform.position - windowStart, direction);

                    if (covered < windowExpected * StallFraction)
                    {
                        contact = enemy.transform.position + direction * castRadius;
                        stop = Stop.Wall;
                        break;
                    }

                    windowStart = enemy.transform.position;
                    windowExpected = 0f;
                    windowTime = 0f;
                }

                speed = Mathf.Min(maxSpeed, speed + acceleration * deltaTime);
            }

            enemy.VelocityOverride = Vector3.zero;

            enemy.Health.Died -= dropGlow;

            if (glow != null)
            {
                glow.FadeOut(glowFadeOut);
            }

            float floor = FloorHeight(enemy);

            if (stop == Stop.Wall)
            {
                HammerImpact.Create(new Vector3(contact.x, floor, contact.z), impact, impactMaterial);

                if (shakeStrength > 0f)
                {
                    TopDownCamera.Shake(shakeStrength, shakeDuration);
                }
            }

            if (stop == Stop.Parried)
            {
                HammerImpact.Create(new Vector3(contact.x, floor, contact.z), parryImpact,
                                    parryImpactMaterial != null ? parryImpactMaterial : impactMaterial);

                if (parryShakeStrength > 0f)
                {
                    TopDownCamera.Shake(parryShakeStrength, parryShakeDuration);
                }

                enemy.PlayIdleAnimation();

                // Thrown straight back, easing to a stop.
                float sliding = 0f;

                while (sliding < knockbackTime)
                {
                    enemy.VelocityOverride = -direction * (knockbackSpeed * (1f - sliding / knockbackTime));
                    trail.KeepEmitting();
                    yield return null;
                    sliding += Time.deltaTime;
                }

                enemy.VelocityOverride = Vector3.zero;
            }
            else
            {
                Slam(enemy, direction, floor);
                enemy.PlayIdleAnimation();
            }

            if (stunTime > 0f)
            {
                yield return new WaitForSeconds(stunTime);
            }

            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;
        }

        private HammerGlow StartGlow(Enemy enemy)
        {
            if (string.IsNullOrEmpty(hammerName))
            {
                return null;
            }

            foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == hammerName)
                {
                    return HammerGlow.On(child.gameObject, glowMaterial, glowColour, Mathf.Min(glowFadeIn, windupTime));
                }
            }

            return null;
        }

        /// <summary>
        /// Whether the front of the charge meets a guarding barrier this
        /// frame, at the height of the player's body, where barriers sit.
        /// </summary>
        private bool Parried(Enemy enemy, Vector3 direction, float speed, float deltaTime, out Vector3 contact)
        {
            Vector3 from = enemy.transform.position;
            from.y = enemy.HasTarget ? enemy.TargetPoint.y : from.y + castHeight;
            Vector3 to = from + direction * (speed * deltaTime);

            contact = to + direction * parryReach;
            return ParryBarrier.TryCatch(from, to, parryReach, direction * speed);
        }

        private bool PlayerHit(Enemy enemy)
        {
            return enemy.HasTarget
                && Flat(enemy.Target.position - enemy.transform.position).sqrMagnitude <= playerHitDistance * playerHitDistance;
        }

        private void Slam(Enemy enemy, Vector3 forward, float floor)
        {
            Vector3 centre = enemy.transform.position;
            centre.y = floor;

            for (int i = 0; i < waveCount; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(360f * i / waveCount, Vector3.up) * forward;
                EarthRipple.Create(centre + direction * waveStartDistance, direction, ripple);
            }
        }

        /// <summary>Height of the floor under the enemy, ignoring the enemy's own colliders.</summary>
        private float FloorHeight(Enemy enemy)
        {
            Vector3 origin = enemy.transform.position + Vector3.up * 2f;
            float floor = enemy.transform.position.y;
            float nearest = float.MaxValue;

            foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 10f, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(enemy.transform) || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                floor = hit.point.y;
            }

            return floor;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
        }
    }
}
