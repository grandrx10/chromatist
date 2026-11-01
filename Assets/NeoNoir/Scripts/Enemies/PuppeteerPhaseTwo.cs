using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's entrance in person: when the boss moves into
    /// <see cref="phase"/>, the stage is cleared, the boss is lowered on its
    /// strings into the middle of the arena, says its piece, and the fight
    /// goes on.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The boss holds its attacks and cannot be hurt, the player's
    ///      controls lock, and the player cannot be hurt either until the
    ///      scene is over. Every enemy projectile in flight fades away
    ///      harmlessly over <see cref="projectileFadeTime"/>.
    ///   2. The stage clears: every eye is hauled up and away (by
    ///      <see cref="PuppeteerEyes"/>), every puppet in the arena with a
    ///      <see cref="PuppetDescent"/> is hauled back up its strings, and any
    ///      targeted strings still out snap back. Gift boxes stay.
    ///   3. After <see cref="liftTime"/>, the boss appears
    ///      <see cref="dropHeight"/> above the middle of the arena, facing the
    ///      player, and is lowered onto the floor over
    ///      <see cref="descentTime"/>, fast at first and settling gently,
    ///      playing <see cref="descentAnimation"/> if set. Its own
    ///      <see cref="PuppetStrings"/>, if any, hang it from above.
    ///   4. After <see cref="dialogueDelay"/>, the dialogue plays through the
    ///      <see cref="DialogueBox"/>, click by click.
    ///   5. Controls unlock, the boss can be hurt, and it carries on with the
    ///      new phase's attacks.
    ///
    /// Goes on the boss's root, beside <see cref="Enemy"/>. Uses the arena
    /// the <see cref="PuppeteerIntro"/> handed the boss. Plays once.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    public sealed class PuppeteerPhaseTwo : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The phase, by its place in the boss's phase list (the first is 0), whose start plays this.")]
        [Min(0)]
        private int phase = 1;

        [Header("Clearing the stage")]
        [SerializeField]
        [Tooltip("Seconds from the eyes and puppets being hauled up to the boss starting down.")]
        [Min(0f)]
        private float liftTime = 1f;

        [SerializeField]
        [Tooltip("Seconds every enemy projectile still in flight takes to fade away as the stage clears. They cannot hurt anyone meanwhile.")]
        [Min(0.01f)]
        private float projectileFadeTime = 0.4f;

        [Header("Descent")]
        [SerializeField]
        [Tooltip("How far above the middle of the arena the boss starts.")]
        [Min(0f)]
        private float dropHeight = 14f;

        [SerializeField]
        [Tooltip("Seconds the boss takes to be lowered onto the floor.")]
        [Min(0.05f)]
        private float descentTime = 1.6f;
        [SerializeField]
        [Tooltip("Animator state played while lowered. Empty keeps its idle.")]
        private string descentAnimation = "";

        [SerializeField]
        [Tooltip("Seconds the blend back into idle takes once landed.")]
        [Min(0f)]
        private float idleBlend = 0.3f;

        [Header("Camera")]
        [SerializeField]
        [Tooltip("Seconds the camera takes to swing round to look from the player toward the boss as it starts down. Its tilt is left alone. 0 snaps; turn off Turn Camera to leave it be.")]
        [Min(0f)]
        private float cameraTurnTime = 1f;

        [SerializeField] private bool turnCamera = true;

        [Header("Dialogue")]
        [SerializeField]
        [Tooltip("Seconds between landing and the first line.")]
        [Min(0f)]
        private float dialogueDelay = 0.5f;

        [SerializeField] private List<DialogueLine> dialogue = new List<DialogueLine>();

        private Enemy enemy;
        private EnemyHealth health;
        private bool played;
        private PlayerInputReader input;
        private PlayerHealth playerHealth;

        /// <summary>Raised once the cutscene is over and the fight goes on.</summary>
        public event Action<PuppeteerPhaseTwo> Finished;

        /// <summary>The phase, by its place in the boss's phase list, this entrance plays for.</summary>
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
        }

        private void OnDisable()
        {
            enemy.PhaseChanged -= OnPhaseChanged;

            // Cut off mid-scene: never leave the player unhurtable.
            Unprotect();
        }

        private void OnPhaseChanged(Enemy changed, int index)
        {
            if (played || index != phase)
            {
                return;
            }

            played = true;

            // Now, inside the phase change, so the new phase's first attack
            // is held before it starts.
            enemy.HoldAttacks = true;
            health.Invulnerable = true;
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
            input = player != null ? player.GetComponent<PlayerInputReader>() : null;
            Lock();

            // Unhurtable for the whole scene, not just the dialogue: the
            // controls are locked from the start, so nothing could be dodged.
            playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;

            if (playerHealth != null)
            {
                playerHealth.SetDamageTakenModifier(this, 0f);
            }

            ClearStage();

            yield return Wait(liftTime);
            yield return Descend(player != null ? player.transform : null);
            yield return Wait(dialogueDelay);

            bool talking = true;
            Say(0, () => talking = false);

            while (talking)
            {
                Lock();
                yield return null;
            }

            DialogueBox.Instance.Close();

            if (input != null)
            {
                input.Locked = false;
            }

            Unprotect();
            enemy.VelocityOverride = null;
            health.Invulnerable = false;
            enemy.HoldAttacks = false;
            Finished?.Invoke(this);
        }

        /// <summary>
        /// Keeps the player's controls locked. Called every frame of the
        /// cutscene rather than once, so a snare running out partway, which
        /// unlocks them, cannot hand control back early.
        /// </summary>
        private void Lock()
        {
            if (input != null)
            {
                input.Locked = true;
            }

            // Standing still, whatever the last attack's walk was.
            enemy.VelocityOverride = Vector3.zero;
        }

        private IEnumerator Wait(float seconds)
        {
            float clock = 0f;

            while (clock < seconds)
            {
                Lock();
                yield return null;
                clock += Time.deltaTime;
            }
        }

        /// <summary>Lets the player be hurt again.</summary>
        private void Unprotect()
        {
            if (playerHealth != null)
            {
                playerHealth.RemoveDamageTakenModifier(this);
                playerHealth = null;
            }
        }

        private void ClearStage()
        {
            EnemyProjectile.FadeOutAll(projectileFadeTime);
            TargetedStrings.ReleaseAll(enemy);

            PuppeteerEyes eyes = GetComponent<PuppeteerEyes>();

            if (eyes != null)
            {
                eyes.LiftAll();
            }

            ArenaBounds arena = enemy.Arena;

            // A copy: lifting can remove puppets from the list as it goes.
            List<EnemyHealth> everyone = new List<EnemyHealth>(EnemyHealth.Active);

            foreach (EnemyHealth other in everyone)
            {
                if (other == null || other == health || other.IsDead)
                {
                    continue;
                }

                PuppetDescent puppet = other.GetComponent<PuppetDescent>();

                if (puppet != null && (arena == null || arena.Contains(other.transform.position)))
                {
                    puppet.LiftAway();
                }
            }
        }

        private IEnumerator Descend(Transform player)
        {
            Vector3 landing = LandingSpot();
            Vector3 top = landing + Vector3.up * dropHeight;

            Rigidbody body = GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                body.isKinematic = true;
                body.position = top;
            }

            transform.position = top;

            if (player != null)
            {
                enemy.TurnModelToward(player.position - landing, 360f);
            }

            if (turnCamera)
            {
                TopDownCamera.TurnToward(landing, cameraTurnTime);
            }

            if (!string.IsNullOrEmpty(descentAnimation))
            {
                enemy.PlayAnimation(descentAnimation, 0f);
            }

            // Lowered fast at first, settling gently onto the floor.
            float clock = 0f;

            while (clock < descentTime)
            {
                Lock();
                yield return null;
                clock += Time.deltaTime;

                float left = 1f - Mathf.Clamp01(clock / descentTime);
                transform.position = landing + Vector3.up * (dropHeight * left * left);
            }

            transform.position = landing;

            if (body != null)
            {
                body.position = landing;
                body.isKinematic = wasKinematic;

                if (!wasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }

            if (!string.IsNullOrEmpty(descentAnimation))
            {
                enemy.PlayAnimation(enemy.IdleAnimation, idleBlend);
            }
        }

        /// <summary>Where the boss's pivot ends up: its feet on the floor in the middle of the arena.</summary>
        private Vector3 LandingSpot()
        {
            ArenaBounds arena = enemy.Arena;
            Vector3 centre = arena != null ? arena.Centre
                : enemy.ArenaCentre.HasValue ? enemy.ArenaCentre.Value
                : transform.position;

            if (arena != null && arena.FloorAt(centre, transform, out float floor))
            {
                centre.y = floor;
            }

            // Measured where it stands, before moving: the pivot need not be at its feet.
            Bounds bodyBounds = health.BodyBounds();
            float feet = bodyBounds.min.y - transform.position.y;

            return centre - Vector3.up * feet;
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
    }
}
