using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Rose the Rapier. As a main blade, holds beside the player and flicks
    /// curving slashes out through the cursor, alternating sides. As a
    /// secondary blade, dashes the player straight at the cursor, leading the
    /// way and leaving a cutting trail behind.
    /// </summary>
    /// <remarks>
    /// Model convention matches every blade: tip down local -Y, flat facing
    /// local +Z, pivot at the centre of the handle.
    ///
    /// Primary. Unlike Luna, the rapier never leaves the player. Each attack is
    /// a short flick in place, and at the release point of the flick a
    /// <see cref="RapierSlash"/> leaves the blade, launched off to the rapier's
    /// side at a random angle so that it curves back in across the cursor.
    /// When the flick finishes, the rapier swings round in front of the player
    /// to the other side if the attack is still held, so slashes alternate
    /// left and right.
    ///
    /// While she is the main blade, every enemy carries a <see cref="RoseMark"/>:
    /// an arc on the floor on one side of it. Slashes that come in through
    /// the arc charge it, and the third bursts it for heavy damage.
    ///
    /// Secondary. A fixed-distance, unsteerable dash through
    /// <see cref="PlayerMotor.DashFixed"/>. The trail damages along the swept
    /// path while the dash runs, then lingers as a damaging line for a moment.
    /// The ability counts as finished only once the trail is gone. The dash
    /// also sheds <see cref="RosePetal"/>s along its path, which drift for a
    /// while after the ability ends and then dissolve on their own.
    /// </remarks>
    public sealed class RoseRapier : Blade
    {
        [Header("Primary: flick")]
        [SerializeField]
        [Tooltip("Slashes per second while the attack is held.")]
        [Range(0.2f, 12f)]
        private float attacksPerSecond = 3f;

        [SerializeField]
        [Tooltip("Distance from the player the rapier holds at while attacking, to the left or right, and the radius of the arc it swings along in front when it crosses over.")]
        private float sideOffset = 0.9f;

        [SerializeField]
        [Tooltip("Height the rapier holds at while attacking or dashing, relative to the middle of the player's collider. 0 is level with the player, and with the slashes.")]
        private float holdHeightOffset;

        [SerializeField]
        [Tooltip("Seconds to swing round the front to the other side between slashes. Also how quickly the hold follows a change of aim.")]
        private float sideSwapTime = 0.08f;

        [SerializeField]
        [Tooltip("Seconds one flick takes. Capped at 80% of the time between slashes.")]
        [Range(0.03f, 0.4f)]
        private float flickDuration = 0.12f;

        [SerializeField]
        [Tooltip("Degrees the tip sweeps through during a flick.")]
        [Range(0f, 150f)]
        private float flickArc = 70f;

        [SerializeField]
        [Tooltip("How far the rapier jabs forward at the peak of a flick.")]
        private float flickThrust = 0.3f;

        [SerializeField]
        [Tooltip("Point in the flick, 0 to 1, at which the slash leaves the blade.")]
        [Range(0f, 1f)]
        private float releasePoint = 0.35f;

        [SerializeField]
        [Tooltip("Degrees the tip angles outward from the cursor between flicks.")]
        [Range(0f, 60f)]
        private float restAngle = 15f;

        [SerializeField]
        [Tooltip("Degrees the tip angles below horizontal while held.")]
        [Range(0f, 80f)]
        private float tipTilt = 10f;

        [SerializeField]
        [Tooltip("How quickly the tip settles between flicks. Higher is stiffer.")]
        private float tipSharpness = 25f;

        [Header("Primary: slash")]
        [SerializeField]
        [Tooltip("Fewest degrees off the line to the cursor a slash leaves at.")]
        [Range(0f, 80f)]
        private float minLaunchAngle = 20f;

        [SerializeField]
        [Tooltip("Most degrees off the line to the cursor a slash leaves at. Each slash picks an angle in between.")]
        [Range(0f, 80f)]
        private float maxLaunchAngle = 55f;

        [SerializeField]
        [Tooltip("Height of the slash relative to the middle of the player's collider.")]
        private float cutHeightOffset;

        [SerializeField] private RapierSlash.Settings slash = new RapierSlash.Settings();

        [Header("Primary: rose marks")]
        [SerializeField] private RoseMark.Settings marks = new RoseMark.Settings();

        [Header("Secondary: dash")]
        [SerializeField]
        [Min(0f)]
        private float dashDistance = 6f;

        [SerializeField]
        [Tooltip("Seconds the dash takes. The distance is the same whatever this is set to; only the speed changes.")]
        [Min(0.02f)]
        private float dashDuration = 0.1f;

        [SerializeField]
        [Min(0f)]
        private float dashDamage = 10f;

        [SerializeField]
        [Tooltip("Radius of the trail's damage along the dash path.")]
        [Min(0.01f)]
        private float trailRadius = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the trail keeps cutting after the dash ends.")]
        [Min(0f)]
        private float trailLinger = 0.25f;

        [SerializeField]
        [Tooltip("Thickness of the trail's line of light.")]
        [Min(0.01f)]
        private float trailWidth = 0.35f;

        [SerializeField]
        [Tooltip("How far ahead of the player the rapier leads during the dash.")]
        private float leadDistance = 1f;

        [SerializeField]
        [Tooltip("Automatic slashes played on each enemy the dash hits, on top of the dash damage. Each deals one slash's damage.")]
        private SlashFlurry.Settings dashFlurry = new SlashFlurry.Settings
        {
            interval = 0.18f,
            size = 1.8f,
        };

        [Header("Secondary: petals")]
        [SerializeField] private RosePetal.Settings petals = new RosePetal.Settings();

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Base material for the slashes and the dash trail. Empty uses the rapier's own. It is copied and tinted, never edited.")]
        private Material slashMaterial;

        [SerializeField]
        [Tooltip("Colour of the slashes and the dash trail. Keep it strongly saturated, or the spot colour threshold crushes it to black or white. Push intensity above 1 to make it bloom.")]
        [ColorUsage(false, true)]
        private Color trailColour = new Color(1f, 0.22f, 0.45f);

        [SerializeField]
        [Tooltip("Material for the petals shed along the dash. Empty uses the rapier's own, untinted, so they match her colour.")]
        private Material petalMaterial;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("TEMP: seconds the rapier keeps attacking after the button is released. Aims at wherever the cursor was last.")]
        [Min(0f)]
        private float releaseDelay;

        private readonly HashSet<EnemyHealth> dashHit = new HashSet<EnemyHealth>();

        private readonly Dictionary<EnemyHealth, RoseMark> markByEnemy = new Dictionary<EnemyHealth, RoseMark>();
        private readonly List<EnemyHealth> staleMarks = new List<EnemyHealth>();

        // Primary state.
        private Vector3 aim = Vector3.forward;
        private Vector3 cursorPoint;
        private float side = 1f;
        private float attackCooldown;
        private float engage;
        private float releaseRemaining;
        private bool attackHeld;

        private bool flicking;
        private bool released;
        private float flickTime;
        private float launchAngle;

        private Vector3 holdPosition;

        // The hold is an angle round the player rather than a point, so that
        // crossing sides travels an arc in front instead of a line through
        // the player. holdAngle is relative to the aim: +90 right, -90 left,
        // 0 straight ahead. holdYaw is the aim's own yaw, smoothed separately
        // so a sudden change of aim swings the hold round rather than
        // snapping it.
        private float holdAngle;
        private float holdAngleVelocity;
        private float holdYaw;
        private float holdYawVelocity;
        private Vector3 tipDirection = Vector3.down;

        // Secondary state.
        private bool dashing;
        private bool lingering;
        private float lingerRemaining;
        private float dashBlend;
        private Vector3 dashDirection = Vector3.forward;
        private Vector3 trailStart;
        private Vector3 trailEnd;
        private Vector3 lastTrailPoint;
        private GameObject trailObject;

        // Distance still to travel along the dash before the next petal.
        private float petalGap;

        private float AttacksPerSecond => attacksPerSecond * AttackSpeedMultiplier;

        private float FlickLength => Mathf.Min(flickDuration, 0.8f / AttacksPerSecond);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Material tintedMaterial;
        private Material tintedSource;
        private Color tintedColour;

        /// <summary>
        /// A tinted copy of the trail material, rebuilt only when the source or
        /// the colour changes. A copy, so tinting the trails never recolours
        /// the rapier itself or anything else sharing her material.
        /// </summary>
        private Material ResolvedSlashMaterial
        {
            get
            {
                Material source = slashMaterial;

                if (source == null)
                {
                    Renderer own = GetComponentInChildren<Renderer>();
                    source = own != null ? own.sharedMaterial : null;
                }

                if (source == null)
                {
                    return null;
                }

                if (tintedMaterial == null || tintedSource != source || tintedColour != trailColour)
                {
                    if (tintedMaterial != null)
                    {
                        Destroy(tintedMaterial);
                    }

                    tintedMaterial = new Material(source) { name = source.name + " (Rose trail)" };
                    tintedSource = source;
                    tintedColour = trailColour;

                    if (tintedMaterial.HasProperty(BaseColorId))
                    {
                        tintedMaterial.SetColor(BaseColorId, trailColour);
                    }

                    if (tintedMaterial.HasProperty(ColorId))
                    {
                        tintedMaterial.SetColor(ColorId, trailColour);
                    }
                }

                return tintedMaterial;
            }
        }

        /// <summary>
        /// The petal material, used as is. Never copied: the petals only fade
        /// through property blocks, so sharing the rapier's own material is safe.
        /// </summary>
        private Material ResolvedPetalMaterial
        {
            get
            {
                if (petalMaterial != null)
                {
                    return petalMaterial;
                }

                Renderer own = GetComponentInChildren<Renderer>();
                return own != null ? own.sharedMaterial : null;
            }
        }

        /// <summary>
        /// Rose's own colour, from her material, for the slash effects her
        /// hits fire. Falls back to the trail colour on a material without one.
        /// </summary>
        private Color RoseColour
        {
            get
            {
                Material material = ResolvedPetalMaterial;
                return material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : trailColour;
            }
        }

        /// <summary>Applies the trail colour to a trail, for shaders that read vertex colour rather than a material colour.</summary>
        private void Tint(TrailRenderer trail)
        {
            trail.startColor = trailColour;
            trail.endColor = trailColour;
        }

        protected override void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime)
        {
            Vector3 ownerPosition = Owner.position;
            bool pressed = PrimaryHeld;

            releaseRemaining = pressed ? releaseDelay : Mathf.Max(0f, releaseRemaining - deltaTime);
            bool held = pressed || releaseRemaining > 0f;
            attackHeld = held;

            // Only the real button moves the aim. During the release delay
            // the rapier keeps cutting wherever it was last pointed.
            if (pressed)
            {
                UpdateAim(ownerPosition);
            }

            bool wasEngaged = engage > 0f;

            attackCooldown = Mathf.Max(0f, attackCooldown - deltaTime);

            if (held && !flicking && attackCooldown <= 0f)
            {
                StartFlick();
            }

            // A flick already started always plays out, even if the button
            // was let go a frame after the click.
            engage = StepEngage(engage, held || flicking, deltaTime);

            if (!wasEngaged && engage > 0f)
            {
                // Coming out of orbit: start the hold already at the side,
                // and let the orbit blend carry the visual transition.
                holdAngle = side * 90f;
                holdAngleVelocity = 0f;
                holdYaw = Yaw(aim);
                holdYawVelocity = 0f;
                holdPosition = HoldPoint(ownerPosition);
                tipDirection = RestTip();
            }

            if (Slot == BladeSlot.Main)
            {
                SyncMarks();
            }

            TickFlick(deltaTime);
            TickDash(deltaTime);

            Vector3 position = orbitPosition;
            Quaternion rotation = orbitRotation;

            float primaryWeight = Ease(engage);
            if (primaryWeight > 0f)
            {
                HoldPose(ownerPosition, deltaTime, out Vector3 holdPose, out Quaternion holdRotation);
                position = Vector3.Lerp(position, holdPose, primaryWeight);
                rotation = Quaternion.Slerp(rotation, holdRotation, primaryWeight);
            }

            float dashWeight = Ease(dashBlend);
            if (dashWeight > 0f)
            {
                LeadPose(ownerPosition, out Vector3 leadPosition, out Quaternion leadRotation);
                position = Vector3.Lerp(position, leadPosition, dashWeight);
                rotation = Quaternion.Slerp(rotation, leadRotation, dashWeight);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void UpdateAim(Vector3 ownerPosition)
        {
            if (!TryGetCursorPoint(OwnerMiddleHeight() + cutHeightOffset, out Vector3 point))
            {
                return;
            }

            cursorPoint = point;

            Vector3 desired = point - ownerPosition;
            desired.y = 0f;

            // Clicking on the player gives no direction, so keep the last one.
            if (desired.sqrMagnitude > 0.0001f)
            {
                aim = desired.normalized;
            }
        }

        // ---- primary ----------------------------------------------------

        private void StartFlick()
        {
            flicking = true;
            released = false;
            flickTime = 0f;
            attackCooldown = 1f / AttacksPerSecond;
            launchAngle = Random.Range(minLaunchAngle, maxLaunchAngle);
        }

        private void TickFlick(float deltaTime)
        {
            if (!flicking)
            {
                return;
            }

            flickTime += deltaTime;
            float progress = Mathf.Clamp01(flickTime / FlickLength);

            if (!released && progress >= releasePoint)
            {
                released = true;
                LaunchSlash();
            }

            if (progress >= 1f)
            {
                flicking = false;

                // Cross over for the next slash only if there will be one.
                if (attackHeld)
                {
                    side = -side;
                }
            }
        }

        private void LaunchSlash()
        {
            float height = OwnerMiddleHeight() + cutHeightOffset;
            Vector3 origin = new Vector3(holdPosition.x, height, holdPosition.z);

            // Off to the rapier's own side, so the arc has to bend back in
            // across the cursor: from the right it curves left, and vice versa.
            Vector3 heading = Quaternion.AngleAxis(side * launchAngle, Vector3.up) * aim;

            RapierSlash spawned = RapierSlash.Spawn(origin, heading, cursorPoint, slash, ResolvedSlashMaterial, OnSlashHit, RoseColour);
            Tint(spawned.GetComponent<TrailRenderer>());
        }

        private void OnSlashHit(EnemyHealth enemy, Vector3 arrivalPoint)
        {
            if (markByEnemy.TryGetValue(enemy, out RoseMark mark) && mark != null)
            {
                mark.RegisterHit(arrivalPoint, slash.damage);
            }
        }

        /// <summary>Each enemy is reported once per dash, so each gets one flurry.</summary>
        private void OnDashHit(EnemyHealth enemy, Vector3 arrivalPoint)
        {
            SlashFlurry.Play(enemy, slash.damage, dashFlurry, RoseColour);
        }

        /// <summary>
        /// Gives every living enemy a mark, and forgets marks that have
        /// removed themselves.
        /// </summary>
        private void SyncMarks()
        {
            foreach (EnemyHealth enemy in EnemyHealth.Active)
            {
                if (enemy.IsDead)
                {
                    continue;
                }

                if (!markByEnemy.TryGetValue(enemy, out RoseMark mark) || mark == null)
                {
                    markByEnemy[enemy] = RoseMark.Create(enemy, marks, ResolvedPetalMaterial);
                }
            }

            foreach (KeyValuePair<EnemyHealth, RoseMark> entry in markByEnemy)
            {
                if (entry.Key == null || entry.Value == null)
                {
                    staleMarks.Add(entry.Key);
                }
            }

            foreach (EnemyHealth enemy in staleMarks)
            {
                markByEnemy.Remove(enemy);
            }

            staleMarks.Clear();
        }

        private void ClearMarks()
        {
            foreach (RoseMark mark in markByEnemy.Values)
            {
                if (mark != null)
                {
                    Destroy(mark.gameObject);
                }
            }

            markByEnemy.Clear();
        }

        private Vector3 HoldPoint(Vector3 ownerPosition)
        {
            Vector3 direction = Quaternion.Euler(0f, holdYaw + holdAngle, 0f) * Vector3.forward;
            Vector3 point = ownerPosition + direction * sideOffset;
            point.y = HoldHeight();
            return point;
        }

        private static float Yaw(Vector3 flat)
        {
            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Measured from the collider's middle rather than the pivot, which may
        /// sit at the feet, so the rapier lines up with the slashes it throws.
        /// </summary>
        private float HoldHeight()
        {
            return OwnerMiddleHeight() + holdHeightOffset;
        }

        private Vector3 RestTip()
        {
            return Tilted(Quaternion.AngleAxis(side * restAngle, Vector3.up) * aim);
        }

        private Vector3 Tilted(Vector3 flat)
        {
            float tilt = tipTilt * Mathf.Deg2Rad;
            return flat * Mathf.Cos(tilt) + Vector3.down * Mathf.Sin(tilt);
        }

        private void HoldPose(Vector3 ownerPosition, float deltaTime, out Vector3 position, out Quaternion rotation)
        {
            float swapTime = Mathf.Max(0.0001f, sideSwapTime);

            // The side angle is smoothed as a plain number between -90 and
            // +90, never wrapped, so crossing over always passes through 0:
            // round the front, never behind.
            holdAngle = Mathf.SmoothDamp(holdAngle, side * 90f, ref holdAngleVelocity,
                                         swapTime, Mathf.Infinity, deltaTime);
            holdYaw = Mathf.SmoothDampAngle(holdYaw, Yaw(aim), ref holdYawVelocity,
                                            swapTime, Mathf.Infinity, deltaTime);
            holdPosition = HoldPoint(ownerPosition);

            Vector3 thrustOffset = Vector3.zero;

            if (flicking)
            {
                float progress = Mathf.Clamp01(flickTime / FlickLength);

                // Cocked outward past the launch angle, then whipped inward
                // past it. Ease-out cubic: nearly all the sweep happens at the
                // start, which is what makes it a flick rather than a swing.
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                float yaw = side * Mathf.Lerp(launchAngle + flickArc * 0.5f, launchAngle - flickArc * 0.5f, eased);

                Vector3 flat = Quaternion.AngleAxis(yaw, Vector3.up) * aim;

                // Set outright, not smoothed: smoothing would eat the snap.
                tipDirection = Tilted(flat);
                thrustOffset = flat * (flickThrust * Mathf.Sin(Mathf.PI * progress));
            }
            else
            {
                float t = 1f - Mathf.Exp(-tipSharpness * deltaTime);
                tipDirection = Vector3.Slerp(tipDirection, RestTip(), t).normalized;
            }

            position = holdPosition + thrustOffset;
            rotation = TipRotation(tipDirection, -aim);
        }

        // ---- secondary --------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            Vector3 ownerPosition = Owner.position;

            Vector3 direction = TryGetCursorPoint(OwnerMiddleHeight(), out Vector3 point)
                ? point - ownerPosition
                : Owner.forward;

            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Owner.forward;
            }

            if (Motor == null)
            {
                FinishSecondary();
                return;
            }

            // The ability wins over a normal dash already in progress.
            if (Motor.IsDashing)
            {
                Motor.CancelDash();
            }

            dashDirection = direction.normalized;
            Motor.DashFixed(dashDirection, dashDistance, dashDuration);

            dashing = true;
            lingering = false;
            dashHit.Clear();

            trailStart = MiddlePoint();
            lastTrailPoint = trailStart;
            petalGap = 0f;

            trailObject = new GameObject("RapierDashTrail");
            trailObject.transform.position = trailStart;

            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = trailLinger + dashDuration;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = RapierSlash.PointedWidth(0.2f);
            trail.minVertexDistance = 0.05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Material material = ResolvedSlashMaterial;
            if (material != null)
            {
                trail.sharedMaterial = material;
            }

            Tint(trail);
        }

        private void TickDash(float deltaTime)
        {
            if (dashing)
            {
                // Snap to the front almost at once: the whole dash is a tenth
                // of a second, and a rapier still catching up would trail it.
                dashBlend = Mathf.MoveTowards(dashBlend, 1f, deltaTime / 0.04f);

                Vector3 point = MiddlePoint();
                BladeDamage.Sweep(lastTrailPoint, point, trailRadius, dashDamage, dashDirection, dashHit, OnDashHit, RoseColour);
                ShedPetals(lastTrailPoint, point);
                lastTrailPoint = point;

                if (trailObject != null)
                {
                    trailObject.transform.position = point;
                }

                if (!Motor.IsDashing)
                {
                    dashing = false;
                    lingering = true;
                    lingerRemaining = trailLinger;
                    trailEnd = point;

                    if (trailObject != null)
                    {
                        Destroy(trailObject, trailLinger + dashDuration + 0.05f);
                        trailObject = null;
                    }
                }

                return;
            }

            dashBlend = StepEngage(dashBlend, false, deltaTime);

            if (!lingering)
            {
                return;
            }

            // The line the player just cut still hurts anything that steps
            // into it, until it fades.
            BladeDamage.Sweep(trailStart, trailEnd, trailRadius, dashDamage, dashDirection, dashHit, OnDashHit, RoseColour);

            lingerRemaining -= deltaTime;

            if (lingerRemaining <= 0f)
            {
                lingering = false;
                FinishSecondary();
            }
        }

        /// <summary>
        /// Drops petals at even spacing along the stretch covered this frame,
        /// carrying the leftover distance on, so the spacing is the same
        /// whatever the frame rate or dash speed.
        /// </summary>
        private void ShedPetals(Vector3 from, Vector3 to)
        {
            float length = Vector3.Distance(from, to);

            if (length < 0.0001f)
            {
                return;
            }

            Material material = ResolvedPetalMaterial;
            float along = petalGap;

            while (along <= length)
            {
                RosePetal.Spawn(Vector3.Lerp(from, to, along / length), dashDirection, petals, material);
                along += petals.spacing;
            }

            petalGap = along - length;
        }

        private void LeadPose(Vector3 ownerPosition, out Vector3 position, out Quaternion rotation)
        {
            position = ownerPosition + dashDirection * leadDistance;
            position.y = HoldHeight();
            Vector3 tip = Tilted(dashDirection);
            rotation = TipRotation(tip, -dashDirection);
        }

        private Vector3 MiddlePoint()
        {
            Vector3 ownerPosition = Owner.position;
            return new Vector3(ownerPosition.x, OwnerMiddleHeight(), ownerPosition.z);
        }

        // ---- lifecycle --------------------------------------------------

        protected override void OnSnap()
        {
            engage = 0f;
            releaseRemaining = 0f;
            attackHeld = false;
            flicking = false;
            released = false;
            attackCooldown = 0f;
            tipDirection = Vector3.down;
            dashBlend = 0f;
        }

        private void OnDisable()
        {
            // Hidden mid-dash: the ability must still end, or the rotation
            // would wait on it forever.
            if (dashing || lingering)
            {
                dashing = false;
                lingering = false;
                FinishSecondary();
            }

            if (trailObject != null)
            {
                Destroy(trailObject);
                trailObject = null;
            }

            // The marks belong to Rose being out; another main blade has no
            // use for them.
            ClearMarks();
        }

        private void OnDestroy()
        {
            // Slashes still in flight keep a reference, but they outlive the
            // rapier by a fraction of a second at most and Unity tolerates a
            // destroyed material on a renderer.
            if (tintedMaterial != null)
            {
                Destroy(tintedMaterial);
            }
        }

        protected override void OnValidate()
        {
            base.OnValidate();

            sideOffset = Mathf.Max(0f, sideOffset);
            sideSwapTime = Mathf.Max(0f, sideSwapTime);
            tipSharpness = Mathf.Max(0f, tipSharpness);
            maxLaunchAngle = Mathf.Max(minLaunchAngle, maxLaunchAngle);
        }
    }
}
