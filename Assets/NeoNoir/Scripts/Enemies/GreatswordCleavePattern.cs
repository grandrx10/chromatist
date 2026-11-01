using System;
using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss's greatsword combo: a run of <see cref="steps"/>, each an
    /// overhead slash -- the boss dashes in and brings the sword down,
    /// splitting a line through the floor -- or a cleave -- it backs off and
    /// sweeps the whole front half of the floor round it -- in any order,
    /// e.g. slash, slash, cleave, slash, slash, cleave.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The sword is borrowed from the boss's <see cref="BossBlades"/>
    ///      -- the one spawned from <see cref="bladePrefab"/> -- and the
    ///      combo starts at once, with no opening flourish.
    ///   2. Each of the <see cref="steps"/> in turn. A slash: the boss homes in on the player,
    ///      as the witch does in her punches -- pulled toward them at
    ///      <see cref="homeAcceleration"/>, never faster than
    ///      <see cref="homeSpeed"/>, so a sidestep is followed round in a
    ///      curve, and easing off once within <see cref="slashStandOff"/> --
    ///      the sword rising back overhead. It closes in for up to
    ///      <see cref="maxApproachTime"/>, then glistens for
    ///      <see cref="slashWarnTime"/>, still homing, until it brakes to a
    ///      stop over <see cref="brakeTime"/>, standing still through the last
    ///      <see cref="lockTime"/>, when the aim stops following the player.
    ///      The sword comes down along that aim and an <see cref="AmberSlam"/>
    ///      line splits from the boss's feet, as in
    ///      <see cref="GreatswordSlamPattern"/>.
    ///   3. A cleave: the boss homes away from the player for
    ///      <see cref="cleaveRetreatTime"/> -- pulled away as the slashes are
    ///      pulled in, no faster than <see cref="homeSpeed"/> -- then brakes to
    ///      a stop over <see cref="brakeTime"/>, drawing the sword back low to
    ///      its left as it turns to the player. The aim locks, and a
    ///      <see cref="CleaveArc"/> warning -- <see cref="cleaveAngle"/> degrees
    ///      across, <see cref="cleaveRadius"/> out -- fills over
    ///      <see cref="cleaveWarnTime"/> as the sword glistens. Then the sword
    ///      sweeps round the front, left to right, and the arc sweeps with
    ///      it, hurting the player if it crosses them.
    ///   4. After each step the sword is held where it ended -- in the floor
    ///      for <see cref="slamHoldTime"/>, at the end of the sweep for
    ///      <see cref="cleaveHoldTime"/> -- plus that step's own Pause After,
    ///      before the next. After the last it goes back into the orbit.
    ///
    /// The movement drives the boss through <see cref="Enemy.VelocityOverride"/>,
    /// so walls stop it. The sword is posed the way <see cref="BossBlades"/>
    /// poses it: built flat side facing local +Z, tip down local -Y, with the
    /// entry's Rotation Offset for one built otherwise, turned half round its
    /// length with <see cref="flipEdge"/> so the edge leads. Set the gleams'
    /// position on the blade with the Glisten's Gleam Offset.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Greatsword Cleave", fileName = "GreatswordCleave")]
    public sealed class GreatswordCleavePattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("The greatsword's prefab, as listed in the boss's Boss Blades. The blade spawned from it is the one swung.")]
        private GameObject bladePrefab;

        [SerializeField]
        [Tooltip("Turn the blade half round its length for this attack, so its edge rather than its back leads every strike.")]
        private bool flipEdge = true;

        [SerializeField]
        [Tooltip("Colour of the lines and the cleave. The sword's glow and gleams take the Glisten's own colour.")]
        private Color colour = new Color(0.6f, 0.2f, 1f);

        [Header("Glisten")]
        [SerializeField] private BladeGlisten.Settings glisten = new BladeGlisten.Settings();

        [Header("Raise")]
        [SerializeField]
        [Tooltip("Seconds the sword takes to rise overhead, at the start and between strikes.")]
        [Min(0.01f)]
        private float riseTime = 0.35f;

        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot when overhead: the point the sword swings about.")]
        private float gripHeight = 4f;

        [SerializeField]
        [Tooltip("Distance from the grip to the sword's pivot, toward the tip.")]
        private float gripToPivot = 1f;

        [SerializeField]
        [Tooltip("Degrees the tip leans back past upright as each slash's warning starts, and as it ends.")]
        private Vector2 windup = new Vector2(10f, 35f);

        [SerializeField]
        [Tooltip("Distance the sword trembles by at the very end of each warning.")]
        [Min(0f)]
        private float tremble = 0.08f;

        [SerializeField]
        [Tooltip("Degrees per second the aim, and the boss, turn after the player while following them.")]
        [Min(0f)]
        private float trackTurnSpeed = 540f;

        [Header("Homing, for the slashes")]
        [SerializeField]
        [Tooltip("Fastest the boss closes in on the player, in units per second.")]
        [Min(0.1f)]
        private float homeSpeed = 9f;

        [SerializeField]
        [Tooltip("Speed gained toward the player every second. Lower swings wider round a player who sidesteps.")]
        [Min(0f)]
        private float homeAcceleration = 20f;

        [SerializeField]
        [Tooltip("Longest the boss closes in, sword overhead, before each slash's warning starts, however far the player is.")]
        [Min(0f)]
        private float maxApproachTime = 1.2f;

        [SerializeField]
        [Tooltip("Seconds the boss takes to brake to a stop before each slash's lock.")]
        [Min(0.01f)]
        private float brakeTime = 0.3f;

        public enum StepKind
        {
            Slash,
            Cleave,
        }

        [Serializable]
        public sealed class Step
        {
            public StepKind kind = StepKind.Slash;

            [Tooltip("Seconds the sword is held where this strike ended, on top of the Slam or Cleave Hold Time, before the next step.")]
            [Min(0f)] public float pauseAfter;

            public Step()
            {
            }

            public Step(StepKind kind) => this.kind = kind;
        }

        [Header("Combo")]
        [SerializeField]
        [Tooltip("The strikes, in order. Add a Pause After to any of them to leave a gap before the next.")]
        private Step[] steps =
        {
            new Step(StepKind.Slash),
            new Step(StepKind.Slash),
            new Step(StepKind.Cleave),
        };

        [Header("Slashes")]

        [SerializeField]
        [Tooltip("The boss eases off closing in once this close to the player before each slash.")]
        [Min(0f)]
        private float slashStandOff = 5f;

        [SerializeField]
        [Tooltip("Seconds each slash glistens overhead before it comes down, lock included.")]
        [Min(0f)]
        private float slashWarnTime = 1f;

        [SerializeField]
        [Tooltip("Seconds before each slash comes down that the sword stops following the player.")]
        [Min(0f)]
        private float lockTime = 0.3f;

        [SerializeField]
        [Tooltip("Seconds from overhead to buried in the floor.")]
        [Min(0.01f)]
        private float swingTime = 0.1f;

        [SerializeField]
        [Tooltip("Degrees from straight up the tip ends at, toward the player. Past 90 points it into the floor.")]
        private float slamAngle = 115f;

        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot once the sword has come down.")]
        private float slamGripHeight = 1.5f;

        [SerializeField]
        [Tooltip("How far toward the player the grip comes down.")]
        private float slamReach = 1f;

        [SerializeField]
        [Tooltip("Seconds the sword stays in the floor after each slash.")]
        [Min(0f)]
        private float slamHoldTime = 0.25f;

        [SerializeField]
        private AmberSlam.Settings slash = new AmberSlam.Settings
        {
            damage = 20f,
            length = 30f,
            width = 5f,
            waveTime = 0.15f,
            holdTime = 0.3f,
            fadeTime = 0.4f,
            frontTaper = 3f,
            farWidth = 1f,
            lineIntensity = 2f,
            wallHeight = 4f,
            wallWidth = 0.4f,
            wallIntensity = 4f,
            arcRadius = 4.5f,
            arcWidth = 1.5f,
            arcTime = 0.2f,
        };

        [SerializeField]
        [Tooltip("Material for the lines and the cleave, e.g. NeoNoir/Flat. Empty uses Colour.")]
        private Material slashMaterial;

        [SerializeField]
        [Tooltip("Layers the lines and the cleave are laid on. Without floor under the boss, they are laid at the boss's pivot.")]
        private LayerMask floorMask = 1;

        [SerializeField]
        [Tooltip("How far each slash jolts the camera. 0 for none.")]
        [Min(0f)]
        private float slashShake = 0.5f;

        [Header("Cleave")]
        [SerializeField]
        [Tooltip("Seconds the boss homes away from the player before the cleave, at the Home Speed, before braking to a stop.")]
        [Min(0f)]
        private float cleaveRetreatTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the cleave's warning fills before the sword sweeps.")]
        [Min(0f)]
        private float cleaveWarnTime = 1.5f;

        [SerializeField]
        [Tooltip("How far out from the boss the cleave reaches.")]
        [Min(0.1f)]
        private float cleaveRadius = 30f;

        [SerializeField]
        [Tooltip("Degrees the cleave spreads across, centred on the aim.")]
        [Range(1f, 360f)]
        private float cleaveAngle = 150f;

        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot through the cleave.")]
        private float cleaveGripHeight = 1.8f;

        [SerializeField]
        [Tooltip("Degrees the tip dips below level through the cleave.")]
        private float cleaveDip = 10f;

        [SerializeField]
        [Tooltip("Degrees past the arc's edge the sword is drawn back before the sweep, and follows through after it.")]
        private Vector2 cleaveOvershoot = new Vector2(30f, 30f);

        [SerializeField]
        [Tooltip("Seconds the sword takes to sweep across, speeding up as it goes.")]
        [Min(0.01f)]
        private float cleaveSwingTime = 0.2f;

        [SerializeField]
        [Tooltip("Seconds the sword holds at the end of each sweep before the next step, or going back into the orbit.")]
        [Min(0f)]
        private float cleaveHoldTime = 0.4f;

        [SerializeField] private CleaveArc.Settings cleave = new CleaveArc.Settings();

        [SerializeField]
        [Tooltip("How far the cleave jolts the camera. 0 for none.")]
        [Min(0f)]
        private float cleaveShake = 0.8f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.3f;

        // Made from Colour when no Slash Material is set; shared by every boss.
        [NonSerialized] private Material runtimeSlashMaterial;

        public override bool CanPerform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            return blades != null && blades.IndexOf(bladePrefab) >= 0;
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null ? blades.IndexOf(bladePrefab) : -1;

            if (index < 0)
            {
                Debug.LogWarning($"[{name}] {enemy.name} has no Boss Blades blade made from the Blade Prefab.", this);
                yield break;
            }

            Combo combo = new Combo(this, enemy, blades, index);

            foreach (Step step in steps)
            {
                if (combo.Stopped)
                {
                    break;
                }

                IEnumerable strike = step.kind == StepKind.Cleave
                    ? combo.Cleave(cleaveHoldTime + step.pauseAfter)
                    : combo.Slash(slamHoldTime + step.pauseAfter);

                foreach (object frame in strike)
                {
                    yield return frame;
                }
            }

            combo.End();
        }

        /// <summary>
        /// One run of the combo: everything it needs to remember, since the
        /// asset itself is shared and holds nothing. Each step yields once a
        /// frame and stops early if the boss dies.
        /// </summary>
        private sealed class Combo
        {
            private readonly GreatswordCleavePattern pattern;
            private readonly Enemy enemy;
            private readonly BossBlades blades;
            private readonly int index;
            private readonly Transform sword;
            private readonly Quaternion modelOffset;
            // Started by the first strike's warning, so the sword only ever
            // glistens building to a strike.
            private BladeGlisten shine;

            private Vector3 aim;
            // How the boss is moving under the combo's own steering.
            private Vector3 velocity;
            // Seconds since the glisten last began building to a strike.
            private float clock;

            public Combo(GreatswordCleavePattern pattern, Enemy enemy, BossBlades blades, int index)
            {
                this.pattern = pattern;
                this.enemy = enemy;
                this.blades = blades;
                this.index = index;

                sword = blades.Borrow(index, this);
                modelOffset = blades.RotationOffset(index)
                              * (pattern.flipEdge ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
                aim = Facing(enemy);
            }

            public bool Stopped => enemy == null || enemy.Health.IsDead;

            public IEnumerable Slash(float holdTime)
            {
                // Closes in, the sword easing back overhead, until near enough
                // or out of time -- never before the sword is up.
                Vector3 fromPosition = sword.position;
                Quaternion fromRotation = sword.rotation;
                float elapsed = 0f;

                while (elapsed < pattern.riseTime || (!Near() && elapsed < pattern.maxApproachTime))
                {
                    Home(Time.deltaTime);
                    Track();
                    Pose to = Overhead(-pattern.windup.x, 0f, Vector3.zero);
                    float eased = Smooth(elapsed / pattern.riseTime);
                    sword.SetPositionAndRotation(Vector3.Lerp(fromPosition, to.position, eased),
                                                 Quaternion.Slerp(fromRotation, to.rotation, eased));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                // Hangs overhead, glistening, still homing, then brakes to a
                // stop and stands through the lock.
                Warn(pattern.slashWarnTime);
                float warn = pattern.slashWarnTime;
                elapsed = 0f;

                while (elapsed < warn)
                {
                    if (elapsed < warn - pattern.lockTime - pattern.brakeTime)
                    {
                        Home(Time.deltaTime);
                    }
                    else
                    {
                        Brake(Time.deltaTime);
                    }

                    if (elapsed < warn - pattern.lockTime)
                    {
                        Track();
                    }

                    float charge = warn > 0f ? elapsed / warn : 1f;
                    Vector3 shake = UnityEngine.Random.insideUnitSphere * (pattern.tremble * charge * charge);
                    Place(Overhead(-Mathf.Lerp(pattern.windup.x, pattern.windup.y, charge), 0f, shake));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                velocity = Vector3.zero;
                enemy.VelocityOverride = Vector3.zero;

                // Down, speeding up all the way.
                elapsed = 0f;

                while (elapsed < pattern.swingTime)
                {
                    float swing = elapsed / pattern.swingTime;
                    swing *= swing;
                    Place(Overhead(Mathf.Lerp(-pattern.windup.y, pattern.slamAngle, swing), swing, Vector3.zero));

                    yield return null;
                    elapsed += Tick();
                }

                pattern.Slam(enemy, aim, Floor());

                // Left in the floor a moment.
                elapsed = 0f;

                do
                {
                    Place(Overhead(pattern.slamAngle, 1f, Vector3.zero));

                    yield return null;
                    elapsed += Tick();
                }
                while (elapsed < holdTime && !Stopped);
            }

            public IEnumerable Cleave(float holdTime)
            {
                float half = pattern.cleaveAngle * 0.5f;
                float drawn = -(half + pattern.cleaveOvershoot.x);
                float through = half + pattern.cleaveOvershoot.y;

                foreach (object frame in Retreat(pattern.riseTime, () => Sideways(drawn, Vector3.zero)))
                {
                    yield return frame;
                }

                if (Stopped)
                {
                    yield break;
                }

                // The aim is locked from here: the warning shows exactly what will be swept.
                enemy.FacingOverride = aim;
                PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;
                CleaveArc arc = CleaveArc.Create(Floor(), aim, pattern.cleaveRadius, pattern.cleaveAngle,
                                                 pattern.cleave, pattern.colour, pattern.slashMaterial, player);
                arc.DestroyWhen(() => Stopped);

                Warn(pattern.cleaveWarnTime);
                float warn = pattern.cleaveWarnTime;
                float elapsed = 0f;

                while (elapsed < warn)
                {
                    float charge = elapsed / warn;
                    arc.Fill = charge;
                    Place(Sideways(drawn, UnityEngine.Random.insideUnitSphere * (pattern.tremble * charge * charge)));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                arc.Fill = 1f;
                arc.Strike(pattern.cleaveSwingTime);

                if (pattern.cleaveShake > 0f)
                {
                    TopDownCamera.Shake(pattern.cleaveShake, pattern.shakeDuration);
                }

                // Round the front, speeding up as it goes, in step with the arc.
                elapsed = 0f;

                while (elapsed < pattern.cleaveSwingTime)
                {
                    float swing = elapsed / pattern.cleaveSwingTime;
                    Place(Sideways(Mathf.Lerp(drawn, through, swing * swing), Vector3.zero));

                    yield return null;
                    elapsed += Tick();
                }

                elapsed = 0f;

                do
                {
                    Place(Sideways(through, Vector3.zero));

                    yield return null;
                    elapsed += Tick();
                }
                while (elapsed < holdTime && !Stopped);
            }

            /// <summary>Puts the sword back and gives the boss its movement back.</summary>
            public void End()
            {
                shine?.End();

                if (blades != null)
                {
                    blades.Return(index);
                }

                if (enemy != null)
                {
                    enemy.VelocityOverride = null;
                    enemy.FacingOverride = null;
                }
            }

            /// <summary>
            /// Pulled toward the player, as the witch's punches are: speed is
            /// gained toward them, never past the Home Speed, so a sidestep is
            /// followed round in a curve. Within the stand-off it eases off.
            /// </summary>
            private void Home(float deltaTime)
            {
                Vector3 toPlayer = enemy.HasTarget ? Flat(enemy.TargetPoint - enemy.transform.position) : Vector3.zero;

                if (toPlayer.sqrMagnitude > pattern.slashStandOff * pattern.slashStandOff)
                {
                    velocity += toPlayer.normalized * (pattern.homeAcceleration * deltaTime);
                    velocity = Vector3.ClampMagnitude(velocity, pattern.homeSpeed);
                }
                else
                {
                    velocity = Vector3.MoveTowards(velocity, Vector3.zero, pattern.homeSpeed * 4f * deltaTime);
                }

                enemy.VelocityOverride = velocity;
            }

            /// <summary>Slows toward a stop, from the Home Speed to nothing over the Brake Time.</summary>
            private void Brake(float deltaTime)
            {
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, pattern.homeSpeed / pattern.brakeTime * deltaTime);
                enemy.VelocityOverride = velocity;
            }

            private bool Near()
            {
                if (!enemy.HasTarget)
                {
                    return true;
                }

                return Flat(enemy.TargetPoint - enemy.transform.position).sqrMagnitude
                       <= pattern.slashStandOff * pattern.slashStandOff;
            }

            /// <summary>
            /// Homes away from the player for the Cleave Retreat Time --
            /// pulled away as the slashes are pulled in, at the Home Speed and
            /// Home Acceleration -- then brakes to a stop, turning after them
            /// all the while, as the sword eases over <paramref name="blendTime"/>
            /// into <paramref name="pose"/>. Lasts until both are done.
            /// </summary>
            private IEnumerable Retreat(float blendTime, Func<Pose> pose)
            {
                Vector3 fromPosition = sword.position;
                Quaternion fromRotation = sword.rotation;
                float moving = pattern.cleaveRetreatTime + pattern.brakeTime;
                float elapsed = 0f;

                while (elapsed < moving || elapsed < blendTime)
                {
                    if (elapsed < pattern.cleaveRetreatTime)
                    {
                        Vector3 away = enemy.HasTarget ? Flat(enemy.transform.position - enemy.TargetPoint) : Vector3.zero;

                        if (away.sqrMagnitude > 0.0001f)
                        {
                            velocity += away.normalized * (pattern.homeAcceleration * Time.deltaTime);
                            velocity = Vector3.ClampMagnitude(velocity, pattern.homeSpeed);
                        }

                        enemy.VelocityOverride = velocity;
                    }
                    else
                    {
                        Brake(Time.deltaTime);
                    }

                    Track();
                    Pose to = pose();
                    float eased = Smooth(blendTime > 0f ? elapsed / blendTime : 1f);
                    sword.SetPositionAndRotation(Vector3.Lerp(fromPosition, to.position, eased),
                                                 Quaternion.Slerp(fromRotation, to.rotation, eased));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                velocity = Vector3.zero;
                enemy.VelocityOverride = Vector3.zero;
            }

            /// <summary>Starts the glisten building to a strike <paramref name="seconds"/> from now.</summary>
            private void Warn(float seconds)
            {
                if (shine == null)
                {
                    shine = BladeGlisten.Begin(sword, pattern.glisten, seconds);
                }
                else
                {
                    shine.Restart(seconds);
                }

                clock = 0f;
            }

            /// <summary>Moves the glisten on a frame, and returns the frame's length.</summary>
            private float Tick()
            {
                float deltaTime = Time.deltaTime;
                clock += deltaTime;
                shine?.Tick(clock);
                return deltaTime;
            }

            private void Place(Pose pose)
            {
                sword.SetPositionAndRotation(pose.position, pose.rotation);
            }

            /// <summary>Turns the aim, and the boss with it, toward the player.</summary>
            private void Track()
            {
                Vector3 toPlayer = enemy.HasTarget ? Flat(enemy.TargetPoint - enemy.transform.position) : Vector3.zero;

                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    Vector3 turned = Vector3.RotateTowards(aim, toPlayer.normalized,
                                                           pattern.trackTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
                    aim = Flat(turned).normalized;
                }

                enemy.FacingOverride = aim;
            }

            /// <summary>
            /// Overhead, its tip <paramref name="angle"/> degrees from straight
            /// up toward the aim, its flat facing across the swing. The grip is
            /// overhead at <paramref name="swing"/> 0, down and out toward the
            /// aim at 1.
            /// </summary>
            private Pose Overhead(float angle, float swing, Vector3 shake)
            {
                Vector3 feet = enemy.transform.position;
                Vector3 grip = Vector3.Lerp(feet + Vector3.up * pattern.gripHeight,
                                            feet + Vector3.up * pattern.slamGripHeight + aim * pattern.slamReach,
                                            swing) + shake;

                float radians = angle * Mathf.Deg2Rad;
                Vector3 tip = Vector3.up * Mathf.Cos(radians) + aim * Mathf.Sin(radians);
                Vector3 across = Vector3.Cross(Vector3.up, aim);

                // As built the tip is down -Y, so up +Y points back at the grip.
                return new Pose(grip + tip * pattern.gripToPivot, Quaternion.LookRotation(across, -tip) * modelOffset);
            }

            /// <summary>
            /// Held out level, a little dipped, <paramref name="angle"/> degrees
            /// round from the aim (negative to the boss's left), its flat
            /// facing up so its edge leads the sweep.
            /// </summary>
            private Pose Sideways(float angle, Vector3 shake)
            {
                Vector3 grip = enemy.transform.position + Vector3.up * pattern.cleaveGripHeight + shake;
                Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * aim;
                Vector3 tip = Quaternion.AngleAxis(pattern.cleaveDip, Vector3.Cross(Vector3.up, outward)) * outward;
                Vector3 flat = Vector3.Cross(tip, Vector3.Cross(Vector3.up, tip));

                return new Pose(grip + tip * pattern.gripToPivot, Quaternion.LookRotation(flat, -tip) * modelOffset);
            }

            /// <summary>The floor under the boss, or its pivot without one.</summary>
            private Vector3 Floor()
            {
                Vector3 feet = enemy.transform.position;
                return Physics.Raycast(feet + Vector3.up, Vector3.down, out RaycastHit hit, 4f, pattern.floorMask, QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : feet;
            }
        }

        private void Slam(Enemy enemy, Vector3 aim, Vector3 floor)
        {
            Vector3 feet = enemy.transform.position;
            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            AmberSlam.Create(floor, aim, feet.y + 1f, feet.y + gripHeight - floor.y, -windup.y, slamAngle,
                             slash, SlashMaterial(), player);

            if (slashShake > 0f)
            {
                TopDownCamera.Shake(slashShake, shakeDuration);
            }
        }

        private Material SlashMaterial()
        {
            if (slashMaterial != null)
            {
                return slashMaterial;
            }

            if (runtimeSlashMaterial == null && FlatMaterial.DoubleSided != null)
            {
                runtimeSlashMaterial = new Material(FlatMaterial.DoubleSided) { name = "Runtime Greatsword Cleave Slash" };
            }

            if (runtimeSlashMaterial != null)
            {
                runtimeSlashMaterial.SetColor(FlatMaterial.BaseColorId, colour);
            }

            return runtimeSlashMaterial;
        }

        /// <summary>The way the boss faces, flat.</summary>
        private static Vector3 Facing(Enemy enemy)
        {
            Vector3 forward = Flat(enemy.Model.forward);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 1.5f;
            Movement.type = MovementType.StandStill;
        }
    }
}
