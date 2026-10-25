using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Amber the Axe. As a main blade, is thrown spinning at the cursor and
    /// comes back like a boomerang, hitting everything it passes both ways.
    /// As a secondary blade, is raised overhead for a second -- the player
    /// slowed but shrugging off half of all damage -- then slammed down,
    /// splitting a long line through the floor toward the cursor.
    /// </summary>
    /// <remarks>
    /// Model convention matches every blade: tip down local -Y, flat facing
    /// local +Z. For an axe, the "tip" is the head end of the handle and the
    /// flat is the cheek of the head. <see cref="flightRotation"/> and
    /// <see cref="slamRotation"/> turn a model that sits differently.
    ///
    /// Primary. While the attack is held and the axe is in hand, it leaves
    /// from wherever it is, at the cursor -- never nearer than
    /// <see cref="minRange"/> nor further than <see cref="maxRange"/> -- lying
    /// flat and spinning. On reaching that point, or on meeting a wall first,
    /// it turns straight round and flies back at the player at the same
    /// speed, steering after them every frame, and flies through walls on
    /// the way back so it can never be lost. It cannot be thrown again until
    /// it is caught, so the nearer the target, the more often it hits.
    /// Enemies never stop it. It hits each enemy at most once going out and
    /// once coming back. Attack speed buffs only make it fly faster.
    ///
    /// Secondary. The axe rises over the player's head and draws back for
    /// <see cref="chargeTime"/>, trembling harder as it goes, while the
    /// player takes <see cref="chargeDamageTaken"/> times damage and walks at
    /// <see cref="chargeMoveSpeed"/> times speed. They can still dash. It
    /// then comes down toward wherever the cursor is at that moment, and an
    /// <see cref="AmberSlam"/> splits from the player's feet that way.
    ///
    /// As with Blue's perimeter, the ability counts as finished the moment
    /// it is cast, so the next blade in rotation can be used straight away;
    /// the axe holds off its fade out with <see cref="Lingering"/> until it
    /// has landed and settled.
    ///
    /// Damage is swept, not touched: give the prefab no
    /// <see cref="BladeHitbox"/>.
    /// </remarks>
    public sealed class AmberAxe : Blade
    {
        [Header("Primary: throw")]
        [SerializeField]
        [Min(0f)]
        private float damage = 8f;

        [SerializeField]
        [Tooltip("Units per second, out and back. Attack speed buffs multiply it.")]
        [Min(0.5f)]
        private float throwSpeed = 22f;

        [SerializeField]
        [Tooltip("The axe always flies at least this far, even with the cursor on the player.")]
        [Min(0f)]
        private float minRange = 1f;

        [SerializeField]
        [Tooltip("The axe never flies further than this. A cursor beyond it is pulled in.")]
        [Min(0.5f)]
        private float maxRange = 25f;

        [SerializeField]
        [Tooltip("Radius of the strip the spinning axe hurts as it flies.")]
        [Min(0.05f)]
        private float hitRadius = 0.6f;

        [SerializeField]
        [Tooltip("Height the axe flies at, relative to the middle of the player's collider.")]
        private float throwHeightOffset;

        [SerializeField]
        [Tooltip("Layers that turn the axe round early on the way out. Enemies never do, whatever their layer.")]
        private LayerMask obstacleLayers = 1;

        [SerializeField]
        [Tooltip("Radius the axe checks for walls with. Smaller than the hit radius, so it can skim past corners.")]
        [Min(0.01f)]
        private float wallRadius = 0.25f;

        [SerializeField]
        [Tooltip("How close the returning axe must come to the player to be caught.")]
        [Min(0.05f)]
        private float catchDistance = 0.6f;

        [Header("Primary: spin")]
        [SerializeField]
        [Tooltip("Degrees per second the axe spins at in flight. Negative spins the other way.")]
        private float spinSpeed = 1080f;

        [SerializeField]
        [Tooltip("Extra degrees to turn the axe by in flight. Zero lies it flat, cheek up.")]
        private Vector3 flightRotation;

        [Header("Secondary: charge")]
        [SerializeField]
        [Tooltip("Seconds the axe is held overhead before it comes down.")]
        [Min(0f)]
        private float chargeTime = 1f;

        [SerializeField]
        [Tooltip("What damage the player takes is multiplied by while charging.")]
        [Range(0f, 1f)]
        private float chargeDamageTaken = 0.5f;

        [SerializeField]
        [Tooltip("What the player's walking speed is multiplied by while charging.")]
        [Range(0f, 1f)]
        private float chargeMoveSpeed = 0.75f;

        [SerializeField]
        [Tooltip("Height of the grip above the middle of the player's collider: the point the axe swings about.")]
        private float gripHeight = 1f;

        [SerializeField]
        [Tooltip("Distance from the grip to the axe's pivot, toward the head.")]
        private float gripToPivot = 0.5f;

        [SerializeField]
        [Tooltip("Degrees the head leans back past upright as the charge starts, and as it ends.")]
        private Vector2 windup = new Vector2(15f, 45f);

        [SerializeField]
        [Tooltip("Distance the axe trembles by at the very end of the charge.")]
        [Min(0f)]
        private float tremble = 0.05f;

        [Header("Secondary: slam")]
        [SerializeField]
        [Tooltip("Seconds the axe takes to come down from overhead.")]
        [Min(0.01f)]
        private float swingTime = 0.08f;

        [SerializeField]
        [Tooltip("Degrees from upright the head comes to rest at, toward the cursor. Past 90 points into the floor.")]
        [Range(60f, 180f)]
        private float slamEndAngle = 120f;

        [SerializeField]
        [Tooltip("Seconds the axe stays planted after landing, before it can fade out.")]
        [Min(0f)]
        private float recoverTime = 0.35f;

        [SerializeField]
        [Tooltip("Extra degrees to turn the axe by while raised and slamming. Zero swings it in its own plane.")]
        private Vector3 slamRotation;

        [SerializeField] private AmberSlam.Settings slam = new AmberSlam.Settings();

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Material for the slam line, normally NeoNoir/Flat in Amber's colour. Empty uses the axe's own.")]
        private Material effectMaterial;

        [SerializeField]
        [Tooltip("Colour of hits. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        [ColorUsage(false, true)]
        private Color amberColour = new Color(1f, 0.6f, 0.1f);

        private enum Flight { Held, Outbound, Returning }

        private enum SlamPhase { None, Charging, Swinging, Recovering }

        private static readonly RaycastHit[] WallHits = new RaycastHit[16];

        private readonly HashSet<EnemyHealth> hitThisPass = new HashSet<EnemyHealth>();

        // Primary state.
        private Flight flight;
        private Vector3 flightPosition;
        private Vector3 outTarget;
        private Vector3 heading = Vector3.forward;
        private float spin;

        // How far the axe is still in its catch pose rather than its orbit,
        // and that pose, relative to the player so it follows them.
        private float caught;
        private Vector3 catchOffset;
        private Quaternion catchRotation = Quaternion.identity;

        // Secondary state.
        private SlamPhase slamPhase;
        private float slamClock;
        private float slamBlend;
        private Vector3 slamAim = Vector3.forward;
        private bool modifiersApplied;

        private float Speed => throwSpeed * AttackSpeedMultiplier;

        /// <summary>Raised, slamming or planted: the arsenal must not fade the axe out from under the slam.</summary>
        public override bool Lingering => slamPhase != SlamPhase.None;

        /// <summary>The effect material, or the axe's own when none is set. Never copied or edited.</summary>
        private Material ResolvedEffectMaterial
        {
            get
            {
                if (effectMaterial != null)
                {
                    return effectMaterial;
                }

                Renderer own = GetComponentInChildren<Renderer>();
                return own != null ? own.sharedMaterial : null;
            }
        }

        protected override void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime)
        {
            if (flight == Flight.Held && PrimaryHeld && slamPhase == SlamPhase.None)
            {
                Throw();
            }

            if (flight != Flight.Held)
            {
                Fly(deltaTime);
            }

            TickSlam(deltaTime);

            if (flight != Flight.Held)
            {
                spin = Mathf.Repeat(spin + spinSpeed * deltaTime, 360f);
                transform.SetPositionAndRotation(flightPosition, FlightRotation());
                return;
            }

            Vector3 position = orbitPosition;
            Quaternion rotation = orbitRotation;

            caught = StepEngage(caught, false, deltaTime);
            float catchWeight = Ease(caught);
            if (catchWeight > 0f)
            {
                position = Vector3.Lerp(position, Owner.position + catchOffset, catchWeight);
                rotation = Quaternion.Slerp(rotation, catchRotation, catchWeight);
            }

            slamBlend = StepEngage(slamBlend, slamPhase != SlamPhase.None, deltaTime);
            float slamWeight = Ease(slamBlend);
            if (slamWeight > 0f)
            {
                SlamPose(out Vector3 slamPosition, out Quaternion slamPoseRotation);
                position = Vector3.Lerp(position, slamPosition, slamWeight);
                rotation = Quaternion.Slerp(rotation, slamPoseRotation, slamWeight);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private float ThrowHeight()
        {
            return OwnerMiddleHeight() + throwHeightOffset;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        // ---- primary ------------------------------------------------------

        /// <summary>Sends the axe from where it is now toward the cursor, at least a little way and at most its range.</summary>
        private void Throw()
        {
            float height = ThrowHeight();
            Vector3 ownerPosition = Owner.position;

            Vector3 toCursor = TryGetCursorPoint(height, out Vector3 point) ? Flat(point - ownerPosition) : Vector3.zero;

            // On the player gives no direction: throw the way they face.
            Vector3 direction = toCursor.sqrMagnitude > 0.0001f ? toCursor.normalized : Flat(Owner.forward).normalized;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.forward;
            }

            float distance = Mathf.Clamp(toCursor.magnitude, minRange, Mathf.Max(minRange, maxRange));

            outTarget = ownerPosition + direction * distance;
            outTarget.y = height;

            flightPosition = transform.position;
            flightPosition.y = height;

            heading = Flat(outTarget - flightPosition);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : direction;

            flight = Flight.Outbound;
            hitThisPass.Clear();
            ResetHitWindow();
        }

        private void Fly(float deltaTime)
        {
            float step = Speed * deltaTime;
            Vector3 from = flightPosition;

            if (flight == Flight.Outbound)
            {
                Vector3 toTarget = outTarget - flightPosition;
                float remaining = toTarget.magnitude;
                float move = Mathf.Min(step, remaining);

                if (remaining > 0.0001f)
                {
                    heading = toTarget / remaining;
                }

                bool wall = TryHitWall(flightPosition, heading, move, out float wallDistance);
                flightPosition += heading * (wall ? wallDistance : move);

                Strike(from, flightPosition);

                if (wall || move >= remaining)
                {
                    TurnBack();
                }

                return;
            }

            // Back at the player wherever they are now, so it bends after
            // them as they move. Through walls, so it always comes home.
            Vector3 home = Owner.position;
            home.y = ThrowHeight();

            Vector3 toHome = home - flightPosition;
            float distance = toHome.magnitude;

            if (distance > 0.0001f)
            {
                heading = toHome / distance;
            }

            flightPosition += heading * Mathf.Min(step, distance);
            Strike(from, flightPosition);

            if (distance - step <= catchDistance)
            {
                Catch();
            }
        }

        private void TurnBack()
        {
            flight = Flight.Returning;

            // A fresh pass: everything hit going out can be hit coming back.
            hitThisPass.Clear();
            ResetHitWindow();
        }

        private void Catch()
        {
            flight = Flight.Held;

            // Blend back into orbit from right where it was caught, rather
            // than popping straight to the orbit pose.
            catchOffset = flightPosition - Owner.position;
            catchRotation = FlightRotation();
            caught = 1f;
        }

        private void Strike(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = heading;
            }

            BladeDamage.Sweep(from, to, hitRadius, damage, direction, hitThisPass, slashTint: amberColour);
        }

        /// <summary>
        /// The nearest wall along this step, if any: something solid on an
        /// obstacle layer that is not an enemy, the player, or a blade.
        /// </summary>
        private bool TryHitWall(Vector3 origin, Vector3 direction, float distance, out float hitDistance)
        {
            hitDistance = distance;

            if (distance <= 0f)
            {
                return false;
            }

            int count = Physics.SphereCastNonAlloc(origin, wallRadius, direction, WallHits, distance,
                                                   obstacleLayers, QueryTriggerInteraction.Ignore);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = WallHits[i];
                Collider other = candidate.collider;

                // Already overlapping at the start of the step reports a
                // distance of 0. Skipping those means the axe can never
                // stick in a wall it began inside.
                if (candidate.distance <= 0f || candidate.distance >= hitDistance)
                {
                    continue;
                }

                if (other.GetComponentInParent<EnemyHealth>() != null
                    || other.GetComponentInParent<Blade>() != null
                    || other.transform.IsChildOf(Owner))
                {
                    continue;
                }

                hitDistance = candidate.distance;
                found = true;
            }

            return found;
        }

        /// <summary>Lying flat, cheek up, spinning about the vertical.</summary>
        private Quaternion FlightRotation()
        {
            return Quaternion.AngleAxis(spin, Vector3.up)
                 * TipRotation(Vector3.forward, Vector3.forward)
                 * Quaternion.Euler(flightRotation);
        }

        // ---- secondary ----------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            // Still coming down from the last cast: let this one go, rather
            // than snatching the axe back up mid-swing.
            if (slamPhase == SlamPhase.None)
            {
                UpdateSlamAim();

                slamPhase = SlamPhase.Charging;
                slamClock = 0f;
                ApplyChargeModifiers(true);
            }

            // Done as far as the rotation is concerned: the next blade can be
            // used straight away while the axe charges.
            FinishSecondary();
        }

        private void TickSlam(float deltaTime)
        {
            if (slamPhase == SlamPhase.None)
            {
                return;
            }

            slamClock += deltaTime;

            // The aim is only fixed at the moment of impact.
            if (slamPhase != SlamPhase.Recovering)
            {
                UpdateSlamAim();
            }

            switch (slamPhase)
            {
                case SlamPhase.Charging when slamClock >= chargeTime:
                    slamPhase = SlamPhase.Swinging;
                    slamClock = 0f;
                    break;

                case SlamPhase.Swinging when slamClock >= swingTime:
                    Land();
                    slamPhase = SlamPhase.Recovering;
                    slamClock = 0f;
                    break;

                case SlamPhase.Recovering when slamClock >= recoverTime:
                    slamPhase = SlamPhase.None;
                    break;
            }
        }

        private void UpdateSlamAim()
        {
            if (!TryGetCursorPoint(OwnerMiddleHeight(), out Vector3 point))
            {
                return;
            }

            Vector3 desired = Flat(point - Owner.position);

            // On the player gives no direction, so keep the last one.
            if (desired.sqrMagnitude > 0.0001f)
            {
                slamAim = desired.normalized;
            }
        }

        private void Land()
        {
            ApplyChargeModifiers(false);

            Vector3 ownerPosition = Owner.position;
            float floor = OwnerCollider != null ? OwnerCollider.bounds.min.y : ownerPosition.y;
            Vector3 feet = new Vector3(ownerPosition.x, floor, ownerPosition.z);

            AmberSlam.Create(feet, slamAim, OwnerMiddleHeight(), OwnerMiddleHeight() + gripHeight - floor,
                             -windup.y, slamEndAngle, slam, ResolvedEffectMaterial, amberColour);
        }

        /// <summary>
        /// The angle of the swing, in degrees from upright toward the aim:
        /// drawing back while charging, accelerating through the swing, and
        /// planted after.
        /// </summary>
        private float SwingAngle()
        {
            switch (slamPhase)
            {
                case SlamPhase.Charging:
                    float charged = chargeTime > 0f ? Mathf.Clamp01(slamClock / chargeTime) : 1f;
                    return -Mathf.Lerp(windup.x, windup.y, Ease(charged));

                case SlamPhase.Swinging:
                    float swung = Mathf.Clamp01(slamClock / swingTime);
                    return Mathf.Lerp(-windup.y, slamEndAngle, swung * swung);

                default:
                    return slamEndAngle;
            }
        }

        /// <summary>Swung about a grip over the player's head, in the upright plane through the aim, cheek facing sideways.</summary>
        private void SlamPose(out Vector3 position, out Quaternion rotation)
        {
            float angle = SwingAngle() * Mathf.Deg2Rad;
            Vector3 tip = Vector3.up * Mathf.Cos(angle) + slamAim * Mathf.Sin(angle);

            Vector3 grip = Owner.position;
            grip.y = OwnerMiddleHeight() + gripHeight;

            position = grip + tip * gripToPivot;

            if (slamPhase == SlamPhase.Charging && tremble > 0f && chargeTime > 0f)
            {
                float strength = Mathf.Clamp01(slamClock / chargeTime);
                strength *= strength;

                float time = Time.time * 40f;
                position += new Vector3(Mathf.PerlinNoise(time, 0f) - 0.5f,
                                        Mathf.PerlinNoise(0f, time) - 0.5f,
                                        Mathf.PerlinNoise(time, time) - 0.5f) * (2f * tremble * strength);
            }

            Vector3 side = Vector3.Cross(Vector3.up, slamAim);
            rotation = Quaternion.LookRotation(side, -tip) * Quaternion.Euler(slamRotation);
        }

        private void ApplyChargeModifiers(bool on)
        {
            if (on == modifiersApplied)
            {
                return;
            }

            modifiersApplied = on;

            // Not ?. : the owner may have been destroyed first, and a
            // destroyed component is only null to Unity's own comparison.
            if (Health != null)
            {
                if (on)
                {
                    Health.SetDamageTakenModifier(this, chargeDamageTaken);
                }
                else
                {
                    Health.RemoveDamageTakenModifier(this);
                }
            }

            if (Motor != null)
            {
                if (on)
                {
                    Motor.SetSpeedModifier(this, chargeMoveSpeed);
                }
                else
                {
                    Motor.RemoveSpeedModifier(this);
                }
            }
        }

        // ---- lifecycle ----------------------------------------------------

        protected override void OnSnap()
        {
            flight = Flight.Held;
            caught = 0f;
            slamBlend = 0f;
        }

        private void OnDisable()
        {
            // Put away mid-throw or mid-charge: come back in hand next time,
            // and never leave the player slowed or shielded.
            flight = Flight.Held;
            caught = 0f;
            slamPhase = SlamPhase.None;
            slamBlend = 0f;
            ApplyChargeModifiers(false);
        }
    }
}
