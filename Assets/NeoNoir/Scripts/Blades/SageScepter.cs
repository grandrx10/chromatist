using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Sage the Scepter. As a main blade, plants a lotus bud at the cursor
    /// that blooms and bursts, damaging enemies round it. As a secondary
    /// blade, grows a trail of lotuses out from the player, steering toward
    /// the cursor as it goes, that burst when stepped on: hurting enemies,
    /// healing the player.
    /// </summary>
    /// <remarks>
    /// Model convention matches every blade: tip down local -Y, flat facing
    /// local +Z. A scepter is held head up, so the tip is its foot.
    ///
    /// Primary. While the attack is held, a <see cref="SageLotus"/> is
    /// planted on the floor under the cursor every 1 / <see cref="attacksPerSecond"/>
    /// seconds, scaled by any attack speed buff. The damage is delayed by the
    /// bloom: it lands where the cursor was when the bud was planted. The
    /// scepter is held upright in front of the player, head leaning toward
    /// the cursor, and flicks further toward it on each cast.
    ///
    /// Secondary. Lotuses are planted one after another, every
    /// <see cref="trailInterval"/> seconds, each <see cref="trailSpacing"/>
    /// on from the last. The trail's heading turns toward wherever the cursor
    /// is now, at up to <see cref="trailTurnRate"/>, so it can be steered
    /// while it grows. It stops after <see cref="trailCount"/>. The ability
    /// counts as finished at once, so the next blade can be used straight
    /// away, but the scepter stays out, hovering over the player's head,
    /// until the trail is done.
    ///
    /// The lotuses, rings and petals all use <see cref="effectMaterial"/>,
    /// expected to be NeoNoir/Flat in Sage's colour, so they dissolve with
    /// the same dither as the blades.
    /// </remarks>
    public sealed class SageScepter : Blade
    {
        [Header("Primary: lotus")]
        [SerializeField]
        [Tooltip("Lotuses planted per second while the attack is held.")]
        [Range(0.2f, 10f)]
        private float attacksPerSecond = 2f;

        [SerializeField]
        [Tooltip("Furthest from the player a lotus can be planted. A cursor further out plants at this distance toward it. 0 is no limit.")]
        [Min(0f)]
        private float maxRange = 14f;

        [SerializeField] private SageLotus.Settings lotus = new SageLotus.Settings();

        [Header("Primary: hold")]
        [SerializeField]
        [Tooltip("Distance from the player to the scepter's pivot, in the direction of the cursor, while casting.")]
        [Min(0f)]
        private float holdDistance = 1f;

        [SerializeField]
        [Tooltip("Height the scepter holds at while casting, relative to the middle of the player's collider.")]
        private float holdHeightOffset = 0.3f;

        [SerializeField]
        [Tooltip("Degrees the head leans toward the cursor while held.")]
        [Range(-80f, 80f)]
        private float headLean = 15f;

        [SerializeField]
        [Tooltip("Extra degrees the head flicks toward the cursor on each cast.")]
        [Range(0f, 80f)]
        private float castFlick = 30f;

        [SerializeField]
        [Tooltip("Seconds a flick takes to settle.")]
        [Min(0.01f)]
        private float flickTime = 0.2f;

        [SerializeField]
        [Tooltip("Extra degrees to turn the scepter by while held, on top of leaning it toward the cursor.")]
        private Vector3 holdRotation;

        [SerializeField]
        [Tooltip("Seconds the scepter takes to follow a change of aim.")]
        [Min(0f)]
        private float aimFollowTime = 0.05f;

        [Header("Secondary: lotus trail")]
        [SerializeField]
        [Tooltip("Lotuses in the trail.")]
        [Range(1, 128)]
        private int trailCount = 32;

        [SerializeField]
        [Tooltip("Seconds between one lotus in the trail and the next.")]
        [Min(0.005f)]
        private float trailInterval = 0.04f;

        [SerializeField]
        [Tooltip("Distance between lotuses in the trail.")]
        [Min(0.05f)]
        private float trailSpacing = 0.8f;

        [SerializeField]
        [Tooltip("Distance from the player's feet to the first lotus, so it does not start under them.")]
        [Min(0f)]
        private float trailStartDistance = 1.2f;

        [SerializeField]
        [Tooltip("Most degrees per second the trail turns toward the cursor at. Very high follows the cursor exactly; 0 grows dead straight.")]
        [Min(0f)]
        private float trailTurnRate = 720f;

        [SerializeField]
        private SageLotus.Settings trailLotus = new SageLotus.Settings
        {
            size = 0.7f,
            bloomTime = 0.1f,
            burstOnBloom = false,
            stepRadius = 0.45f,
            lifetime = 12f,
            damage = 5f,
            burstRadius = 1f,
            heal = 5f,
            petals = 8,
        };

        [Header("Secondary: hover")]
        [SerializeField]
        [Tooltip("Height above the top of the player's collider the scepter hovers at while the trail grows.")]
        private float hoverHeight = 0.6f;

        [SerializeField]
        [Tooltip("Degrees per second the hovering scepter turns at.")]
        private float hoverSpin = 90f;

        [SerializeField]
        [Tooltip("Height the hovering scepter bobs up and down by.")]
        [Min(0f)]
        private float hoverBob = 0.08f;

        [SerializeField]
        [Tooltip("Bobs per second.")]
        [Min(0f)]
        private float hoverBobRate = 1f;

        [SerializeField]
        [Tooltip("Seconds the scepter keeps hovering after the last lotus is planted.")]
        [Min(0f)]
        private float hoverLinger = 0.2f;

        [SerializeField]
        [Tooltip("Degrees to turn the scepter by while hovering, so it hangs the way you want. Zero is as modelled.")]
        private Vector3 hoverRotation;

        [SerializeField]
        [Tooltip("Seconds the scepter takes to fly to and from its hover.")]
        [Min(0.01f)]
        private float hoverEngageTime = 0.15f;

        [Header("Kill")]
        [SerializeField]
        [Tooltip("Lotus petals flung out from an enemy Sage kills, by any of her attacks.")]
        [Range(0, 64)]
        private int killPetals = 16;

        [SerializeField]
        private RosePetal.Settings killPetal = new RosePetal.Settings
        {
            size = new Vector2(0.16f, 0.3f),
            scatter = 0.3f,
            burstSpeed = 3f,
            carrySpeed = 3.5f,
            drag = 4f,
            fallSpeed = 0.3f,
            floatTime = 0.5f,
            floatJitter = 0.5f,
            fadeTime = 0.6f,
        };

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Material for the lotuses, rings and petals, normally NeoNoir/Flat in Sage's colour. Empty uses the scepter's own.")]
        private Material effectMaterial;

        [SerializeField]
        [Tooltip("A lotus model, one unit across and facing up. Empty uses a built one.")]
        private GameObject lotusModel;

        [SerializeField]
        [Tooltip("Colour of the slash on enemies a burst hits. Black leaves it the plain inversion.")]
        [ColorUsage(false, true)]
        private Color sageColour = new Color(0.45f, 1f, 0.55f);

        // Primary state.
        private Vector3 aim = Vector3.forward;
        private float holdYaw;
        private float holdYawVelocity;
        private float engage;
        private float cooldown;
        private float flick;

        // Secondary state.
        private bool trailRunning;
        private int trailPlaced;
        private float trailClock;
        private float trailFloor;
        private Vector3 trailHead;
        private Vector3 trailHeading = Vector3.forward;
        private float hoverRemaining;
        private float hoverBlend;
        private float hoverYaw;

        private float AttacksPerSecond => attacksPerSecond * AttackSpeedMultiplier;

        private bool Hovering => trailRunning || hoverRemaining > 0f;

        /// <summary>Hovering over the player until the trail is grown, so the arsenal holds off its fade out.</summary>
        public override bool Lingering => Hovering;

        /// <summary>The effect material, or the scepter's own when none is set. Never copied or edited.</summary>
        private Material ResolvedEffectMaterial => effectMaterial != null ? effectMaterial : OwnMaterial;

        /// <summary>A burst of lotus petals from the body.</summary>
        protected override void OnKill(EnemyHealth enemy, Bounds body)
        {
            RosePetal.Scatter(body.center, killPetals, killPetal, ResolvedEffectMaterial);
        }

        protected override void Awake()
        {
            base.Awake();

            // While the scepter is being equipped, not mid-fight.
            SageLotus.Prewarm(lotusModel);
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

            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            flick = Mathf.MoveTowards(flick, 0f, deltaTime / flickTime);

            if (held && cooldown <= 0f && TryGetCursorPoint(Floor(), out Vector3 point))
            {
                Cast(ownerPosition, point);
                cooldown = 1f / AttacksPerSecond;
            }

            engage = StepEngage(engage, held, deltaTime);

            if (!wasEngaged && engage > 0f)
            {
                // Coming out of orbit: face the aim straight away rather than
                // swinging round to it from wherever the yaw was left.
                holdYaw = Yaw(aim);
                holdYawVelocity = 0f;
            }

            holdYaw = Mathf.SmoothDampAngle(holdYaw, Yaw(aim), ref holdYawVelocity,
                                            Mathf.Max(0.0001f, aimFollowTime), Mathf.Infinity, deltaTime);

            TickTrail(deltaTime);

            Vector3 position = orbitPosition;
            Quaternion rotation = orbitRotation;

            float weight = Ease(engage);
            if (weight > 0f)
            {
                HoldPose(ownerPosition, out Vector3 holdPosition, out Quaternion holdPoseRotation);
                position = Vector3.Lerp(position, holdPosition, weight);
                rotation = Quaternion.Slerp(rotation, holdPoseRotation, weight);
            }

            float hoverWeight = Ease(hoverBlend);
            if (hoverWeight > 0f)
            {
                HoverPose(ownerPosition, out Vector3 hoverPosition, out Quaternion hoverPoseRotation);
                position = Vector3.Lerp(position, hoverPosition, hoverWeight);
                rotation = Quaternion.Slerp(rotation, hoverPoseRotation, hoverWeight);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void UpdateAim(Vector3 ownerPosition)
        {
            if (!TryGetCursorPoint(OwnerMiddleHeight(), out Vector3 point))
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
        }

        /// <summary>The height of the floor the player stands on: the bottom of their collider.</summary>
        private float Floor()
        {
            return OwnerCollider != null ? OwnerCollider.bounds.min.y : Owner.position.y;
        }

        private static float Yaw(Vector3 flat)
        {
            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        // ---- pose ---------------------------------------------------------

        /// <summary>Upright in front of the player, head leaning toward the cursor, further on a cast.</summary>
        private void HoldPose(Vector3 ownerPosition, out Vector3 position, out Quaternion rotation)
        {
            Vector3 direction = Quaternion.Euler(0f, holdYaw, 0f) * Vector3.forward;

            position = ownerPosition + direction * holdDistance;
            position.y = OwnerMiddleHeight() + holdHeightOffset;

            // Eased out, so the flick snaps forward then settles back.
            float lean = (headLean + castFlick * flick * flick) * Mathf.Deg2Rad;
            Vector3 head = Vector3.up * Mathf.Cos(lean) + direction * Mathf.Sin(lean);

            rotation = TipRotation(-head, -direction) * Quaternion.Euler(holdRotation);
        }

        /// <summary>Straight over the player's head, turning slowly and bobbing.</summary>
        private void HoverPose(Vector3 ownerPosition, out Vector3 position, out Quaternion rotation)
        {
            float top = OwnerCollider != null ? OwnerCollider.bounds.max.y : ownerPosition.y;
            float bob = Mathf.Sin(Time.time * hoverBobRate * Mathf.PI * 2f) * hoverBob;

            // The player's own position, not the smoothed orbit anchor: it
            // stays directly above them as they move.
            position = new Vector3(ownerPosition.x, top + hoverHeight + bob, ownerPosition.z);
            rotation = Quaternion.Euler(0f, hoverYaw, 0f) * Quaternion.Euler(hoverRotation);
        }

        // ---- primary ------------------------------------------------------

        private void Cast(Vector3 ownerPosition, Vector3 point)
        {
            Vector3 offset = point - ownerPosition;
            offset.y = 0f;

            if (maxRange > 0f && offset.sqrMagnitude > maxRange * maxRange)
            {
                offset = offset.normalized * maxRange;
            }

            Vector3 floorPoint = new Vector3(ownerPosition.x + offset.x, Floor(), ownerPosition.z + offset.z);
            SageLotus.Plant(floorPoint, lotus, ResolvedEffectMaterial, lotusModel, Owner, sageColour, this);

            flick = 1f;
        }

        // ---- secondary ----------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            Vector3 ownerPosition = Owner.position;
            trailFloor = Floor();
            trailHead = new Vector3(ownerPosition.x, trailFloor, ownerPosition.z);

            Vector3 direction = TryGetCursorPoint(trailFloor, out Vector3 point) ? point - trailHead : Owner.forward;
            direction.y = 0f;
            trailHeading = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            trailPlaced = 0;
            // Due at once, so the first lotus appears on the cast.
            trailClock = trailInterval;
            trailRunning = true;
            hoverRemaining = 0f;

            // Done as far as the rotation is concerned: the next blade can
            // be used straight away while the scepter hovers and the trail grows.
            FinishSecondary();
        }

        private void TickTrail(float deltaTime)
        {
            if (trailRunning)
            {
                trailClock += deltaTime;

                // More than one per frame when the interval is shorter than a frame.
                while (trailRunning && trailClock >= trailInterval)
                {
                    trailClock -= trailInterval;
                    PlantNext();
                }
            }
            else
            {
                hoverRemaining = Mathf.Max(0f, hoverRemaining - deltaTime);
            }

            if (Hovering)
            {
                hoverYaw = Mathf.Repeat(hoverYaw + hoverSpin * deltaTime, 360f);
            }

            hoverBlend = Mathf.MoveTowards(hoverBlend, Hovering ? 1f : 0f, deltaTime / hoverEngageTime);
        }

        private void PlantNext()
        {
            Steer();

            float step = trailPlaced == 0 ? trailStartDistance : trailSpacing;
            trailHead += trailHeading * step;

            SageLotus.Plant(trailHead, trailLotus, ResolvedEffectMaterial, lotusModel, Owner, sageColour, this);
            trailPlaced++;

            if (trailPlaced >= trailCount)
            {
                trailRunning = false;
                hoverRemaining = hoverLinger;
            }
        }

        /// <summary>Turns the trail's heading toward where the cursor is now, as far as the turn rate allows for one step.</summary>
        private void Steer()
        {
            if (!TryGetCursorPoint(trailFloor, out Vector3 point))
            {
                return;
            }

            Vector3 toCursor = point - trailHead;
            toCursor.y = 0f;

            // Once the trail reaches the cursor there is nowhere left to turn
            // toward, and chasing it would make the trail double back on
            // itself: carry straight on.
            if (toCursor.sqrMagnitude < trailSpacing * trailSpacing)
            {
                return;
            }

            float turn = trailTurnRate * Mathf.Deg2Rad * trailInterval;
            trailHeading = Vector3.RotateTowards(trailHeading, toCursor.normalized, turn, 0f);
            trailHeading.y = 0f;
            trailHeading.Normalize();
        }

        // ---- lifecycle ----------------------------------------------------

        protected override void OnSnap()
        {
            engage = 0f;
            flick = 0f;
        }

        private void OnDisable()
        {
            // Put away: lotuses already planted stay, but the trail stops
            // growing, and the scepter comes back from orbit next time
            // rather than from its old hover.
            trailRunning = false;
            hoverRemaining = 0f;
            hoverBlend = 0f;
        }
    }
}
