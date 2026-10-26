using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Leaps into the sky from wherever the enemy stands, crashes back down
    /// onto the middle of the arena, and sends curving waves of earth cubes
    /// out in every direction.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. Windup: the attack's own animation (HammerJump) plays for
    ///      <see cref="windup"/> seconds, the enemy standing still.
    ///   2. Liftoff: the enemy shoots up <see cref="riseHeight"/> over
    ///      <see cref="riseTime"/> seconds, and a big red
    ///      <see cref="WarningCircle"/> appears on the landing spot, filling
    ///      in over <see cref="fillTime"/> seconds from liftoff.
    ///   3. At the top it moves, out of sight, to <see cref="dropHeight"/>
    ///      above the landing spot and waits for the circle to fill.
    ///   4. The fall: <see cref="descendAnimation"/> plays and the enemy
    ///      drops, gathering speed, to land <see cref="dropTime"/> seconds
    ///      later.
    ///   5. On landing: the circle goes, a <see cref="HammerImpact"/> bursts,
    ///      the camera shakes, and <see cref="waveCount"/>
    ///      <see cref="EarthRipple"/>s run out evenly spaced round the
    ///      compass, each bending by the ripple's Curve Per Cube. The landing
    ///      itself does no damage; only the waves do.
    ///   6. Getting up: <see cref="getUpAnimation"/> (HammerEntry) plays
    ///      from the frame he hits the floor, and the attack waits for it to
    ///      finish, then <see cref="recovery"/> more.
    ///
    /// The landing spot is the enemy's <see cref="Enemy.ArenaCentre"/>, which
    /// <see cref="BossIntro"/> sets; without one, the enemy comes back down
    /// where it took off.
    ///
    /// From liftoff to landing every collider on the enemy is off, so blades
    /// pass under it, and its Rigidbody is kinematic, so gravity has no say.
    /// Both go back as they were on landing.
    ///
    /// It lands standing exactly as high as it stood at takeoff: the bottom
    /// of the colliders on its root, measured before liftoff, ends up on the
    /// floor. Landing lower would bury them, and when they came back on the
    /// physics would push the enemy out -- as often down through the floor
    /// as up.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Hammer Center", fileName = "HammerCenter")]
    public sealed class HammerCenterPattern : AttackPattern
    {
        [Header("Windup")]
        [SerializeField]
        [Tooltip("Seconds the Attack Animation above (HammerJump) plays before the enemy leaves the floor.")]
        [Min(0f)]
        private float windup = 1f;

        [Header("Jump")]
        [SerializeField]
        [Tooltip("How far up the enemy flies before it is moved over the landing spot. High enough to leave the screen.")]
        [Min(0f)]
        private float riseHeight = 30f;

        [SerializeField]
        [Tooltip("Seconds the flight up takes. Fastest off the floor, slowing towards the top.")]
        [Min(0.01f)]
        private float riseTime = 0.5f;

        [Header("Fall")]
        [SerializeField]
        [Tooltip("How far above the landing spot the enemy starts falling from.")]
        [Min(0f)]
        private float dropHeight = 30f;

        [SerializeField]
        [Tooltip("Seconds the fall takes. It speeds up all the way down.")]
        [Min(0.01f)]
        private float dropTime = 0.6f;

        [SerializeField]
        [Tooltip("Animator state played from the top of the fall, landing included.")]
        private string descendAnimation = "HammerDescend";

        [SerializeField]
        [Tooltip("Animator state played the moment the enemy hits the floor, for getting back up. The attack waits for it to finish. Empty skips it.")]
        private string getUpAnimation = "HammerEntry";

        [SerializeField]
        [Tooltip("Seconds to blend from the descend animation into getting up.")]
        [Min(0f)]
        private float getUpBlend = 0.05f;

        [SerializeField]
        [Tooltip("Extra height on top of where the enemy's colliders rest on the floor, which is found by itself. Usually 0.")]
        private float landingHeightOffset;

        [SerializeField]
        [Tooltip("What counts as floor when finding the landing height. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Indicator")]
        [SerializeField]
        [Tooltip("Seconds from liftoff for the circle to fill. The fall starts once it is full. Never shorter than the rise.")]
        [Min(0f)]
        private float fillTime = 2f;

        [SerializeField]
        [Tooltip("Radius of the circle on the floor.")]
        [Min(0.1f)]
        private float indicatorRadius = 6f;

        [SerializeField]
        [Tooltip("Thickness of the circle's outline.")]
        [Min(0f)]
        private float outlineWidth = 0.2f;

        [SerializeField]
        [Tooltip("Height above the floor the circle is drawn at.")]
        [Range(0f, 0.5f)]
        private float floorOffset = 0.03f;

        [SerializeField]
        [Tooltip("Material for the circle, e.g. NeoNoir/Flat. Its colour is replaced by the one below. Empty makes a NeoNoir/Flat one.")]
        private Material indicatorMaterial;

        [SerializeField]
        [ColorUsage(false, true)]
        [Tooltip("Colour of the circle. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        private Color indicatorColour = new Color(1f, 0.04f, 0.08f, 1f);

        [Header("Landing impact")]
        [SerializeField]
        [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want.")]
        private Material impactMaterial;

        [SerializeField]
        private HammerImpact.Settings impact = new HammerImpact.Settings
        {
            ringRadius = 7f,
            ringWidth = 1.2f,
            ringTime = 0.5f,
            debrisCount = 16,
            debrisSize = 0.6f,
            debrisUpSpeed = new Vector2(7f, 12f),
            debrisOutSpeed = new Vector2(3f, 8f),
            debrisTime = 0.9f,
        };

        [SerializeField]
        [Tooltip("How far the camera shakes on landing. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.9f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.6f;

        [Header("Waves")]
        [SerializeField]
        [Tooltip("Waves sent out from the landing, evenly spaced around the full circle.")]
        [Range(1, 32)]
        private int waveCount = 8;

        [SerializeField]
        [Tooltip("Degrees the whole pattern is turned from world forward (+Z).")]
        private float angleOffset;

        [SerializeField]
        [Tooltip("How far from the landing spot each wave's first cube rises.")]
        [Min(0f)]
        private float waveStartDistance = 2.5f;

        [SerializeField]
        private EarthRipple.Settings ripple = new EarthRipple.Settings { curvePerCube = 5f };

        [SerializeField]
        [Tooltip("Seconds after getting up before the attack counts as over.")]
        [Min(0f)]
        private float recovery = 0.5f;

        public override IEnumerator Perform(Enemy enemy)
        {
            // Whatever the movement says, the enemy goes nowhere but up and down.
            enemy.VelocityOverride = Vector3.zero;

            if (windup > 0f)
            {
                yield return new WaitForSeconds(windup);
            }

            Transform root = enemy.transform;
            Rigidbody body = enemy.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            float standingHeight = HeightAboveFeet(enemy);
            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            Vector3 floor = FindFloor(enemy.ArenaCentre ?? root.position);
            Vector3 landing = floor + Vector3.up * (standingHeight + landingHeightOffset);

            WarningCircle circle = WarningCircle.Create(floor + Vector3.up * floorOffset, indicatorRadius,
                                                        outlineWidth, indicatorMaterial, indicatorColour);
            circle.DestroyWhen(() => enemy == null || enemy.Health.IsDead);

            float fullAt = Mathf.Max(fillTime, riseTime);
            Vector3 takeoff = root.position;
            float clock = 0f;

            while (clock < riseTime)
            {
                yield return null;
                clock += Time.deltaTime;
                circle.Fill = fullAt > 0f ? clock / fullAt : 1f;

                // Launched hard, slowing as it climbs.
                float t = Mathf.Clamp01(clock / riseTime);
                root.position = takeoff + Vector3.up * (riseHeight * (1f - (1f - t) * (1f - t)));
            }

            root.position = landing + Vector3.up * dropHeight;

            while (clock < fullAt)
            {
                yield return null;
                clock += Time.deltaTime;
                circle.Fill = clock / fullAt;
            }

            circle.Fill = 1f;
            enemy.PlayAnimation(descendAnimation);

            // From rest under constant pull, like the boss's entrance.
            float falling = 0f;

            while (falling < dropTime)
            {
                yield return null;
                falling += Time.deltaTime;

                float t = Mathf.Clamp01(falling / dropTime);
                root.position = landing + Vector3.up * (dropHeight * (1f - t * t));
            }

            root.position = landing;
            enemy.PlayAnimation(getUpAnimation, getUpBlend);

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            if (body != null)
            {
                body.isKinematic = wasKinematic;

                if (!wasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }

            Destroy(circle.gameObject);
            Land(floor);

            yield return enemy.WaitForAnimation(getUpAnimation);

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private void Land(Vector3 floor)
        {
            HammerImpact.Create(floor, impact, impactMaterial);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            for (int i = 0; i < waveCount; i++)
            {
                float angle = angleOffset + 360f * i / waveCount;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

                EarthRipple.Create(floor + direction * waveStartDistance, direction, ripple);
            }
        }

        /// <summary>
        /// How far the enemy's pivot is above the bottom of the solid
        /// colliders on its root, which are what it stands on. 0 if it has none.
        /// </summary>
        private static float HeightAboveFeet(Enemy enemy)
        {
            float lowest = float.MaxValue;

            foreach (Collider part in enemy.GetComponents<Collider>())
            {
                if (part.enabled && !part.isTrigger)
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            return lowest == float.MaxValue ? 0f : enemy.transform.position.y - lowest;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            // From well above, so a raised floor still counts. The enemy's own
            // colliders are already off, so it never finds itself.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
        }
    }
}
