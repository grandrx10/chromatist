using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Scarlett the Scythe. As a main blade, rakes jagged claw swipes across
    /// the cursor, alternating left to right and right to left, healing the
    /// player for a share of the damage. As a secondary blade, reaps one
    /// massive tear across the cursor with a far larger share of lifesteal.
    /// </summary>
    /// <remarks>
    /// Model convention matches every blade: tip down local -Y, flat facing
    /// local +Z, pivot at the centre of the handle.
    ///
    /// Primary. Each swipe is a <see cref="ScarlettClaw"/> centred on the
    /// cursor, pulled in to the max range if the cursor is further. The
    /// scythe itself swings across in front of the player in step with it,
    /// and finishes each swing exactly where the next one starts, so the
    /// alternation flows without a reset.
    ///
    /// Bloodlust: the lower the player's health, the faster the swipes come,
    /// rising linearly from the base rate at full health to
    /// <see cref="lowHealthAttackSpeed"/> times it at zero.
    ///
    /// Secondary. A huge claw tear is raked across the cursor, at any
    /// distance. The scythe reaps round the player at the same moment, but
    /// only as a flourish: what is hit is decided by the tear alone. The
    /// ability counts as finished only once the tear has closed.
    /// </remarks>
    public sealed class ScarlettScythe : Blade
    {
        [Header("Primary: claw")]
        [SerializeField]
        [Tooltip("Swipes per second at full health.")]
        [Range(0.2f, 12f)]
        private float attacksPerSecond = 2.5f;

        [SerializeField]
        [Tooltip("Attack speed multiplier as health approaches zero. Rises linearly from 1 at full health.")]
        [Range(1f, 4f)]
        private float lowHealthAttackSpeed = 2f;

        [SerializeField]
        [Tooltip("Furthest from the player a swipe can be centred. A cursor beyond it is pulled in.")]
        [Min(0f)]
        private float maxRange = 6f;

        [SerializeField]
        [Tooltip("Fraction of the damage dealt that heals the player.")]
        [Range(0f, 1f)]
        private float lifesteal = 0.05f;

        [SerializeField]
        [Tooltip("Height of the swipe relative to the middle of the player's collider.")]
        private float cutHeightOffset;

        [SerializeField] private ScarlettClaw.Settings claw = new ScarlettClaw.Settings();

        [Header("Primary: scythe")]
        [SerializeField]
        [Tooltip("Distance from the player the scythe is held at while attacking.")]
        [Min(0f)]
        private float holdDistance = 1f;

        [SerializeField]
        [Tooltip("Height the scythe holds at, relative to the middle of the player's collider.")]
        private float holdHeightOffset;

        [SerializeField]
        [Tooltip("Degrees the scythe swings across in front of the player with each swipe.")]
        [Range(0f, 180f)]
        private float swingArc = 100f;

        [SerializeField]
        [Tooltip("Degrees the tip angles below horizontal while attacking.")]
        [Range(0f, 80f)]
        private float tipTilt = 20f;

        [SerializeField]
        [Tooltip("Seconds the scythe takes to follow a change of aim.")]
        [Min(0f)]
        private float aimFollowTime = 0.05f;

        [SerializeField]
        [Tooltip("Swap which way the scythe faces for each swing direction. Tick this if it swings with the back of its blade leading.")]
        private bool invertFacing;

        [Header("Secondary: reap")]
        [SerializeField]
        [Tooltip("Fraction of the damage dealt that heals the player.")]
        [Range(0f, 1f)]
        private float reapLifesteal = 0.2f;

        [SerializeField]
        private ScarlettClaw.Settings reap = new ScarlettClaw.Settings
        {
            damage = 30f,
            span = 11f,
            speed = 60f,
            thickness = 2f,
            bow = 2f,
            kinks = 3,
            kinkSize = 0.35f,
            raggedness = 0.6f,
            splinters = 12,
            splinterLength = 1.8f,
            hold = 0.12f,
            fadeTime = 0.7f,
            lift = 4f,
        };

        [SerializeField]
        [Tooltip("Seconds to bring the scythe round before the tear opens.")]
        [Min(0f)]
        private float reapWindup = 0.12f;

        [SerializeField]
        [Tooltip("Degrees the scythe swings round the player. Only an action; the tear decides what is hit.")]
        [Range(10f, 360f)]
        private float reapArc = 220f;

        [SerializeField]
        [Tooltip("Seconds the scythe's swing takes.")]
        [Min(0.02f)]
        private float reapDuration = 0.25f;

        [SerializeField]
        [Tooltip("Distance from the player the scythe is held at during the reap.")]
        [Min(0f)]
        private float reapHoldDistance = 2f;

        [Header("Blood")]
        [SerializeField]
        [Tooltip("Blood spewed out of each enemy a claw cuts, in place of the usual slash streak.")]
        private ScarlettBlood.Settings blood = new ScarlettBlood.Settings();

        [SerializeField]
        [Tooltip("Blood spewed out of each enemy the reap cuts.")]
        private ScarlettBlood.Settings reapBlood = new ScarlettBlood.Settings
        {
            droplets = 22,
            spread = 70f,
            minSpeed = 7f,
            maxSpeed = 20f,
            size = 0.2f,
            lifetime = 0.6f,
        };

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Base material for the swipes and the reap. Empty uses the scythe's own. It is copied and tinted, never edited.")]
        private Material swipeMaterial;

        [SerializeField]
        [Tooltip("Colour of the slash itself: what the tear shows where nothing bright is behind it. Keep it strongly saturated, or the spot colour threshold crushes it to black or white. Push intensity above 1 to make it bloom.")]
        [ColorUsage(false, true)]
        private Color swipeColour = new Color(1f, 0.04f, 0.08f);

        [SerializeField]
        [Tooltip("Colour that everything under the slash turns: bright surfaces and edges behind the tear show in this, everything else in the slash colour, never a mix. Needs a material that shows what is behind it (NeoNoir/Tear). Keep it strongly saturated too.")]
        [ColorUsage(false, true)]
        private Color overshadowColour = new Color(1f, 0.35f, 0.4f);

        [SerializeField]
        [Tooltip("Colour of the blood spewed out of enemies. Keep it distinct from the slash colour: the blood sprays along the tear and vanishes against it otherwise. Push intensity above 1 to make it glow.")]
        [ColorUsage(false, true)]
        private Color bloodColour = new Color(1.2f, 0.45f, 0.62f);

        // Primary state.
        private Vector3 aim = Vector3.forward;
        private Vector3 cursorPoint;
        private float clawSide = 1f;
        private float attackCooldown;
        private float engage;

        private bool swinging;
        private float swingTime;
        private float swingDuration;
        private float swingFrom;
        private float swingTo;
        private float holdAngle;
        private float holdYaw;
        private float holdYawVelocity;

        // Degrees the scythe is turned about its own length, 0 or 180, so its
        // blade leads the current swing. Set once as each swing starts and
        // held for the whole of it.
        private float facing;

        // Secondary state.
        private enum ReapPhase { None, Windup, Sweep, Linger }

        private ReapPhase reapPhase;
        private float reapClock;
        private float reapAngle;
        private float reapBlend;
        private Vector3 reapAim = Vector3.forward;
        private Vector3 reapPoint;
        private ScarlettClaw reapTear;

        private Material tintedMaterial;
        private Material tintedSource;
        private Color tintedColour;
        private Color tintedOvershadow;
        private Color tintedBlood;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BehindColorId = Shader.PropertyToID("_BehindColor");
        private static readonly int BloodColorId = Shader.PropertyToID("_BloodColor");

        /// <summary>
        /// Swipes per second right now: the base rate, raised linearly as the
        /// player's health falls, up to <see cref="lowHealthAttackSpeed"/>
        /// times it at zero, and scaled by any attack speed buff.
        /// </summary>
        public float CurrentAttacksPerSecond
        {
            get
            {
                float missing = Health != null ? 1f - Mathf.Clamp01(Health.Fraction) : 0f;
                return attacksPerSecond * Mathf.Lerp(1f, lowHealthAttackSpeed, missing) * AttackSpeedMultiplier;
            }
        }

        /// <summary>
        /// A tinted copy of the swipe material, rebuilt only when the source or
        /// the colour changes. A copy, so tinting the swipes never recolours
        /// the scythe itself or anything else sharing its material.
        /// </summary>
        private Material ResolvedSwipeMaterial
        {
            get
            {
                Material source = swipeMaterial;

                if (source == null)
                {
                    Renderer own = GetComponentInChildren<Renderer>();
                    source = own != null ? own.sharedMaterial : null;
                }

                if (source == null)
                {
                    return null;
                }

                if (tintedMaterial == null || tintedSource != source || tintedColour != swipeColour
                    || tintedOvershadow != overshadowColour || tintedBlood != bloodColour)
                {
                    if (tintedMaterial != null)
                    {
                        Destroy(tintedMaterial);
                    }

                    tintedMaterial = new Material(source) { name = source.name + " (Scarlett swipe)" };
                    tintedSource = source;
                    tintedColour = swipeColour;
                    tintedOvershadow = overshadowColour;
                    tintedBlood = bloodColour;

                    if (tintedMaterial.HasProperty(BloodColorId))
                    {
                        tintedMaterial.SetColor(BloodColorId, bloodColour);
                    }

                    if (tintedMaterial.HasProperty(BehindColorId))
                    {
                        tintedMaterial.SetColor(BehindColorId, overshadowColour);
                    }

                    if (tintedMaterial.HasProperty(BaseColorId))
                    {
                        tintedMaterial.SetColor(BaseColorId, swipeColour);
                    }

                    if (tintedMaterial.HasProperty(ColorId))
                    {
                        tintedMaterial.SetColor(ColorId, swipeColour);
                    }
                }

                return tintedMaterial;
            }
        }

        protected override void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime)
        {
            Vector3 ownerPosition = Owner.position;
            bool held = PrimaryHeld;

            if (held)
            {
                UpdateAim(ownerPosition);
            }

            bool wasEngaged = engage > 0f;

            attackCooldown = Mathf.Max(0f, attackCooldown - deltaTime);

            if (held && attackCooldown <= 0f)
            {
                StartSwipe();
            }

            // A swing already started always plays out, even if the button
            // was let go a frame after the click.
            engage = StepEngage(engage, held || swinging, deltaTime);

            if (!wasEngaged && engage > 0f)
            {
                // Coming out of orbit: start the hold already facing the aim,
                // and let the orbit blend carry the visual transition.
                holdYaw = Yaw(aim);
                holdYawVelocity = 0f;
            }

            TickSwing(deltaTime);
            TickReap(ownerPosition, deltaTime);

            Vector3 position = orbitPosition;
            Quaternion rotation = orbitRotation;

            float primaryWeight = Ease(engage);
            if (primaryWeight > 0f)
            {
                SwingPose(ownerPosition, deltaTime, out Vector3 swingPosition, out Quaternion swingRotation);
                position = Vector3.Lerp(position, swingPosition, primaryWeight);
                rotation = Quaternion.Slerp(rotation, swingRotation, primaryWeight);
            }

            float reapWeight = Ease(reapBlend);
            if (reapWeight > 0f)
            {
                ReapPose(ownerPosition, out Vector3 reapPosition, out Quaternion reapRotation);
                position = Vector3.Lerp(position, reapPosition, reapWeight);
                rotation = Quaternion.Slerp(rotation, reapRotation, reapWeight);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void UpdateAim(Vector3 ownerPosition)
        {
            if (!TryGetCursorPoint(OwnerMiddleHeight() + cutHeightOffset, out Vector3 point))
            {
                return;
            }

            Vector3 desired = point - ownerPosition;
            desired.y = 0f;

            // Clicking on the player gives no direction, so keep the last one.
            if (desired.sqrMagnitude > 0.0001f)
            {
                aim = desired.normalized;
            }

            // Pulled in along the line to the cursor, never sideways, so the
            // swipe still lands in the direction the player is pointing.
            if (desired.magnitude > maxRange)
            {
                point = new Vector3(ownerPosition.x, point.y, ownerPosition.z) + aim * maxRange;
            }

            cursorPoint = point;
        }

        // ---- primary ----------------------------------------------------

        private void StartSwipe()
        {
            attackCooldown = 1f / CurrentAttacksPerSecond;

            // Cross(up, aim) is the player's right as they face the cursor,
            // so a positive side rakes left to right.
            Vector3 across = Vector3.Cross(Vector3.up, aim) * clawSide;

            ScarlettClaw.Spawn(cursorPoint, across, aim, Owner.position, claw, blood, ResolvedSwipeMaterial,
                               swipeColour, Lifesteal);

            // The scythe swings the same way, over the time the claw takes to
            // cross, from the side the last swing finished on.
            swinging = true;
            swingTime = 0f;
            swingDuration = claw.span / claw.speed;
            swingFrom = -clawSide * swingArc * 0.5f;
            swingTo = clawSide * swingArc * 0.5f;
            FaceSwing(clawSide);

            clawSide = -clawSide;
        }

        private void Lifesteal(float dealt)
        {
            if (Health != null)
            {
                Health.Heal(dealt * lifesteal);
            }
        }

        private void TickSwing(float deltaTime)
        {
            if (!swinging)
            {
                return;
            }

            swingTime += deltaTime;

            // Ease-out: the scythe snaps through the start of the swing and
            // settles into the end, so it reads as a rake rather than a pan.
            float progress = Mathf.Clamp01(swingTime / Mathf.Max(0.0001f, swingDuration));
            float eased = 1f - (1f - progress) * (1f - progress);
            holdAngle = Mathf.Lerp(swingFrom, swingTo, eased);

            if (progress >= 1f)
            {
                swinging = false;
            }
        }

        private void SwingPose(Vector3 ownerPosition, float deltaTime, out Vector3 position, out Quaternion rotation)
        {
            holdYaw = Mathf.SmoothDampAngle(holdYaw, Yaw(aim), ref holdYawVelocity,
                                            Mathf.Max(0.0001f, aimFollowTime), Mathf.Infinity, deltaTime);

            Vector3 radial = Quaternion.Euler(0f, holdYaw + holdAngle, 0f) * Vector3.forward;

            position = ownerPosition + radial * holdDistance;
            position.y = OwnerMiddleHeight() + holdHeightOffset;
            rotation = Faced(Tilted(radial, tipTilt), -radial);
        }

        /// <summary>
        /// Flips the scythe to lead a swing going <paramref name="direction"/>:
        /// +1 left to right, -1 right to left. At once, not over time: called
        /// as a swing starts, so each swing is made in one facing from start
        /// to finish and the flip happens between them.
        /// </summary>
        private void FaceSwing(float direction)
        {
            // Turned for left to right: as modelled, the blade leads a right
            // to left swing.
            bool turned = (direction > 0f) != invertFacing;
            facing = turned ? 180f : 0f;
        }

        /// <summary>The blade's rotation for a tip direction, turned about its own length by the current facing.</summary>
        private Quaternion Faced(Vector3 tip, Vector3 fallbackFlat)
        {
            return Quaternion.AngleAxis(facing, tip) * TipRotation(tip, fallbackFlat);
        }

        // ---- secondary --------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            Vector3 ownerPosition = Owner.position;
            float height = OwnerMiddleHeight() + cutHeightOffset;

            // No range limit: the tear lands wherever the cursor is.
            if (!TryGetCursorPoint(height, out reapPoint))
            {
                reapPoint = ownerPosition + Owner.forward * reap.span * 0.5f;
                reapPoint.y = height;
            }

            Vector3 direction = reapPoint - ownerPosition;
            direction.y = 0f;
            reapAim = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            reapPhase = ReapPhase.Windup;

            // The reap always rakes left to right.
            FaceSwing(1f);
            reapClock = 0f;
            reapAngle = -reapArc * 0.5f;
            reapTear = null;
        }

        private void TickReap(Vector3 ownerPosition, float deltaTime)
        {
            reapBlend = StepEngage(reapBlend, reapPhase != ReapPhase.None, deltaTime);

            if (reapPhase == ReapPhase.None)
            {
                return;
            }

            reapClock += deltaTime;

            if (reapPhase == ReapPhase.Windup)
            {
                if (reapClock < reapWindup)
                {
                    return;
                }

                reapClock -= reapWindup;
                reapPhase = ReapPhase.Sweep;

                // Aimed where the cursor is now, not where it was at the
                // click: the windup is long enough for it to have moved.
                if (TryGetCursorPoint(OwnerMiddleHeight() + cutHeightOffset, out Vector3 point))
                {
                    reapPoint = point;
                }

                Vector3 toTarget = reapPoint - ownerPosition;
                toTarget.y = 0f;
                Vector3 tearAim = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : reapAim;

                // Left to right across the cursor, as the scythe swings.
                Vector3 across = Vector3.Cross(Vector3.up, tearAim);
                reapTear = ScarlettClaw.Spawn(reapPoint, across, tearAim, ownerPosition, reap, reapBlood,
                                              ResolvedSwipeMaterial, swipeColour, ReapLifesteal);
            }

            if (reapPhase == ReapPhase.Sweep)
            {
                float progress = Mathf.Clamp01(reapClock / reapDuration);
                reapAngle = Mathf.Lerp(-reapArc * 0.5f, reapArc * 0.5f, Ease(progress));

                if (progress >= 1f)
                {
                    reapPhase = ReapPhase.Linger;
                }

                return;
            }

            // Done once the tear has closed and destroyed itself.
            if (reapTear == null)
            {
                reapPhase = ReapPhase.None;
                FinishSecondary();
            }
        }

        private void ReapLifesteal(float dealt)
        {
            if (Health != null)
            {
                Health.Heal(dealt * reapLifesteal);
            }
        }

        private Vector3 ReapRadial(float angle)
        {
            return Quaternion.AngleAxis(angle, Vector3.up) * reapAim;
        }

        private void ReapPose(Vector3 ownerPosition, out Vector3 position, out Quaternion rotation)
        {
            Vector3 radial = ReapRadial(reapAngle);

            position = ownerPosition + radial * reapHoldDistance;
            position.y = OwnerMiddleHeight() + holdHeightOffset;
            rotation = Faced(Tilted(radial, tipTilt), -radial);
        }

        // ---- shared -----------------------------------------------------

        private static Vector3 Tilted(Vector3 flat, float degrees)
        {
            float tilt = degrees * Mathf.Deg2Rad;
            return flat * Mathf.Cos(tilt) + Vector3.down * Mathf.Sin(tilt);
        }

        private static float Yaw(Vector3 flat)
        {
            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        protected override void OnSnap()
        {
            engage = 0f;
            attackCooldown = 0f;
            swinging = false;
            holdAngle = -clawSide * swingArc * 0.5f;
            reapBlend = 0f;
        }

        private void OnDisable()
        {
            // Hidden mid-reap: the ability must still end, or the rotation
            // would wait on it forever.
            if (reapPhase != ReapPhase.None)
            {
                reapPhase = ReapPhase.None;
                FinishSecondary();
            }

            // A tear already open is left to fade by itself.
            reapTear = null;
        }

        private void OnDestroy()
        {
            // Claws still in flight keep a reference, but they outlive the
            // scythe by a fraction of a second at most and Unity tolerates a
            // destroyed material on a renderer.
            if (tintedMaterial != null)
            {
                Destroy(tintedMaterial);
            }
        }
    }
}
