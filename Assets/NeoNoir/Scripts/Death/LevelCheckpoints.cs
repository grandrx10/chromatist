using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A level's two respawn points, the start of its minion part and the
    /// start of its boss part, and the save of which one the player has
    /// reached.
    /// </summary>
    /// <remarks>
    /// One per level scene (L1 to L5; not L4.2, which counts as L4's boss
    /// fight and so saves nothing). When the level starts it saves the
    /// minion part; the moment <see cref="bossFight"/> is triggered it saves
    /// the boss part (see <see cref="RespawnSave"/>). With no boss fight set,
    /// the whole level is one part, as in levels 4 and 5.
    ///
    /// When the level is loaded as a respawn, through the
    /// <see cref="AfterworldDoor"/>, the player is moved to the saved
    /// part's spawn point. A boss part respawn also switches off every
    /// object in <see cref="skippedMinions"/>, and lights every altar in
    /// <see cref="altarsLit"/> as if the player had struck it out, so the
    /// summoning is ready to go (L2's witch).
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class LevelCheckpoints : MonoBehaviour
    {
        [Header("Opening")]
        [SerializeField]
        [Tooltip("When the level starts fresh (not through the Afterworld door), the player rises out of a white circle where they stand, as in the Afterworld. For level 1, which has no door.")]
        private bool riseOnFreshStart;

        [Header("Minion part")]
        [SerializeField]
        [Tooltip("Where the player respawns after dying before the boss fight. Empty: where the player is placed in the scene.")]
        private Transform levelSpawn;

        [Header("Boss part")]
        [SerializeField]
        [Tooltip("A Boss Intro or Puppeteer Intro. When it is triggered, the respawn point becomes the boss spawn. Empty: the whole level is the boss fight.")]
        private MonoBehaviour bossFight;

        [SerializeField]
        [Tooltip("Where the player respawns after dying once the boss fight has started. Need not be at the boss.")]
        private Transform bossSpawn;

        [SerializeField]
        [Tooltip("Minions (or anything else) switched off when respawning at the boss spawn, so they are not fought again.")]
        private List<GameObject> skippedMinions = new List<GameObject>();

        [SerializeField]
        [Tooltip("Altars lit straight away when respawning at the boss spawn.")]
        private List<Altar> altarsLit = new List<Altar>();

        private string sceneName;
        private bool respawning;
        private bool atBoss;
        private ReturnCircle circle;
        private BossIntro bossIntro;
        private PuppeteerIntro puppeteerIntro;

        private void Awake()
        {
            sceneName = gameObject.scene.name;
            // Dragging in the object picks its first script, which may be
            // something beside the intro (L2's Summoning Altar), so the
            // intro is looked for on the same object too.
            GameObject host = bossFight != null ? bossFight.gameObject : null;
            bossIntro = bossFight as BossIntro ?? (host != null ? host.GetComponent<BossIntro>() : null);
            puppeteerIntro = bossFight as PuppeteerIntro ?? (host != null ? host.GetComponent<PuppeteerIntro>() : null);

            if (bossFight != null && bossIntro == null && puppeteerIntro == null)
            {
                Debug.LogWarning($"[LevelCheckpoints] {bossFight.name}'s {bossFight.GetType().Name} is not a Boss Intro or Puppeteer Intro, so the boss part never starts.", this);
            }

            respawning = RespawnSave.ConsumeRespawn(sceneName);
            atBoss = respawning && RespawnSave.AtBoss && HasBossPart;

            // Came back through a return circle (from the Afterworld, or by
            // dying after using one): back onto that circle.
            circle = respawning ? ReturnCircle.Find(RespawnSave.Circle) : null;

            if (atBoss)
            {
                SwitchOff(skippedMinions);
            }

            if (circle != null)
            {
                SwitchOff(circle.SkippedMinions);
            }
        }

        private static readonly HashSet<GameObject> skipped = new HashSet<GameObject>();

        /// <summary>True if <paramref name="target"/> was switched off on a respawn, as good as killed.</summary>
        public static bool WasSkipped(GameObject target)
        {
            return target != null && skipped.Contains(target);
        }

        private static void SwitchOff(IReadOnlyList<GameObject> objects)
        {
            foreach (GameObject target in objects)
            {
                if (target != null)
                {
                    target.SetActive(false);
                    skipped.Add(target);
                }
            }
        }

        private void OnDestroy()
        {
            skipped.RemoveWhere(target => target == null || target.scene == gameObject.scene);
        }

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            skipped.Clear();
        }

        private bool HasBossPart => bossIntro != null || puppeteerIntro != null;

        private void OnEnable()
        {
            if (bossIntro != null)
            {
                bossIntro.Triggered += OnBossIntroTriggered;
            }

            if (puppeteerIntro != null)
            {
                puppeteerIntro.Triggered += OnPuppeteerIntroTriggered;
            }
        }

        private void OnDisable()
        {
            if (bossIntro != null)
            {
                bossIntro.Triggered -= OnBossIntroTriggered;
            }

            if (puppeteerIntro != null)
            {
                puppeteerIntro.Triggered -= OnPuppeteerIntroTriggered;
            }
        }

        private void Start()
        {
            if (!respawning && riseOnFreshStart)
            {
                PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (player != null)
                {
                    DeathSequence.Rise(player.transform, PlayerDeath.SettingsOf(player));
                }
            }

            if (circle != null)
            {
                PlacePlayer(circle.SpawnPosition);
            }
            else if (respawning)
            {
                Transform spawn = atBoss ? bossSpawn : levelSpawn;

                if (spawn != null)
                {
                    PlacePlayer(spawn.position);
                }
            }

            if (atBoss)
            {
                foreach (Altar altar in altarsLit)
                {
                    // Drained without a blow, so no damage numbers; dying
                    // lights it the same way striking it out would.
                    if (altar != null && altar.Health != null && !altar.IsLit)
                    {
                        altar.Health.TakeLinkedDamage(altar.Health.MaxHealth);
                    }
                }
            }

            if (circle != null)
            {
                RespawnSave.SetCircle(sceneName, atBoss, circle.Id);
            }
            else
            {
                RespawnSave.Set(sceneName, atBoss);
            }
        }

        private void OnBossIntroTriggered(BossIntro intro)
        {
            RespawnSave.Set(sceneName, true);
        }

        private void OnPuppeteerIntroTriggered(PuppeteerIntro intro)
        {
            RespawnSave.Set(sceneName, true);
        }

        private static void PlacePlayer(Vector3 spawn)
        {
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (player == null)
            {
                return;
            }

            player.Teleport(spawn);

            TopDownCamera view = FindFirstObjectByType<TopDownCamera>();

            if (view != null)
            {
                view.SnapToTarget();
            }
        }

        private void OnDrawGizmos()
        {
            DrawSpawn(levelSpawn, new Color(0.4f, 0.8f, 1f, 0.9f));
            DrawSpawn(bossSpawn, new Color(1f, 0.3f, 0.25f, 0.9f));
        }

        private static void DrawSpawn(Transform spawn, Color colour)
        {
            if (spawn == null)
            {
                return;
            }

            Gizmos.color = colour;
            Gizmos.DrawWireSphere(spawn.position, 0.6f);
            Gizmos.DrawLine(spawn.position, spawn.position + Vector3.up * 2f);
        }
    }
}
