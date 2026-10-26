using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Spins on the spot like a top, hammer out and glowing, and careens
    /// after the player with momentum, bouncing off walls and lunging at the
    /// player every few seconds.
    /// </summary>
    /// <remarks>
    /// The model spins at <see cref="spinSpeed"/> the whole time, whatever
    /// the animation does; the attack's own animation is expected to hold the
    /// hammer out.
    ///
    /// The enemy is never steered straight at the player. It accelerates
    /// toward them at <see cref="acceleration"/>, up to <see cref="maxSpeed"/>,
    /// so it carries on past a player who sidesteps and has to swing back
    /// round, like a ball rolling in a bowl. It starts with whatever speed
    /// it already had.
    ///
    /// Every <see cref="dashInterval"/> seconds it dashes: its velocity snaps
    /// toward where the player stands at that moment, at
    /// <see cref="dashSpeed"/>, then eases back down to the top speed, still
    /// carrying its momentum. The <see cref="chargeTime"/> before each dash
    /// is a warning: it brakes, spins faster, the hammer flares and a
    /// <see cref="ChargeRing"/> closes in on it, arriving as it goes. A dash
    /// that would land after the attack ends is skipped.
    ///
    /// Once in every stretch between charge-ups -- from the start of the
    /// attack to the first, from each dash to the next charge-up, and from
    /// the last dash to the end -- it fires a burst from its centre:
    /// <see cref="roundsPerBurst"/> rounds, <see cref="roundInterval"/>
    /// seconds apart, each <see cref="shotsPerRound"/> straight shots evenly
    /// spaced round the compass, and each round turned
    /// <see cref="roundAngleStep"/> further than the last. The first round's
    /// angle is random. <see cref="burstTiming"/> places the burst within the
    /// stretch; a stretch too short to fit it gets none.
    ///
    /// Walls bounce it, keeping <see cref="bounceKeep"/> of its speed, with
    /// an impact burst and a camera shake. Two things find them: a cast
    /// ahead, which sees a wall coming, and the body's own contacts through
    /// <see cref="WallContacts"/>, which catch the walls a glancing body
    /// touches before the cast does -- without them it would slide along.
    /// If something pins it anyway, it bounces straight back.
    ///
    /// The hammer becomes a <see cref="WhirlingHammer"/> while the attack
    /// lasts: glowing, trailing wind, and dealing <see cref="damage"/> to the
    /// player anywhere inside the circle it sweeps round the boss, plus
    /// <see cref="hitPadding"/>, at most once every <see cref="hitInterval"/>
    /// seconds. Parrying it does not destroy it. A burst and a camera shake
    /// go off where it was caught, and the enemy is thrown straight away
    /// from the player at <see cref="knockbackSpeed"/> and slides with no
    /// steering, and no braking for a charge-up, for
    /// <see cref="knockbackTime"/>, then comes back round. A dash still goes
    /// when it is due.
    ///
    /// Lasts <see cref="duration"/> seconds, then ends at once: the spin
    /// stops and the next attack starts, with no delay after by default.
    ///
    /// Needs a non-kinematic Rigidbody on the enemy, for walls to hold it
    /// back and for its contacts to be heard.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Hammer Whirl", fileName = "HammerWhirl")]
    public sealed class HammerWhirlPattern : AttackPattern
    {
        /// <summary>Covering less than this fraction of the asked-for distance over one stall window counts as pinned.</summary>
        private const float StallFraction = 0.25f;

        /// <summary>Seconds each stall check looks back over. Several physics steps, so a frame between two steps never reads as pinned.</summary>
        private const float StallWindow = 0.15f;

        /// <summary>Slower than this, the enemy is not trying to go anywhere, so it cannot be pinned.</summary>
        private const float StallMinSpeed = 1f;

        [SerializeField]
        [Tooltip("Seconds the whirl lasts before the next attack starts.")]
        [Min(0.1f)]
        private float duration = 8f;

        [SerializeField]
        [Tooltip("Degrees per second the model spins. Negative spins the other way.")]
        private float spinSpeed = 720f;

        [Header("Movement")]
        [SerializeField]
        [Tooltip("Speed gained toward the player every second. Lower carries further past them before it turns back.")]
        [Min(0f)]
        private float acceleration = 12f;

        [SerializeField]
        [Tooltip("The whirl never goes faster than this, except while dashing or thrown back by a parry.")]
        [Min(0f)]
        private float maxSpeed = 10f;

        [Header("Dash")]
        [SerializeField]
        [Tooltip("Seconds between dashes, counted from the start of the attack. 0 never dashes.")]
        [Min(0f)]
        private float dashInterval = 3f;

        [SerializeField]
        [Tooltip("Speed the dash snaps to, toward the player. It eases back down to the top speed after.")]
        [Min(0f)]
        private float dashSpeed = 22f;

        [SerializeField]
        [Tooltip("Seconds of warning before each dash.")]
        [Min(0f)]
        private float chargeTime = 0.6f;

        [SerializeField]
        [Tooltip("Speed lost every second while charging up, so it slows before it lunges.")]
        [Min(0f)]
        private float chargeBraking = 15f;

        [SerializeField]
        [Tooltip("Spin speed at the end of a charge-up, as a multiple of the normal spin. It winds up toward this.")]
        [Min(0f)]
        private float chargeSpinMultiplier = 2f;

        [SerializeField]
        [Tooltip("Hammer glow brightness at the end of a charge-up, as a multiple of its normal glow.")]
        [Min(0f)]
        private float chargeGlowMultiplier = 2.5f;

        [SerializeField] private ChargeRing.Settings chargeRing = new ChargeRing.Settings();

        [Header("Shots")]
        [SerializeField]
        [Tooltip("Straight projectile fired in the bursts between charges. Empty fires none.")]
        private EnemyProjectile shotPrefab;

        [SerializeField]
        [Tooltip("Shots in each round, evenly spaced around the full circle.")]
        [Range(1, 32)]
        private int shotsPerRound = 8;

        [SerializeField]
        [Tooltip("Rounds in each burst.")]
        [Min(1)]
        private int roundsPerBurst = 3;

        [SerializeField]
        [Tooltip("Seconds between one round of a burst and the next.")]
        [Min(0f)]
        private float roundInterval = 0.12f;

        [SerializeField]
        [Tooltip("Degrees each round is turned from the one before. Negative turns the other way.")]
        private float roundAngleStep = 10f;

        [SerializeField]
        [Tooltip("Where the burst falls in each stretch between charges: 0 right after a dash (or the start), 1 just before the next charge-up.")]
        [Range(0f, 1f)]
        private float burstTiming = 0.5f;

        [SerializeField]
        [Tooltip("Height above the bottom of the enemy's body the shots fly at. About the middle of the player.")]
        [Min(0f)]
        private float shotHeight = 1f;

        [Header("Hammer")]
        [SerializeField]
        [Tooltip("Name of the hammer object somewhere under the enemy.")]
        private string hammerName = "HammerBig";

        [SerializeField]
        [Tooltip("Colour the hammer glows during the whirl, tinting its own materials. Above 1 blooms. Ignored if a glow material is set.")]
        [ColorUsage(false, true)]
        private Color glowColour = new Color(3f, 0.1f, 0.05f);

        [SerializeField]
        [Tooltip("Optional. Replaces the hammer's materials during the whirl instead of tinting them. Its own materials come back afterwards.")]
        private Material glowMaterial;

        [SerializeField]
        [Tooltip("Seconds the hammer takes to reach its full glow once the whirl starts.")]
        [Min(0f)]
        private float glowFadeIn = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the glow takes to fade once the whirl ends.")]
        [Min(0f)]
        private float glowFadeOut = 0.4f;

        [SerializeField] private WhirlingHammer.Trails trails = new WhirlingHammer.Trails();

        [SerializeField]
        [Tooltip("Damage the hammer deals the player it touches.")]
        [Min(0f)]
        private float damage = 20f;

        [SerializeField]
        [Tooltip("The hitbox is a circle round the boss out to the furthest the hammer reaches. This adds to its radius.")]
        [Min(0f)]
        private float hitPadding = 0.5f;

        [SerializeField]
        [Tooltip("Seconds after a hit, or a parry, before the hammer can hit or be parried again.")]
        [Min(0f)]
        private float hitInterval = 1f;

        [Header("Parry")]
        [SerializeField]
        [Tooltip("Speed the enemy is thrown away from the player when the hammer is parried.")]
        [Min(0f)]
        private float knockbackSpeed = 14f;

        [SerializeField]
        [Tooltip("Seconds the enemy slides after a parry before it steers toward the player again.")]
        [Min(0f)]
        private float knockbackTime = 0.4f;

        [SerializeField]
        [Tooltip("Material for the burst where the hammer is parried. Empty uses the wall Impact Material below; with neither, no burst.")]
        private Material parryImpactMaterial;

        [SerializeField] private HammerImpact.Settings parryImpact = new HammerImpact.Settings();

        [SerializeField]
        [Tooltip("How far the camera shakes on a parry. 0 disables it.")]
        [Min(0f)]
        private float parryShakeStrength = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the parry's camera shake takes to die away.")]
        [Min(0.01f)]
        private float parryShakeDuration = 0.3f;

        [Header("Walls")]
        [SerializeField]
        [Tooltip("What the whirl bounces off. Anything facing upward on it counts as floor and is ignored.")]
        private LayerMask wallMask = 1;

        [SerializeField]
        [Tooltip("Radius of the cast ahead of the enemy. Walls the body touches bounce it too, so this only decides how early a wall straight ahead is seen.")]
        [Min(0.05f)]
        private float castRadius = 0.8f;

        [SerializeField]
        [Tooltip("Height above the enemy's pivot the cast runs at. High enough to clear kerbs and steps.")]
        [Min(0f)]
        private float castHeight = 1f;

        [SerializeField]
        [Tooltip("Fraction of its speed the enemy keeps when it bounces off a wall.")]
        [Range(0f, 1f)]
        private float bounceKeep = 0.8f;

        [SerializeField]
        [Tooltip("Material for the burst where it hits a wall. Empty skips the burst.")]
        private Material impactMaterial;

        [SerializeField] private HammerImpact.Settings impact = new HammerImpact.Settings();

        [SerializeField]
        [Tooltip("How far the camera shakes on a wall bounce. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.25f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.25f;

        [Header("Dust")]
        [SerializeField] private DustTrail.Settings dust = new DustTrail.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            WhirlingHammer hammer = ArmHammer(enemy);
            WallContacts contacts = WallContacts.On(enemy, wallMask);
            DustTrail trail = DustTrail.On(enemy.gameObject, dust);
            bool parried = false;
            Vector3 parryPoint = Vector3.zero;

            if (hammer != null)
            {
                hammer.Parried += (point, _) =>
                {
                    parried = true;
                    parryPoint = point;
                };
            }

            enemy.SpinOverride = spinSpeed;

            Vector3 velocity = enemy.Velocity;
            float elapsed = 0f;
            float knockback = 0f;

            float nextDash = dashInterval > 0f ? dashInterval : float.PositiveInfinity;
            bool charging = false;

            // The burst for the stretch since the last dash, or the start.
            float stretchStart = 0f;
            int roundsFired = 0;
            float burstAngle = 0f;

            Vector3 windowStart = BodyPosition(enemy);
            float windowExpected = 0f;
            float windowTime = 0f;

            while (elapsed < duration)
            {
                enemy.VelocityOverride = velocity;
                trail.KeepEmitting();
                yield return null;

                float deltaTime = Time.deltaTime;
                elapsed += deltaTime;

                // Pinned by something nothing else caught: barely moving
                // though it was asked to go somewhere. Judged against the
                // velocity it actually ran at this frame, before anything
                // below changes it -- a dash or a parry sets a new speed the
                // body has not had a chance to reach yet, and measuring that
                // against the distance just covered would read as pinned.
                Vector3 applied = velocity;
                windowExpected += applied.magnitude * deltaTime;
                windowTime += deltaTime;

                if (windowTime >= StallWindow)
                {
                    float covered = Flat(BodyPosition(enemy) - windowStart).magnitude;

                    if (applied.magnitude > StallMinSpeed && covered < windowExpected * StallFraction)
                    {
                        velocity = -applied * bounceKeep;
                    }

                    ResetWindow(enemy, ref windowStart, ref windowExpected, ref windowTime);
                }

                if (parried)
                {
                    parried = false;
                    velocity = AwayFromPlayer(enemy, velocity) * knockbackSpeed;
                    knockback = knockbackTime;
                    ResetWindow(enemy, ref windowStart, ref windowExpected, ref windowTime);
                    ParryEffect(enemy, parryPoint);
                }

                bool sliding = knockback > 0f;

                if (sliding)
                {
                    knockback -= deltaTime;
                }

                // Charge-up, then the dash itself.
                if (!charging && nextDash < duration && elapsed >= nextDash - chargeTime)
                {
                    charging = true;
                    ChargeRing.Create(enemy.transform, FloorHeight(enemy), nextDash - elapsed, chargeRing);
                }

                if (charging)
                {
                    float wound = chargeTime > 0f ? Mathf.Clamp01(1f - (nextDash - elapsed) / chargeTime) : 1f;
                    enemy.SpinOverride = spinSpeed * Mathf.Lerp(1f, chargeSpinMultiplier, wound);

                    if (hammer != null)
                    {
                        hammer.GlowIntensity = Mathf.Lerp(1f, chargeGlowMultiplier, wound);
                    }

                    if (elapsed >= nextDash)
                    {
                        charging = false;
                        nextDash += dashInterval;
                        stretchStart = elapsed;
                        roundsFired = 0;
                        velocity = TowardPlayer(enemy, velocity) * dashSpeed;
                        knockback = 0f;
                        ResetWindow(enemy, ref windowStart, ref windowExpected, ref windowTime);

                        enemy.SpinOverride = spinSpeed;

                        if (hammer != null)
                        {
                            hammer.GlowIntensity = 1f;
                        }
                    }
                    else if (!sliding)
                    {
                        velocity = Vector3.MoveTowards(velocity, Vector3.zero, chargeBraking * deltaTime);
                    }
                }
                else if (!sliding && enemy.HasTarget)
                {
                    Vector3 toTarget = Flat(enemy.Target.position - enemy.transform.position);

                    if (toTarget.sqrMagnitude > 0.0001f)
                    {
                        velocity += toTarget.normalized * (acceleration * deltaTime);
                    }

                    // Eases back down after a dash or a parry throws it past
                    // the top speed, rather than snapping.
                    float speed = velocity.magnitude;
                    if (speed > maxSpeed)
                    {
                        velocity *= Mathf.MoveTowards(speed, maxSpeed, acceleration * deltaTime) / speed;
                    }
                }

                if (!charging && roundsFired < roundsPerBurst && shotPrefab != null)
                {
                    float stretchEnd = nextDash < duration ? nextDash - chargeTime : duration;
                    float burstLength = (roundsPerBurst - 1) * roundInterval;
                    float spare = stretchEnd - stretchStart - burstLength;

                    if (spare >= 0f)
                    {
                        float burstStart = stretchStart + spare * burstTiming;

                        // Catches up if a long frame skipped a round.
                        while (roundsFired < roundsPerBurst && elapsed >= burstStart + roundsFired * roundInterval)
                        {
                            if (roundsFired == 0)
                            {
                                burstAngle = Random.Range(0f, 360f);
                            }

                            FireRound(enemy, burstAngle + roundsFired * roundAngleStep);
                            roundsFired++;
                        }
                    }
                }

                if (Bounce(enemy, contacts, ref velocity, deltaTime))
                {
                    ResetWindow(enemy, ref windowStart, ref windowExpected, ref windowTime);
                }
            }

            enemy.SpinOverride = null;
            enemy.VelocityOverride = null;

            if (hammer != null)
            {
                Destroy(hammer);
            }
        }

        private void ParryEffect(Enemy enemy, Vector3 point)
        {
            Material material = parryImpactMaterial != null ? parryImpactMaterial : impactMaterial;

            if (material != null)
            {
                HammerImpact.Create(new Vector3(point.x, FloorHeight(enemy), point.z), parryImpact, material);
            }

            if (parryShakeStrength > 0f)
            {
                TopDownCamera.Shake(parryShakeStrength, parryShakeDuration);
            }
        }

        /// <summary>Fires one ring of shots out from the enemy's centre, the first along <paramref name="angle"/> degrees from world forward.</summary>
        private void FireRound(Enemy enemy, float angle)
        {
            Vector3 centre = enemy.transform.position;
            Vector3 origin = new Vector3(centre.x, FloorHeight(enemy) + shotHeight, centre.z);

            for (int i = 0; i < shotsPerRound; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(angle + 360f * i / shotsPerRound, Vector3.up) * Vector3.forward;

                EnemyProjectile shot = Instantiate(shotPrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
                shot.Launch(direction);
            }
        }

        private WhirlingHammer ArmHammer(Enemy enemy)
        {
            foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == hammerName)
                {
                    WhirlingHammer hammer = child.gameObject.AddComponent<WhirlingHammer>();
                    hammer.Arm(enemy, damage, hitInterval, hitPadding, glowMaterial, glowColour, trails,
                               glowFadeIn, glowFadeOut);
                    return hammer;
                }
            }

            Debug.LogWarning($"[{name}] No object called \"{hammerName}\" under {enemy.name}; the whirl will do no damage.", this);
            return null;
        }

        /// <summary>Flat direction from the enemy to the player, or along its velocity if there is no player.</summary>
        private static Vector3 TowardPlayer(Enemy enemy, Vector3 velocity)
        {
            Vector3 toward = enemy.HasTarget ? Flat(enemy.Target.position - enemy.transform.position) : Vector3.zero;

            if (toward.sqrMagnitude < 0.0001f)
            {
                toward = velocity;
            }

            return toward.sqrMagnitude > 0.0001f ? toward.normalized : Flat(enemy.Model.forward).normalized;
        }

        /// <summary>Flat direction from the player to the enemy, or back along its velocity if they stand on top of each other.</summary>
        private static Vector3 AwayFromPlayer(Enemy enemy, Vector3 velocity)
        {
            Vector3 away = enemy.HasTarget ? Flat(enemy.transform.position - enemy.Target.position) : Vector3.zero;

            if (away.sqrMagnitude < 0.0001f)
            {
                away = -velocity;
            }

            return away.sqrMagnitude > 0.0001f ? away.normalized : -Flat(enemy.Model.forward).normalized;
        }

        /// <summary>
        /// Reflects the velocity off a wall the body is touching, or is about
        /// to hit this frame. Returns whether it bounced.
        /// </summary>
        private bool Bounce(Enemy enemy, WallContacts contacts, ref Vector3 velocity, float deltaTime)
        {
            float speed = velocity.magnitude;

            if (speed < 0.01f)
            {
                return false;
            }

            Vector3 contact;
            Vector3 normal;

            bool touching = contacts.TryGetWall(out normal, out contact);

            if (!touching && !WallProbe.Cast(enemy, velocity / speed, speed * deltaTime, castRadius, castHeight,
                                             wallMask, out contact, out normal))
            {
                return false;
            }

            // Already heading away, from the last bounce: not a new one.
            if (Vector3.Dot(velocity, normal) >= 0f)
            {
                return false;
            }

            velocity = Vector3.Reflect(velocity, normal) * bounceKeep;
            contacts.Clear();

            if (impactMaterial != null)
            {
                HammerImpact.Create(new Vector3(contact.x, FloorHeight(enemy), contact.z), impact, impactMaterial);
            }

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            return true;
        }

        /// <summary>Starts a fresh stall window from where the body is now.</summary>
        private static void ResetWindow(Enemy enemy, ref Vector3 start, ref float expected, ref float time)
        {
            start = BodyPosition(enemy);
            expected = 0f;
            time = 0f;
        }

        /// <summary>
        /// Where the enemy's body really is. An interpolated Rigidbody's
        /// transform trails its body by up to a physics step, which is enough
        /// to make a short stall window misjudge how far it went.
        /// </summary>
        private static Vector3 BodyPosition(Enemy enemy)
        {
            Rigidbody body = enemy.GetComponent<Rigidbody>();
            return body != null ? body.position : enemy.transform.position;
        }

        /// <summary>Bottom of the enemy's body, or its pivot if it has no collider.</summary>
        private static float FloorHeight(Enemy enemy)
        {
            Collider body = enemy.GetComponent<Collider>();
            return body != null ? body.bounds.min.y : enemy.transform.position.y;
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
