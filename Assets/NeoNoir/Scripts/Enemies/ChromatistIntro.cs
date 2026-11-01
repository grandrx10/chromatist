using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Chromatist's entrance: when the player walks into this trigger,
    /// the boss floats down out of the sky in front of them, turning as it
    /// comes, hangs just above the floor in a ring of dust, touches down
    /// softly, says its piece, and the fight begins.
    /// </summary>
    /// <remarks>
    /// Goes on the trigger box itself, which needs a trigger collider. Plays
    /// once.
    ///
    /// In order:
    ///   1. The player's controls lock, and the camera swings round toward
    ///      where the boss will come down.
    ///   2. The landing spot is <see cref="landingDistance"/> in front of the
    ///      player: the way they were walking, or, standing still, toward the
    ///      <see cref="arenaCentre"/>, or failing that along this trigger's
    ///      forward. It is dropped onto the floor below, and pulled back to
    ///      the arena centre if it falls outside the <see cref="arena"/>.
    ///   3. The boss appears <see cref="dropHeight"/> above it, plays
    ///      <see cref="entranceAnimation"/>, and comes down at
    ///      <see cref="descentSpeed"/>, turning <see cref="descentSpin"/>
    ///      degrees on the way to end facing the player.
    ///   4. From <see cref="slowHeight"/> it slows evenly, coming to rest just
    ///      as its feet touch. From <see cref="dustStartHeight"/> down, dust
    ///      blows out across the floor beneath it, harder the lower it gets,
    ///      with a soft ring as it touches down. It then blends into
    ///      <see cref="idleAnimation"/>.
    ///   5. Its feet -- the bottom of its colliders -- end on the floor,
    ///      wherever its pivot is; <see cref="landingHeightOffset"/> nudges
    ///      that for a model that does not match its colliders.
    ///   6. The dialogue plays through the <see cref="DialogueBox"/>, click by
    ///      click.
    ///   7. Controls unlock, the <see cref="BossHealthBar"/> appears, and the
    ///      boss wakes. Its own Start Delay counts from here.
    ///
    /// The boss is either an object in the scene with an <see cref="Enemy"/>,
    /// switched off until step 3 so nothing can find it early, or a prefab
    /// spawned then. It cannot wake by itself before step 7, even if hurt,
    /// and it moves kinematic until it has touched down. It is handed the
    /// arena, as its <see cref="Enemy.Arena"/> and
    /// <see cref="Enemy.ArenaCentre"/>, so attacks such as
    /// <see cref="StoneSwordPattern"/> keep to it.
    /// </remarks>
    public sealed class ChromatistIntro : MonoBehaviour
    {
        [Header("Boss")]
        [SerializeField]
        [Tooltip("The boss in the scene, or a boss prefab to spawn when the player walks in.")]
        private Enemy boss;

        [SerializeField]
        [Tooltip("Name shown over the boss health bar.")]
        private string bossName = "The Chromatist";

        [SerializeField]
        [Tooltip("The arena. The boss never comes down outside it, and its attacks keep to it. Optional.")]
        private ArenaBounds arena;

        [SerializeField]
        [Tooltip("Middle of the arena, for attacks that pick spots round it. Empty uses the arena's centre, or the landing spot.")]
        private Transform arenaCentre;

        [SerializeField]
        [Tooltip("Raises (or, negative, lowers) the boss once landed. Its feet are already put on the floor by the bottom of its colliders; this is for a model that does not match them.")]
        private float landingHeightOffset;

        [Header("Landing spot")]
        [SerializeField]
        [Tooltip("How far in front of the player the boss comes down.")]
        [Min(0f)]
        private float landingDistance = 7f;

        [SerializeField]
        [Tooltip("Layers the boss can land on. Must include the floor's own layer, such as NonOcclude. The player and triggers are always ignored.")]
        private LayerMask floorMask = ~0;

        [Header("Descent")]
        [SerializeField]
        [Tooltip("How far above the floor the boss's feet start from.")]
        [Min(0f)]
        private float dropHeight = 14f;

        [SerializeField]
        [Tooltip("Speed the boss comes down at, in units per second, until it starts slowing.")]
        [Min(0.1f)]
        private float descentSpeed = 9f;

        [SerializeField]
        [Tooltip("Height above the floor at which the boss starts slowing. It slows evenly from there, coming to rest just as its feet touch.")]
        [Min(0.01f)]
        private float slowHeight = 2.5f;

        [SerializeField]
        [Tooltip("Degrees the boss turns about itself on the way down, ending facing the player. 0 for none.")]
        private float descentSpin = 180f;

        [Header("Animation")]
        [SerializeField]
        [Tooltip("Animator state played from the top of the fall. Empty plays nothing.")]
        private string entranceAnimation = "";

        [SerializeField]
        [Tooltip("Animator state blended into once the boss touches down. Empty uses the boss's own Idle Animation.")]
        private string idleAnimation = "";

        [SerializeField]
        [Min(0f)]
        private float idleBlend = 0.4f;

        [Header("Dust ring")]
        [SerializeField]
        [Tooltip("Height above the floor at which the dust starts to stir beneath the boss. It blows harder the lower the boss gets.")]
        [Min(0f)]
        private float dustStartHeight = 4f;

        [SerializeField]
        [Tooltip("Puffs a second blown out at the strongest, while the boss hangs just over the floor.")]
        [Min(0f)]
        private float puffsPerSecond = 40f;

        [SerializeField]
        [Tooltip("Puffs in the soft ring as the boss touches down.")]
        [Min(0)]
        private int landingPuffs = 24;

        [SerializeField]
        [Tooltip("How far from under the boss the puffs start.")]
        [Min(0f)]
        private float ringRadius = 0.8f;

        [SerializeField]
        [Tooltip("Speed the puffs blow outward across the floor at the strongest. Each varies a little.")]
        [Min(0f)]
        private float ringSpeed = 6f;

        [SerializeField]
        private DustTrail.Settings dust = new DustTrail.Settings
        {
            size = 0.45f,
            lifetime = 0.9f,
            rise = 0.5f,
            colour = new Color(0.85f, 0.85f, 0.85f),
        };

        [Header("Camera")]
        [SerializeField] private bool turnCamera = true;

        [SerializeField]
        [Tooltip("Seconds the camera takes to swing round toward the landing spot. 0 snaps.")]
        [Min(0f)]
        private float cameraTurnTime = 0.8f;

        [Header("Dialogue")]
        [SerializeField] private List<DialogueLine> dialogue = new List<DialogueLine>();

        private bool played;
        private PlayerInputReader input;
        private Enemy fighter;
        private DustTrail.Cloud cloud;

        /// <summary>The boss taking part, once the intro has started; null before.</summary>
        public Enemy Boss => fighter;

        /// <summary>Raised the moment the player walks in and the intro starts.</summary>
        public event Action<ChromatistIntro> Triggered;

        /// <summary>Raised once the dialogue is over and the fight begins.</summary>
        public event Action<ChromatistIntro> FightStarted;

        /// <summary>Raised when the boss dies.</summary>
        public event Action<ChromatistIntro> Defeated;

        private bool BossInScene => boss != null && boss.gameObject.scene.IsValid();

        private void Awake()
        {
            if (boss == null)
            {
                Debug.LogWarning($"[ChromatistIntro] {name} has no boss assigned.", this);
                return;
            }

            // A prefab asset is left alone: changes to it would stick after
            // play mode.
            if (BossInScene)
            {
                boss.StayDormant = true;
                boss.gameObject.SetActive(false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (played || boss == null)
            {
                return;
            }

            PlayerMotor player = other.GetComponentInParent<PlayerMotor>();

            if (player == null)
            {
                return;
            }

            played = true;
            input = player.GetComponent<PlayerInputReader>();
            Triggered?.Invoke(this);
            StartCoroutine(Play(player));
        }

        private void OnBossDied(EnemyHealth died)
        {
            died.Died -= OnBossDied;
            Defeated?.Invoke(this);
        }

        private IEnumerator Play(PlayerMotor player)
        {
            if (input != null)
            {
                input.Locked = true;
            }

            Vector3 floor = LandingSpot(player);

            if (turnCamera)
            {
                TopDownCamera.TurnToward(floor, cameraTurnTime);
            }

            Spawn(floor + Vector3.up * dropHeight, floor);

            Rigidbody body = fighter.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                body.isKinematic = true;
            }

            // Where the pivot must be for the feet -- the bottom of the
            // colliders -- to rest on the floor.
            Physics.SyncTransforms();
            float footOffset = fighter.transform.position.y - fighter.Health.BodyBounds().min.y;
            Vector3 landing = floor + Vector3.up * (footOffset + landingHeightOffset);

            Vector3 facing = player.transform.position - landing;
            facing.y = 0f;
            Turn(facing, 1f);
            StartAnimation(entranceAnimation);
            cloud = DustTrail.Cloud.Create("ChromatistIntroDust");

            // Comes down steadily, then slows evenly over the Slow Height to
            // rest just as its feet touch.
            float slow = Mathf.Min(slowHeight, dropHeight);
            float cruiseTime = (dropHeight - slow) / descentSpeed;
            float slowTime = 2f * slow / descentSpeed;
            float total = cruiseTime + slowTime;

            for (float clock = 0f; clock < total; clock += Time.deltaTime)
            {
                float height;

                if (clock < cruiseTime)
                {
                    height = dropHeight - descentSpeed * clock;
                }
                else
                {
                    float since = clock - cruiseTime;
                    height = slow - descentSpeed * since + descentSpeed * since * since / (2f * slowTime);
                }

                Place(landing, Mathf.Max(0f, height));
                Turn(facing, 1f - clock / total);
                Blow(floor, height, Time.deltaTime);
                yield return null;
            }

            Place(landing, 0f);
            Turn(facing, 0f);
            Ring(floor, landingPuffs, 0.6f);
            cloud.Release();
            cloud = null;

            if (body != null)
            {
                body.isKinematic = wasKinematic;
                body.linearVelocity = Vector3.zero;
            }

            fighter.PlayAnimation(string.IsNullOrEmpty(idleAnimation) ? fighter.IdleAnimation : idleAnimation, idleBlend);

            bool talking = true;
            Say(0, () => talking = false);

            while (talking)
            {
                yield return null;
            }

            DialogueBox.Instance.Close();

            if (input != null)
            {
                input.Locked = false;
            }

            BossHealthBar.Show(fighter.Health, bossName);
            fighter.StayDormant = false;
            fighter.Wake();
            FightStarted?.Invoke(this);
        }

        /// <summary>The floor in front of the player where the boss comes down.</summary>
        private Vector3 LandingSpot(PlayerMotor player)
        {
            Vector3 from = player.transform.position;
            Vector3 centre = CentreOr(from);
            Vector3 ahead = player.PlanarVelocity;
            ahead.y = 0f;

            if (ahead.sqrMagnitude < 0.25f)
            {
                ahead = centre - from;
                ahead.y = 0f;
            }

            if (ahead.sqrMagnitude < 0.0001f)
            {
                ahead = transform.forward;
                ahead.y = 0f;
            }

            Vector3 spot = from + ahead.normalized * landingDistance;

            if (arena != null && !arena.Contains(spot))
            {
                spot = centre;
            }

            if (arena != null && arena.FloorAt(spot, player.transform, out float arenaFloor))
            {
                return new Vector3(spot.x, arenaFloor, spot.z);
            }

            // The highest floor under the spot, the player aside.
            float nearest = float.MaxValue;
            Vector3 found = spot;

            foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 10f, Vector3.down, 40f,
                                                          floorMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance < nearest && !hit.collider.transform.IsChildOf(player.transform))
                {
                    nearest = hit.distance;
                    found = hit.point;
                }
            }

            if (nearest == float.MaxValue)
            {
                Debug.LogWarning($"[ChromatistIntro] {name} found no floor under the landing spot. Check the Floor Mask.", this);
                found.y = from.y;
            }

            return found;
        }

        private Vector3 CentreOr(Vector3 fallback)
        {
            return arenaCentre != null ? arenaCentre.position
                : arena != null ? arena.Centre
                : fallback;
        }

        private void Spawn(Vector3 top, Vector3 floor)
        {
            if (BossInScene)
            {
                fighter = boss;
                fighter.transform.position = top;
            }
            else
            {
                // Its brain only first checks for waking a frame later, so
                // setting it dormant here is in time.
                fighter = Instantiate(boss, top, boss.transform.rotation);
                fighter.name = boss.name;
            }

            fighter.StayDormant = true;
            fighter.ArenaCentre = CentreOr(floor);
            fighter.Arena = arena;
            fighter.gameObject.SetActive(true);

            if (fighter.Health != null)
            {
                fighter.Health.Died += OnBossDied;
            }
        }

        private void Place(Vector3 landing, float height)
        {
            fighter.transform.position = landing + Vector3.up * height;
        }

        /// <summary>Faces the model <paramref name="left"/> of the Descent Spin short of <paramref name="facing"/>.</summary>
        private void Turn(Vector3 facing, float left)
        {
            if (facing.sqrMagnitude > 0.0001f)
            {
                fighter.TurnModelToward(Quaternion.Euler(0f, descentSpin * left, 0f) * facing, 360f);
            }
        }

        /// <summary>Puffs of dust spread evenly round the spot, blown outward across the floor.</summary>
        private void Ring(Vector3 floor, int count, float strength)
        {
            float start = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            for (int i = 0; i < count; i++)
            {
                float angle = start + Mathf.PI * 2f * i / count + UnityEngine.Random.Range(-0.1f, 0.1f);
                Puff(floor, angle, strength);
            }
        }

        // Puffs owed but not yet blown, carried between frames.
        private float puffDue;

        /// <summary>
        /// The outward blow under the boss, stirring once it is below the
        /// Dust Start Height and strongest just over the floor.
        /// </summary>
        private void Blow(Vector3 floor, float height, float deltaTime)
        {
            float strength = dustStartHeight > 0f ? 1f - Mathf.Clamp01(height / dustStartHeight) : 0f;

            if (strength <= 0f)
            {
                return;
            }

            puffDue += puffsPerSecond * strength * deltaTime;

            while (puffDue >= 1f)
            {
                puffDue -= 1f;
                Puff(floor, UnityEngine.Random.Range(0f, Mathf.PI * 2f), Mathf.Lerp(0.4f, 1f, strength));
            }
        }

        private void Puff(Vector3 floor, float angle, float strength)
        {
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 velocity = outward * (ringSpeed * strength * UnityEngine.Random.Range(0.7f, 1.1f))
                               + Vector3.up * (dust.rise * UnityEngine.Random.Range(0.5f, 1f));
            cloud.Emit(floor + outward * ringRadius + Vector3.up * (dust.size * 0.3f), velocity, dust);
        }

        /// <summary>
        /// Puts the boss straight into <paramref name="state"/> from its first
        /// frame, as <see cref="BossIntro"/> does, so the idle its enemy asks
        /// for on waking cannot win.
        /// </summary>
        private void StartAnimation(string state)
        {
            Animator animator = fighter.GetComponentInChildren<Animator>();

            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state))
            {
                return;
            }

            int hash = Animator.StringToHash(state);

            if (!animator.HasState(0, hash))
            {
                Debug.LogWarning($"[ChromatistIntro] {fighter.name}'s animator has no state called \"{state}\".", this);
                return;
            }

            animator.Play(hash, 0, 0f);
            animator.Update(0f);
        }

        /// <summary>Says the lines from <paramref name="index"/> on, then calls <paramref name="done"/>.</summary>
        private void Say(int index, Action done)
        {
            if (index >= dialogue.Count)
            {
                done();
                return;
            }

            DialogueBox.Instance.Say(dialogue[index], () => Say(index + 1, done));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.25f, 0.8f, 0.8f);
            Vector3 spot = transform.position + transform.forward * landingDistance;
            Gizmos.DrawWireSphere(spot, 1f);
            Gizmos.DrawLine(spot, spot + Vector3.up * dropHeight);
        }
    }
}
