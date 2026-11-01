using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Chromatist's turn into its last phase: it stops, talks, then leaps
    /// and slams the floor with its mace hard enough to throw the player up
    /// onto a wall -- where they land running, up the wall, the boss bursting
    /// up behind them a moment later in pursuit. From there it is a forced
    /// run like <see cref="SpearHallIntro"/>'s, the other way round, and this
    /// time the boss can be hurt.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. When the boss moves into <see cref="phase"/>, it holds its own
    ///      attacks and cannot be hurt, the player's controls lock, and the
    ///      player cannot be hurt either until they land. Every enemy
    ///      projectile in flight fades away harmlessly.
    ///   2. After <see cref="dialogueDelay"/>, the dialogue plays through the
    ///      <see cref="DialogueBox"/>, click by click.
    ///   3. The boss leaps into the middle of its arena and slams it with the
    ///      mace -- its own Mace Leap attack, found among its phases, done
    ///      once with no warning, no damage and no blocks -- the web of cracks
    ///      spreading <see cref="slamRadius"/> across the floor.
    ///   4. The moment the mace hits, the player is thrown up in an arc onto
    ///      the stage's wall, turning in the air to land feet first on it.
    ///      The camera turns with them, ending square on the wall.
    ///   5. The landing. The wall is never really run on: the run happens on
    ///      a flat strip somewhere else, built to look the same, its foot at
    ///      the stage's Strip Base. As the player touches the wall, they, the
    ///      boss and the camera are all moved by the one turn that lays the
    ///      wall flat onto the strip, so the picture does not change -- but
    ///      the player is now upright on flat ground, and everything works as
    ///      normal. The camera shakes, the screen flashes and dust rings out,
    ///      covering any difference in the lighting.
    ///   6. The run: the player is carried up the strip at
    ///      <see cref="runSpeedMultiplier"/> times walking speed, steering only
    ///      left and right, facing back down it if <see cref="faceBackward"/>.
    ///      Attacks work as usual, through <see cref="ChaseFrame"/>. The
    ///      stage's strip pieces are laid on ahead of the player as they go.
    ///   7. <see cref="emergeDelay"/> later, the boss leaps up from behind,
    ///      off the bottom of the screen, to <see cref="followDistance"/>
    ///      behind the player, and can be hurt from then on. It keeps up at
    ///      run speed, weaving, and cycles through <see cref="runAttacks"/>. A
    ///      player who falls back to within <see cref="catchDistance"/> of it
    ///      -- snagged on something -- is struck and thrown on ahead. Any
    ///      <see cref="CuttableColumn"/> it runs into on the way is cut
    ///      apart, the boss untouched, if <see cref="cutColumns"/>.
    ///   8. When the boss dies, <see cref="Defeated"/> is raised and the stage
    ///      takes over (<see cref="WallRunStage.Finish"/>): the end piece --
    ///      the top of the wall -- goes on the end of the strip, and the
    ///      player runs on to it and leaps up over the edge onto the roof.
    ///
    /// The wall and the strip are the level's: a <see cref="WallRunStage"/>
    /// in the scene holds them, and is found as the phase starts. The arena's
    /// centre wants to be further from the wall than the camera's distance,
    /// or the boss is in the last frame on the wall and gone from the first
    /// on the strip.
    ///
    /// Run attacks should stand still, or know about the run, as Mace Smash,
    /// Mace Disturb and Spear Line do: the boss is placed by hand every frame, so one that
    /// moves it fights the run. Their movement and animation settings are
    /// ignored.
    ///
    /// Goes on the boss's root, beside <see cref="Enemy"/>. Plays once.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    public sealed class ChromatistPhaseThree : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The phase, by its place in the boss's phase list (the first is 0), whose start plays this.")]
        [Min(0)]
        private int phase = 2;

        [SerializeField]
        [Tooltip("Seconds every enemy projectile still in flight takes to fade away as the scene starts. They cannot hurt anyone meanwhile.")]
        [Min(0.01f)]
        private float projectileFadeTime = 0.4f;

        [Header("Dialogue")]
        [SerializeField]
        [Tooltip("Seconds from the scene starting to the first line.")]
        [Min(0f)]
        private float dialogueDelay = 0.6f;

        [SerializeField] private List<DialogueLine> dialogue = new List<DialogueLine>();

        [Header("Slam")]
        [SerializeField]
        [Tooltip("Radius of the web of cracks the slam spreads across the arena floor.")]
        [Min(0.1f)]
        private float slamRadius = 50f;

        [SerializeField]
        [Tooltip("Seconds from the mace hitting to the player being thrown.")]
        [Min(0f)]
        private float launchDelay = 0.05f;

        [Header("Flight")]
        [SerializeField]
        [Tooltip("Seconds the player is in the air, from the slam to the wall.")]
        [Min(0.1f)]
        private float flightTime = 1.4f;

        [SerializeField]
        [Tooltip("How high the arc bulges above the straight line from where the player was to the landing.")]
        [Min(0f)]
        private float flightArc = 6f;

        [Header("Landing")]
        [SerializeField] private float landingShake = 1.2f;
        [SerializeField] [Min(0f)] private float landingShakeDuration = 0.5f;

        [SerializeField]
        [Tooltip("Flashes the screen with the impact frames on landing, if the scene has them. It also hides the cut to the strip.")]
        private bool impactFrames = true;

        [SerializeField]
        [Tooltip("Puffs of dust ringing out round the player as they land.")]
        [Min(0)]
        private int landingPuffs = 28;

        [SerializeField]
        [Tooltip("Speed the landing dust blows outward at. Each varies a little.")]
        [Min(0f)]
        private float landingPuffSpeed = 7f;

        [SerializeField]
        private DustTrail.Settings landingDust = new DustTrail.Settings
        {
            size = 0.45f,
            lifetime = 0.8f,
            rise = 0.4f,
            colour = new Color(0.85f, 0.85f, 0.85f),
        };

        [Header("Run")]
        [SerializeField]
        [Tooltip("The player's speed up the strip, as a multiple of their walking speed.")]
        [Min(0f)]
        private float runSpeedMultiplier = 2.5f;

        [SerializeField]
        [Tooltip("The player's left-right steering speed during the run, as a multiple of their walking speed.")]
        [Min(0f)]
        private float sideSpeedMultiplier = 1f;

        [SerializeField]
        [Tooltip("The player faces back down the run, toward the camera and the boss, running backwards.")]
        private bool faceBackward = true;

        [SerializeField]
        [Tooltip("Shifts what the camera frames along the run during it: negative shows more behind the player, where the boss is.")]
        private float cameraFocusShift = -3f;

        [Header("Pursuit")]
        [SerializeField]
        [Tooltip("Seconds after landing before the boss leaps up from behind.")]
        [Min(0f)]
        private float emergeDelay = 1.5f;

        [SerializeField]
        [Tooltip("How far behind the player the boss waits, off the bottom of the screen, until it leaps up.")]
        [Min(0f)]
        private float emergeFrom = 30f;

        [SerializeField]
        [Tooltip("Seconds the boss's leap up takes.")]
        [Min(0.05f)]
        private float emergeTime = 0.9f;

        [SerializeField]
        [Tooltip("How high the boss's leap up arcs.")]
        [Min(0f)]
        private float emergeHeight = 6f;

        [SerializeField]
        [Tooltip("How far behind the player the boss runs.")]
        [Min(0f)]
        private float followDistance = 10f;

        [SerializeField]
        [Tooltip("Speed the boss drifts left and right as it runs. 0 keeps it on the strip's centre line.")]
        [Min(0f)]
        private float bossWeaveSpeed = 3f;

        [SerializeField]
        [Tooltip("Furthest the boss drifts to either side of the strip's centre line.")]
        [Min(0f)]
        private float bossWeaveRange = 3f;

        [SerializeField]
        [Tooltip("Degrees per second the boss turns to face the player.")]
        [Min(1f)]
        private float bossTurnSpeed = 360f;

        [SerializeField]
        [Tooltip("Animator state the boss plays, looping, through the run. Empty uses the boss's own Idle Animation.")]
        private string runAnimation = "";

        [Header("Run attacks")]
        [SerializeField]
        [Tooltip("Attacks the boss cycles through, in order, as it chases. Each waits its own Delay After before the next. Their movement and animation settings are ignored here.")]
        private List<AttackPattern> runAttacks = new List<AttackPattern>();

        [SerializeField]
        [Tooltip("Seconds after the boss has leapt up before its first attack.")]
        [Min(0f)]
        private float firstRunAttackDelay = 1.5f;

        [SerializeField]
        [Tooltip("Attacks that may cut in after any other run attack, each at its own odds, before the cycle carries on where it was.")]
        private List<Enemy.CutIn> runCutIns = new List<Enemy.CutIn>();

        [Header("Caught")]
        [SerializeField]
        [Tooltip("How close behind the player the boss can get -- the player snagged on something -- before it strikes them.")]
        [Min(0f)]
        private float catchDistance = 3f;

        [SerializeField]
        [Min(0f)]
        private float catchDamage = 20f;

        [Header("Columns")]
        [SerializeField]
        [Tooltip("The boss cuts apart any Cuttable Column it runs into, untouched by it, rather than passing through it.")]
        private bool cutColumns = true;

        [SerializeField]
        [Tooltip("How close, measured flat from the boss's pivot to the column's middle, the boss cuts a column.")]
        [Min(0f)]
        private float columnCutReach = 2f;

        [SerializeField]
        [Tooltip("How the column's top is cut to pieces as the boss goes through it.")]
        private KatanaCut.Settings columnCut = new KatanaCut.Settings { slashCount = 2, maxPieces = 6 };

        [SerializeField]
        [Tooltip("How far cutting a column jolts the camera. 0 for none.")]
        [Min(0f)]
        private float columnCutShake = 0.3f;

        [SerializeField]
        [Tooltip("Speed the struck player is thrown on up the run and toward its centre line at, clear of what held them. The boss drops back to its distance.")]
        [Min(0f)]
        private float catchPush = 25f;

        [Header("Hazards")]
        [SerializeField]
        [Tooltip("Chance each strip piece laid once the boss has leapt up gets one of its Hall Columns switched on. The piece after one never does.")]
        [Range(0f, 1f)]
        private float hazardChance = 0.1f;

        /// <summary>A laid piece of strip, and how far up the run its far edge is.</summary>
        private struct Laid
        {
            public GameObject piece;
            public float end;
        }

        private Enemy enemy;
        private EnemyHealth health;
        private bool played;

        // The level's half, found as the phase starts, and what it holds.
        private WallRunStage stage;
        private Transform wallBase;
        private Transform stripBase;
        private Transform chainStart;
        private float landingHeight;
        private float landingSpread;

        private PlayerMotor motor;
        private PlayerInputReader input;
        private PlayerHealth playerHealth;
        private Rigidbody playerBody;
        private RigidbodyInterpolation playerInterpolation;
        private bool flying;
        private bool running;

        // Set once the boss has died mid-run and the stage has the player.
        private bool handedOff;

        // Up the run and across it to the right, both flat; and the one turn
        // that lays the wall onto the strip.
        private Vector3 along;
        private Vector3 across;
        private Quaternion wallToStrip = Quaternion.identity;

        private readonly Queue<Laid> laid = new Queue<Laid>();

        // One seamless floor the player really runs on, just above the strip
        // pieces. A body pushed along a row of separate floor colliders can
        // catch on the edge where two meet and stall, running in place, while
        // the boss closes in. Raised clear of them, the seams are never touched.
        private const float RunFloorRaise = 0.05f;
        private const float RunFloorBehind = 20f;
        private const float RunFloorAhead = 40f;
        private BoxCollider runFloor;
        private float runFloorWidth;
        private float runFloorCentre;

        private float chainEnd;
        private float segmentStep;
        private int laidCount;
        private bool chasing;
        private bool lastHadHazard;

        /// <summary>Raised once the player has landed and the run begins.</summary>
        public event Action<ChromatistPhaseThree> Landed;

        /// <summary>Raised when the boss dies, ending the run.</summary>
        public event Action<ChromatistPhaseThree> Defeated;

        /// <summary>The phase, by its place in the boss's phase list, this plays for.</summary>
        public int Phase => phase;

        /// <summary>True from the moment the boss moves into <see cref="Phase"/> on.</summary>
        public bool HasStarted => played;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            health = GetComponent<EnemyHealth>();
        }

        private void OnEnable()
        {
            enemy.PhaseChanged += OnPhaseChanged;
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            enemy.PhaseChanged -= OnPhaseChanged;
            health.Died -= OnDied;

            // Cut off mid-scene: never leave the player stuck in it.
            ReleasePlayer();
        }

        private void OnPhaseChanged(Enemy changed, int index)
        {
            if (played || index != phase)
            {
                return;
            }

            stage = FindAnyObjectByType<WallRunStage>();
            List<string> missing = new List<string>();

            if (stage == null) missing.Add("there is no Wall Run Stage in the scene");
            else if (stage.WallBase == null) missing.Add("the Wall Run Stage's Wall Base is empty");
            else if (stage.StripBase == null) missing.Add("the Wall Run Stage's Strip Base is empty");

            if (missing.Count > 0)
            {
                Debug.LogWarning($"[ChromatistPhaseThree] {name} won't play: {string.Join("; ", missing)}.", this);
                return;
            }

            wallBase = stage.WallBase;
            stripBase = stage.StripBase;
            chainStart = stage.ChainStart;
            landingHeight = stage.LandingHeight;
            landingSpread = stage.LandingSpread;

            played = true;

            // Now, inside the phase change, so the new phase's first attack
            // is held before it starts. Its own attacks stay held for good:
            // the run's are started from here.
            enemy.HoldAttacks = true;
            health.Invulnerable = true;
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (motor == null)
            {
                yield break;
            }

            input = motor.GetComponent<PlayerInputReader>();
            playerBody = motor.GetComponent<Rigidbody>();
            playerHealth = motor.GetComponent<PlayerHealth>();
            LockPlayer();

            if (playerHealth != null)
            {
                playerHealth.SetDamageTakenModifier(this, 0f);
            }

            EnemyProjectile.FadeOutAll(projectileFadeTime);
            enemy.PlayAnimation(enemy.IdleAnimation, 0.3f);
            TopDownCamera.TurnToward(transform.position, 0.8f);

            yield return Wait(dialogueDelay);

            for (int i = 0; i < dialogue.Count; i++)
            {
                bool next = false;
                DialogueBox.Instance.Say(dialogue[i], () => next = true);

                while (!next)
                {
                    HoldStill();
                    yield return null;
                }
            }

            if (dialogue.Count > 0)
            {
                DialogueBox.Instance.Close();
            }

            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;

            // The slam; the player is thrown the moment it hits.
            bool hit = false;

            MaceLeapPattern leap = FindMaceLeap();

            if (leap != null)
            {
                Vector3 point = enemy.ArenaCentre ?? motor.transform.position;
                bool over = false;
                StartCoroutine(Slam(leap, point, () => hit = true, () => over = true));

                while (!hit && !over)
                {
                    LockPlayer();
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning($"[ChromatistPhaseThree] {name} has no Mace Leap attack in any phase; the player is thrown without a slam.", this);
            }

            if (health.IsDead || motor == null)
            {
                yield break;
            }

            yield return Wait(launchDelay);
            yield return Flight();

            if (motor != null && !health.IsDead)
            {
                yield return Run();
            }
        }

        /// <summary>The slam, run on the boss so it ends with it.</summary>
        private IEnumerator Slam(MaceLeapPattern leap, Vector3 point, Action impacted, Action over)
        {
            yield return enemy.StartCoroutine(leap.Slam(enemy, point, slamRadius, centre => impacted()));
            over();
        }

        /// <summary>The boss's own Mace Leap, from the first phase that has one.</summary>
        private MaceLeapPattern FindMaceLeap()
        {
            foreach (Enemy.Phase stage in enemy.Phases)
            {
                foreach (AttackPattern pattern in stage.patterns)
                {
                    if (pattern is MaceLeapPattern leap)
                    {
                        return leap;
                    }
                }

                foreach (Enemy.CutIn cutIn in stage.cutIns)
                {
                    if (cutIn.pattern is MaceLeapPattern leap)
                    {
                        return leap;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The player is thrown in an arc onto the wall, turning to land feet
        /// first, the camera turning with them to end square on it; then the
        /// cut to the strip, and the landing.
        /// </summary>
        private IEnumerator Flight()
        {
            along = Flat(stripBase.forward).normalized;

            if (along.sqrMagnitude < 0.0001f)
            {
                along = Vector3.forward;
            }

            across = Vector3.Cross(Vector3.up, along);
            wallToStrip = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Inverse(WallFrame());

            Vector3 normal = WallNormal();
            Vector3 wallRight = Vector3.Cross(normal, Vector3.up);
            Vector3 start = motor.transform.position;
            Quaternion startRotation = motor.transform.rotation;

            // Where the player lands, on the wall and on the strip.
            float side = Mathf.Clamp(Vector3.Dot(start - wallBase.position, wallRight), -landingSpread, landingSpread);
            Vector3 wallLanding = wallBase.position + wallRight * side + Vector3.up * landingHeight + normal * PlayerFootOffset();
            Vector3 stripLanding = ToStrip(wallLanding);

            Vector3 facing = faceBackward ? -along : along;
            Quaternion stripRotation = Quaternion.LookRotation(facing, Vector3.up);
            Quaternion wallRotation = Quaternion.Inverse(wallToStrip) * stripRotation;

            // The camera ends where the turn takes the run's framing back to
            // the wall: square on it, the run's up the screen.
            float runYaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            TopDownCamera.FocusShift = along * cameraFocusShift;
            TopDownCamera.Framing(stripLanding, runYaw, out Vector3 stripCamera, out Quaternion stripCameraRotation);
            Vector3 cameraTo = ToWall(stripCamera) - wallLanding;
            Quaternion cameraToRotation = Quaternion.Inverse(wallToStrip) * stripCameraRotation;

            Transform view = Camera.main != null ? Camera.main.transform : null;
            Vector3 cameraFrom = view != null ? view.position - start : cameraTo;
            Quaternion cameraFromRotation = view != null ? view.rotation : cameraToRotation;

            // Flown by hand.
            flying = true;
            motor.CancelDash();
            motor.enabled = false;

            if (playerBody != null)
            {
                playerInterpolation = playerBody.interpolation;
                playerBody.interpolation = RigidbodyInterpolation.None;
                playerBody.isKinematic = true;
            }

            for (float clock = 0f; clock < flightTime && motor != null; clock += Time.deltaTime)
            {
                LockPlayer();

                float t = clock / flightTime;
                float turn = Smooth(t);
                Vector3 at = Vector3.Lerp(start, wallLanding, t) + Vector3.up * (flightArc * 4f * t * (1f - t));
                motor.transform.SetPositionAndRotation(at, Quaternion.Slerp(startRotation, wallRotation, turn));

                // Kept round the player as it swings, so they stay in view.
                TopDownCamera.Hold(at + Vector3.Slerp(cameraFrom, cameraTo, turn),
                                   Quaternion.Slerp(cameraFromRotation, cameraToRotation, turn));
                yield return null;
            }

            if (motor == null)
            {
                yield break;
            }

            Land(stripLanding, stripRotation, runYaw);
        }

        /// <summary>
        /// The cut: the player, the boss and the camera all move by the turn
        /// that lays the wall onto the strip, so the picture stays as it is,
        /// and the run begins.
        /// </summary>
        private void Land(Vector3 stripLanding, Quaternion stripRotation, float runYaw)
        {
            SetAll(stage.HideOnLanding, false);
            SetAll(stage.ShowOnLanding, true);

            PrepareChain();
            BuildRunFloor(Progress(stripLanding));
            stripLanding += Vector3.up * RunFloorRaise;

            RestorePlayerBody();
            motor.Teleport(stripLanding);
            motor.transform.rotation = stripRotation;

            if (playerBody != null)
            {
                playerBody.rotation = stripRotation;
            }

            TopDownCamera.Release(runYaw);
            TopDownCamera.Shake(landingShake, landingShakeDuration);

            if (impactFrames)
            {
                ImpactFrames frames = FindAnyObjectByType<ImpactFrames>();

                if (frames != null)
                {
                    frames.TriggerImpact(stripLanding);
                }
            }

            RingDust(new Vector3(stripLanding.x, stripBase.position.y, stripLanding.z));

            // Off the bottom of the screen until it leaps up.
            PlaceBossForRun();

            if (playerHealth != null)
            {
                playerHealth.RemoveDamageTakenModifier(this);
            }

            float runSpeed = motor.MoveSpeed * runSpeedMultiplier;
            SetLocked(false);
            running = true;
            ChaseFrame.Begin(along * runSpeed);
            motor.StartAutoRun(along * runSpeed, motor.MoveSpeed * sideSpeedMultiplier);
            motor.FacingOverride = faceBackward ? -along : (Vector3?)null;

            StartCoroutine(KeepLaying());
            Landed?.Invoke(this);
        }

        /// <summary>
        /// Lays the strip on ahead of the player for as long as the run lasts.
        /// Its own loop, apart from the boss's, so nothing going wrong with the
        /// boss can leave the player running off the end of the floor.
        /// </summary>
        private IEnumerator KeepLaying()
        {
            while (motor != null && running)
            {
                float player = Progress(motor.transform.position);
                LayHall(player);
                PlaceRunFloor(player);
                yield return null;
            }
        }

        /// <summary>
        /// The run's floor: one box as wide as a strip piece, its top
        /// <see cref="RunFloorRaise"/> above the strip, moved along under the
        /// player as they go.
        /// </summary>
        private void BuildRunFloor(float player)
        {
            DestroyRunFloor();

            GameObject floor = new GameObject("RunFloor");
            floor.layer = chainStart != null ? chainStart.gameObject.layer : stripBase.gameObject.layer;
            floor.transform.rotation = Quaternion.LookRotation(along, Vector3.up);

            Rigidbody body = floor.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;

            runFloor = floor.AddComponent<BoxCollider>();

            // As wide as the strip pieces, where they are; without one to
            // measure, wide enough for anything.
            Vector2 width = chainStart != null ? Extent(chainStart.gameObject, across) : Vector2.zero;

            if (width.y - width.x > 1f)
            {
                runFloorWidth = width.y - width.x;
                runFloorCentre = Vector3.Dot(chainStart.position - stripBase.position, across) + (width.x + width.y) * 0.5f;
            }
            else
            {
                runFloorWidth = 200f;
                runFloorCentre = 0f;
            }

            PlaceRunFloor(player);
        }

        /// <summary>
        /// Moves the run's floor to reach from behind <paramref name="player"/>
        /// to the end of the laid strip.
        /// </summary>
        private void PlaceRunFloor(float player)
        {
            if (runFloor == null)
            {
                return;
            }

            // Out to the end of the laid strip, so it still reaches the end
            // piece once the boss is dead and no more is laid.
            float near = player - RunFloorBehind;
            float far = chainStart != null && segmentStep > 0f ? chainEnd : player + RunFloorAhead;

            far = Mathf.Max(far, near + 1f);
            const float thickness = 1f;

            runFloor.size = new Vector3(runFloorWidth, thickness, far - near);
            runFloor.transform.position = stripBase.position
                                          + along * ((near + far) * 0.5f)
                                          + across * runFloorCentre
                                          + Vector3.up * (RunFloorRaise - thickness * 0.5f);
        }

        private void DestroyRunFloor()
        {
            if (runFloor != null)
            {
                Destroy(runFloor.gameObject);
                runFloor = null;
            }
        }

        private void OnStageFinished(WallRunStage finished)
        {
            finished.Finished -= OnStageFinished;
            DestroyRunFloor();
        }

        /// <summary>
        /// Up the strip, which is laid ahead of the player as they go: the
        /// boss leaps up behind them, then keeps up, attacking, until it dies.
        /// </summary>
        private IEnumerator Run()
        {
            float runSpeed = motor.MoveSpeed * runSpeedMultiplier;
            float bossHeight = stripBase.position.y + BossFootOffset();
            float bossProgress = Progress(motor.transform.position) - emergeFrom;
            float weave = 0f;
            float weaveTo = 0f;
            float clock = 0f;

            // Seconds into the leap up, or negative before it.
            float emergeClock = -1f;
            float emergeFromProgress = 0f;
            chasing = false;
            DustTrail dust = DustTrail.On(gameObject);

            enemy.PlayAnimation(string.IsNullOrEmpty(runAnimation) ? enemy.IdleAnimation : runAnimation, 0.2f);

            while (motor != null && running && !health.IsDead)
            {
                yield return null;
                clock += Time.deltaTime;

                float player = Progress(motor.transform.position);

                // An attack that let go of it would have it walking itself.
                enemy.PhysicsDriven = true;

                float height = bossHeight;
                Vector3 before = transform.position;

                if (!chasing && emergeClock < 0f)
                {
                    // Keeping pace out of sight.
                    bossProgress = player - emergeFrom;

                    if (clock >= emergeDelay)
                    {
                        emergeClock = 0f;
                        emergeFromProgress = bossProgress;
                    }
                }
                else if (!chasing)
                {
                    // The leap up from behind, landing at its distance.
                    emergeClock += Time.deltaTime;
                    float t = Mathf.Clamp01(emergeClock / emergeTime);
                    bossProgress = Mathf.Lerp(emergeFromProgress, player - followDistance, Smooth(t));
                    height += emergeHeight * 4f * t * (1f - t);

                    if (t >= 1f)
                    {
                        chasing = true;
                        health.Invulnerable = false;
                        StartCoroutine(RunAttacks());
                    }
                }
                else
                {
                    // Keeps up at run speed, never dropping back past its
                    // distance; a stalled player is caught up with.
                    bossProgress = Mathf.Max(bossProgress + runSpeed * Time.deltaTime, player - followDistance);

                    if (Mathf.Abs(weave - weaveTo) < 0.01f)
                    {
                        weaveTo = UnityEngine.Random.Range(-bossWeaveRange, bossWeaveRange);
                    }

                    weave = Mathf.MoveTowards(weave, weaveTo, bossWeaveSpeed * Time.deltaTime);

                    if (player - bossProgress < catchDistance)
                    {
                        Catch();
                        bossProgress = player - followDistance;
                    }
                }

                transform.position = OnCentreLine(bossProgress, height) + across * weave;
                enemy.TurnModelToward(motor.transform.position - transform.position, bossTurnSpeed * Time.deltaTime);

                // Out of sight, it leaves the columns standing for the player to see cut.
                if (cutColumns && (emergeClock >= 0f || chasing))
                {
                    // A column that cannot be cut -- a mesh without Read/Write,
                    // say -- must not stop the boss mid-run: it is reported
                    // once, and the boss goes through columns from then on.
                    try
                    {
                        CutColumns((transform.position - before) / Mathf.Max(Time.deltaTime, 0.0001f));
                    }
                    catch (Exception failure)
                    {
                        Debug.LogException(failure, this);
                        Debug.LogWarning($"[ChromatistPhaseThree] {name} couldn't cut a column, so it stops cutting them this run. The error above says why.", this);
                        cutColumns = false;
                    }
                }

                if ((transform.position - before).sqrMagnitude > 0.000001f && height <= bossHeight + 0.01f)
                {
                    dust.KeepEmitting();
                }
            }
        }

        /// <summary>The boss strikes a player it has caught up with, throwing them on ahead.</summary>
        private void Catch()
        {
            Vector3 at = motor.transform.position;

            if (playerHealth != null)
            {
                playerHealth.TakeDamage(catchDamage, transform.position);
            }

            float offset = Vector3.Dot(at - stripBase.position, across);
            float side = Mathf.Abs(offset) > 0.5f ? -Mathf.Sign(offset) : 0f;
            motor.AddImpulse((along + across * side).normalized * catchPush);
            TopDownCamera.Shake(0.5f, 0.3f);
        }

        /// <summary>
        /// Every column the boss has run into is cut off at its cut height,
        /// the top cut to pieces carried on at <paramref name="velocity"/>,
        /// the boss's own. The boss is not hurt or slowed.
        /// </summary>
        private void CutColumns(Vector3 velocity)
        {
            if (!cutColumns)
            {
                return;
            }

            IReadOnlyList<CuttableColumn> columns = CuttableColumn.Active;

            // Backwards: a column cut comes off the list.
            for (int i = columns.Count - 1; i >= 0; i--)
            {
                CuttableColumn column = columns[i];

                if (column == null || column.Taken)
                {
                    continue;
                }

                Vector3 offset = Flat(column.CutPoint - transform.position);

                if (offset.sqrMagnitude > columnCutReach * columnCutReach)
                {
                    continue;
                }

                GameObject top = column.CutOff(across, columnCut);

                if (top == null)
                {
                    continue;
                }

                KatanaCut.Cut(top, columnCut, transform.position, velocity);
                Destroy(top);

                if (columnCutShake > 0f)
                {
                    TopDownCamera.Shake(columnCutShake, 0.2f);
                }
            }
        }

        /// <summary>The boss's attacks during the run, in order and round again, until it dies.</summary>
        private IEnumerator RunAttacks()
        {
            if (runAttacks.Count == 0)
            {
                // Nothing to play, so nothing to wait for: it can die.
                enemy.MarkPhaseAttacked();
                yield break;
            }

            yield return new WaitForSeconds(firstRunAttackDelay);

            AttackPattern cutIn = null;

            for (int next = 0; running && !health.IsDead; )
            {
                AttackPattern attack = cutIn;
                cutIn = null;

                if (attack == null)
                {
                    attack = runAttacks[next];
                    next = (next + 1) % runAttacks.Count;
                }

                if (attack == null || !attack.CanPerform(enemy))
                {
                    // Every one skipped would otherwise spin here forever.
                    yield return null;
                    continue;
                }

                // Run on the boss, so it ends with it. It counts as the
                // phase's attack, so the boss can be worn down past it.
                enemy.MarkPhaseAttacked();
                yield return enemy.StartCoroutine(attack.Perform(enemy));
                cutIn = RollRunCutIn(attack);
                yield return new WaitForSeconds(attack.DelayAfter);
            }
        }

        /// <summary>
        /// The cut-in, if any, to perform after <paramref name="performed"/>:
        /// the first in the list, other than it, whose odds come up.
        /// </summary>
        private AttackPattern RollRunCutIn(AttackPattern performed)
        {
            foreach (Enemy.CutIn entry in runCutIns)
            {
                if (entry != null && entry.pattern != null && entry.pattern != performed &&
                    UnityEngine.Random.value < entry.chance)
                {
                    return entry.pattern;
                }
            }

            return null;
        }

        private void OnDied(EnemyHealth died)
        {
            if (!played)
            {
                return;
            }

            if (running && motor != null)
            {
                // The run goes on without it, the stage taking the player to
                // the top of the wall and up onto the roof. This goes with
                // the boss, so it lets go of the player here.
                running = false;
                handedOff = true;

                // The run's floor stays, reaching the end piece, until the
                // player is up on the roof.
                PlaceRunFloor(Progress(motor.transform.position));
                stage.Finished += OnStageFinished;
                stage.Finish(motor, along, chainEnd);
            }
            else
            {
                ReleasePlayer();
            }

            Defeated?.Invoke(this);
        }

        /// <summary>The boss, moved onto the strip behind the player, out of sight, and taken off its arena.</summary>
        private void PlaceBossForRun()
        {
            enemy.Arena = null;
            enemy.ArenaCentre = null;
            enemy.PhysicsDriven = true;
            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;

            Rigidbody body = GetComponent<Rigidbody>();

            // Moved by hand throughout. Interpolation would drag the
            // transform back toward the body's last physics position.
            if (body != null)
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }

            float player = Progress(motor.transform.position);
            transform.position = OnCentreLine(player - emergeFrom, stripBase.position.y + BossFootOffset());
            enemy.TurnModelToward(along, 360f);
        }

        /// <summary>Gets the strip's chain ready to lay copies on from Chain Start.</summary>
        private void PrepareChain()
        {
            if (chainStart == null)
            {
                return;
            }

            Vector2 first = Extent(chainStart.gameObject, along);
            segmentStep = stage.SegmentLength > 0f ? stage.SegmentLength : first.y - first.x;

            if (segmentStep < 1f)
            {
                Debug.LogWarning($"[ChromatistPhaseThree] {name} can't tell how long a strip piece is, so it lays none. Set Segment Length.", this);
                segmentStep = 0f;
            }

            chainEnd = Progress(chainStart.position) + first.x + segmentStep;
        }

        /// <summary>Lays pieces until the strip reaches Build Ahead past <paramref name="player"/>, and removes ones far behind.</summary>
        private void LayHall(float player)
        {
            if (chainStart == null || segmentStep <= 0f)
            {
                return;
            }

            while (chainEnd < player + stage.BuildAhead)
            {
                laidCount++;

                GameObject source = stage.HallSegment != null ? stage.HallSegment : chainStart.gameObject;
                GameObject piece = Instantiate(source, chainStart.position + along * (segmentStep * laidCount), chainStart.rotation);
                piece.name = source.name;
                PlaceHazard(piece);

                chainEnd += segmentStep;
                laid.Enqueue(new Laid { piece = piece, end = chainEnd });
            }

            while (laid.Count > 0 && laid.Peek().end < player - stage.KeepBehind)
            {
                Destroy(laid.Dequeue().piece);
            }
        }

        /// <summary>
        /// Switches on one of the piece's Hall Columns, at random, at
        /// <see cref="hazardChance"/> -- once the boss is chasing, and never
        /// on two pieces in a row. The rest are switched off.
        /// </summary>
        private void PlaceHazard(GameObject piece)
        {
            HallColumn[] columns = piece.GetComponentsInChildren<HallColumn>(true);

            foreach (HallColumn column in columns)
            {
                column.gameObject.SetActive(false);
            }

            bool hazard = chasing && !lastHadHazard && columns.Length > 0 && UnityEngine.Random.value < hazardChance;
            lastHadHazard = hazard;

            if (hazard)
            {
                columns[UnityEngine.Random.Range(0, columns.Length)].gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// The wall's own frame: forward up the wall, up out of it. The turn
        /// takes it to the strip's: forward up the run, up out of the strip.
        /// </summary>
        private Quaternion WallFrame()
        {
            return Quaternion.LookRotation(Vector3.up, WallNormal());
        }

        private Vector3 WallNormal()
        {
            Vector3 normal = Flat(wallBase.forward);
            return normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.back;
        }

        /// <summary>Where a point by the wall goes on the strip.</summary>
        private Vector3 ToStrip(Vector3 point)
        {
            return stripBase.position + wallToStrip * (point - wallBase.position);
        }

        /// <summary>Where a point by the strip goes by the wall.</summary>
        private Vector3 ToWall(Vector3 point)
        {
            return wallBase.position + Quaternion.Inverse(wallToStrip) * (point - stripBase.position);
        }

        /// <summary>How far up the run <paramref name="position"/> is from the Strip Base.</summary>
        private float Progress(Vector3 position)
        {
            return Vector3.Dot(position - stripBase.position, along);
        }

        /// <summary>The point on the strip's centre line <paramref name="progress"/> up it, at <paramref name="height"/>.</summary>
        private Vector3 OnCentreLine(float progress, float height)
        {
            Vector3 point = stripBase.position + along * progress;
            point.y = height;
            return point;
        }

        /// <summary>Height of the player's pivot above the bottom of their colliders.</summary>
        private float PlayerFootOffset()
        {
            float bottom = float.MaxValue;

            foreach (Collider part in motor.GetComponentsInChildren<Collider>())
            {
                if (!part.isTrigger && part.enabled)
                {
                    bottom = Mathf.Min(bottom, part.bounds.min.y);
                }
            }

            return bottom < float.MaxValue ? motor.transform.position.y - bottom : 0f;
        }

        /// <summary>Height of the boss's pivot above the bottom of its colliders.</summary>
        private float BossFootOffset()
        {
            Physics.SyncTransforms();
            return transform.position.y - health.BodyBounds().min.y;
        }

        /// <summary>Puffs of dust spread evenly round the spot, blown outward across the floor.</summary>
        private void RingDust(Vector3 floor)
        {
            if (landingPuffs <= 0)
            {
                return;
            }

            DustTrail.Cloud cloud = DustTrail.Cloud.Create("WallLandingDust");
            float start = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            for (int i = 0; i < landingPuffs; i++)
            {
                float angle = start + Mathf.PI * 2f * i / landingPuffs + UnityEngine.Random.Range(-0.1f, 0.1f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 velocity = outward * (landingPuffSpeed * UnityEngine.Random.Range(0.7f, 1.1f))
                                   + Vector3.up * (landingDust.rise * UnityEngine.Random.Range(0.5f, 1f));
                cloud.Emit(floor + outward * 0.6f + Vector3.up * (landingDust.size * 0.3f), velocity, landingDust);
            }

            cloud.Release();
        }

        /// <summary>Keeps the player's controls locked and the boss standing facing them.</summary>
        private void HoldStill()
        {
            LockPlayer();
            enemy.VelocityOverride = Vector3.zero;

            if (motor != null)
            {
                Vector3 toward = Flat(motor.transform.position - transform.position);

                if (toward.sqrMagnitude > 0.0001f)
                {
                    enemy.FacingOverride = toward;
                }
            }
        }

        /// <summary>
        /// Locks the player's controls. Called every frame of the scene rather
        /// than once, so a snare running out partway, which unlocks them,
        /// cannot hand control back early.
        /// </summary>
        private void LockPlayer()
        {
            SetLocked(true);
        }

        private IEnumerator Wait(float seconds)
        {
            for (float clock = 0f; clock < seconds; clock += Time.deltaTime)
            {
                HoldStill();
                yield return null;
            }
        }

        private void SetLocked(bool locked)
        {
            if (input != null)
            {
                input.Locked = locked;
            }
        }

        /// <summary>Gives the player back to physics after the flight.</summary>
        private void RestorePlayerBody()
        {
            if (!flying)
            {
                return;
            }

            flying = false;

            if (motor != null)
            {
                motor.enabled = true;
            }

            if (playerBody != null)
            {
                playerBody.isKinematic = false;
                playerBody.interpolation = playerInterpolation;
            }
        }

        /// <summary>Gives the player their normal controls back, whatever state the scene left them in.</summary>
        private void ReleasePlayer()
        {
            if (!played)
            {
                return;
            }

            if (handedOff)
            {
                // The stage has the player now.
                if (playerHealth != null)
                {
                    playerHealth.RemoveDamageTakenModifier(this);
                }

                return;
            }

            if (flying)
            {
                RestorePlayerBody();
                TopDownCamera.Release(Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
            }

            if (running)
            {
                running = false;
                ChaseFrame.End();
            }

            DestroyRunFloor();

            TopDownCamera.FocusShift = Vector3.zero;

            if (motor != null)
            {
                motor.StopAutoRun();
                motor.FacingOverride = null;
            }

            SetLocked(false);

            if (playerHealth != null)
            {
                playerHealth.RemoveDamageTakenModifier(this);
            }
        }

        private static void SetAll(List<GameObject> objects, bool active)
        {
            foreach (GameObject thing in objects)
            {
                if (thing != null)
                {
                    thing.SetActive(active);
                }
            }
        }

        /// <summary>How far behind (x) and ahead (y) of its pivot, along <paramref name="direction"/>, the renderers of <paramref name="piece"/> reach.</summary>
        private static Vector2 Extent(GameObject piece, Vector3 direction)
        {
            Vector3 pivot = piece.transform.position;
            float near = float.MaxValue;
            float far = float.MinValue;

            foreach (Renderer part in piece.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = part.bounds;

                // Every corner, so a turned box is measured right.
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));

                    float distance = Vector3.Dot(corner - pivot, direction);
                    near = Mathf.Min(near, distance);
                    far = Mathf.Max(far, distance);
                }
            }

            return near <= far ? new Vector2(near, far) : Vector2.zero;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z);
        }
    }
}
