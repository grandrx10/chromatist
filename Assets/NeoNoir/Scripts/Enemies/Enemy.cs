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
    /// third. The order of the list does not matter.
    ///
    /// Within a phase, attacks run one at a time in list order and loop. Each
    /// attack plays out fully, then the enemy waits that attack's
    /// <see cref="AttackPattern.DelayAfter"/>. The phase is only re-checked
    /// between attacks, so crossing a threshold never interrupts an attack
    /// halfway through; the next attack comes from the new phase, starting at
    /// its first pattern.
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

        [Header("Animation")]
        [SerializeField]
        [Tooltip("Animator state played whenever the enemy is not attacking.")]
        private string idleAnimation = "GuardIdle";

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

        private EnemyHealth health;
        private Rigidbody body;
        private Collider targetCollider;
        private int phaseIndex = -1;

        // Movement of the current attack, from its start until the next's.
        private AttackPattern.MovementSettings movement;
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

        /// <summary>While true the enemy cannot wake, neither by the player coming close nor by taking damage.</summary>
        public bool StayDormant { get; set; }

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
            movement = null;
            ClearOverrides();
            lastPosition = transform.position;
            PlayAnimation(idleAnimation);
            StartCoroutine(Run());
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
            health.Damaged -= OnDamaged;
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

        private void OnDied(EnemyHealth died)
        {
            StopAllCoroutines();
            movement = null;
            Halt();
        }

        private void Update()
        {
            if (!health.IsDead && IsAwake)
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
            if (health.IsDead)
            {
                return;
            }

            Vector3 velocity = VelocityOverride.HasValue
                ? Flat(VelocityOverride.Value)
                : DesiredVelocity(Time.fixedDeltaTime);

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
        public string IdleAnimation => idleAnimation;

        public void PlayIdleAnimation()
        {
            PlayAnimation(idleAnimation);
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

            while (!health.IsDead)
            {
                int current = FindPhase();

                if (current != phaseIndex)
                {
                    phaseIndex = current;
                    patternIndex = 0;

                    if (current >= 0)
                    {
                        PhaseChanged?.Invoke(this, current);
                    }
                }

                AttackPattern pattern = NextPattern(current, ref patternIndex);

                // Nothing to do yet -- no phase, an empty one, or no player in
                // the scene. Check again next frame.
                if (pattern == null || !ResolveTarget())
                {
                    yield return null;
                    continue;
                }

                BeginMovement(pattern.Movement);
                PlayAnimation(pattern.AttackAnimation, BlendOrDefault(pattern.AttackAnimationBlend));

                yield return StartCoroutine(pattern.Perform(this));

                if (!string.IsNullOrEmpty(pattern.AttackAnimation))
                {
                    PlayAnimation(idleAnimation, BlendOrDefault(pattern.IdleReturnBlend));
                }

                if (pattern.DelayAfter > 0f)
                {
                    yield return new WaitForSeconds(pattern.DelayAfter);
                }
                else
                {
                    // Never spin a zero-delay loop inside one frame.
                    yield return null;
                }
            }
        }

        private float BlendOrDefault(float blend)
        {
            return blend >= 0f ? blend : animationBlend;
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

        private bool PlayerInRange()
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
