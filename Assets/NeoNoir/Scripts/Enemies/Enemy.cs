using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// An enemy's brain: which phase it is in, and which attack it performs
    /// next. Health lives in <see cref="EnemyHealth"/>; what each attack does
    /// lives in its <see cref="AttackPattern"/> asset.
    /// </summary>
    /// <remarks>
    /// Phases are picked by remaining health. Each phase starts at a health
    /// percentage, and the enemy is in the phase with the lowest start
    /// percentage it has dropped to. So a boss with phases at 100, 60 and 25
    /// is in the first until it falls to 60%, the second until 25%, then the
    /// third. The order of the list does not matter. Phases only ever move
    /// on: healing back above a threshold, as a <see cref="WitchHealPattern"/>
    /// does, never returns the enemy to an earlier phase.
    ///
    /// With more than one phase, none can be skipped: until a phase has
    /// started at least one attack, no blow can take the health past the
    /// start of the phase after the next, nor kill the enemy before its last
    /// phase has attacked. Each blow stops at that floor. A phase with no
    /// attacks never holds things up.
    ///
    /// Within a phase, attacks run one at a time in list order and loop. Each
    /// attack plays out fully, then the enemy waits that attack's
    /// <see cref="AttackPattern.DelayAfter"/>. The phase is only re-checked
    /// between attacks, so crossing a threshold never interrupts an attack
    /// halfway through; the next attack comes from the new phase, starting at
    /// its first pattern.
    ///
    /// A phase's cut-ins break into that loop: after each attack, every
    /// cut-in other than the attack just performed rolls its
    /// <see cref="CutIn.chance"/>, in list order, and the first to succeed
    /// is performed next. The loop then carries on where it left off. A
    /// change of phase drops a cut-in that was waiting.
    ///
    /// An enemy starts dormant: standing still in its idle animation, neither
    /// turning nor attacking. It wakes for good the moment the player comes
    /// within <see cref="detectionRadius"/>, or the moment it takes damage
    /// from anything, wherever the player is. <see cref="startDelay"/> counts
    /// from waking. While <see cref="StayDormant"/> is set it cannot wake at
    /// all, so something like <see cref="BossIntro"/> can choose the moment.
    ///
    /// The model is separate from the enemy itself: the enemy turns the
    /// model, never the root, so colliders and anything else on the root stay
    /// put. It turns the model toward the player all the time, never faster
    /// than <see cref="turnSpeed"/>, whichever way it happens to be walking.
    /// An attack can point it elsewhere with <see cref="FacingOverride"/>,
    /// drive it directly with <see cref="VelocityOverride"/>, as a charge
    /// does, or set it spinning with <see cref="SpinOverride"/>, as a whirl
    /// does. All three are cleared whenever an attack starts.
    ///
    /// Each attack plays its animation while it runs, and the idle animation
    /// plays otherwise. Each attack's movement is followed from its start
    /// until the next attack's, so it covers the wait after it too. Before
    /// the first attack the enemy stands still. With a non-kinematic
    /// Rigidbody on the root, movement sets its velocity, so walls stop it;
    /// without one, the root is moved directly and passes through anything.
    /// A body left on no interpolation is switched to Interpolate, or it would
    /// only move on physics steps and stutter on screen, worst at speed.
    ///
    /// With a <see cref="lastStand"/> set, the enemy cannot die. The blow
    /// that would kill it leaves it on 1 health and invulnerable; the attack
    /// under way plays out, the wait after it is cut short, and the last
    /// stand is performed as its final attack. The enemy attacks no more
    /// after it: whatever ends the fight is up to the attack.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class Enemy : MonoBehaviour
    {
        /// <summary>Close enough to an idle wander point to pick the next.</summary>
        private const float WanderArriveDistance = 0.2f;

        [Serializable]
        public sealed class Phase
        {
            [Tooltip("Just a label for the inspector and the PhaseChanged event.")]
            public string name = "Phase";

            [Tooltip("The enemy enters this phase once its health drops to this percentage. Give the first phase 100.")]
            [Range(0f, 100f)] public float startsAtHealthPercent = 100f;

            [Tooltip("Attacks performed in this phase, in order, looping.")]
            public List<AttackPattern> patterns = new List<AttackPattern>();

            [Tooltip("Attacks that may cut in after any other attack, each at its own odds, before the loop carries on where it was.")]
            public List<CutIn> cutIns = new List<CutIn>();
        }

        [Serializable]
        public sealed class CutIn
        {
            public AttackPattern pattern;

            [Tooltip("Odds, 0 to 1, that this comes next after any attack other than itself. 0.2 is one in five.")]
            [Range(0f, 1f)] public float chance = 0.2f;
        }

        [Header("Parts")]
        [SerializeField]
        [Tooltip("The visual model that turns to aim. Empty uses this object.")]
        private Transform model;

        [SerializeField]
        [Tooltip("Where projectiles are fired from. Should be a child of the model so it turns with it. Empty uses the model.")]
        private Transform muzzle;

        [SerializeField]
        [Tooltip("Who to attack. Empty finds the player automatically.")]
        private Transform target;

        [SerializeField]
        [Tooltip("Plays the idle and attack animations. Empty finds one under the enemy.")]
        private Animator animator;

        [Header("Behaviour")]
        [SerializeField]
        [Tooltip("The enemy stays dormant until the player comes this close, or until it is hurt.")]
        [Min(0f)]
        private float detectionRadius = 25f;

        [SerializeField]
        [Tooltip("Seconds after waking before the first attack.")]
        [Min(0f)]
        private float startDelay = 1f;

        [SerializeField]
        [Tooltip("Degrees per second the model turns toward the player. It never turns faster, attacking or not.")]
        [Min(0f)]
        private float turnSpeed = 360f;

        [SerializeField]
        private List<Phase> phases = new List<Phase> { new Phase { name = "Phase 1" } };

        [SerializeField]
        [Tooltip("Played once, as the final attack, when a blow would kill the enemy: it is left on 1 health, invulnerable, instead. The attack decides how the fight ends. Empty lets the enemy die as usual.")]
        private AttackPattern lastStand;

        /// <summary>The idle state used when the Idle Animation field is left empty.</summary>
        public const string DefaultIdleAnimation = "GuardIdle";

        [Header("Animation")]
        [SerializeField]
        [Tooltip("Animator state played whenever the enemy is not attacking. Empty uses GuardIdle.")]
        private string idleAnimation = DefaultIdleAnimation;

        [SerializeField]
        [Tooltip("Seconds to blend between animations.")]
        [Min(0f)]
        private float animationBlend = 0.1f;

        [SerializeField]
        [Tooltip("Float parameter set to how fast the enemy is actually moving, in units per second, for blending idle into walking. Ignored if the animator has no such parameter.")]
        private string speedParameter = "Speed";

        [SerializeField]
        [Tooltip("Seconds the speed parameter takes to catch up, so the legs ease in and out instead of snapping.")]
        [Min(0f)]
        private float speedSmoothing = 0.1f;

        [Header("Ledges")]
        [SerializeField]
        [Tooltip("Stops the enemy walking or charging off the edge of the floor. Stairs and ramps still work.")]
        private bool stayOnFloor = true;

        [SerializeField]
        [Tooltip("What counts as floor. Narrow this to a Ground layer if props get mistaken for floor.")]
        private LayerMask floorMask = ~0;

        [SerializeField]
        [Tooltip("Highest step up the enemy will walk onto.")]
        [Min(0f)]
        private float maxStepUp = 1f;

        [SerializeField]
        [Tooltip("Furthest drop the enemy will walk down. Anything deeper is treated as a ledge.")]
        [Min(0f)]
        private float maxStepDown = 1f;

        private EnemyHealth health;
        private Rigidbody body;
        private Collider bodyCollider;
        private Collider targetCollider;
        private int phaseIndex = -1;
        private bool lastStandDue;

        // The phases from first to last, by start percentage, and how far
        // along them an attack has been played: none can be skipped.
        private readonly List<int> phaseOrder = new List<int>();
        private int attackedThrough = -1;

        // Movement of the current attack, from its start until the next's.
        private AttackPattern.MovementSettings movement;
        private readonly AttackPattern.MovementSettings approach = new AttackPattern.MovementSettings { type = MovementType.Chase };
        private Vector3 movementAnchor;
        private Vector3 wanderPoint;
        private float wanderTimer;

        // Measured from how the enemy really moves, not what it asked for,
        // so pressing into a wall reads as standing still.
        private int speedHash;
        private bool hasSpeedParameter;
        private Vector3 lastPosition;

        /// <summary>Raised when the enemy moves into a new phase, with its index in the list.</summary>
        public event Action<Enemy, int> PhaseChanged;

        /// <summary>Raised as each attack begins, once its movement and animation have started.</summary>
        public event Action<Enemy, AttackPattern> AttackStarted;

        /// <summary>Raised once, when the enemy wakes from dormant.</summary>
        public event Action<Enemy> Awakened;

        /// <summary>False while dormant: before the player has come within range or the enemy has been hurt.</summary>
        public bool IsAwake { get; private set; }

        public EnemyHealth Health => health;

        public Transform Model => model != null ? model : transform;

        public Transform Muzzle => muzzle != null ? muzzle : Model;

        public Transform Target => target;

        public bool HasTarget => ResolveTarget();

        /// <summary>The middle of the target's body, or its pivot if it has no collider.</summary>
        public Vector3 TargetPoint => targetCollider != null ? targetCollider.bounds.center : target.position;

        /// <summary>Index of the current phase in the list, or -1 before the first attack.</summary>
        public int PhaseIndex => phaseIndex;

        /// <summary>The phases, in order, with their attacks.</summary>
        public IReadOnlyList<Phase> Phases => phases;

        /// <summary>How the enemy is really moving across the floor this frame, in units per second. Flat.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// Scales the current attack's walking speed, so an attack can slow
        /// the enemy partway through. Back to 1 whenever an attack starts.
        /// </summary>
        public float MovementSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Middle of the area the enemy fights in, for attacks that pick spots
        /// around it. Null until something sets one, such as
        /// <see cref="BossIntro"/>; attacks then use wherever the enemy stands.
        /// </summary>
        public Vector3? ArenaCentre { get; set; }

        /// <summary>
        /// The arena the enemy fights in, for attacks that bring things into
        /// it, such as <see cref="SummonPuppetsPattern"/>. Null until something
        /// sets one, such as <see cref="PuppeteerIntro"/>.
        /// </summary>
        public ArenaBounds Arena { get; set; }

        /// <summary>
        /// Whoever called the enemy into the fight, for attacks laid out
        /// relative to them, such as <see cref="LavenderVolleyPattern"/>.
        /// Null unless something sets one, such as <see cref="LunaCaptives"/>.
        /// </summary>
        public Transform Summoner { get; set; }

        /// <summary>While true the enemy cannot wake, neither by the player coming close nor by taking damage.</summary>
        public bool StayDormant { get; set; }

        /// <summary>
        /// While true the enemy leaves its body to physics: it neither moves
        /// nor turns its model, so shoves, blasts and gravity have their way.
        /// For something like a <see cref="GiftBox"/>, which never walks.
        /// </summary>
        public bool PhysicsDriven { get; set; }

        /// <summary>
        /// While true the enemy starts no new attack: it finishes the one it
        /// is on, then waits. For a cutscene, such as
        /// <see cref="PuppeteerPhaseTwo"/>, to have the stage to itself. A
        /// phase change is raised before the wait, so a listener can set this
        /// in time to hold the new phase's first attack.
        /// </summary>
        public bool HoldAttacks { get; set; }

        /// <summary>
        /// While set, the model turns toward this flat direction instead of
        /// toward the player, still no faster than the turn speed. Cleared
        /// whenever an attack starts.
        /// </summary>
        public Vector3? FacingOverride { get; set; }

        /// <summary>
        /// While set, the enemy moves at this flat velocity instead of
        /// following its attack's movement. Cleared whenever an attack starts.
        /// </summary>
        public Vector3? VelocityOverride { get; set; }

        /// <summary>
        /// While set, the model spins about the vertical axis at this many
        /// degrees per second, ignoring the player and the turn speed. Wins
        /// over <see cref="FacingOverride"/>. Cleared whenever an attack starts.
        /// </summary>
        public float? SpinOverride { get; set; }

        /// <summary>Remaining health, 0 to 100.</summary>
        public float HealthPercent => health.CurrentHealth / health.MaxHealth * 100f;

        /// <summary>
        /// Turns the model about the vertical axis toward the target by at
        /// most <paramref name="maxDegrees"/>. Returns how many degrees off it
        /// still is afterwards.
        /// </summary>
        public float TurnModelTowardTarget(float maxDegrees)
        {
            if (!ResolveTarget())
            {
                return 180f;
            }

            return TurnModelToward(target.position - Model.position, maxDegrees);
        }

        /// <summary>
        /// Turns the model about the vertical axis toward <paramref name="direction"/>
        /// by at most <paramref name="maxDegrees"/>. Returns how many degrees
        /// off it still is afterwards.
        /// </summary>
        public float TurnModelToward(Vector3 direction, float maxDegrees)
        {
            Transform turning = Model;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
            turning.rotation = Quaternion.RotateTowards(turning.rotation, facing, maxDegrees);
            return Quaternion.Angle(turning.rotation, facing);
        }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            body = GetComponent<Rigidbody>();

            if (lastStand != null)
            {
                health.CannotDie = true;
            }

            phaseOrder.Clear();

            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i] != null)
                {
                    phaseOrder.Add(i);
                }
            }

            // Stable: phases starting at the same percentage keep list order.
            phaseOrder.Sort((a, b) =>
            {
                int byStart = phases[b].startsAtHealthPercent.CompareTo(phases[a].startsAtHealthPercent);
                return byStart != 0 ? byStart : a.CompareTo(b);
            });

            UpdatePhaseFloor();

            bodyCollider = GetComponent<Collider>();

            if (body != null && body.interpolation == RigidbodyInterpolation.None)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            speedHash = Animator.StringToHash(speedParameter);
            hasSpeedParameter = false;

            if (animator != null && !string.IsNullOrEmpty(speedParameter))
            {
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float)
                    {
                        hasSpeedParameter = true;
                        break;
                    }
                }
            }
        }

        /// <summary>Wakes the enemy from dormant, if it is not awake already. It stays awake from then on.</summary>
        public void Wake()
        {
            if (IsAwake || StayDormant || health.IsDead)
            {
                return;
            }

            IsAwake = true;
            Awakened?.Invoke(this);
        }

        private void OnEnable()
        {
            health.Died += OnDied;
            health.Damaged += OnDamaged;
            health.DeathPrevented += OnDeathPrevented;
            movement = null;
            ClearOverrides();
            lastPosition = transform.position;
            PlayAnimation(IdleAnimation);
            StartCoroutine(Run());
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
            health.Damaged -= OnDamaged;
            health.DeathPrevented -= OnDeathPrevented;
            StopAllCoroutines();
            movement = null;
            ClearOverrides();
        }

        private void ClearOverrides()
        {
            FacingOverride = null;
            VelocityOverride = null;
            SpinOverride = null;
        }

        private void OnDamaged(EnemyHealth damaged, float amount)
        {
            Wake();
        }

        private void OnDeathPrevented(EnemyHealth saved)
        {
            if (lastStand != null && !lastStandDue)
            {
                lastStandDue = true;
                health.Invulnerable = true;
            }
        }

        private void OnDied(EnemyHealth died)
        {
            StopAllCoroutines();
            movement = null;
            Halt();
        }

        private void Update()
        {
            if (!health.IsDead && IsAwake && !PhysicsDriven)
            {
                if (SpinOverride.HasValue)
                {
                    Model.Rotate(Vector3.up, SpinOverride.Value * Time.deltaTime, Space.World);
                }
                else if (FacingOverride.HasValue)
                {
                    TurnModelToward(FacingOverride.Value, turnSpeed * Time.deltaTime);
                }
                else
                {
                    TurnModelTowardTarget(turnSpeed * Time.deltaTime);
                }
            }

            UpdateSpeedParameter(Time.deltaTime);
        }

        private void UpdateSpeedParameter(float deltaTime)
        {
            Vector3 position = transform.position;

            // A body only moves on physics steps, so frame to frame it reads
            // as stop-start; its own velocity is steady. That is still what
            // it really moved at, after walls have had their say.
            if (body != null && !body.isKinematic)
            {
                Velocity = Flat(body.linearVelocity);
            }
            else
            {
                Velocity = deltaTime > 0f ? Flat(position - lastPosition) / deltaTime : Vector3.zero;
            }

            lastPosition = position;

            if (hasSpeedParameter && !health.IsDead)
            {
                animator.SetFloat(speedHash, Velocity.magnitude, speedSmoothing, deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (health.IsDead || PhysicsDriven)
            {
                return;
            }

            Vector3 velocity = VelocityOverride.HasValue
                ? Flat(VelocityOverride.Value)
                : DesiredVelocity(Time.fixedDeltaTime);

            if (stayOnFloor)
            {
                velocity = KeepOnFloor(velocity, Time.fixedDeltaTime);
            }

            if (body != null && !body.isKinematic)
            {
                // Planar only: gravity keeps the vertical.
                body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);
            }
            else
            {
                transform.position += velocity * Time.fixedDeltaTime;
            }
        }

        /// <summary>
        /// <paramref name="velocity"/>, bent along the edge of the floor if it
        /// would carry the enemy off. Measured from the root collider when
        /// there is one, else from the pivot as the feet.
        /// </summary>
        private Vector3 KeepOnFloor(Vector3 velocity, float deltaTime)
        {
            Vector3 feet = transform.position;
            float radius = 0.5f;

            if (bodyCollider != null && bodyCollider.enabled)
            {
                Bounds bounds = bodyCollider.bounds;
                feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            }

            return FloorProbe.KeepOnFloor(transform, feet, radius, velocity, deltaTime, maxStepUp, maxStepDown, floorMask);
        }

        private void Halt()
        {
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
            }
        }

        /// <summary>Starts following an attack's movement, from wherever the enemy stands now.</summary>
        private void BeginMovement(AttackPattern.MovementSettings settings)
        {
            movement = settings;
            MovementSpeedMultiplier = 1f;
            ClearOverrides();
            movementAnchor = transform.position;
            wanderPoint = movementAnchor;
            wanderTimer = 0f;
        }

        /// <summary>The flat velocity the current movement wants this step.</summary>
        private Vector3 DesiredVelocity(float deltaTime)
        {
            float speed = movement != null ? movement.speed * Mathf.Max(0f, MovementSpeedMultiplier) : 0f;

            if (speed <= 0f)
            {
                return Vector3.zero;
            }

            switch (movement.type)
            {
                case MovementType.Chase:
                {
                    if (!ResolveTarget())
                    {
                        return Vector3.zero;
                    }

                    Vector3 toTarget = Flat(target.position - transform.position);
                    float distance = toTarget.magnitude;

                    // Never steps past the stop distance in one go.
                    float step = Mathf.Min(speed, (distance - movement.stopDistance) / deltaTime);
                    return step > 0f ? toTarget / distance * step : Vector3.zero;
                }

                case MovementType.Idle:
                    return WanderVelocity(speed, deltaTime);

                default:
                    return Vector3.zero;
            }
        }

        /// <summary>
        /// Walks toward a random point inside the wander radius for a short
        /// while, then picks another. Every point is inside the radius, so the
        /// enemy can only leave it if something shoves it out -- and then the
        /// next point brings it back.
        /// </summary>
        private Vector3 WanderVelocity(float speed, float deltaTime)
        {
            Vector3 toPoint = Flat(wanderPoint - transform.position);
            wanderTimer -= deltaTime;

            if (wanderTimer <= 0f || toPoint.magnitude <= WanderArriveDistance)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * movement.wanderRadius;
                wanderPoint = movementAnchor + new Vector3(offset.x, 0f, offset.y);

                float shortest = Mathf.Max(0.05f, movement.wanderLegTime.x);
                wanderTimer = UnityEngine.Random.Range(shortest, Mathf.Max(shortest, movement.wanderLegTime.y));
                toPoint = Flat(wanderPoint - transform.position);
            }

            float distance = toPoint.magnitude;
            if (distance <= WanderArriveDistance)
            {
                return Vector3.zero;
            }

            return toPoint / distance * Mathf.Min(speed, distance / deltaTime);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        /// <summary>
        /// Blends to an animator state. Attacks with more than one part use
        /// this to move on from their own <see cref="AttackPattern.AttackAnimation"/>;
        /// the enemy still goes back to idle when the attack ends.
        /// </summary>
        public void PlayAnimation(string state)
        {
            PlayAnimation(state, animationBlend);
        }

        /// <summary>Blends to an animator state over <paramref name="blend"/> seconds instead of the usual blend time.</summary>
        public void PlayAnimation(string state, float blend)
        {
            if (animator == null || string.IsNullOrEmpty(state))
            {
                return;
            }

            int hash = Animator.StringToHash(state);

            if (!animator.HasState(0, hash))
            {
                Debug.LogWarning($"[Enemy] {name}'s animator has no state called \"{state}\".", this);
                return;
            }

            animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, blend), 0);
        }

        /// <summary>
        /// Plays <paramref name="state"/> from its start even if it is already
        /// playing, as a combo throwing the same strike twice running needs:
        /// a cross-fade into the state already playing does not restart it.
        /// </summary>
        public void RestartAnimation(string state)
        {
            if (animator == null || string.IsNullOrEmpty(state))
            {
                return;
            }

            int hash = Animator.StringToHash(state);

            if (!animator.HasState(0, hash))
            {
                Debug.LogWarning($"[Enemy] {name}'s animator has no state called \"{state}\".", this);
                return;
            }

            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash || animator.IsInTransition(0))
            {
                animator.Play(hash, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, animationBlend), 0);
            }
        }

        /// <summary>
        /// Waits until <paramref name="state"/> has played to its end, or the
        /// animator has moved on from it by itself. Returns at once if there
        /// is no such state.
        /// </summary>
        public IEnumerator WaitForAnimation(string state)
        {
            if (animator == null || string.IsNullOrEmpty(state))
            {
                yield break;
            }

            int hash = Animator.StringToHash(state);

            if (!animator.HasState(0, hash))
            {
                yield break;
            }

            // The crossfade into it may not have begun yet; give it a moment
            // to become the current or upcoming state before giving up.
            float waited = 0f;

            while (!InState(hash) && waited < 0.5f)
            {
                yield return null;
                waited += Time.deltaTime;
            }

            while (InState(hash))
            {
                AnimatorStateInfo info = animator.IsInTransition(0)
                    ? animator.GetNextAnimatorStateInfo(0)
                    : animator.GetCurrentAnimatorStateInfo(0);

                if (info.shortNameHash == hash && info.normalizedTime >= 1f)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private bool InState(int hash)
        {
            return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash
                || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == hash);
        }

        /// <summary>Animator state the enemy plays whenever it is not attacking.</summary>
        /// <summary>The idle state: the one set on this enemy, or <see cref="DefaultIdleAnimation"/> if none is.</summary>
        public string IdleAnimation => string.IsNullOrWhiteSpace(idleAnimation) ? DefaultIdleAnimation : idleAnimation.Trim();

        public void PlayIdleAnimation()
        {
            PlayAnimation(IdleAnimation);
        }

        private IEnumerator Run()
        {
            // Dormant: watch for the player, or for damage to wake it. Waits
            // a frame before every check, never after: this starts in
            // OnEnable, possibly before EnemyHealth has woken and set its
            // health, and an enemy that reads as dead can never be woken --
            // so checking first would spin here forever inside one frame.
            while (!IsAwake)
            {
                yield return null;

                if (health.IsDead)
                {
                    yield break;
                }

                if (PlayerInRange())
                {
                    Wake();
                }
            }

            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            int patternIndex = 0;
            AttackPattern cutIn = null;

            while (!health.IsDead)
            {
                int current = FindPhase();

                // Healed back above the current phase's start: stay in it.
                if (phaseIndex >= 0 && (current < 0 ||
                    phases[current].startsAtHealthPercent > phases[phaseIndex].startsAtHealthPercent))
                {
                    current = phaseIndex;
                }

                if (current != phaseIndex)
                {
                    phaseIndex = current;
                    patternIndex = 0;
                    cutIn = null;

                    if (current >= 0)
                    {
                        PhaseChanged?.Invoke(this, current);
                    }
                }

                while (HoldAttacks && !health.IsDead)
                {
                    yield return null;
                }

                if (health.IsDead)
                {
                    yield break;
                }

                if (lastStandDue)
                {
                    yield return LastStand();
                    yield break;
                }

                AttackPattern pattern = cutIn != null ? cutIn : NextPattern(current, ref patternIndex);
                cutIn = null;

                // Nothing to do yet -- no phase, an empty one, or no player in
                // the scene. Check again next frame.
                if (pattern == null || !ResolveTarget())
                {
                    yield return null;
                    continue;
                }

                // Skipped outright. A frame's wait, so a phase whose every
                // attack is skipped never spins inside one frame.
                if (!pattern.CanPerform(this))
                {
                    yield return null;
                    continue;
                }

                if (pattern.MaxRange > 0f && FlatDistanceToTarget() >= pattern.MaxRange)
                {
                    yield return Approach(pattern);

                    if (!ResolveTarget())
                    {
                        continue;
                    }
                }

                BeginMovement(pattern.Movement);
                PlayAnimation(pattern.AttackAnimation, BlendOrDefault(pattern.AttackAnimationBlend));
                MarkPhaseAttacked();
                AttackStarted?.Invoke(this, pattern);

                yield return StartCoroutine(pattern.Perform(this));
                cutIn = RollCutIn(current, pattern);

                if (!string.IsNullOrEmpty(pattern.AttackAnimation))
                {
                    PlayAnimation(IdleAnimation, BlendOrDefault(pattern.IdleReturnBlend));
                }

                // At least a frame, so a zero-delay loop never spins inside
                // one. A last stand falling due cuts the wait short.
                float waited = 0f;

                do
                {
                    yield return null;
                    waited += Time.deltaTime;
                }
                while (waited < pattern.DelayAfter && !lastStandDue);
            }
        }

        private IEnumerator LastStand()
        {
            BeginMovement(lastStand.Movement);
            PlayAnimation(lastStand.AttackAnimation, BlendOrDefault(lastStand.AttackAnimationBlend));
            AttackStarted?.Invoke(this, lastStand);

            yield return StartCoroutine(lastStand.Perform(this));
        }

        /// <summary>
        /// Walks toward the player until within the attack's approach
        /// distance, or until there is no player to walk toward.
        /// </summary>
        private IEnumerator Approach(AttackPattern pattern)
        {
            approach.speed = pattern.ApproachSpeed;
            approach.stopDistance = pattern.ApproachDistance;
            BeginMovement(approach);

            // A little slack, as the chase eases to a stop right at the
            // stop distance and may never quite reach it.
            while (ResolveTarget() && FlatDistanceToTarget() > approach.stopDistance + 0.1f)
            {
                yield return null;
            }

            movement = null;
        }

        private float FlatDistanceToTarget()
        {
            return Flat(target.position - transform.position).magnitude;
        }

        private float BlendOrDefault(float blend)
        {
            return blend >= 0f ? blend : animationBlend;
        }

        /// <summary>
        /// Counts an attack as played in the current phase, letting the enemy
        /// be worn down into the next one (or killed, in the last). Called as
        /// each of its own attacks starts; a script running a phase's attacks
        /// itself, as <see cref="ChromatistPhaseThree"/> does, calls it too.
        /// </summary>
        public void MarkPhaseAttacked()
        {
            int position = phaseOrder.IndexOf(phaseIndex);

            if (position > attackedThrough)
            {
                attackedThrough = position;
                UpdatePhaseFloor();
            }
        }

        /// <summary>
        /// With more than one phase, keeps the health from reaching past the
        /// phase after the last one to have played an attack: down into that
        /// next phase, but no further, and never to death until the last phase
        /// has played one. A phase with no attacks never holds things up.
        /// One phase alone sets no floor, so ordinary enemies die as usual.
        /// </summary>
        private void UpdatePhaseFloor()
        {
            if (phaseOrder.Count < 2)
            {
                health.MinimumHealth = 0f;
                return;
            }

            // Past every phase played, and any empty one after them.
            int through = attackedThrough;

            while (through + 1 < phaseOrder.Count && !HasAttacks(phaseOrder[through + 1]))
            {
                through++;
            }

            int next = through + 1;

            if (next >= phaseOrder.Count)
            {
                // The last phase has played: free to die.
                health.MinimumHealth = 0f;
                return;
            }

            if (next + 1 >= phaseOrder.Count)
            {
                // Down into the last phase, but not dead before it plays.
                health.MinimumHealth = EnemyHealth.DeathFloor;
                return;
            }

            // Down into the next phase, but just short of the one after.
            float start = phases[phaseOrder[next + 1]].startsAtHealthPercent;
            health.MinimumHealth = Mathf.Max(EnemyHealth.DeathFloor, health.MaxHealth * start / 100f + 0.01f);
        }

        private bool HasAttacks(int phase)
        {
            foreach (AttackPattern pattern in phases[phase].patterns)
            {
                if (pattern != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The phase with the lowest start the health has dropped to, or -1 if none has started.</summary>
        private int FindPhase()
        {
            float percent = HealthPercent;
            int found = -1;

            for (int i = 0; i < phases.Count; i++)
            {
                if (percent <= phases[i].startsAtHealthPercent &&
                    (found < 0 || phases[i].startsAtHealthPercent < phases[found].startsAtHealthPercent))
                {
                    found = i;
                }
            }

            return found;
        }

        /// <summary>The next non-empty pattern slot in the phase, advancing the index past it.</summary>
        /// <summary>
        /// The cut-in, if any, to perform after <paramref name="performed"/>:
        /// the first in the phase's list, other than it, whose odds come up.
        /// </summary>
        private AttackPattern RollCutIn(int phase, AttackPattern performed)
        {
            if (phase < 0)
            {
                return null;
            }

            foreach (CutIn entry in phases[phase].cutIns)
            {
                if (entry != null && entry.pattern != null && entry.pattern != performed &&
                    UnityEngine.Random.value < entry.chance)
                {
                    return entry.pattern;
                }
            }

            return null;
        }

        private AttackPattern NextPattern(int phase, ref int patternIndex)
        {
            if (phase < 0)
            {
                return null;
            }

            List<AttackPattern> patterns = phases[phase].patterns;

            for (int tries = 0; tries < patterns.Count; tries++)
            {
                AttackPattern pattern = patterns[patternIndex % patterns.Count];
                patternIndex = (patternIndex + 1) % patterns.Count;

                if (pattern != null)
                {
                    return pattern;
                }
            }

            return null;
        }

        /// <summary>True if the player is within the enemy's detection radius.</summary>
        public bool PlayerInRange()
        {
            return ResolveTarget()
                && (target.position - transform.position).sqrMagnitude <= detectionRadius * detectionRadius;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }

        private bool ResolveTarget()
        {
            if (target == null)
            {
                PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (player == null)
                {
                    return false;
                }

                target = player.transform;
                targetCollider = null;
            }

            if (targetCollider == null)
            {
                targetCollider = target.GetComponent<Collider>();
            }

            return true;
        }
    }
}
