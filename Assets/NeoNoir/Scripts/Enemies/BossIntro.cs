using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A boss's entrance: when the player walks into this trigger, the boss
    /// drops out of the sky onto its landing spot, says its piece, and the
    /// fight begins.
    /// </summary>
    /// <remarks>
    /// Goes on the trigger box itself, which needs a trigger collider. The
    /// boss is either a separate object in the scene with an
    /// <see cref="Enemy"/>, or a prefab, which is spawned at the top of the
    /// drop and never itself touched. A scene boss is switched off until the
    /// drop, so nothing -- not homing blades, not stray shots -- can find it
    /// early; leave it switched on or off in the scene, either works. Plays
    /// once.
    ///
    /// In order:
    ///   1. The player's controls lock.
    ///   2. The boss appears <see cref="dropHeight"/> above its landing spot,
    ///      facing the player, starts <see cref="entranceAnimation"/>, and
    ///      falls, gathering speed, to land <see cref="dropTime"/> seconds
    ///      later. Match the drop time to the frame the animation touches down.
    ///   3. On landing: a <see cref="HammerImpact"/> burst and a camera shake.
    ///   4. Once the entrance animation has played to its very end, the boss
    ///      blends into <see cref="idleAnimation"/> over
    ///      <see cref="idleBlend"/> seconds, and the dialogue plays
    ///      through the <see cref="DialogueBox"/>, click by click.
    ///   5. Controls unlock, the <see cref="BossHealthBar"/> appears, and the
    ///      boss wakes. Its own Start Delay counts from here.
    ///
    /// The boss cannot wake by itself before step 5, even if hurt: this sets
    /// <see cref="Enemy.StayDormant"/> until then. It falls kinematic, so a
    /// Rigidbody's gravity has no say until it has landed.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public sealed class BossIntro : MonoBehaviour
    {
        [Header("Boss")]
        [SerializeField]
        [Tooltip("The boss in the scene, or a boss prefab to spawn when the drop starts.")]
        private Enemy boss;

        [SerializeField]
        [Tooltip("Name shown over the boss health bar.")]
        private string bossName = "Boss";

        [SerializeField]
        [Tooltip("Where the boss lands. Its pivot ends up exactly here. Empty uses where a scene boss is placed, or this trigger's position for a prefab.")]
        private Transform landingPoint;

        [SerializeField]
        [Tooltip("Middle of the arena, for attacks that pick random spots in it, such as Hammer Rain. Empty uses the landing spot.")]
        private Transform arenaCentre;

        [SerializeField]
        [Tooltip("Raises (or, negative, lowers) where the boss's pivot ends up, for a model whose feet are not at its pivot.")]
        private float landingHeightOffset;

        [Header("Drop")]
        [SerializeField]
        [Tooltip("How far above the landing spot the boss starts falling from.")]
        [Min(0f)]
        private float dropHeight = 30f;

        [SerializeField]
        [Tooltip("Seconds the fall takes. It speeds up all the way down.")]
        [Min(0.01f)]
        private float dropTime = 0.6f;

        [SerializeField]
        [Tooltip("Animator state played from the top of the fall, landing included. Should not loop.")]
        private string entranceAnimation = "HammerEntry";

        [SerializeField]
        [Tooltip("Animator state blended into once the entrance animation ends. Empty uses the boss's own Idle Animation.")]
        private string idleAnimation = "HammerIdle";

        [SerializeField]
        [Tooltip("Seconds the blend from the end of the entrance animation into idle takes.")]
        [Min(0f)]
        private float idleBlend = 0.5f;

        [Header("Landing impact")]
        [SerializeField]
        [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want.")]
        private Material impactMaterial;

        [SerializeField]
        private HammerImpact.Settings impact = new HammerImpact.Settings
        {
            ringRadius = 7f,
            ringWidth = 1.2f,
            ringTime = 0.5f,
            debrisCount = 16,
            debrisSize = 0.6f,
            debrisUpSpeed = new Vector2(7f, 12f),
            debrisOutSpeed = new Vector2(3f, 8f),
            debrisTime = 0.9f,
        };

        [SerializeField]
        [Tooltip("How far the camera shakes on landing. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.9f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.6f;

        [Header("Dialogue")]
        [SerializeField] private List<DialogueLine> dialogue = new List<DialogueLine>();

        private bool played;
        private PlayerInputReader input;

        // The boss actually fighting: the scene one, or the prefab's spawn.
        private Enemy fighter;

        /// <summary>The boss taking part, once the drop has started; null before.</summary>
        public Enemy Boss => fighter;

        /// <summary>False when the boss field holds a prefab asset rather than an object in the scene.</summary>
        private bool BossInScene => boss != null && boss.gameObject.scene.IsValid();

        /// <summary>Where the boss's pivot ends up once it has landed.</summary>
        private Vector3 LandingPosition
        {
            get
            {
                Vector3 spot = landingPoint != null ? landingPoint.position
                    : BossInScene ? boss.transform.position
                    : transform.position;

                return spot + Vector3.up * landingHeightOffset;
            }
        }

        /// <summary>Raised the moment the player walks into the trigger, before the drop.</summary>
        public event Action<BossIntro> Triggered;

        /// <summary>Raised once the dialogue is over and the boss wakes.</summary>
        public event Action<BossIntro> FightStarted;

        /// <summary>Raised when the boss dies.</summary>
        public event Action<BossIntro> Defeated;

        private void Awake()
        {
            if (boss == null)
            {
                Debug.LogWarning($"[BossIntro] {name} has no boss assigned.", this);
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
            StartCoroutine(Play(player.transform));
        }

        private void OnBossDied(EnemyHealth died)
        {
            died.Died -= OnBossDied;
            Defeated?.Invoke(this);
        }

        private IEnumerator Play(Transform player)
        {
            if (input != null)
            {
                input.Locked = true;
            }

            Vector3 landing = LandingPosition;

            Vector3 top = landing + Vector3.up * dropHeight;

            if (BossInScene)
            {
                fighter = boss;
                fighter.transform.position = top;
            }
            else
            {
                // Its Awake and OnEnable run here, but its brain only first
                // checks for waking a frame later, so this is in time.
                fighter = Instantiate(boss, top, boss.transform.rotation);
                fighter.name = boss.name;
            }

            fighter.StayDormant = true;
            fighter.ArenaCentre = arenaCentre != null ? arenaCentre.position : landing - Vector3.up * landingHeightOffset;

            Rigidbody body = fighter.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                body.isKinematic = true;
            }

            fighter.gameObject.SetActive(true);

            if (fighter.Health != null)
            {
                fighter.Health.Died += OnBossDied;
            }

            fighter.TurnModelToward(player.position - landing, 360f);
            StartEntranceAnimation();

            // Falls from rest under constant pull: height drops with the
            // square of time, so it is fastest at the bottom.
            float clock = 0f;

            while (clock < dropTime)
            {
                yield return null;
                clock += Time.deltaTime;

                float t = Mathf.Clamp01(clock / dropTime);
                fighter.transform.position = landing + Vector3.up * (dropHeight * (1f - t * t));
            }

            fighter.transform.position = landing;

            if (body != null)
            {
                body.isKinematic = wasKinematic;
                body.linearVelocity = Vector3.zero;
            }

            HammerImpact.Create(landing - Vector3.up * landingHeightOffset, impact, impactMaterial);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            yield return fighter.WaitForAnimation(entranceAnimation);

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

        /// <summary>
        /// Puts the boss straight into the entrance animation from its first
        /// frame. A blend asked for on the frame an animator wakes can be lost
        /// to the idle its enemy asks for in the same frame, so this starts
        /// the state outright and applies it at once instead.
        /// </summary>
        private void StartEntranceAnimation()
        {
            Animator animator = fighter.GetComponentInChildren<Animator>();

            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(entranceAnimation))
            {
                return;
            }

            int hash = Animator.StringToHash(entranceAnimation);

            if (!animator.HasState(0, hash))
            {
                Debug.LogWarning($"[BossIntro] {fighter.name}'s animator has no state called \"{entranceAnimation}\".", this);
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

        private void OnDrawGizmos()
        {
            if (landingPoint == null && boss == null)
            {
                return;
            }

            Vector3 landing = LandingPosition;

            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(landing, 1f);
            Gizmos.DrawLine(landing, landing + Vector3.up * dropHeight);
        }
    }
}
