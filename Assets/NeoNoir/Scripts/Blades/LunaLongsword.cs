using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Luna the Longsword. As a main blade, flies to the cursor and strikes
    /// through it at a fixed rate for as long as the attack is held. As a
    /// secondary blade, parries: a barrier snaps up around the player for half
    /// a second, and every enemy projectile that touches it in that time is
    /// nullified in a burst of sparks.
    /// </summary>
    /// <remarks>
    /// Model convention: the blade points down its local -Y (tip down at rest)
    /// and its flat faces local +Z. That is the default pose -- vertical, tip
    /// down, flat turned in toward the player.
    ///
    /// While attacking, the blade is not animated along a fixed arc. The cut
    /// point (the part of the blade that connects) is simulated as a particle
    /// with a position and a velocity, and every attack is a constraint on it:
    ///
    ///   approach        be AT the cursor, moving at strike velocity, exactly
    ///                   windup * period seconds after the attack began. Solved
    ///                   as a cubic Hermite from the blade's current position
    ///                   and velocity, re-solved every frame so a moving cursor
    ///                   is still hit on time.
    ///   follow-through  after contact the blade flies free and bleeds speed
    ///                   to drag, carrying past the target.
    ///
    /// Because each approach starts from wherever the last follow-through left
    /// the blade, still moving, the path between strikes is never authored. A
    /// blade that overshot to the left has to loop back around to cut right,
    /// and that loop is what gives the attack its flow. The only thing chosen
    /// per strike is the direction of the cut at the moment of contact.
    ///
    /// On top of that path, each strike rolls a random spin -- a whole number
    /// of turns about a randomly leaning axis, fastest at contact -- plus a
    /// random slant and tilt, so consecutive cuts never look the same.
    ///
    /// Secondary. Spawns the barrier prefab (pure art -- HexBarrier) and adds a
    /// <see cref="ParryBarrier"/> to it, which does the catching. Each catch
    /// throws a <see cref="ParrySpark"/> burst glancing off the shell. The
    /// ability counts as finished once the barrier has dissolved.
    /// </remarks>
    public sealed class LunaLongsword : Blade
    {
        [Header("Attack")]
        [SerializeField]
        [Tooltip("Keep following the cursor while attacking. Off locks the spot that was clicked.")]
        private bool trackCursor = true;

        [Header("Strike")]
        [SerializeField]
        [Tooltip("Strikes per second. Each one passes through the cursor exactly once.")]
        [Range(0.2f, 12f)]
        private float attacksPerSecond = 2.5f;

        [SerializeField]
        [Tooltip("Fraction of each attack spent travelling to the cursor. The rest is follow-through.")]
        [Range(0.2f, 0.9f)]
        private float windup = 0.55f;

        [SerializeField]
        [Tooltip("Speed of the cut point as it passes through the cursor.")]
        private float strikeSpeed = 18f;

        [SerializeField]
        [Tooltip("0 is a pure sideways slash across the target, 1 is a straight stab outward through it.")]
        [Range(0f, 1f)]
        private float thrust = 0.25f;

        [SerializeField]
        [Tooltip("Random degrees added to each strike's direction, so no two cuts are identical.")]
        [Range(0f, 60f)]
        private float strikeVariance = 15f;

        [SerializeField]
        [Tooltip("Random degrees each cut slants up or down, so strikes come in rising, falling and level.")]
        [Range(0f, 60f)]
        private float strikeElevation = 20f;

        [SerializeField]
        [Tooltip("How quickly the blade bleeds speed after contact. Lower carries further past the target.")]
        private float followThroughDrag = 7f;

        [Header("Spin")]
        [SerializeField]
        [Tooltip("Fewest whole turns the blade spins through per strike.")]
        [Range(0, 4)]
        private int minSpins;

        [SerializeField]
        [Tooltip("Most whole turns the blade spins through per strike. Each strike picks a count in between.")]
        [Range(0, 4)]
        private int maxSpins = 2;

        [SerializeField]
        [Tooltip("Random degrees the spin axis leans away from vertical, so spins wobble at different angles instead of all spinning flat.")]
        [Range(0f, 75f)]
        private float spinAxisVariance = 30f;

        [Header("Blade")]
        [SerializeField]
        [Tooltip("Distance from the handle to the tip. The object's pivot must be the centre of the handle.")]
        private float bladeLength = 1.2f;

        [SerializeField]
        [Tooltip("Which point along the blade passes through the cursor. 0 is the handle, 1 is the tip.")]
        [Range(0f, 1f)]
        private float cutPoint = 0.6f;

        [SerializeField]
        [Tooltip("Height of the cut relative to the middle of the player's collider. 0 cuts level with the player. The cursor is placed at this height, so the blade passes under it on screen.")]
        private float cutHeightOffset;

        [SerializeField]
        [Tooltip("Degrees the tip angles below horizontal while attacking.")]
        [Range(0f, 80f)]
        private float swingTilt = 15f;

        [SerializeField]
        [Tooltip("Random degrees added to or taken from the tilt on each strike.")]
        [Range(0f, 40f)]
        private float tiltVariance = 15f;

        [SerializeField]
        [Tooltip("Degrees the blade banks into the cut at full strike speed.")]
        [Range(0f, 90f)]
        private float swingRoll = 35f;

        [SerializeField]
        [Tooltip("Seconds of velocity the tip trails behind by, so the blade sweeps through a cut instead of sliding rigidly.")]
        private float tipDrag = 0.04f;

        [SerializeField]
        [Tooltip("How quickly the tip turns to its new direction. Higher is stiffer.")]
        private float tipSharpness = 18f;

        [Header("Secondary: parry")]
        [SerializeField]
        [Tooltip("What the barrier looks like. Spawned around the player for each parry; the behaviour is added at runtime, so any mesh works. Empty still parries, just invisibly.")]
        private GameObject barrierPrefab;

        [SerializeField]
        [Tooltip("Radius of the barrier. 0 fits it around the player's collider.")]
        [Min(0f)]
        private float barrierRadius;

        [SerializeField]
        [Tooltip("Gap left between the player's collider and the barrier when it is fitted.")]
        [Min(0f)]
        private float barrierPadding = 0.35f;

        [SerializeField] private ParryBarrier.Settings parry = new ParryBarrier.Settings();

        [Header("Secondary: sparks")]
        [SerializeField]
        [Tooltip("Material for the sparks. Empty uses Luna's own.")]
        private Material sparkMaterial;

        [SerializeField] private ParrySpark.Settings sparks = new ParrySpark.Settings();

        [Header("Debug")]
        [SerializeField]
        [Tooltip("TEMP: seconds the blade keeps attacking after the button is released.")]
        private float releaseDelay = 2f;

        private ParryBarrier barrier;

        private Vector3 attackPoint;
        private Vector3 aim = Vector3.forward;

        private Vector3 cutPosition;
        private Vector3 cutVelocity;
        private Vector3 tipDirection = Vector3.down;

        private Vector3 strikeVelocity;
        private float strikeClock;
        private bool hasStruck;

        private float strikeTilt;
        private float spinDegrees;
        private Vector3 spinAxis = Vector3.up;

        private float engage;
        private float releaseRemaining;
        private bool wasHeld;
        private bool wasAttacking;

        private float CutDistance => bladeLength * cutPoint;

        /// <summary>
        /// Raised at the instant the cut point passes through the target, with
        /// the contact point and the blade's velocity there.
        /// </summary>
        public event Action<Vector3, Vector3> Struck;

        /// <summary>True from the moment the attack is pressed until the blade has fully settled back into orbit.</summary>
        public bool IsAttacking => engage > 0f;

        /// <summary>
        /// Only while strikes are actually being thrown -- not while the blade
        /// is still blending back into orbit afterwards.
        /// </summary>
        public override bool IsDealingDamage => wasAttacking;

        public override Vector3 HitVelocity => cutVelocity;

        // ---- secondary --------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            Vector3 centreOffset = Vector3.up * (OwnerMiddleHeight() - Owner.position.y);

            GameObject host = barrierPrefab != null
                ? Instantiate(barrierPrefab, Owner.position + centreOffset, Quaternion.identity)
                : new GameObject("ParryBarrier");

            if (!host.TryGetComponent(out barrier))
            {
                barrier = host.AddComponent<ParryBarrier>();
            }

            barrier.Parried += OnParried;
            barrier.Finished += OnBarrierFinished;
            barrier.Play(Owner, centreOffset, BarrierRadius(), parry);
        }

        /// <summary>The set radius, or one fitted round the player's collider.</summary>
        private float BarrierRadius()
        {
            if (barrierRadius > 0f)
            {
                return barrierRadius;
            }

            Collider body = OwnerCollider;
            Vector3 extents = body != null ? body.bounds.extents : Vector3.one * 0.5f;
            return Mathf.Max(extents.x, extents.y, extents.z) + barrierPadding;
        }

        /// <summary>Sparks glance off the shell the way the projectile would have bounced.</summary>
        private void OnParried(Vector3 point, Vector3 normal, Vector3 velocity)
        {
            Vector3 glance = velocity.sqrMagnitude > 0.000001f
                ? Vector3.Reflect(velocity.normalized, normal)
                : normal;

            // A shot only grazing the shell would reflect almost along it;
            // lean the spray outward so the sparks still leave the barrier.
            Vector3 direction = (glance + normal * 0.5f).normalized;

            ParrySpark.Spawn(point, direction, sparks, ResolvedSparkMaterial);
        }

        private void OnBarrierFinished(ParryBarrier finished)
        {
            finished.Parried -= OnParried;
            finished.Finished -= OnBarrierFinished;

            if (barrier == finished)
            {
                barrier = null;
                FinishSecondary();
            }
        }

        private Material ResolvedSparkMaterial
        {
            get
            {
                if (sparkMaterial != null)
                {
                    return sparkMaterial;
                }

                Renderer own = GetComponentInChildren<Renderer>();
                return own != null ? own.sharedMaterial : null;
            }
        }

        private void OnDisable()
        {
            // Hidden mid-parry: the ability must still end, or the rotation
            // would wait on it forever. The barrier goes with it.
            if (barrier != null)
            {
                ParryBarrier dropped = barrier;
                barrier = null;
                dropped.Parried -= OnParried;
                dropped.Finished -= OnBarrierFinished;
                Destroy(dropped.gameObject);
                FinishSecondary();
            }
        }

        // ---- primary ----------------------------------------------------

        protected override void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime)
        {
            Vector3 ownerPosition = Owner.position;
            bool held = PrimaryHeld;

            releaseRemaining = held ? releaseDelay : Mathf.Max(0f, releaseRemaining - deltaTime);
            bool attacking = held || releaseRemaining > 0f;

            // Only a held button moves the target. During the release delay
            // the blade keeps cutting wherever it was last sent.
            if (held && (trackCursor || !wasHeld))
            {
                if (TryGetCursorPoint(OwnerMiddleHeight() + cutHeightOffset, out Vector3 point))
                {
                    attackPoint = point;
                }
            }

            wasHeld = held;

            if (attacking && !wasAttacking)
            {
                if (engage <= 0f)
                {
                    // Coming from a settled orbit: pick the blade up exactly
                    // where it is, so the first approach starts from its real
                    // pose rather than teleporting.
                    tipDirection = -transform.up;
                    cutPosition = transform.position + tipDirection * CutDistance;
                    cutVelocity = AnchorVelocity;
                }

                // Re-pressing mid-release keeps the momentum it already has.
                BeginStrike(ownerPosition);
            }

            wasAttacking = attacking;

            engage = StepEngage(engage, attacking, deltaTime);
            float weight = Ease(engage);

            if (weight <= 0f)
            {
                transform.SetPositionAndRotation(orbitPosition, orbitRotation);
                return;
            }

            Simulate(ownerPosition, attacking, deltaTime);
            StrikePose(ownerPosition, deltaTime, out Vector3 strikePosition, out Quaternion strikeRotation);

            transform.SetPositionAndRotation(
                Vector3.Lerp(orbitPosition, strikePosition, weight),
                Quaternion.Slerp(orbitRotation, strikeRotation, weight));
        }

        /// <summary>Picks the velocity the next cut must have at the moment of contact.</summary>
        private void BeginStrike(Vector3 ownerPosition)
        {
            strikeClock = 0f;
            hasStruck = false;

            Vector3 desired = attackPoint - ownerPosition;
            desired.y = 0f;

            // Clicking on the player gives no direction to strike along, so
            // keep whatever was there.
            if (desired.sqrMagnitude > 0.0001f)
            {
                aim = desired.normalized;
            }

            Vector3 across = Vector3.Cross(Vector3.up, aim);

            // Cut toward whichever side of the target the blade is not on.
            // After a follow-through the blade has carried past the target, so
            // this alternates by itself -- and since it is still moving away,
            // the approach has to swing it back around to get there.
            float side = Vector3.Dot(cutPosition - attackPoint, across);
            if (Mathf.Abs(side) < 0.05f)
            {
                side = -Vector3.Dot(cutVelocity, across);
            }

            Vector3 direction = across * (-Mathf.Sign(side) * (1f - thrust)) + aim * thrust;
            direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-strikeVariance, strikeVariance), Vector3.up) * direction;
            direction.Normalize();

            // Slant the cut up or down about the horizontal axis across it.
            Vector3 slantAxis = Vector3.Cross(direction, Vector3.up);
            direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-strikeElevation, strikeElevation), slantAxis) * direction;

            strikeVelocity = direction * strikeSpeed;
            strikeTilt = Mathf.Clamp(swingTilt + UnityEngine.Random.Range(-tiltVariance, tiltVariance), 0f, 85f);

            // Whole turns only, so by the end of the strike the spin is back to
            // the unspun pose and the next strike picks up without a jump.
            // Spun the same way the cut travels, so it whips.
            int turns = UnityEngine.Random.Range(minSpins, maxSpins + 1);
            float spinSign = Mathf.Sign(Vector3.Dot(direction, across));
            spinDegrees = turns * 360f * spinSign;

            Vector3 lean = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up) * Vector3.forward;
            spinAxis = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, spinAxisVariance), lean) * Vector3.up;

            ResetHitWindow();
        }

        /// <summary>
        /// How far through this strike's spin the blade is, 0 to 1. Accelerates
        /// into contact and decelerates out, with its fastest point exactly at
        /// the hit so the blade is whipping hardest as it cuts.
        /// </summary>
        /// <remarks>
        /// Two quadratics joined at the moment of contact. Making the value
        /// there equal to windup makes their slopes match too, so the spin
        /// speeds up and slows down without a kink.
        /// </remarks>
        private float SpinProgress()
        {
            float t = Mathf.Clamp01(strikeClock * AttacksPerSecond);
            float h = windup;

            return t < h
                ? t * t / h
                : 1f - (1f - t) * (1f - t) / (1f - h);
        }

        private float AttacksPerSecond => attacksPerSecond * AttackSpeedMultiplier;

        private void Simulate(Vector3 ownerPosition, bool attacking, float deltaTime)
        {
            float period = 1f / AttacksPerSecond;
            float hitTime = period * windup;
            float remaining = deltaTime;

            // Walk the attack timeline in pieces, so a long frame that spans
            // the moment of contact still lands exactly on the target before
            // carrying on into the follow-through.
            while (remaining > 0f)
            {
                if (attacking && !hasStruck)
                {
                    float untilHit = hitTime - strikeClock;

                    if (untilHit <= remaining)
                    {
                        cutPosition = attackPoint;
                        cutVelocity = strikeVelocity;
                        hasStruck = true;
                        strikeClock = hitTime;
                        remaining -= Mathf.Max(0f, untilHit);

                        Struck?.Invoke(cutPosition, cutVelocity);
                        continue;
                    }

                    Approach(remaining, untilHit);
                    strikeClock += remaining;
                    return;
                }

                // Not attacking means coasting out the release with no
                // further strikes, so there is no end of period to stop at.
                float step = attacking ? Mathf.Min(remaining, period - strikeClock) : remaining;
                Coast(Mathf.Max(0f, step));
                strikeClock += step;
                remaining -= step;

                if (attacking && strikeClock >= period)
                {
                    BeginStrike(ownerPosition);
                }
            }
        }

        /// <summary>
        /// Advances along the cubic that starts at the blade's current position
        /// and velocity and ends at the target with strike velocity after
        /// <paramref name="untilHit"/> seconds.
        /// </summary>
        /// <remarks>
        /// Re-solving from the current state every frame is a standard guidance
        /// law: it is exact when nothing changes, and when the cursor moves the
        /// blade bends smoothly onto the new path instead of snapping.
        /// </remarks>
        private void Approach(float step, float untilHit)
        {
            float u = step / untilHit;
            float u2 = u * u;
            float u3 = u2 * u;

            Vector3 p0 = cutPosition;
            Vector3 m0 = cutVelocity * untilHit;
            Vector3 p1 = attackPoint;
            Vector3 m1 = strikeVelocity * untilHit;

            cutPosition = (2f * u3 - 3f * u2 + 1f) * p0
                          + (u3 - 2f * u2 + u) * m0
                          + (-2f * u3 + 3f * u2) * p1
                          + (u3 - u2) * m1;

            cutVelocity = ((6f * u2 - 6f * u) * p0
                           + (3f * u2 - 4f * u + 1f) * m0
                           + (-6f * u2 + 6f * u) * p1
                           + (3f * u2 - 2f * u) * m1) / untilHit;
        }

        /// <summary>Free flight under linear drag, integrated exactly so the frame rate cannot change how far it carries.</summary>
        private void Coast(float step)
        {
            if (followThroughDrag <= 0.0001f)
            {
                cutPosition += cutVelocity * step;
                return;
            }

            float decay = Mathf.Exp(-followThroughDrag * step);
            cutPosition += cutVelocity * ((1f - decay) / followThroughDrag);
            cutVelocity *= decay;
        }

        /// <summary>Turns the simulated cut point into a handle position and blade rotation.</summary>
        private void StrikePose(Vector3 ownerPosition, float deltaTime, out Vector3 position, out Quaternion rotation)
        {
            Vector3 radial = cutPosition - ownerPosition;
            radial.y = 0f;
            radial = radial.sqrMagnitude > 0.0001f ? radial.normalized : aim;

            // Point the tip outward from the player, but let it trail behind
            // the motion: the blade sweeps through a cut like something held,
            // rather than sliding sideways like something carried.
            Vector3 planarVelocity = new Vector3(cutVelocity.x, 0f, cutVelocity.z);
            Vector3 trailing = radial - planarVelocity * tipDrag;
            trailing = trailing.sqrMagnitude > 0.0001f ? trailing.normalized : radial;

            float tilt = strikeTilt * Mathf.Deg2Rad;
            Vector3 desiredTip = trailing * Mathf.Cos(tilt) + Vector3.down * Mathf.Sin(tilt);

            float t = 1f - Mathf.Exp(-tipSharpness * deltaTime);
            tipDirection = Vector3.Slerp(tipDirection, desiredTip, t).normalized;

            // Flat facing up, so an edge leads the cut, then banked into the
            // direction of travel by however fast it is moving sideways. A tip
            // pointing straight down (just picked up from orbit) falls back to
            // facing the player like the orbit pose.
            Vector3 across = Vector3.Cross(Vector3.up, tipDirection);
            float lateral = strikeSpeed > 0f
                ? Mathf.Clamp(Vector3.Dot(cutVelocity, across) / strikeSpeed, -1f, 1f)
                : 0f;

            rotation = Quaternion.AngleAxis(swingRoll * lateral, tipDirection) * TipRotation(tipDirection, -radial);

            // Spin about the cut point rather than the handle, so the part of
            // the blade that has to connect stays exactly on its path.
            Quaternion spin = Quaternion.AngleAxis(spinDegrees * SpinProgress(), spinAxis);

            position = cutPosition - spin * tipDirection * CutDistance;
            rotation = spin * rotation;
        }

        protected override void OnSnap()
        {
            attackPoint = Owner.position;
            cutPosition = Owner.position;
            cutVelocity = Vector3.zero;
            tipDirection = Vector3.down;
            strikeClock = 0f;
            hasStruck = false;
            strikeTilt = swingTilt;
            spinDegrees = 0f;
            spinAxis = Vector3.up;
            engage = 0f;
            releaseRemaining = 0f;
            wasHeld = false;
            wasAttacking = false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();

            bladeLength = Mathf.Max(0f, bladeLength);
            releaseDelay = Mathf.Max(0f, releaseDelay);
            barrierRadius = Mathf.Max(0f, barrierRadius);
            barrierPadding = Mathf.Max(0f, barrierPadding);
            strikeSpeed = Mathf.Max(0f, strikeSpeed);
            followThroughDrag = Mathf.Max(0f, followThroughDrag);
            tipDrag = Mathf.Max(0f, tipDrag);
            tipSharpness = Mathf.Max(0f, tipSharpness);
            maxSpins = Mathf.Max(minSpins, maxSpins);
        }
    }
}
