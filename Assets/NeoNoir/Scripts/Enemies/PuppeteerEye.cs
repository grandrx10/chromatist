using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of the Puppeteer's eyes: a stand-in that takes blows on the
    /// boss's behalf. Whatever health it loses, the boss loses too, and when
    /// it is destroyed the boss takes <see cref="PuppeteerEyes"/>' kill bonus
    /// on top.
    /// </summary>
    /// <remarks>
    /// Only health it actually loses is passed on: an eye on 30 hit for 80
    /// costs the boss 30, plus the bonus. It never heals.
    ///
    /// Otherwise it stares: <see cref="eye"/> turns, up and down as well as
    /// round, to look straight at the player, its local +Z being the pupil.
    /// The exceptions: <see cref="FireLaser"/>, for
    /// <see cref="EyeballLaserPattern"/>, which takes over its gaze until the
    /// laser is spent; and <see cref="GazeOverride"/>, which holds its gaze
    /// on a direction for as long as it is set, as <see cref="EyeRing"/> does,
    /// pairing it with <see cref="StartBeam"/> for a steady beam straight
    /// along that gaze.
    ///
    /// Destroyed, it is not removed at once: it goes intangible and is
    /// hauled straight up into the sky, faster and faster, and removed once
    /// it is out of the camera's view. A laser it was firing fizzles out.
    ///
    /// It never falls: a Rigidbody on it is made kinematic, with no gravity.
    ///
    /// Goes on the eye prefab's root, beside <see cref="EnemyHealth"/>, whose
    /// Max Health is the eye's (100 by default). Dropped and linked to the
    /// boss by <see cref="PuppeteerEyes"/>; one placed by hand stares but
    /// hurts no one.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class PuppeteerEye : MonoBehaviour
    {
        private enum LaserPhase { None, Tracking, Lowering, Firing, BeamCharging, Beaming }

        [SerializeField]
        [Tooltip("The part that turns to look at the player, its local +Z being the pupil. Empty turns this object.")]
        private Transform eye;

        [SerializeField]
        [Tooltip("Fastest the eye turns, in degrees per second. 0 snaps straight to the player.")]
        [Min(0f)]
        private float turnSpeed = 540f;

        [Header("Death")]
        [SerializeField]
        [Tooltip("How quickly a destroyed eye picks up speed as it is hauled up into the sky, in units per second per second.")]
        [Min(0.1f)]
        private float ascendAcceleration = 30f;

        [SerializeField]
        [Tooltip("Fastest a destroyed eye is hauled up, in units per second.")]
        [Min(0.1f)]
        private float ascendMaxSpeed = 40f;

        [SerializeField]
        [Tooltip("Seconds after which a destroyed eye is removed even if it is somehow still in view.")]
        [Min(0.1f)]
        private float ascendTimeout = 4f;

        private static readonly Plane[] frustum = new Plane[6];
        private static readonly RaycastHit[] groundHits = new RaycastHit[16];

        private EnemyHealth health;
        private EnemyHealth boss;
        private float killBonus;
        private float lastHealth;

        private Transform player;
        private Collider playerCollider;

        // Laser state.
        private LaserPhase laserPhase;
        private EyeballLaserPattern.Settings laser;
        private float laserClock;
        private Quaternion lowerFrom;
        private Vector3 heading;
        private Transform muzzle;
        private float muzzleDistance;

        // Steady beam state, from StartBeam.
        private SweepBeam.Settings steadyBeam;
        private float steadyChargeTime;
        private SceptorCharge charge;
        private SweepBeam beam;
        private LaserScorch scorch;

        // Set once it starts up into the sky, by death or by LiftAway.
        private bool leaving;

        public EnemyHealth Health => health;

        /// <summary>True from <see cref="FireLaser"/> or <see cref="StartBeam"/> until the laser fizzles out.</summary>
        public bool IsLasering => laserPhase != LaserPhase.None;

        /// <summary>
        /// While set, the eye looks along this direction, at once, instead of
        /// at the player. For something steering it, such as an <see cref="EyeRing"/>.
        /// </summary>
        public Vector3? GazeOverride { get; set; }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();

            // Destroyed by its own ascent instead.
            health.DestroyOnDeath = false;

            // It hangs where it is put. A falling body would drop through
            // the floor, which non-convex mesh colliders cannot stand on.
            Rigidbody body = GetComponent<Rigidbody>();

            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            if (eye == null)
            {
                eye = transform;
            }
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            EndLaser();
        }

        /// <summary>
        /// From now on, health this eye loses comes off <paramref name="bossHealth"/>
        /// too, and its destruction costs it <paramref name="bonus"/> more.
        /// </summary>
        public void Link(EnemyHealth bossHealth, float bonus)
        {
            boss = bossHealth;
            killBonus = bonus;
            lastHealth = health.CurrentHealth;
        }

        /// <summary>
        /// Charges up and fires a laser as <paramref name="settings"/>
        /// describes. <paramref name="owner"/> is the boss, whose death
        /// snuffs out the charge. False if the eye is dead or already firing.
        /// </summary>
        public bool FireLaser(EyeballLaserPattern.Settings settings, Enemy owner)
        {
            if (health.IsDead || IsLasering)
            {
                return false;
            }

            laser = settings;
            laserClock = 0f;
            laserPhase = LaserPhase.Tracking;
            muzzleDistance = laser.muzzleDistance;

            MakeMuzzle();
            charge = SceptorCharge.Create(muzzle, owner, laser.chargeTime, laser.charge);
            return true;
        }

        /// <summary>
        /// Charges for <paramref name="chargeTime"/> seconds, then fires a
        /// steady beam straight along the eye's gaze, following it as it
        /// turns, until <see cref="StopBeam"/>. The gaze is left to whoever
        /// steers the eye, through <see cref="GazeOverride"/>. False if the
        /// eye is dead or already firing.
        /// </summary>
        public bool StartBeam(SweepBeam.Settings beamSettings, SceptorCharge.Settings chargeSettings, float chargeTime,
                              float muzzleOffset, Enemy owner)
        {
            if (health.IsDead || IsLasering)
            {
                return false;
            }

            steadyBeam = beamSettings;
            steadyChargeTime = chargeTime;
            muzzleDistance = muzzleOffset;
            laserClock = 0f;
            laserPhase = LaserPhase.BeamCharging;

            MakeMuzzle();
            charge = SceptorCharge.Create(muzzle, owner, chargeTime, chargeSettings);
            return true;
        }

        /// <summary>Snuffs out a charge or beam from <see cref="StartBeam"/>.</summary>
        public void StopBeam()
        {
            if (laserPhase == LaserPhase.BeamCharging || laserPhase == LaserPhase.Beaming)
            {
                EndLaser();
            }
        }

        private void MakeMuzzle()
        {
            if (muzzle == null)
            {
                muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(eye, false);
            }

            PlaceMuzzle();
        }

        private void OnDamaged(EnemyHealth damaged, float amount)
        {
            // The blow's own amount can overshoot what was left; only what
            // the eye really lost is passed on.
            float lost = lastHealth - health.CurrentHealth;
            lastHealth = health.CurrentHealth;

            if (boss != null && lost > 0f)
            {
                boss.TakeLinkedDamage(lost);
            }
        }

        /// <summary>
        /// Hauls the eye up into the sky and away, as when destroyed, but
        /// unhurt and costing the boss nothing. For a phase change.
        /// </summary>
        public void LiftAway()
        {
            if (leaving || health.IsDead)
            {
                return;
            }

            leaving = true;
            boss = null;
            health.Invulnerable = true;
            EndLaser();
            StartCoroutine(Ascend());
        }

        private void OnDied(EnemyHealth died)
        {
            if (leaving)
            {
                return;
            }

            leaving = true;

            if (boss != null && killBonus > 0f)
            {
                boss.TakeLinkedDamage(killBonus);
            }

            EndLaser();
            StartCoroutine(Ascend());
        }

        // Late, so the stare is laid over whatever moved the eye this frame.
        private void LateUpdate()
        {
            if (health.IsDead)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (GazeOverride.HasValue && GazeOverride.Value.sqrMagnitude > 0.0001f)
            {
                eye.rotation = Quaternion.LookRotation(GazeOverride.Value, Vector3.up);
            }
            else if (laserPhase == LaserPhase.None || laserPhase == LaserPhase.Tracking)
            {
                Stare(deltaTime);
            }

            if (laserPhase == LaserPhase.BeamCharging || laserPhase == LaserPhase.Beaming)
            {
                TickBeam(deltaTime);
            }
            else if (laserPhase != LaserPhase.None)
            {
                TickLaser(deltaTime);
            }
        }

        private void Stare(float deltaTime)
        {
            if (!ResolvePlayer())
            {
                return;
            }

            Vector3 direction = PlayerCentre() - eye.position;

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
            eye.rotation = turnSpeed > 0f
                ? Quaternion.RotateTowards(eye.rotation, look, turnSpeed * deltaTime)
                : look;
        }

        private Vector3 PlayerCentre()
        {
            return playerCollider != null ? playerCollider.bounds.center : player.position;
        }

        // ---- laser ----------------------------------------------------------

        private void TickLaser(float deltaTime)
        {
            laserClock += deltaTime;

            float lowerAt = Mathf.Min(laser.trackTime, laser.chargeTime);
            float fireAt = Mathf.Max(laser.trackTime, laser.chargeTime);

            if (laserPhase == LaserPhase.Tracking && laserClock >= lowerAt)
            {
                BeginLowering();
            }

            if (laserPhase == LaserPhase.Lowering)
            {
                float span = fireAt - lowerAt;
                float lowered = span > 0f ? Mathf.Clamp01((laserClock - lowerAt) / span) : 1f;
                eye.rotation = Quaternion.Slerp(lowerFrom, Gaze(laser.startAngle), Mathf.SmoothStep(0f, 1f, lowered));

                if (laserClock >= fireAt)
                {
                    BeginFiring();
                }
            }

            PlaceMuzzle();

            if (laserPhase != LaserPhase.Firing)
            {
                return;
            }

            float swept = Mathf.Clamp01((laserClock - fireAt) / laser.sweepTime);
            eye.rotation = Gaze(Mathf.Lerp(laser.startAngle, 90f, swept));
            PlaceMuzzle();

            beam.Aim(muzzle.position, eye.forward);
            MarkGround();

            if (swept >= 1f)
            {
                EndLaser();
            }
        }

        private void TickBeam(float deltaTime)
        {
            laserClock += deltaTime;
            PlaceMuzzle();

            if (laserPhase == LaserPhase.BeamCharging)
            {
                if (laserClock < steadyChargeTime)
                {
                    return;
                }

                if (charge != null)
                {
                    Destroy(charge.gameObject);
                    charge = null;
                }

                beam = SweepBeam.Fire(muzzle.position, eye.forward, transform, steadyBeam);
                laserPhase = LaserPhase.Beaming;
                return;
            }

            beam.Aim(muzzle.position, eye.forward);
        }

        /// <summary>Stops following the player and settles which way the laser will run.</summary>
        private void BeginLowering()
        {
            lowerFrom = eye.rotation;

            Vector3 toward = player != null ? PlayerCentre() - eye.position : eye.forward;
            toward.y = 0f;

            if (toward.sqrMagnitude < 0.0001f)
            {
                toward = eye.forward;
                toward.y = 0f;
            }

            heading = toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.forward;
            laserPhase = LaserPhase.Lowering;
        }

        private void BeginFiring()
        {
            if (charge != null)
            {
                Destroy(charge.gameObject);
                charge = null;
            }

            eye.rotation = Gaze(laser.startAngle);
            PlaceMuzzle();

            beam = SweepBeam.Fire(muzzle.position, eye.forward, transform, laser.beam);
            scorch = LaserScorch.Create(laser.marks);
            laserPhase = LaserPhase.Firing;
        }

        /// <summary>
        /// Looking <paramref name="angle"/> degrees off straight down, toward
        /// <see cref="heading"/>: 0 at its feet, 90 dead level.
        /// </summary>
        private Quaternion Gaze(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);

            // The top of the eye kept toward the sky, square to the gaze.
            Vector3 forward = Vector3.down * cos + heading * sin;
            Vector3 up = Vector3.up * sin + heading * cos;
            return Quaternion.LookRotation(forward, up);
        }

        private void PlaceMuzzle()
        {
            if (muzzle != null)
            {
                muzzle.position = eye.position + eye.forward * muzzleDistance;
            }
        }

        /// <summary>Burns a mark wherever the laser is touching something.</summary>
        private void MarkGround()
        {
            // Every hit, not just the first, as the beam does: the first can be the eye itself.
            int count = Physics.RaycastNonAlloc(muzzle.position, eye.forward, groundHits, laser.beam.length,
                                                laser.beam.wallMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            RaycastHit nearest = default;

            for (int i = 0; i < count; i++)
            {
                if (groundHits[i].collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!found || groundHits[i].distance < nearest.distance)
                {
                    nearest = groundHits[i];
                    found = true;
                }
            }

            if (found)
            {
                scorch.Mark(nearest.point, nearest.normal);
            }
            else
            {
                scorch.Break();
            }
        }

        /// <summary>Snuffs out the charge, fizzles the laser and lets the marks fade. Safe to call when not firing.</summary>
        private void EndLaser()
        {
            if (charge != null)
            {
                Destroy(charge.gameObject);
                charge = null;
            }

            if (beam != null)
            {
                beam.Stop();
                beam = null;
            }

            if (scorch != null)
            {
                scorch.Stop();
                scorch = null;
            }

            laserPhase = LaserPhase.None;
        }

        // ---- death ----------------------------------------------------------

        /// <summary>Hauled up into the sky, faster and faster, and removed once out of view.</summary>
        private IEnumerator Ascend()
        {
            // A frame first: the blow that killed it still reads its body
            // for kill effects.
            yield return null;

            health.Intangible = true;

            foreach (Collider part in GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            Rigidbody body = GetComponent<Rigidbody>();

            if (body != null)
            {
                body.isKinematic = true;
            }

            Renderer[] parts = GetComponentsInChildren<Renderer>();
            float speed = 0f;
            float clock = 0f;

            while (clock < ascendTimeout)
            {
                yield return null;

                float deltaTime = Time.deltaTime;
                clock += deltaTime;
                speed = Mathf.Min(ascendMaxSpeed, speed + ascendAcceleration * deltaTime);
                transform.position += Vector3.up * (speed * deltaTime);

                if (!InView(parts))
                {
                    break;
                }
            }

            Destroy(gameObject);
        }

        /// <summary>Whether any of <paramref name="parts"/> can still be seen. True without a camera, leaving it to the timeout.</summary>
        private static bool InView(Renderer[] parts)
        {
            Camera view = Camera.main;

            if (view == null)
            {
                return true;
            }

            GeometryUtility.CalculateFrustumPlanes(view, frustum);

            foreach (Renderer part in parts)
            {
                if (part != null && part.enabled && GeometryUtility.TestPlanesAABB(frustum, part.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ResolvePlayer()
        {
            if (player != null)
            {
                return true;
            }

            PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (motor == null)
            {
                return false;
            }

            player = motor.transform;
            playerCollider = motor.GetComponent<Collider>();
            return true;
        }
    }
}
