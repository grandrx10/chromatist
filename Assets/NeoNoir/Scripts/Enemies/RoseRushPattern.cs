using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Rose the Rapier's fight, all one attack: she leaps to the middle of
    /// the arena and lands tip first, stuck in the floor, then, blade stuck
    /// in it throughout, slides after the player, cutting a glowing line of
    /// petals that hurts for a while behind her. Every few seconds she
    /// slows, charges, and dashes straight at them, then slides on, until
    /// she is beaten.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The drop: she appears <see cref="dropHeight"/> straight above
    ///      the enemy's <see cref="Enemy.ArenaCentre"/>, tip down, and drops
    ///      onto it over <see cref="dropTime"/> (see <see cref="BladeDrop"/>),
    ///      while a <see cref="WarningCircle"/> of <see cref="landingRadius"/>
    ///      fills where she will land. She comes down tip first, sunk
    ///      <see cref="sinkDepth"/>, with a heavy impact and
    ///      <see cref="landingDamage"/> to the player inside the circle.
    ///      Without an arena centre she drops where she is. Her colliders
    ///      are off while she is in the air.
    ///   2. She stays stuck for <see cref="landingHold"/>, turning to the
    ///      player.
    ///   3. Over and over:
    ///      - Slide, <see cref="slideTime"/>: she sets off at
    ///        <see cref="slideSpeed"/> and homes as the Lightning Punch dash
    ///        does: only ever pulled toward the player, at
    ///        <see cref="slideAcceleration"/>, so a sidestep is followed round
    ///        in a curve. Faster than the slide speed, as just after a dash,
    ///        she sheds <see cref="momentumBrake"/> a second down to it, so
    ///        she overshoots and swings back.
    ///      - Charge: she brakes to a stop over <see cref="brakeTime"/>, then
    ///        stands <see cref="chargeTime"/>, a <see cref="ChargeRing"/>
    ///        closing in and her blade glistening, turning after the player
    ///        until <see cref="lockBefore"/> before the end.
    ///      - Dash: straight the way she faces, at <see cref="dashSpeed"/>,
    ///        for <see cref="dashDistance"/> -- less if the arena's edge
    ///        stops her. Back to the slide, carrying the dash's speed into it.
    ///
    /// From the landing on, her tip cuts a <see cref="RoseFloorTrail"/>
    /// wherever she goes. Touching her hurts too: <see cref="touchDamage"/>,
    /// at most once per slide and once per dash.
    ///
    /// She is moved and posed by hand for the whole fight, the enemy's own
    /// movement and turning switched off, so give her no Rigidbody. She is
    /// kept <see cref="edgeMargin"/> inside the enemy's
    /// <see cref="Enemy.Arena"/>, sliding along its edge; without one, she
    /// goes where she likes. Her blade stands tip down, edge leading,
    /// leaning <see cref="lean"/> back while she moves so the hilt leads and
    /// the tip drags behind, raised so the tip is always
    /// <see cref="sinkDepth"/> under the floor; standing to charge, she
    /// straightens. How far her tip reaches below her pivot is measured from
    /// her renderers. Her art is posed tip down local -Y, flat facing local
    /// +Z, as the player's blades are.
    ///
    /// The attack never ends by itself, so give the enemy only this pattern,
    /// and leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Blade Attacks/Rose Rush", fileName = "RoseRush")]
    public sealed class RoseRushPattern : AttackPattern
    {
        private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

        [Header("Blade")]
        [SerializeField]
        [Tooltip("How far the tip sinks under the floor.")]
        [Min(0f)]
        private float sinkDepth = 0.3f;

        [SerializeField]
        [Tooltip("Degrees she leans back while moving, hilt leading and tip dragging behind.")]
        [Range(0f, 80f)]
        private float lean = 30f;

        [SerializeField]
        [Tooltip("How quickly the lean follows her speed. Higher is stiffer.")]
        [Min(0f)]
        private float leanSharpness = 10f;

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Drop to the middle")]
        [SerializeField]
        [Tooltip("How far above where she lands she appears, to drop straight down.")]
        [FormerlySerializedAs("leapHeight")]
        [Min(0f)]
        private float dropHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. She gathers speed all the way down.")]
        [FormerlySerializedAs("leapTime")]
        [Min(0.01f)]
        private float dropTime = 0.9f;

        [SerializeField]
        [Tooltip("Seconds she stays stuck after landing, turning to the player, before sliding off.")]
        [Min(0f)]
        private float landingHold = 0.5f;

        [SerializeField]
        [Tooltip("Radius of the warning circle where she lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 4f;

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.12f;

        [SerializeField]
        [Tooltip("Colour of the warning circle.")]
        private Color warningColour = new Color(1f, 0.3f, 0.55f);

        [SerializeField]
        [Tooltip("Height above the floor the warning circle is drawn at.")]
        [Min(0f)]
        private float warningHeight = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as she lands. The stab, with no leap, deals none.")]
        [Min(0f)]
        private float landingDamage = 25f;

        [Header("Heavy impact")]
        [SerializeField]
        [Tooltip("Material for the landing's burst, e.g. NeoNoir/Flat in the colour you want. Empty draws no burst.")]
        private Material landingMaterial;

        [SerializeField]
        private HammerImpact.Settings landing = new HammerImpact.Settings
        {
            ringRadius = 5f,
            ringWidth = 0.8f,
            ringTime = 0.4f,
            debrisCount = 12,
            debrisSize = 0.4f,
            debrisUpSpeed = new Vector2(5f, 9f),
            debrisOutSpeed = new Vector2(3f, 6f),
            debrisTime = 0.7f,
        };

        [SerializeField]
        [Tooltip("Radius of the explosion sphere the landing bursts into. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 3f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.12f,
            fadeTime = 0.35f,
            colour = new Color(3f, 0.6f, 1.3f, 1f),
            outlineWidth = 0.15f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 0.6f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.35f;

        [Header("Slide")]
        [SerializeField]
        [Tooltip("Seconds of sliding between one dash and the next.")]
        [Min(0f)]
        private float slideTime = 3f;

        [SerializeField]
        [Tooltip("Speed she slides at, and sets off at.")]
        [Min(0f)]
        private float slideSpeed = 10f;

        [SerializeField]
        [Tooltip("Speed gained toward the player every second. Lower swings wider round a player who sidesteps.")]
        [Min(0f)]
        private float slideAcceleration = 20f;

        [SerializeField]
        [Tooltip("Speed shed every second while faster than the slide speed, as just after a dash. Lower carries the dash further.")]
        [Min(0f)]
        private float momentumBrake = 15f;

        [SerializeField]
        [Tooltip("How far inside the arena's edge she is kept.")]
        [Min(0f)]
        private float edgeMargin = 0.5f;

        [Header("Charge")]
        [SerializeField]
        [Tooltip("Seconds she takes to brake to a stop before charging.")]
        [Min(0.01f)]
        private float brakeTime = 0.25f;

        [SerializeField]
        [Tooltip("Seconds she stands charging before the dash.")]
        [Min(0.01f)]
        private float chargeTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds before the dash she stops turning after the player, so it can be dodged.")]
        [Min(0f)]
        private float lockBefore = 0.15f;

        [SerializeField]
        [Tooltip("Degrees a second she turns after the player while charging, and while stuck after landing.")]
        [Min(0f)]
        private float chargeTurnSpeed = 720f;

        [SerializeField]
        private ChargeRing.Settings chargeRing = new ChargeRing.Settings
        {
            startRadius = 4f,
            endRadius = 0.8f,
            colour = new Color(3f, 0.6f, 1.3f),
        };

        [SerializeField]
        private BladeGlisten.Settings glisten = new BladeGlisten.Settings
        {
            colour = new Color(1f, 0.22f, 0.45f),
        };

        [Header("Dash")]
        [SerializeField]
        [Tooltip("Distance the dash runs, unless the arena's edge stops it sooner.")]
        [Min(0f)]
        private float dashDistance = 20f;

        [SerializeField]
        [Min(0.01f)]
        private float dashSpeed = 25f;

        [Header("Touch")]
        [SerializeField]
        [Tooltip("Damage to the player who touches her, at most once per slide and once per dash.")]
        [Min(0f)]
        private float touchDamage = 15f;

        [SerializeField]
        [Tooltip("How close to her pivot, measured flat, counts as touching. Keep it a little over her collider's radius.")]
        [Min(0f)]
        private float touchRadius = 1f;

        [Header("Trail")]
        [SerializeField] private RoseFloorTrail.Settings trail = new RoseFloorTrail.Settings();

        [SerializeField]
        [Tooltip("Material for the petals shed along the trail, e.g. Rose's own. Empty sheds none.")]
        private Material petalMaterial;

        public override IEnumerator Perform(Enemy enemy)
        {
            Rush rush = new Rush(this, enemy);

            foreach (object frame in rush.Land())
            {
                yield return frame;
            }

            // Until she dies, which stops this from outside.
            while (true)
            {
                foreach (object frame in rush.Slide())
                {
                    yield return frame;
                }

                foreach (object frame in rush.Charge())
                {
                    yield return frame;
                }

                foreach (object frame in rush.Dash())
                {
                    yield return frame;
                }
            }
        }

        /// <summary>One fight's worth of state: where she is, where she heads, how fast, and how far she leans.</summary>
        private sealed class Rush
        {
            private readonly RoseRushPattern pattern;
            private readonly Enemy enemy;
            private readonly Transform root;
            private readonly PlayerHealth player;

            // How far her tip reaches below her pivot, standing upright.
            private readonly float tipBelow;

            private RoseFloorTrail floorTrail;
            private Vector3 heading;
            private Vector3 velocity;
            private float leanNow;
            private bool touched;
            private float lastFloor = float.NaN;
            private bool warnedNoFloor;

            public Rush(RoseRushPattern pattern, Enemy enemy)
            {
                this.pattern = pattern;
                this.enemy = enemy;
                root = enemy.transform;
                player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

                // Moved and posed by hand from here on.
                enemy.VelocityOverride = Vector3.zero;
                enemy.PhysicsDriven = true;

                Rigidbody body = enemy.GetComponent<Rigidbody>();

                if (body != null)
                {
                    body.isKinematic = true;
                }

                heading = Flat(root.forward);
                heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;
                tipBelow = MeasureTip();
            }

            /// <summary>Drops onto the arena centre, or where she is without one, and stays stuck a moment, turning to the player.</summary>
            public IEnumerable Land()
            {
                foreach (object frame in Drop(enemy.ArenaCentre ?? root.position))
                {
                    yield return frame;
                }

                Vector3 tip = TipOnFloor();
                floorTrail = RoseFloorTrail.Create(enemy, pattern.trail, pattern.petalMaterial);
                floorTrail.Emit(tip, heading);

                for (float t = 0f; t < pattern.landingHold; t += Time.deltaTime)
                {
                    TurnToPlayer();
                    Pose(FindFloor(root.position));
                    yield return null;
                }

                heading = ToPlayer(heading);
                velocity = heading * pattern.slideSpeed;
            }

            /// <summary>A drop straight down onto <paramref name="centre"/>, tip first into the floor.</summary>
            private IEnumerable Drop(Vector3 centre)
            {
                List<Collider> switchedOff = new List<Collider>();

                foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
                {
                    if (part.enabled)
                    {
                        part.enabled = false;
                        switchedOff.Add(part);
                    }
                }

                Vector3 floor = FindFloor(centre);
                Vector3 flight = Flat(floor - root.position);
                heading = flight.sqrMagnitude > 0.0001f ? flight.normalized : heading;

                leanNow = 0f;
                StuckPose(floor, out Vector3 stuck, out Quaternion rotation);
                root.rotation = rotation;

                WarningCircle warning = WarningCircle.Create(floor + Vector3.up * pattern.warningHeight,
                                                             pattern.landingRadius, pattern.landingOutline,
                                                             null, pattern.warningColour);

                foreach (object frame in BladeDrop.Fall(root, stuck, pattern.dropHeight, pattern.dropTime, warning))
                {
                    yield return frame;
                }

                Object.Destroy(warning.gameObject);
                Pose(floor);

                HeavyImpact(floor);
                Hurt(floor, pattern.landingRadius, pattern.landingDamage);

                foreach (Collider part in switchedOff)
                {
                    if (part != null)
                    {
                        part.enabled = true;
                    }
                }
            }

            /// <summary>Homes on the player, carrying any extra speed from the dash until it is shed.</summary>
            public IEnumerable Slide()
            {
                touched = false;

                for (float t = 0f; t < pattern.slideTime; t += Time.deltaTime)
                {
                    float deltaTime = Time.deltaTime;
                    float speed = velocity.magnitude;

                    if (enemy.HasTarget)
                    {
                        Vector3 toPlayer = Flat(enemy.TargetPoint - root.position);

                        if (toPlayer.sqrMagnitude > 0.0001f)
                        {
                            // Pulled toward them, never pointed at them.
                            velocity += toPlayer.normalized * (pattern.slideAcceleration * deltaTime);
                        }
                    }

                    float cap = Mathf.Max(pattern.slideSpeed, speed - pattern.momentumBrake * deltaTime);
                    velocity = Vector3.ClampMagnitude(velocity, cap);

                    Move();
                    yield return null;
                }
            }

            /// <summary>Brakes to a stop, then charges, turning after the player until just before the dash.</summary>
            public IEnumerable Charge()
            {
                float brakeRate = velocity.magnitude / pattern.brakeTime;

                for (float t = 0f; t < pattern.brakeTime; t += Time.deltaTime)
                {
                    velocity = Vector3.MoveTowards(velocity, Vector3.zero, brakeRate * Time.deltaTime);
                    Move();
                    yield return null;
                }

                velocity = Vector3.zero;

                ChargeRing.Create(root, FindFloor(root.position).y, pattern.chargeTime, pattern.chargeRing);
                BladeGlisten shine = BladeGlisten.Begin(root, pattern.glisten, pattern.chargeTime);

                for (float t = 0f; t < pattern.chargeTime; t += Time.deltaTime)
                {
                    if (t < pattern.chargeTime - pattern.lockBefore)
                    {
                        TurnToPlayer();
                    }

                    shine.Tick(t);
                    Move();
                    yield return null;
                }

                shine.End();
            }

            /// <summary>Straight the way she faces, until the distance is run or the arena's edge stops her.</summary>
            public IEnumerable Dash()
            {
                touched = false;
                velocity = heading * pattern.dashSpeed;

                // The edge can hold her short of the distance; the time is up
                // anyway by then, with some to spare.
                float timeLimit = pattern.dashDistance / pattern.dashSpeed * 1.5f;
                float travelled = 0f;

                for (float t = 0f; travelled < pattern.dashDistance && t < timeLimit; t += Time.deltaTime)
                {
                    travelled += Move();
                    yield return null;
                }
            }

            /// <summary>
            /// Moves her a frame at the current velocity, kept inside the
            /// arena, poses her, cuts the trail and hurts the player who
            /// touches her. Returns how far she went.
            /// </summary>
            private float Move()
            {
                float deltaTime = Time.deltaTime;
                Vector3 from = root.position;
                Vector3 step = Flat(velocity) * deltaTime;

                if (step.sqrMagnitude > 0f && !Inside(from + step))
                {
                    // Along the edge, one way or the other, losing the speed into it.
                    Vector3 alongX = new Vector3(step.x, 0f, 0f);
                    Vector3 alongZ = new Vector3(0f, 0f, step.z);

                    if (Inside(from + alongX))
                    {
                        step = alongX;
                        velocity.z = 0f;
                    }
                    else if (Inside(from + alongZ))
                    {
                        step = alongZ;
                        velocity.x = 0f;
                    }
                    else
                    {
                        step = Vector3.zero;
                        velocity = Vector3.zero;
                    }
                }

                Vector3 flat = Flat(velocity);

                if (flat.sqrMagnitude > 0.01f)
                {
                    heading = flat.normalized;
                }

                float target = Mathf.Clamp01(velocity.magnitude / Mathf.Max(pattern.slideSpeed, 0.01f));
                leanNow = Mathf.Lerp(leanNow, target, 1f - Mathf.Exp(-pattern.leanSharpness * deltaTime));

                Pose(FindFloor(from + step));

                if (floorTrail != null)
                {
                    floorTrail.Emit(TipOnFloor(), heading);
                }

                TryTouch();
                return step.magnitude;
            }

            private bool Inside(Vector3 point)
            {
                ArenaBounds arena = enemy.Arena;

                if (arena == null)
                {
                    return true;
                }

                Vector3 direction = Flat(point - root.position);
                Vector3 probe = direction.sqrMagnitude > 0.000001f
                    ? point + direction.normalized * pattern.edgeMargin
                    : point;

                return arena.Contains(probe);
            }

            private void TurnToPlayer()
            {
                heading = Vector3.RotateTowards(heading, ToPlayer(heading),
                                                pattern.chargeTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
            }

            private void TryTouch()
            {
                if (touched || player == null || player.IsDead || pattern.touchDamage <= 0f)
                {
                    return;
                }

                Vector3 offset = Flat(player.transform.position - root.position);

                if (offset.sqrMagnitude <= pattern.touchRadius * pattern.touchRadius)
                {
                    touched = true;
                    player.TakeDamage(pattern.touchDamage, root.position);
                }
            }

            /// <summary>Stands her stuck in the floor at <paramref name="floor"/>, posed for the current heading and lean.</summary>
            private void Pose(Vector3 floor)
            {
                StuckPose(floor, out Vector3 position, out Quaternion rotation);
                root.SetPositionAndRotation(position, rotation);
            }

            /// <summary>
            /// Tip down, edge leading along the heading, leaning back by the
            /// current fraction of the full lean, raised so the tip sits the
            /// sink depth under <paramref name="floor"/>.
            /// </summary>
            private void StuckPose(Vector3 floor, out Vector3 position, out Quaternion rotation)
            {
                float degrees = pattern.lean * leanNow;
                Vector3 across = Vector3.Cross(Vector3.up, heading);

                // Hilt (+Y) tipped forward along the heading; the tip, below,
                // swings back behind.
                rotation = Quaternion.AngleAxis(degrees, across) * Quaternion.LookRotation(across, Vector3.up);
                position = new Vector3(floor.x, floor.y + StuckHeight(degrees), floor.z);
            }

            /// <summary>Height of her pivot over the floor that sinks the tip, leaning <paramref name="degrees"/>.</summary>
            private float StuckHeight(float degrees)
            {
                return tipBelow * Mathf.Cos(degrees * Mathf.Deg2Rad) - pattern.sinkDepth;
            }

            private Vector3 TipOnFloor()
            {
                Vector3 tip = root.position - root.up * tipBelow;
                tip.y = FindFloor(tip).y;
                return tip;
            }

            /// <summary>
            /// How far her art reaches below her pivot, stood upright:
            /// measured from her renderers by trying the pose on and putting
            /// it back, all within the frame.
            /// </summary>
            private float MeasureTip()
            {
                Quaternion rotation = root.rotation;
                root.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, heading), Vector3.up);

                float lowest = float.MaxValue;

                foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
                {
                    if (part.enabled && !(part is ParticleSystemRenderer) && !(part is LineRenderer) && !(part is TrailRenderer))
                    {
                        lowest = Mathf.Min(lowest, part.bounds.min.y);
                    }
                }

                float below = lowest < float.MaxValue ? root.position.y - lowest : 1f;
                root.rotation = rotation;
                return Mathf.Max(below, 0f);
            }

            /// <summary>
            /// The floor under <paramref name="point"/>, never herself. With
            /// none found, the last floor found, or, before any, the point's
            /// own height -- never her pivot, which would lift her a little
            /// more every frame.
            /// </summary>
            private Vector3 FindFloor(Vector3 point)
            {
                // From well above, so a raised floor still counts.
                int count = Physics.RaycastNonAlloc(point + Vector3.up * 20f, Vector3.down, FloorHits, 40f,
                                                    pattern.groundMask, QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;

                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = FloorHits[i];

                    if (hit.normal.y >= 0.5f && !hit.transform.IsChildOf(root) && hit.point.y > best)
                    {
                        best = hit.point.y;
                    }
                }

                if (best > float.NegativeInfinity)
                {
                    lastFloor = best;
                }
                else if (!float.IsNaN(lastFloor))
                {
                    point.y = lastFloor;
                    return point;
                }
                else
                {
                    if (!warnedNoFloor)
                    {
                        warnedNoFloor = true;
                        Debug.LogWarning($"[{pattern.name}] No floor found under {enemy.name} on the Ground Mask layers.", pattern);
                    }

                    lastFloor = point.y;
                }

                point.y = lastFloor;
                return point;
            }

            /// <summary>The heavy landing: dust and debris, an explosion sphere, and a jolt.</summary>
            private void HeavyImpact(Vector3 floor)
            {
                if (pattern.landingMaterial != null)
                {
                    HammerImpact.Create(floor, pattern.landing, pattern.landingMaterial);
                }

                if (pattern.blastRadius > 0f)
                {
                    BlastSphere.Create(floor, pattern.blastRadius, pattern.blast);
                }

                if (pattern.landingShake > 0f)
                {
                    TopDownCamera.Shake(pattern.landingShake, pattern.landingShakeDuration);
                }
            }

            /// <summary>Hurts the player if within <paramref name="radius"/> of <paramref name="centre"/>, measured flat.</summary>
            private void Hurt(Vector3 centre, float radius, float damage)
            {
                if (player == null || player.IsDead || damage <= 0f)
                {
                    return;
                }

                if (Flat(player.transform.position - centre).sqrMagnitude <= radius * radius)
                {
                    player.TakeDamage(damage, centre);
                }
            }

            /// <summary>Flat direction to the player, or <paramref name="fallback"/> without one.</summary>
            private Vector3 ToPlayer(Vector3 fallback)
            {
                if (!enemy.HasTarget)
                {
                    return fallback;
                }

                Vector3 toPlayer = Flat(enemy.TargetPoint - root.position);
                return toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : fallback;
            }
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }
    }
}
