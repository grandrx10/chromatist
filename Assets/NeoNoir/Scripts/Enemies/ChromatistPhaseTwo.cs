using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Chromatist's turn into its second phase: it stops, talks, and
    /// partway through conjures a new blade -- the Dark Dagger -- floating in
    /// front of it, which turns on it and drives itself into its chest, where
    /// it stays for the rest of the fight.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. When the boss moves into <see cref="phase"/>, it holds its attacks
    ///      and cannot be hurt, the player's controls lock, and the player
    ///      cannot be hurt either until the scene is over. Every enemy
    ///      projectile in flight fades away harmlessly. The boss stands still
    ///      facing the player, in its idle.
    ///   2. After <see cref="dialogueDelay"/>, the dialogue plays through the
    ///      <see cref="DialogueBox"/>, click by click.
    ///   3. Line <see cref="stabLine"/> (the first is 0) cannot be clicked
    ///      past until the stab is done. <see cref="stabDelay"/> after it
    ///      appears, the stab plays. Nothing on the boss itself moves; only
    ///      the dagger does:
    ///        a. Summon: the dagger dissolves into being at
    ///           <see cref="floatOffset"/> in front of the boss, in a dark
    ///           burst, spinning, and turns its point on the chest.
    ///        b. Hold: it hovers, trembling, drawing back
    ///           <see cref="drawBack"/>.
    ///        c. Plunge: it flies point first into the chest from the front,
    ///           faster and faster. On impact the camera shakes, the screen
    ///           flashes, and sparks and a black explosion burst from the
    ///           wound. The dagger is left in the chest, parented to the chest
    ///           bone, so it moves with the body from then on.
    ///        d. A pause of <see cref="impactHold"/> before the line can be
    ///           moved on from.
    ///      If the line is past the end of the dialogue, the stab plays after
    ///      the last line instead.
    ///   4. From the impact on, the boss's <see cref="AfterimageAura"/> --
    ///      added with its defaults if it has none -- leaves grey copies of it
    ///      behind it as it moves.
    ///   5. Once the dialogue is done, controls unlock, the boss can be hurt,
    ///      and it carries on with the new phase's attacks.
    ///
    /// Select the boss to see the wound and the hover spot as gizmos, to tune
    /// <see cref="entryOffset"/> and <see cref="floatOffset"/>.
    ///
    /// The Dark Dagger prefab is pure art, its tip pointing along its local
    /// +Z (forward) -- or corrected with <see cref="daggerRotation"/>. <see cref="tipDistance"/> is how
    /// far the tip is from its pivot.
    ///
    /// Goes on the boss's root, beside <see cref="Enemy"/>. Plays once.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    public sealed class ChromatistPhaseTwo : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField]
        [Tooltip("The phase, by its place in the boss's phase list (the first is 0), whose start plays this.")]
        [Min(0)]
        private int phase = 1;

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

        [SerializeField]
        [Tooltip("The line, by its place in the list above (the first is 0), during which the boss stabs itself. It cannot be clicked past until the stab is done. Past the end of the list, the stab plays after the last line.")]
        [Min(0)]
        private int stabLine = 1;

        [SerializeField]
        [Tooltip("Seconds from the stab line appearing to the dagger appearing.")]
        [Min(0f)]
        private float stabDelay = 0.8f;

        [Header("Dark Dagger")]
        [SerializeField]
        [Tooltip("The dagger's art, its tip pointing along its local +Z.")]
        private GameObject daggerPrefab;

        [SerializeField]
        [Tooltip("Turns the model, for one whose tip does not point along +Z.")]
        private Vector3 daggerRotation;

        [SerializeField]
        [Min(0.01f)]
        private float daggerScale = 1f;

        [SerializeField]
        [Tooltip("Distance from the dagger's pivot to the tip of the blade.")]
        [Min(0f)]
        private float tipDistance = 1.2f;

        [Header("Placement")]
        [SerializeField]
        [Tooltip("Name of the chest bone, somewhere under the boss. The dagger goes in here and stays with it.")]
        private string chestBone = "Bone.001";

        [SerializeField]
        [Tooltip("Where the blade goes in, from the chest bone: right, up and forward of the way the boss faces.")]
        private Vector3 entryOffset = new Vector3(0f, 0.6f, 0.5f);

        [SerializeField]
        [Tooltip("Where the dagger's tip hovers before the plunge, from the wound: right, up and forward of the way the boss faces.")]
        private Vector3 floatOffset = new Vector3(0f, 0.6f, 2.5f);

        [SerializeField]
        [Tooltip("How far past the wound the tip ends up, inside the body.")]
        [Min(0f)]
        private float stabDepth = 0.4f;

        [Header("Summon")]
        [SerializeField]
        [Tooltip("Seconds the dagger takes to form and turn its point on the chest.")]
        [Min(0.05f)]
        private float summonTime = 1.2f;

        [SerializeField]
        [Tooltip("Height it rises by as it forms, coming up into its hover spot.")]
        private float summonRise = 0.6f;

        [SerializeField]
        [Tooltip("Degrees it spins about its blade as it forms, slowing to a stop.")]
        private float summonSpin = 720f;

        [Header("Hold")]
        [SerializeField]
        [Tooltip("Seconds it hovers, aimed, before the plunge.")]
        [Min(0f)]
        private float holdTime = 0.9f;

        [SerializeField]
        [Tooltip("How far it draws back along its aim while hovering, winding up.")]
        [Min(0f)]
        private float drawBack = 0.5f;

        [SerializeField]
        [Tooltip("How hard it trembles while hovering.")]
        [Min(0f)]
        private float tremble = 0.03f;

        [SerializeField] private float bobAmplitude = 0.08f;
        [SerializeField] private float bobFrequency = 0.8f;

        [Header("Plunge")]
        [SerializeField]
        [Tooltip("Seconds the plunge takes. Short.")]
        [Min(0.02f)]
        private float plungeTime = 0.12f;

        [SerializeField]
        [Tooltip("Seconds after impact before the stab line can be moved on from.")]
        [Min(0f)]
        private float impactHold = 1.5f;

        [Header("Effects")]
        [SerializeField]
        private EnergyBurst.Settings summonBurst = new EnergyBurst.Settings
        {
            flashSize = 0.8f,
            sparkCount = 14,
            colour = new Color(0.6f, 0.05f, 1.2f, 1f),
        };

        [SerializeField]
        private EnergyBurst.Settings impactBurst = new EnergyBurst.Settings
        {
            flashSize = 1.6f,
            flashTime = 0.2f,
            sparkCount = 28,
            sparkSpeed = new Vector2(6f, 14f),
            sparkLife = 0.5f,
            colour = new Color(3f, 0.1f, 0.15f, 1f),
        };

        [SerializeField]
        [Tooltip("Radius of the black explosion that bursts out of the wound. 0 for none.")]
        [Min(0f)]
        private float explosionRadius = 1.6f;

        [SerializeField]
        private BlastSphere.Settings explosion = new BlastSphere.Settings
        {
            growTime = 0.1f,
            fadeTime = 0.35f,
            colour = Color.black,
            outlineWidth = 0.12f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField] private float impactShake = 0.6f;
        [SerializeField] [Min(0f)] private float impactShakeDuration = 0.5f;

        [SerializeField]
        [Tooltip("Flashes the screen with the impact frames on impact, if the scene has them.")]
        private bool impactFrames = true;

        [Header("Camera")]
        [SerializeField] private bool turnCamera = true;

        [SerializeField]
        [Tooltip("Seconds the camera takes to swing round to look from the player toward the boss. 0 snaps.")]
        [Min(0f)]
        private float cameraTurnTime = 0.8f;

        private Enemy enemy;
        private EnemyHealth health;
        private bool played;
        private PlayerInputReader input;
        private PlayerHealth playerHealth;

        private Transform chest;
        private Transform dagger;
        private Renderer[] daggerRenderers;
        private MaterialPropertyBlock block;
        private bool lodged;

        // Seconds into the stab, or negative while none is playing.
        private float stabClock = -1f;
        private float trembleSeed;
        private Vector3 plungeFrom;
        private Vector3 plungeAim;

        /// <summary>Raised once the scene is over and the fight goes on.</summary>
        public event Action<ChromatistPhaseTwo> Finished;

        /// <summary>The phase, by its place in the boss's phase list, this plays for.</summary>
        public int Phase => phase;

        /// <summary>True from the moment the boss moves into <see cref="Phase"/> on.</summary>
        public bool HasStarted => played;

        /// <summary>The Dark Dagger, once summoned; null before.</summary>
        public Transform Dagger => dagger;

        private float PlungeStart => summonTime + holdTime;
        private float ImpactTime => PlungeStart + plungeTime;
        private float StabLength => ImpactTime + impactHold;

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

        private void OnDestroy()
        {
            // Not yet in the chest, so not going with the boss by itself.
            if (dagger != null && !lodged)
            {
                Destroy(dagger.gameObject);
            }
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
            Lock(player);

            playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;

            if (playerHealth != null)
            {
                playerHealth.SetDamageTakenModifier(this, 0f);
            }

            EnemyProjectile.FadeOutAll(projectileFadeTime);
            enemy.PlayAnimation(enemy.IdleAnimation, 0.3f);

            if (turnCamera)
            {
                TopDownCamera.TurnToward(transform.position, cameraTurnTime);
            }

            yield return Wait(dialogueDelay, player);

            bool stabbed = false;

            for (int i = 0; i < dialogue.Count; i++)
            {
                bool next = false;
                DialogueBox.Instance.Say(dialogue[i], () => next = true);

                if (i == stabLine)
                {
                    DialogueBox.Instance.Unskippable = true;
                    yield return Wait(stabDelay, player);
                    yield return Stab(player);
                    DialogueBox.Instance.Unskippable = false;
                    stabbed = true;
                }

                while (!next)
                {
                    Lock(player);
                    yield return null;
                }
            }

            if (!stabbed)
            {
                yield return Stab(player);
            }

            DialogueBox.Instance.Close();

            if (input != null)
            {
                input.Locked = false;
            }

            Unprotect();
            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;
            health.Invulnerable = false;
            enemy.HoldAttacks = false;
            Finished?.Invoke(this);
        }

        /// <summary>
        /// Keeps the player's controls locked and the boss standing facing
        /// them. Called every frame of the scene rather than once, so a snare
        /// running out partway, which unlocks them, cannot hand control back
        /// early.
        /// </summary>
        private void Lock(PlayerMotor player)
        {
            if (input != null)
            {
                input.Locked = true;
            }

            enemy.VelocityOverride = Vector3.zero;

            // Facing fixed once the stab starts: the dagger is placed off it.
            if (player != null && stabClock < 0f && !lodged)
            {
                Vector3 toward = player.transform.position - transform.position;
                toward.y = 0f;

                if (toward.sqrMagnitude > 0.0001f)
                {
                    enemy.FacingOverride = toward;
                }
            }
        }

        private IEnumerator Wait(float seconds, PlayerMotor player)
        {
            for (float clock = 0f; clock < seconds; clock += Time.deltaTime)
            {
                Lock(player);
                yield return null;
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

        private IEnumerator Stab(PlayerMotor player)
        {
            chest = Find(chestBone);

            if (chest == null)
            {
                Debug.LogWarning($"[ChromatistPhaseTwo] {name} has no bone called \"{chestBone}\". Check the Chest Bone name.", this);
                yield break;
            }

            enemy.FacingOverride = enemy.Model.forward;
            SpawnDagger();
            trembleSeed = UnityEngine.Random.value * 100f;
            stabClock = 0f;

            if (dagger != null)
            {
                EnergyBurst.Create(HoverTip() - Vector3.up * summonRise, summonBurst);
            }

            bool hit = false;

            // The dagger is placed in LateUpdate, after the animator.
            while (stabClock < StabLength)
            {
                Lock(player);
                yield return null;
                stabClock += Time.deltaTime;

                if (!hit && stabClock >= ImpactTime)
                {
                    hit = true;
                    Impact();
                }
            }

            stabClock = -1f;
        }

        private void Impact()
        {
            Vector3 wound = Wound();
            Vector3 outward = enemy.Model.forward;

            EnergyBurst.Create(wound + outward * 0.1f, impactBurst);

            if (explosionRadius > 0f)
            {
                BlastSphere.Create(wound, explosionRadius, explosion);
            }

            TopDownCamera.Shake(impactShake, impactShakeDuration);

            if (impactFrames)
            {
                ImpactFrames frames = FindAnyObjectByType<ImpactFrames>();

                if (frames != null)
                {
                    frames.TriggerImpact(wound);
                }
            }

            // Left in the chest for good, moving with it.
            if (dagger != null)
            {
                PlaceDagger(PlungeEnd(), plungeAim, 0f);
                dagger.SetParent(chest, true);
                lodged = true;
                SetDaggerFade(1f);
            }

            AfterimageAura aura = GetComponent<AfterimageAura>();

            if (aura == null)
            {
                aura = gameObject.AddComponent<AfterimageAura>();
            }

            aura.Activate();
        }

        private Transform Find(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == boneName)
                {
                    return child;
                }
            }

            return null;
        }

        private void SpawnDagger()
        {
            if (daggerPrefab == null)
            {
                Debug.LogWarning($"[ChromatistPhaseTwo] {name} has no Dark Dagger prefab; the stab plays without one.", this);
                return;
            }

            dagger = Instantiate(daggerPrefab).transform;
            dagger.name = "Dark Dagger";
            dagger.localScale = daggerPrefab.transform.localScale * daggerScale;
            daggerRenderers = dagger.GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();

            // Art only: lodged in the chest, a collider would become part of
            // the boss's body.
            foreach (Collider part in dagger.GetComponentsInChildren<Collider>(true))
            {
                Destroy(part);
            }

            SetDaggerFade(0f);
        }

        private void LateUpdate()
        {
            if (stabClock < 0f || chest == null || dagger == null || lodged)
            {
                return;
            }

            float t = stabClock;

            Vector3 hover = HoverTip();
            Vector3 aim = (Wound() - hover).normalized;

            if (t < summonTime)
            {
                // Rises into place, tip down, turning its point on the chest.
                float e = Smooth(t / summonTime);
                Vector3 tip = hover - Vector3.up * (summonRise * (1f - e));
                Vector3 point = Vector3.Slerp(Vector3.down, aim, e).normalized;
                PlaceDagger(tip, point, summonSpin * (1f - e) * (1f - e));
                SetDaggerFade(Mathf.Clamp01(t / (summonTime * 0.7f)));
            }
            else if (t < PlungeStart)
            {
                float e = holdTime > 0f ? Smooth((t - summonTime) / holdTime) : 1f;
                float bob = Mathf.Sin(t * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
                Vector3 tip = hover - aim * (drawBack * e) + Vector3.up * bob + Tremble(t) * e;
                PlaceDagger(tip, aim, 0f);
                plungeFrom = tip;
                plungeAim = aim;
            }
            else
            {
                // No hold to have aimed it.
                if (plungeAim == Vector3.zero)
                {
                    plungeFrom = hover;
                    plungeAim = aim;
                }

                // Straight in, faster and faster.
                float u = Mathf.Clamp01((t - PlungeStart) / plungeTime);
                PlaceDagger(Vector3.Lerp(plungeFrom, PlungeEnd(), u * u), plungeAim, 0f);
            }
        }

        /// <summary>Where the tip ends up: past the wound along the aim it was given.</summary>
        private Vector3 PlungeEnd()
        {
            if (plungeAim == Vector3.zero)
            {
                plungeAim = -enemy.Model.forward;
            }

            return Wound() + plungeAim * stabDepth;
        }

        /// <summary>Puts the dagger's tip at <paramref name="tip"/>, pointing along <paramref name="point"/>, turned <paramref name="spin"/> degrees about the blade.</summary>
        private void PlaceDagger(Vector3 tip, Vector3 point, float spin)
        {
            // Edge up, as near as pointing along point allows.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, point);

            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.ProjectOnPlane(enemy.Model.forward, point);
            }

            Quaternion rotation = Quaternion.AngleAxis(spin, point)
                                  * Quaternion.LookRotation(point, up.normalized)
                                  * Quaternion.Euler(daggerRotation);
            dagger.SetPositionAndRotation(tip - point * tipDistance, rotation);
        }

        /// <summary>Where the blade goes in.</summary>
        private Vector3 Wound()
        {
            return chest.position + enemy.Model.TransformDirection(entryOffset);
        }

        private Vector3 HoverTip()
        {
            return Wound() + enemy.Model.TransformDirection(floatOffset);
        }

        private Vector3 Tremble(float t)
        {
            float speed = 18f;
            return new Vector3(Mathf.PerlinNoise(trembleSeed, t * speed) - 0.5f,
                               Mathf.PerlinNoise(trembleSeed + 7f, t * speed) - 0.5f,
                               Mathf.PerlinNoise(trembleSeed + 13f, t * speed) - 0.5f) * (tremble * 2f);
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private void SetDaggerFade(float visibility)
        {
            if (daggerRenderers == null)
            {
                return;
            }

            foreach (Renderer part in daggerRenderers)
            {
                if (part == null)
                {
                    continue;
                }

                Material material = part.sharedMaterial;

                if (material != null && material.HasProperty(FadeId))
                {
                    part.GetPropertyBlock(block);
                    block.SetFloat(FadeId, visibility);
                    part.SetPropertyBlock(block);
                }
                else
                {
                    // Not on a shader that can dissolve: just switch on halfway.
                    part.enabled = visibility > 0.5f;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform bone = chest != null ? chest : Find(chestBone);

            if (bone == null)
            {
                return;
            }

            Enemy owner = GetComponent<Enemy>();
            Transform model = owner != null ? owner.Model : transform;
            Vector3 wound = bone.position + model.TransformDirection(entryOffset);
            Vector3 hover = wound + model.TransformDirection(floatOffset);
            Vector3 aim = (wound - hover).normalized;

            Gizmos.color = new Color(1f, 0.1f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(wound, 0.08f);
            Gizmos.DrawLine(wound, wound + aim * stabDepth);

            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.9f);
            Gizmos.DrawWireSphere(hover, 0.12f);
            Gizmos.DrawLine(hover, hover - aim * tipDistance);
            Gizmos.DrawLine(hover, wound);
        }
    }
}
