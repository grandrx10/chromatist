using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's entrance: when the player walks into this trigger, the
    /// unseen boss speaks, its puppets appear round the player partway
    /// through, and once it has said its piece the fight begins.
    /// </summary>
    /// <remarks>
    /// Goes on the trigger box itself, which needs a trigger collider. Plays
    /// once.
    ///
    /// In order:
    ///   1. The player's controls lock.
    ///   2. The boss is put out of sight, dormant: at <see cref="bossPoint"/>,
    ///      or <see cref="hideDepth"/> below the arena. It is never seen in
    ///      its first phase, so it is kept somewhere nothing can find it.
    ///   3. After <see cref="dialogueDelay"/>, the dialogue plays through the
    ///      <see cref="DialogueBox"/>, click by click. Once the player clicks
    ///      past line <see cref="puppetsAfterLine"/>, <see cref="puppetCount"/>
    ///      puppets are placed at valid spots in the <see cref="arena"/>
    ///      between <see cref="puppetMinRadius"/> and
    ///      <see cref="puppetMaxRadius"/> of the player, clear of each other.
    ///      One with a <see cref="PuppetDescent"/> drops in on its strings at
    ///      once and hangs limp after landing; one without simply stands
    ///      there, dormant.
    ///   4. Controls unlock, the <see cref="BossHealthBar"/> appears, and the
    ///      boss and every puppet wake. A puppet still on its way down wakes
    ///      once it has landed and its Inactive Time is up.
    ///
    /// The boss is either an object in the scene with an <see cref="Enemy"/>,
    /// switched off until step 2, or a prefab spawned then. It is handed the
    /// arena, as its <see cref="Enemy.Arena"/> and <see cref="Enemy.ArenaCentre"/>,
    /// for its eyes and summons to appear in.
    /// </remarks>
    public sealed class PuppeteerIntro : MonoBehaviour
    {
        [Header("Start")]
        [SerializeField]
        [Tooltip("Seconds between the player walking in and the first line of dialogue.")]
        [Min(0f)]
        private float dialogueDelay = 0.5f;

        [Header("Boss")]
        [SerializeField]
        [Tooltip("The boss in the scene, or a boss prefab to spawn when the player walks in.")]
        private Enemy boss;

        [SerializeField]
        [Tooltip("Name shown over the boss health bar.")]
        private string bossName = "The Puppeteer";

        [SerializeField]
        [Tooltip("Where the boss is kept while it fights unseen, out of the player's sight. Empty puts it Hide Depth below the middle of the arena.")]
        private Transform bossPoint;

        [SerializeField]
        [Tooltip("How far below the arena the boss is kept when no Boss Point is set.")]
        [Min(0f)]
        private float hideDepth = 200f;

        [SerializeField]
        [Tooltip("The arena. Puppets and eyes only appear inside it.")]
        private ArenaBounds arena;

        [Header("Puppets")]
        [SerializeField]
        [Tooltip("Enemies to pick from, with how likely each is.")]
        private List<HammerSummonPattern.SummonOption> puppets = new List<HammerSummonPattern.SummonOption>();

        [SerializeField]
        [Tooltip("How many puppets appear.")]
        [Min(0)]
        private int puppetCount = 4;

        [SerializeField]
        [Tooltip("Closest to the player a puppet can appear.")]
        [Min(0f)]
        private float puppetMinRadius = 4f;

        [SerializeField]
        [Tooltip("Furthest from the player a puppet can appear. If nowhere in this ring will do, it appears anywhere in the arena.")]
        [Min(0f)]
        private float puppetMaxRadius = 8f;

        [Header("Dialogue")]
        [SerializeField]
        [Tooltip("The puppets appear once the player clicks past this line. 1 is the first line; 0 brings them in as the dialogue opens; past the last line, as it closes.")]
        [Min(0)]
        private int puppetsAfterLine = 1;

        [SerializeField] private List<DialogueLine> dialogue = new List<DialogueLine>();

        private bool played;
        private PlayerInputReader input;
        private Enemy fighter;
        private readonly List<Enemy> spawned = new List<Enemy>();

        /// <summary>The boss taking part, once the intro has started; null before.</summary>
        public Enemy Boss => fighter;

        /// <summary>Raised the moment the player walks in and the intro starts.</summary>
        public event Action<PuppeteerIntro> Triggered;

        /// <summary>Raised once the dialogue is over and the fight begins.</summary>
        public event Action<PuppeteerIntro> FightStarted;

        /// <summary>Raised when the boss dies.</summary>
        public event Action<PuppeteerIntro> Defeated;

        private bool BossInScene => boss != null && boss.gameObject.scene.IsValid();

        private void Awake()
        {
            if (boss == null || arena == null)
            {
                Debug.LogWarning($"[PuppeteerIntro] {name} needs a boss and an arena.", this);
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
            if (played || boss == null || arena == null)
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
            StartCoroutine(Play(player.transform));
            Triggered?.Invoke(this);
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

            SpawnBoss();

            if (dialogueDelay > 0f)
            {
                yield return new WaitForSeconds(dialogueDelay);
            }

            bool talking = true;
            Say(0, player, () => talking = false);

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

            // A puppet on strings wakes itself once let go: straight away if
            // it has landed, or once it does.
            foreach (Enemy puppet in spawned)
            {
                PuppetSummoning.Release(puppet);
            }

            fighter.StayDormant = false;
            fighter.Wake();
            FightStarted?.Invoke(this);
        }

        private void SpawnBoss()
        {
            Vector3 spot = bossPoint != null ? bossPoint.position : arena.Centre + Vector3.down * hideDepth;

            if (BossInScene)
            {
                fighter = boss;
                fighter.transform.position = spot;
            }
            else
            {
                fighter = Instantiate(boss, spot, boss.transform.rotation);
                fighter.name = boss.name;
            }

            fighter.StayDormant = true;
            fighter.ArenaCentre = arena.Centre;
            fighter.Arena = arena;

            fighter.gameObject.SetActive(true);
            fighter.Health.Died += OnBossDied;
        }

        private void SpawnPuppets(Transform player)
        {
            for (int i = 0; i < puppetCount; i++)
            {
                Enemy prefab = HammerSummonPattern.Pick(puppets);

                if (prefab == null)
                {
                    return;
                }

                // Drops in now, but stays limp until the fight begins.
                Enemy unit = PuppetSummoning.Summon(prefab, arena, player.position, puppetMinRadius, puppetMaxRadius,
                                                    spawned, true, this);

                if (unit != null)
                {
                    spawned.Add(unit);
                }
            }
        }

        /// <summary>
        /// Says the lines from <paramref name="index"/> on, bringing the
        /// puppets in at their line, then calls <paramref name="done"/>.
        /// </summary>
        private void Say(int index, Transform player, Action done)
        {
            // Lines are numbered from 1, so after line n is index n.
            if (index == Mathf.Min(puppetsAfterLine, dialogue.Count))
            {
                SpawnPuppets(player);
            }

            if (index >= dialogue.Count)
            {
                done();
                return;
            }

            DialogueBox.Instance.Say(dialogue[index], () => Say(index + 1, player, done));
        }
    }
}
