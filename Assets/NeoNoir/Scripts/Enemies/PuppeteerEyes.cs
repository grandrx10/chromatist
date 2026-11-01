using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's way of fighting without being there: while in its eye
    /// phases the boss is intangible, and every attack it starts tops its
    /// <see cref="PuppeteerEye"/>s back up to <see cref="maxEyes"/>. The eyes
    /// are the only way to hurt it.
    /// </summary>
    /// <remarks>
    /// Goes on the boss's root, beside <see cref="Enemy"/>. The boss is
    /// <see cref="EnemyHealth.Intangible"/> from the start: blows pass it by,
    /// and marks and homing never pick it out. It turns solid the moment it
    /// moves into a phase not listed in <see cref="eyePhases"/>. With a
    /// <see cref="PuppeteerPhaseTwo"/> on the boss, its phase and every one
    /// after it has started are never eye phases, even if listed: the boss
    /// fights those in person, solid, with no eyes.
    ///
    /// Only eyes that are gone are replaced. One that survived the last
    /// attack stays as hurt as it was; new ones come in at full health. They
    /// appear at random spots over the <see cref="Arena"/>, hanging
    /// <see cref="hangHeight"/> above the floor; any descent on strings is up
    /// to the eye prefab itself. Eyes left when the boss dies are destroyed
    /// with it; moving into a phase not in the list hauls them up and away.
    ///
    /// The drop counts as part of the attack, so the boss needs at least one
    /// attack in each eye phase, even one that does nothing to the player.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    public sealed class PuppeteerEyes : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The eye prefab dropped into the arena.")]
        private PuppeteerEye eyePrefab;

        [SerializeField]
        [Tooltip("Most eyes in the arena at once. Each attack replaces the ones destroyed, up to this.")]
        [Min(1)]
        private int maxEyes = 3;

        [SerializeField]
        [Tooltip("Extra damage the boss takes when an eye is destroyed, on top of the health the eye lost.")]
        [Min(0f)]
        private float killBonus = 50f;

        [SerializeField]
        [Tooltip("Phases, by their place in the boss's phase list (the first is 0), in which the boss is intangible and drops eyes.")]
        private int[] eyePhases = { 0 };

        [SerializeField]
        [Tooltip("Also drop eyes the moment the fight starts, so there is something to hit before the first attack.")]
        private bool dropOnWake = true;

        [Header("Placement")]
        [SerializeField]
        [Tooltip("Arena the eyes appear in. Empty uses the boss's, which the Puppeteer Intro sets.")]
        private ArenaBounds arena;

        [SerializeField]
        [Tooltip("How far above the floor the bottom of each eye hangs.")]
        [Min(0f)]
        private float hangHeight = 1f;

        private Enemy enemy;
        private EnemyHealth health;
        private PuppeteerPhaseTwo phaseTwo;
        private readonly List<PuppeteerEye> eyes = new List<PuppeteerEye>();

        /// <summary>Arena the eyes appear in: this one's own, or else the boss's.</summary>
        private ArenaBounds Arena => arena != null ? arena : enemy.Arena;

        /// <summary>The eyes dropped so far. Some may be destroyed, or dead and on their way out.</summary>
        public IReadOnlyList<PuppeteerEye> Eyes => eyes;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            health = GetComponent<EnemyHealth>();
            phaseTwo = GetComponent<PuppeteerPhaseTwo>();
            health.Intangible = InEyePhase(-1);
        }

        private void OnEnable()
        {
            enemy.Awakened += OnAwakened;
            enemy.AttackStarted += OnAttackStarted;
            enemy.PhaseChanged += OnPhaseChanged;
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            enemy.Awakened -= OnAwakened;
            enemy.AttackStarted -= OnAttackStarted;
            enemy.PhaseChanged -= OnPhaseChanged;
            health.Died -= OnDied;
        }

        /// <summary>
        /// Whether <paramref name="phase"/> is one the boss fights through its
        /// eyes. Before the first attack the boss has no phase yet, and counts
        /// as in its first. The phase a <see cref="PuppeteerPhaseTwo"/> plays
        /// for, and every phase once it has played, is never an eye phase,
        /// whatever <see cref="eyePhases"/> says: from then on the boss is in
        /// the arena in person.
        /// </summary>
        private bool InEyePhase(int phase)
        {
            int index = Mathf.Max(0, phase);

            if (phaseTwo != null && (phaseTwo.HasStarted || index == phaseTwo.Phase))
            {
                return false;
            }

            return System.Array.IndexOf(eyePhases, index) >= 0;
        }

        private void OnAwakened(Enemy woke)
        {
            if (dropOnWake && InEyePhase(enemy.PhaseIndex))
            {
                Drop();
            }
        }

        private void OnAttackStarted(Enemy attacker, AttackPattern pattern)
        {
            if (InEyePhase(enemy.PhaseIndex))
            {
                Drop();
            }
        }

        private void OnPhaseChanged(Enemy changed, int phase)
        {
            health.Intangible = InEyePhase(phase);

            if (!InEyePhase(phase))
            {
                LiftAll();
            }
        }

        /// <summary>Hauls every eye still in the arena up and away, unhurt.</summary>
        public void LiftAll()
        {
            foreach (PuppeteerEye eye in eyes)
            {
                if (eye != null)
                {
                    eye.LiftAway();
                }
            }

            eyes.Clear();
        }

        private void OnDied(EnemyHealth died)
        {
            foreach (PuppeteerEye eye in eyes)
            {
                if (eye != null)
                {
                    Destroy(eye.gameObject);
                }
            }

            eyes.Clear();
        }

        /// <summary>Replaces destroyed eyes, up to <see cref="maxEyes"/>.</summary>
        private void Drop()
        {
            ArenaBounds into = Arena;

            if (eyePrefab == null || into == null)
            {
                Debug.LogWarning($"[PuppeteerEyes] {name} needs an eye prefab and an arena to drop eyes.", this);
                return;
            }

            eyes.RemoveAll(eye => eye == null || eye.Health.IsDead);

            for (int i = eyes.Count; i < maxEyes; i++)
            {
                PuppeteerEye eye = Instantiate(eyePrefab, into.Centre, eyePrefab.transform.rotation);
                eye.name = eyePrefab.name;

                // A descent hides the eye, colliders and all, the moment it
                // is made, so its body is measured through the descent.
                PuppetDescent descent = eye.GetComponent<PuppetDescent>();
                bool placed = descent != null
                    ? into.TryPlace(eye.Health, descent.BodyBounds(), null, 0f, 0f, hangHeight)
                    : into.TryPlace(eye.Health, hangHeight);

                if (!placed)
                {
                    Debug.LogWarning($"[PuppeteerEyes] Found nowhere in {into.name} for {eyePrefab.name}: {into.LastFailure}.", this);
                    Destroy(eye.gameObject);
                    continue;
                }

                // Dropped by the boss, so it comes down now rather than
                // waiting hidden for the player to come near.
                if (descent != null)
                {
                    descent.DescendNow();
                }

                eye.Link(health, killBonus);
                eyes.Add(eye);
            }
        }
    }
}
