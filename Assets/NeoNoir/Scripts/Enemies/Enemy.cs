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
    /// The model is separate from the enemy itself: attacks turn the model,
    /// never the root, so colliders and anything else on the root stay put.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class Enemy : MonoBehaviour
    {
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

        [Header("Behaviour")]
        [SerializeField]
        [Tooltip("Seconds after spawning before the first attack.")]
        [Min(0f)]
        private float startDelay = 1f;

        [SerializeField]
        private List<Phase> phases = new List<Phase> { new Phase { name = "Phase 1" } };

        private EnemyHealth health;
        private Collider targetCollider;
        private int phaseIndex = -1;

        /// <summary>Raised when the enemy moves into a new phase, with its index in the list.</summary>
        public event Action<Enemy, int> PhaseChanged;

        public EnemyHealth Health => health;

        public Transform Model => model != null ? model : transform;

        public Transform Muzzle => muzzle != null ? muzzle : Model;

        public Transform Target => target;

        public bool HasTarget => ResolveTarget();

        /// <summary>The middle of the target's body, or its pivot if it has no collider.</summary>
        public Vector3 TargetPoint => targetCollider != null ? targetCollider.bounds.center : target.position;

        /// <summary>Index of the current phase in the list, or -1 before the first attack.</summary>
        public int PhaseIndex => phaseIndex;

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

            Transform turning = Model;
            Vector3 toTarget = target.position - turning.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            Quaternion facing = Quaternion.LookRotation(toTarget, Vector3.up);
            turning.rotation = Quaternion.RotateTowards(turning.rotation, facing, maxDegrees);
            return Quaternion.Angle(turning.rotation, facing);
        }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
        }

        private void OnEnable()
        {
            health.Died += OnDied;
            StartCoroutine(Run());
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
            StopAllCoroutines();
        }

        private void OnDied(EnemyHealth died)
        {
            StopAllCoroutines();
        }

        private IEnumerator Run()
        {
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

                yield return StartCoroutine(pattern.Perform(this));

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
