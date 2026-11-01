using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss's katana as a guard: while on, nothing the player throws gets
    /// near the boss and nothing they swing lands. The katana darts out of
    /// the boss's orbit to each one and cuts it aside in a burst of sparks,
    /// leaving a streak of light behind it.
    /// </summary>
    /// <remarks>
    /// Goes on the boss's root, beside <see cref="BossBlades"/>, whose blade
    /// spawned from <see cref="bladePrefab"/> is the katana. Off until
    /// <see cref="Guarding"/> is set; <see cref="SpearHallIntro"/> sets it
    /// for the hall run.
    ///
    /// While guarding:
    ///   - Any player projectile (<see cref="PlayerProjectiles"/>) that comes
    ///     within <see cref="radius"/> of the boss, measured flat, is held
    ///     where it is, harmless, and the katana strikes it there; it breaks
    ///     up as the katana arrives.
    ///   - Every other blow -- a swing, a sweep, a burst -- is offered to this
    ///     as the boss's <see cref="EnemyHealth.Guard"/> and turned aside: it
    ///     does no damage and none of its hit effects, and the katana strikes
    ///     where it landed.
    ///   - While something else has the katana out of the orbit, such as a
    ///     <see cref="KatanaColumnThrowPattern"/>, the katana is left alone:
    ///     projectiles are not held, and every blow on the boss simply
    ///     misses, doing nothing.
    ///
    /// Each strike takes <see cref="strikeTime"/>: the katana flies a curve
    /// to the spot, bowing out to alternate sides and slowing into the
    /// catch, and arrives with its blade pointing out at the player through
    /// the blow, handle behind it, tip raised by <see cref="tipLift"/>. The
    /// clash comes as it arrives; the cut carries on, swinging round the
    /// handle by <see cref="followThrough"/> degrees, edge first. It chains
    /// straight from one parry into the next. After the last it hangs there
    /// for <see cref="holdTime"/>, then goes back into the orbit.
    ///
    /// The handle never points toward the player: the blade is aimed at
    /// them afresh every frame, the cut never swings past side-on, and the
    /// katana only ever turns the short way between poses.
    ///
    /// A ribbon of light trails the outer part of the blade, from
    /// <see cref="trailFrom"/> along it to the tip, while it strikes and
    /// for <see cref="trailAfterReturn"/> as it goes back; each stretch
    /// narrows toward the tip and is gone after <see cref="trailTime"/>.
    ///
    /// Everything is placed relative to the boss, parries and trail alike,
    /// so it all keeps up with the boss when it moves.
    ///
    /// The katana is posed the way <see cref="BossBlades"/> poses it: built
    /// flat side facing local +Z, tip down local -Y, edge +X -- as
    /// KatanaBlade.obj is. <see cref="baseOffset"/> says where along it the
    /// blade begins, for a prefab whose pivot is not at the base of the
    /// blade.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class KatanaGuard : MonoBehaviour, IBlowGuard
    {
        // The cut never swings further than this, so the handle never comes
        // round to face the player.
        private const float MaxFollowThrough = 80f;

        [SerializeField]
        [Tooltip("The katana's prefab, as listed in this boss's Boss Blades.")]
        private GameObject bladePrefab;

        [SerializeField]
        [Tooltip("Player projectiles that come this close to the boss, measured flat, are held for the katana to cut.")]
        [Min(0f)]
        private float radius = 4f;

        [SerializeField]
        [Tooltip("Guard from the start, rather than only once something turns it on.")]
        private bool guardOnStart;

        [Header("Katana")]
        [SerializeField]
        [Tooltip("Length of the katana's blade, base to tip, at its spawned scale. The middle of the blade meets each parry.")]
        [Min(0.01f)]
        private float bladeLength = 2.2f;

        [SerializeField]
        [Tooltip("Distance from the katana's pivot to the base of its blade, toward the tip, at its spawned scale. Negative when the pivot sits partway down the blade.")]
        private float baseOffset;

        [SerializeField]
        [Tooltip("Degrees the tip is raised from level as the katana points out through a blow.")]
        [Range(-60f, 60f)]
        private float tipLift = 15f;

        [SerializeField]
        [Tooltip("Parries waiting at most. Past this the oldest are skipped, so the katana never falls far behind.")]
        [Min(1)]
        private int maxWaiting = 3;

        [Header("Strike")]
        [SerializeField]
        [Tooltip("Seconds the katana takes to fly to each parry.")]
        [Min(0.01f)]
        private float strikeTime = 0.12f;

        [SerializeField]
        [Tooltip("How far the flight bows out to the side, as a fraction of its length. Alternates sides each strike.")]
        [Min(0f)]
        private float strikeCurve = 0.35f;

        [SerializeField]
        [Tooltip("How far the flight rises above a straight line at its middle.")]
        [Min(0f)]
        private float strikeArc = 0.5f;

        [SerializeField]
        [Tooltip("Degrees the cut carries on round, swinging about the handle, after the clash. Never more than 80, so the handle never faces the player.")]
        [Range(0f, 80f)]
        private float followThrough = 70f;

        [SerializeField]
        [Tooltip("Seconds the follow-through takes.")]
        [Min(0.01f)]
        private float followThroughTime = 0.1f;

        [SerializeField]
        [Tooltip("Seconds the katana stays at its last parry before going back into the orbit.")]
        [Min(0f)]
        private float holdTime = 0.25f;

        [Header("Trail")]
        [SerializeField]
        [Tooltip("Colour of the trail.")]
        private Color trailColour = new Color(0.2f, 0.95f, 1f);

        [SerializeField]
        [Tooltip("Colour intensity of the trail. 1 is the colour as given; above 1 blooms.")]
        [Min(0f)]
        private float trailIntensity = 3f;

        [SerializeField]
        [Tooltip("Seconds each stretch of trail lasts.")]
        [Min(0.01f)]
        private float trailTime = 0.18f;

        [SerializeField]
        [Tooltip("Where along the blade the trail starts, base 0 to tip 1. It runs from there to the tip.")]
        [Range(0f, 1f)]
        private float trailFrom = 0.3f;

        [SerializeField]
        [Tooltip("Seconds the trail keeps drawing as the katana flies back into the orbit.")]
        [Min(0f)]
        private float trailAfterReturn = 0.3f;

        [Header("Effects")]
        [SerializeField] private ParrySpark.Settings sparks = new ParrySpark.Settings();

        [SerializeField]
        [Tooltip("Material for the sparks, e.g. NN_Spark. Empty uses a plain runtime one.")]
        private Material sparkMaterial;

        [SerializeField]
        [Tooltip("How far each parry jolts the camera. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.06f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.1f;

        private enum Phase { Idle, Striking, FollowingThrough, Holding }

        /// <summary>A parry waiting its turn: a held projectile, or the spot a blow landed, from the boss.</summary>
        private sealed class Parry
        {
            public IParryableProjectile projectile;
            public Vector3 offset;
        }

        /// <summary>The blade at one moment, for the trail: its middle, from the boss, and the way its tip points.</summary>
        private struct TrailSample
        {
            public Vector3 centre;
            public Vector3 tip;
            public float time;
        }

        // Trail pieces per sample gap, at most, and degrees of turn per piece.
        private const int MaxTrailPieces = 12;
        private const float TrailDegreesPerPiece = 8f;

        private EnemyHealth health;
        private Enemy enemy;
        private BossBlades blades;
        private bool guarding;

        private readonly Queue<Parry> waiting = new Queue<Parry>();

        private Transform katana;
        private int index = -1;

        private Phase phase;
        private float clock;
        private float swingSign = 1f;

        // The strike under way: what it is after, where the middle of the
        // blade flies from and to, from the boss, through what bend; and the
        // blade's turn as it set off, without the model's Rotation Offset.
        private Parry current;
        private Vector3 fromCentre;
        private Vector3 toCentre;
        private Vector3 bend;
        private Quaternion fromTurn;

        private readonly List<TrailSample> trail = new List<TrailSample>();
        private readonly List<Vector3> trailVertices = new List<Vector3>();
        private readonly List<int> trailTriangles = new List<int>();
        private float trailUntil;
        private Mesh trailMesh;
        private MeshRenderer trailRenderer;

        /// <summary>
        /// Raised each time something is turned aside, projectile or blow, as
        /// it is caught -- before the katana gets to it. Not for blows that
        /// simply miss while the katana is busy elsewhere.
        /// </summary>
        public event System.Action Parried;

        /// <summary>Whether the katana is guarding the boss right now.</summary>
        public bool Guarding
        {
            get => guarding;
            set
            {
                guarding = value;

                if (!value)
                {
                    // Nothing left hanging in the air.
                    DropWaiting();
                }

                if (health != null)
                {
                    if (value)
                    {
                        health.Guard = this;
                    }
                    else if (ReferenceEquals(health.Guard, this))
                    {
                        health.Guard = null;
                    }
                }
            }
        }

        // From the pivot to the middle of the blade, toward the tip.
        private float HalfBlade => baseOffset + bladeLength * 0.5f;

        /// <summary>True while something else has the katana out of the orbit.</summary>
        private bool KatanaBusy => index >= 0 && blades.IsBorrowed(index) && !ReferenceEquals(blades.BorrowerOf(index), this);

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            enemy = GetComponent<Enemy>();
            blades = GetComponent<BossBlades>();
        }

        private void Start()
        {
            if (guardOnStart)
            {
                Guarding = true;
            }
        }

        private void OnDisable()
        {
            Guarding = false;
            ReturnKatana();
            trail.Clear();

            if (trailRenderer != null)
            {
                trailRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (trailRenderer != null)
            {
                Destroy(trailRenderer.gameObject);
            }

            if (trailMesh != null)
            {
                Destroy(trailMesh);
            }
        }

        public bool TryParry(Vector3? at, Blade source)
        {
            if (!guarding || !FindKatana())
            {
                return false;
            }

            // The katana is busy elsewhere: the blow simply misses.
            if (!KatanaBusy)
            {
                Queue(new Parry { offset = (at ?? DefaultParryPoint()) - transform.position });
            }

            return true;
        }

        private void Update()
        {
            if (!guarding || health == null || health.IsDead || !FindKatana() || KatanaBusy)
            {
                return;
            }

            float reach = radius * radius;

            foreach (IParryableProjectile projectile in PlayerProjectiles.Active)
            {
                Vector3 offset = projectile.Position - transform.position;
                offset.y = 0f;

                if (offset.sqrMagnitude <= reach && !Claimed(projectile))
                {
                    projectile.Hold();
                    Queue(new Parry { projectile = projectile });
                }
            }
        }

        private void LateUpdate()
        {
            if (katana == null)
            {
                return;
            }

            if (KatanaBusy)
            {
                // Taken mid-strike: let it go, and let the trail die away.
                if (phase != Phase.Idle)
                {
                    phase = Phase.Idle;
                    trailUntil = 0f;
                }

                Break(current);
                current = null;
                DropWaiting();
                DrawTrail();
                return;
            }

            Move(Time.deltaTime);

            if (phase != Phase.Idle || Time.time < trailUntil)
            {
                SampleTrail();
            }

            DrawTrail();
        }

        private void Move(float deltaTime)
        {
            bool alive = health == null || !health.IsDead;

            // Chains straight into the next parry from wherever it is.
            if (alive && waiting.Count > 0 && phase != Phase.Striking)
            {
                Begin(waiting.Dequeue());
            }

            switch (phase)
            {
                case Phase.Striking:
                {
                    clock = Mathf.Min(1f, clock + deltaTime / strikeTime);
                    float eased = 1f - (1f - clock) * (1f - clock);

                    Vector3 centre = Bezier(fromCentre, bend, toCentre, eased);
                    Quaternion turn = Quaternion.Slerp(fromTurn, ParryTurn(toCentre), eased);

                    Place(centre, turn);

                    if (clock >= 1f)
                    {
                        Clash();
                        phase = Phase.FollowingThrough;
                        clock = 0f;
                    }

                    break;
                }

                case Phase.FollowingThrough:
                {
                    clock = Mathf.Min(1f, clock + deltaTime / followThroughTime);
                    float eased = 1f - (1f - clock) * (1f - clock);

                    FollowedThrough(eased, out Vector3 centre, out Quaternion turn);
                    Place(centre, turn);

                    if (clock >= 1f)
                    {
                        phase = Phase.Holding;
                        clock = 0f;
                    }

                    break;
                }

                case Phase.Holding:
                {
                    clock += deltaTime;
                    FollowedThrough(1f, out Vector3 centre, out Quaternion turn);
                    Place(centre, turn);

                    if (clock >= holdTime)
                    {
                        ReturnKatana();
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// The cut carried on <paramref name="amount"/> of the way: the blade
        /// swung round about its base, edge first, never past side-on to the
        /// player.
        /// </summary>
        private void FollowedThrough(float amount, out Vector3 centre, out Quaternion turn)
        {
            Quaternion aimed = ParryTurn(toCentre);
            Vector3 half = aimed * Vector3.down * (bladeLength * 0.5f);
            float degrees = Mathf.Min(followThrough, MaxFollowThrough) * amount;
            Quaternion swing = Quaternion.AngleAxis(swingSign * degrees, Vector3.up);

            turn = swing * aimed;
            centre = toCentre - half + swing * half;
        }

        private bool Claimed(IParryableProjectile projectile)
        {
            if (current != null && current.projectile == projectile)
            {
                return true;
            }

            foreach (Parry parry in waiting)
            {
                if (parry.projectile == projectile)
                {
                    return true;
                }
            }

            return false;
        }

        private void Queue(Parry parry)
        {
            waiting.Enqueue(parry);
            Parried?.Invoke();

            // Too many waiting: the oldest break up without the katana.
            while (waiting.Count > maxWaiting)
            {
                Break(waiting.Dequeue());
            }
        }

        private void DropWaiting()
        {
            while (waiting.Count > 0)
            {
                Break(waiting.Dequeue());
            }
        }

        /// <summary>Ends a held projectile's flight, if the parry has one still showing.</summary>
        private static void Break(Parry parry)
        {
            if (parry?.projectile != null && !(parry.projectile is Object gone && gone == null))
            {
                parry.projectile.Deflect();
            }
        }

        /// <summary>Sends the katana from wherever it is to <paramref name="parry"/>.</summary>
        private void Begin(Parry parry)
        {
            if (index < 0 || blades == null)
            {
                return;
            }

            // A strike cut short by this one still breaks what it was after.
            if (phase == Phase.Striking)
            {
                Break(current);
            }

            // Borrowing again is harmless: it only marks it borrowed.
            katana = blades.Borrow(index, this);
            current = parry;

            fromTurn = katana.rotation * Quaternion.Inverse(blades.RotationOffset(index));
            fromCentre = katana.position + fromTurn * Vector3.down * HalfBlade - transform.position;

            bool gone = parry.projectile is Object dead && dead == null;
            toCentre = parry.projectile != null && !gone ? parry.projectile.Position - transform.position : parry.offset;

            // Bows out and cuts to alternate sides, so a run of parries weaves.
            swingSign = -swingSign;
            Vector3 flight = toCentre - fromCentre;
            Vector3 side = Vector3.Cross(Vector3.up, flight);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.zero;
            bend = (fromCentre + toCentre) * 0.5f + side * (swingSign * strikeCurve * flight.magnitude) + Vector3.up * strikeArc;

            phase = Phase.Striking;
            clock = 0f;
        }

        private void Clash()
        {
            Break(current);
            current = null;

            Vector3 point = transform.position + toCentre;

            ParrySpark.Spawn(point, TowardPlayer(toCentre), sparks, sparkMaterial != null ? sparkMaterial : FlatMaterial.DoubleSided);
            TopDownCamera.Shake(shakeStrength, shakeDuration);
        }

        private void ReturnKatana()
        {
            if (phase != Phase.Idle)
            {
                trailUntil = Time.time + trailAfterReturn;
            }

            Break(current);
            current = null;
            phase = Phase.Idle;

            if (blades != null && index >= 0 && ReferenceEquals(blades.BorrowerOf(index), this))
            {
                blades.Return(index);
            }
        }

        /// <summary>
        /// Pointing out through a blow at <paramref name="offset"/> from the
        /// boss, at the player: handle behind, away from them, tip toward
        /// them and raised by <see cref="tipLift"/>, edge facing the way the
        /// cut will swing. Without the model's Rotation Offset. Worked out
        /// afresh each frame, so it keeps facing a player on the move.
        /// </summary>
        private Quaternion ParryTurn(Vector3 offset)
        {
            Vector3 toward = TowardPlayer(offset);
            float lift = tipLift * Mathf.Deg2Rad;
            Vector3 tip = toward * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift);

            // A positive turn about up carries the tip toward up x tip.
            Vector3 edge = Vector3.Cross(Vector3.up, toward) * swingSign;

            // As built the tip is down -Y and the edge +X.
            return Quaternion.LookRotation(Vector3.Cross(edge, -tip), -tip);
        }

        /// <summary>Places the katana with the middle of its blade at <paramref name="centre"/> from the boss, turned <paramref name="turn"/>.</summary>
        private void Place(Vector3 centre, Quaternion turn)
        {
            Vector3 tip = turn * Vector3.down;
            katana.SetPositionAndRotation(transform.position + centre - tip * HalfBlade,
                                          turn * blades.RotationOffset(index));
        }

        /// <summary>
        /// Flat, from the spot at <paramref name="offset"/> from the boss
        /// toward the player; out from the boss when the player is on the
        /// spot or there is none.
        /// </summary>
        private Vector3 TowardPlayer(Vector3 offset)
        {
            if (enemy != null && enemy.HasTarget)
            {
                Vector3 toPlayer = enemy.TargetPoint - (transform.position + offset);
                toPlayer.y = 0f;

                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    return toPlayer.normalized;
                }
            }

            offset.y = 0f;
            return offset.sqrMagnitude > 0.0001f ? offset.normalized : transform.forward;
        }

        /// <summary>For a blow that does not say where it landed: the edge of the boss's body facing the player.</summary>
        private Vector3 DefaultParryPoint()
        {
            Bounds body = health != null ? health.BodyBounds() : new Bounds(transform.position, Vector3.zero);
            Vector3 toward = TowardPlayer(Vector3.zero);
            return body.center + toward * Mathf.Max(body.extents.x, body.extents.z);
        }

        /// <summary>Finds the katana among the boss's blades; false if it has none.</summary>
        private bool FindKatana()
        {
            if (katana != null)
            {
                return true;
            }

            if (blades == null)
            {
                return false;
            }

            index = blades.IndexOf(bladePrefab);

            if (index < 0)
            {
                return false;
            }

            // Only looked up here; borrowed when it first strikes.
            katana = blades.BladeAt(index);
            return katana != null;
        }

        // ---- trail --------------------------------------------------------

        private void SampleTrail()
        {
            Quaternion turn = katana.rotation * Quaternion.Inverse(blades.RotationOffset(index));
            Vector3 tip = turn * Vector3.down;

            trail.Add(new TrailSample
            {
                centre = katana.position + tip * HalfBlade - transform.position,
                tip = tip,
                time = Time.time,
            });
        }

        /// <summary>
        /// Rebuilds the ribbon from the samples still young enough, each gap
        /// filled in along the turn between them so a fast swing draws a
        /// round sweep rather than a fan of straight cuts.
        /// </summary>
        private void DrawTrail()
        {
            float oldest = Time.time - trailTime;

            while (trail.Count > 0 && trail[0].time <= oldest)
            {
                trail.RemoveAt(0);
            }

            if (trail.Count < 2)
            {
                if (trailRenderer != null)
                {
                    trailRenderer.enabled = false;
                }

                return;
            }

            BuildTrailObject();

            trailVertices.Clear();
            trailTriangles.Clear();

            Vector3 boss = transform.position;
            AddTrailEdge(trail[0], boss);

            for (int i = 1; i < trail.Count; i++)
            {
                TrailSample from = trail[i - 1];
                TrailSample to = trail[i];
                float turned = Vector3.Angle(from.tip, to.tip);
                int pieces = Mathf.Clamp(Mathf.CeilToInt(turned / TrailDegreesPerPiece), 1, MaxTrailPieces);

                for (int k = 1; k <= pieces; k++)
                {
                    float t = k / (float)pieces;

                    AddTrailEdge(new TrailSample
                    {
                        centre = Vector3.Lerp(from.centre, to.centre, t),
                        tip = Vector3.Slerp(from.tip, to.tip, t),
                        time = Mathf.Lerp(from.time, to.time, t),
                    }, boss);

                    int at = trailVertices.Count - 4;
                    trailTriangles.Add(at);
                    trailTriangles.Add(at + 2);
                    trailTriangles.Add(at + 1);
                    trailTriangles.Add(at + 1);
                    trailTriangles.Add(at + 2);
                    trailTriangles.Add(at + 3);
                }
            }

            trailMesh.Clear();
            trailMesh.SetVertices(trailVertices);
            trailMesh.SetTriangles(trailTriangles, 0);
            trailMesh.RecalculateBounds();
            trailRenderer.enabled = true;
        }

        /// <summary>One edge across the ribbon: from the trail's start along the blade to the tip, narrowing toward the tip with age.</summary>
        private void AddTrailEdge(TrailSample sample, Vector3 boss)
        {
            float age = Mathf.Clamp01((Time.time - sample.time) / trailTime);
            float from = Mathf.Lerp(trailFrom, 1f, age);

            Vector3 middle = boss + sample.centre;
            trailVertices.Add(middle + sample.tip * (bladeLength * (from - 0.5f)));
            trailVertices.Add(middle + sample.tip * (bladeLength * 0.5f));
        }

        private void BuildTrailObject()
        {
            if (trailRenderer != null)
            {
                return;
            }

            // Its own object, in world space, so the boss's turning and
            // scale never touch it.
            GameObject host = new GameObject($"{name} Katana Trail");

            trailMesh = new Mesh { name = "Katana trail" };
            trailMesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = trailMesh;

            trailRenderer = host.AddComponent<MeshRenderer>();
            trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trailRenderer.receiveShadows = false;
            trailRenderer.sharedMaterial = FlatMaterial.DoubleSided;

            Color colour = trailColour * trailIntensity;
            colour.a = trailColour.a;
            FlatMaterial.Tint(trailRenderer, colour, new MaterialPropertyBlock());
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return a * (u * u) + b * (2f * u * t) + c * (t * t);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
