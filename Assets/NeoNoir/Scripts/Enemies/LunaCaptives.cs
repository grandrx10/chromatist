using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Luna takes the player's weapons hostage, and the player wins them back
    /// one at a time: the first for free, the rest by beating each in a fight
    /// of its own. Once every weapon is dealt with, Luna's own fight begins.
    /// </summary>
    /// <remarks>
    /// Started by <see cref="LunaAwakening"/> once she has landed after the
    /// explosion. Goes on any object in the scene. In order:
    ///   1. Luna floats over to just in front of the player.
    ///   2. The player's blades fade away, abilities and all, and every
    ///      weapon in <see cref="weapons"/> materialises in a ring round the
    ///      player, equipped or not.
    ///   3. A glowing rope (<see cref="WeaponChain"/>) grows out from Luna to
    ///      each, in the weapon's own colour.
    ///   4. She trembles and grows to her full size.
    ///   5. She pulls the weapons away from the player into an orbit round
    ///      her, where they stay, chained, until their turn.
    ///   6. <see cref="opening"/>, then <see cref="firstQuestion"/>: a choice
    ///      among the weapons the player owns. The one picked strains toward
    ///      the player, snaps its rope, flies into the player's orbit as the
    ///      main blade, and its save dialogue plays. With none owned, this
    ///      step is skipped.
    ///   7. <see cref="nextQuestion"/>: a choice among every weapon still
    ///      held, owned or not. Luna retreats to where she landed, and the
    ///      weapon picked rises <see cref="ascendHeight"/> straight up out of
    ///      her orbit, still chained, and becomes its boss prefab up there,
    ///      with its own health bar. Its attack drops it into the arena.
    ///      Controls come back for the fight.
    ///   8. Beaten, it turns back into the weapon and its rope snaps. Owned,
    ///      it flies to the player as the next secondary and its save
    ///      dialogue plays; not owned, it shatters and fades, with no
    ///      dialogue. Back to 7 until none are left.
    ///   9. Luna can be hurt from here, and <see cref="lunaIntro"/> starts
    ///      her own fight.
    ///
    /// Controls are locked throughout, except during the weapons' fights.
    ///
    /// The weapons, until they become bosses, are plain copies of the
    /// player's blades, <see cref="Blade.Seized"/> so they never act.
    /// A boss prefab's pivot goes on the floor under the weapon; it is
    /// spawned on the floor beneath where the weapon flew to, turned toward
    /// the player, and should show the weapon about
    /// <see cref="orbitHeight"/> up, so the swap is seamless. A weapon with no
    /// boss prefab skips its fight. Whatever its prefab does on death, the
    /// boss is removed once it dies.
    ///
    /// Ownership is a checkbox per weapon for now, until there is an unlock
    /// system to ask.
    ///
    /// On <see cref="lunaIntro"/>: turn Start On Trigger off, leave Boss and
    /// Landing Point empty, Landing Height Offset at 0, and clear Entrance
    /// Animation unless she has one. Her pre-fight dialogue goes on it.
    /// </remarks>
    public sealed class LunaCaptives : MonoBehaviour
    {
        [Serializable]
        public sealed class Weapon
        {
            public BladeDefinition blade;

            [Tooltip("What it becomes for its fight: art plus Enemy, EnemyHealth and its attack patterns. Pivot on the floor. Empty skips its fight.")]
            public Enemy bossPrefab;

            [Tooltip("Whether the player has this weapon. Only owned weapons can be the first, free save; one not owned shatters when beaten.")]
            public bool owned = true;

            [Tooltip("Played when it is saved, whether chosen first or won back in its fight, while the blade is below level 3 in the Afterworld.")]
            public List<DialogueLine> saveDialogue = new List<DialogueLine>();

            [Tooltip("Played instead of Save Dialogue when the blade is level 3 or higher. Empty uses Save Dialogue.")]
            public List<DialogueLine> levelThreeSaveDialogue = new List<DialogueLine>();

            /// <summary>The save dialogue for the blade's level in <see cref="BladeUnlocks"/>.</summary>
            public List<DialogueLine> SaveDialogueForLevel =>
                BladeUnlocks.LevelOf(blade) >= 3 && levelThreeSaveDialogue.Count > 0 ? levelThreeSaveDialogue : saveDialogue;

            [Tooltip("Colour of its rope. Leave alpha at 0 to take the weapon's own colour.")]
            [ColorUsage(true, true)]
            public Color chainColour = new Color(0f, 0f, 0f, 0f);
        }

        private enum State
        {
            Held,
            Orbiting,
            Busy,
            Fighting,
            Done,
        }

        private sealed class Captive
        {
            public Weapon Weapon;
            public Blade Blade;
            public WeaponChain Chain;
            public Color Colour;
            public State State;

            // Where it waited round the player, and how far it has been
            // pulled from there into Luna's orbit.
            public Vector3 HeldPosition;
            public Quaternion HeldRotation;
            public float Pull;

            // Its place round Luna, in degrees, easing toward an even share
            // as others leave.
            public float Slot;

            public string Name => Weapon.blade.DisplayName;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Weapons")]
        [SerializeField]
        [Tooltip("Every weapon Luna takes, in the order they sit round her and appear in the choices.")]
        private List<Weapon> weapons = new List<Weapon>();

        [SerializeField]
        [Tooltip("Starts Luna's own fight once every weapon is dealt with.")]
        private BossIntro lunaIntro;

        [Header("Dialogue")]
        [SerializeField]
        [Tooltip("Once the weapons are in her orbit, before the first choice.")]
        private List<DialogueLine> opening = new List<DialogueLine>();

        [SerializeField]
        [Tooltip("The first choice comes up under this: which owned weapon to save for free.")]
        private DialogueLine firstQuestion = new DialogueLine();

        [SerializeField]
        [Tooltip("Every later choice comes up under this: which weapon to save next.")]
        private DialogueLine nextQuestion = new DialogueLine();

        [Header("Luna approaches")]
        [SerializeField]
        [Tooltip("How far in front of the player she stops.")]
        [Min(0f)]
        private float approachDistance = 3.5f;

        [SerializeField]
        [Min(0.01f)]
        private float approachTime = 1.2f;

        [Header("Weapons appear")]
        [SerializeField]
        [Tooltip("Distance from the player the weapons appear at.")]
        [Min(0f)]
        private float ringRadius = 2f;

        [SerializeField]
        [Tooltip("Height above the floor they appear at.")]
        private float ringHeight = 1.2f;

        [SerializeField]
        [Min(0.01f)]
        private float appearTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the player's own blades take to fade away.")]
        [Min(0.01f)]
        private float disarmTime = 0.3f;

        [Header("Chains")]
        [SerializeField]
        private WeaponChain.Settings chain = new WeaponChain.Settings();

        [SerializeField]
        [Tooltip("Seconds each rope takes to reach its weapon.")]
        [Min(0.01f)]
        private float bindTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds between one rope setting out and the next.")]
        [Min(0f)]
        private float bindStagger = 0.08f;

        [SerializeField]
        [Tooltip("The burst where a rope snaps. Its colour is replaced by the weapon's.")]
        private EnergyBurst.Settings snapBurst = new EnergyBurst.Settings
        {
            flashSize = 1.2f,
            flashTime = 0.2f,
            sparkCount = 14,
            sparkSize = 0.2f,
            sparkSpeed = new Vector2(5f, 10f),
            sparkLife = 0.4f,
        };

        [SerializeField]
        [Tooltip("Brightness the weapon's colour is multiplied by for its burst and shards.")]
        [Min(0f)]
        private float burstIntensity = 3f;

        [Header("Growth")]
        [SerializeField]
        [Tooltip("Seconds she trembles and draws in before growing.")]
        [Min(0f)]
        private float trembleTime = 0.6f;

        [SerializeField]
        [Tooltip("How far she shakes at the height of the tremble.")]
        [Min(0f)]
        private float trembleAmount = 0.06f;

        [SerializeField]
        [Tooltip("Fraction of her size she draws in by while trembling.")]
        [Range(0f, 0.5f)]
        private float drawIn = 0.12f;

        [SerializeField]
        [Tooltip("Seconds the growth takes.")]
        [Min(0.01f)]
        private float growTime = 0.5f;

        [SerializeField]
        [Tooltip("How far past full size she swells before settling, as a fraction of the growth. 0 for none.")]
        [Range(0f, 0.5f)]
        private float overshoot = 0.12f;

        [SerializeField]
        [Tooltip("How far the camera shakes as she reaches full size. 0 disables it.")]
        [Min(0f)]
        private float growShake = 0.8f;

        [SerializeField]
        [Min(0.01f)]
        private float growShakeDuration = 0.5f;

        [Header("Orbit")]
        [SerializeField]
        [Tooltip("Seconds she takes to pull the weapons from the player into her orbit.")]
        [Min(0.01f)]
        private float pullTime = 1f;

        [SerializeField]
        [Tooltip("Distance from her the weapons orbit at.")]
        [Min(0f)]
        private float orbitRadius = 3.5f;

        [SerializeField]
        [Tooltip("Height above the floor the weapons orbit at.")]
        private float orbitHeight = 1.5f;

        [SerializeField]
        [Tooltip("Degrees per second round her.")]
        private float orbitSpeed = 40f;

        [SerializeField]
        [Tooltip("Degrees per second the rest close up the gap one leaves.")]
        [Min(0f)]
        private float respaceSpeed = 60f;

        [Header("First save")]
        [SerializeField]
        [Tooltip("Seconds the chosen weapon strains toward the player before its rope snaps.")]
        [Min(0.01f)]
        private float struggleTime = 1.2f;

        [SerializeField]
        [Tooltip("How far it pulls out of her orbit toward the player while straining.")]
        [Min(0f)]
        private float struggleReach = 1.5f;

        [SerializeField]
        [Tooltip("How far it, and its rope, shudder at the height of the strain.")]
        [Min(0f)]
        private float struggleShake = 0.15f;

        [SerializeField]
        [Tooltip("Seconds a saved weapon takes to fly into the player's orbit.")]
        [Min(0.01f)]
        private float returnTime = 0.8f;

        [SerializeField]
        [Tooltip("How high a saved weapon arcs on its way to the player.")]
        [Min(0f)]
        private float returnArc = 1.5f;

        [Header("Fights")]
        [SerializeField]
        [Tooltip("Seconds Luna takes to retreat to where she landed when she sends a weapon out.")]
        [Min(0.01f)]
        private float retreatTime = 1.2f;

        [SerializeField]
        [Tooltip("How far a weapon rises straight up out of her orbit before it turns into its boss, which then drops into the arena.")]
        [Min(0f)]
        private float ascendHeight = 12f;

        [SerializeField]
        [Tooltip("Seconds the rise takes.")]
        [FormerlySerializedAs("deployTime")]
        [Min(0.01f)]
        private float ascendTime = 0.8f;

        [SerializeField]
        [Tooltip("Health each weapon fought has over the one fought before it. The first keeps its prefab's own.")]
        [Min(0f)]
        private float healthPerFight = 200f;

        [Header("Shatter")]
        [SerializeField]
        [Tooltip("Seconds a weapon not owned trembles before it shatters.")]
        [Min(0f)]
        private float crackTime = 0.3f;

        [SerializeField]
        [Tooltip("Seconds what is left of it takes to fade.")]
        [Min(0.01f)]
        private float shatterFadeTime = 0.4f;

        [SerializeField]
        private HammerImpact.Settings shards = new HammerImpact.Settings
        {
            ringRadius = 0.01f,
            ringWidth = 0.01f,
            ringTime = 0.05f,
            ringIntensity = 0f,
            debrisCount = 14,
            debrisSize = 0.18f,
            debrisUpSpeed = new Vector2(2f, 6f),
            debrisOutSpeed = new Vector2(2f, 6f),
            debrisTime = 0.8f,
            debrisIntensity = 2f,
        };

        private readonly List<Captive> captives = new List<Captive>();

        private Enemy luna;
        private PlayerMotor player;
        private PlayerInputReader input;
        private BladeArsenal arsenal;

        private float floor;
        private Vector3 home;
        private float spin;
        private bool begun;

        // The last weapon fought's health, which the next one builds on.
        private float? lastHealth;

        /// <summary>The middle of Luna's arena if she has one, otherwise the floor under where she landed.</summary>
        private Vector3 FightCentre => luna.Arena != null ? luna.Arena.BoundsCentre : new Vector3(home.x, floor, home.z);

        /// <summary>
        /// Takes over from <see cref="LunaAwakening"/>: <paramref name="boss"/>
        /// has landed on a floor at <paramref name="floorHeight"/>, at her
        /// small size, and grows to <paramref name="fullScale"/>.
        /// </summary>
        public void Begin(Enemy boss, PlayerMotor playerMotor, float floorHeight, Vector3 fullScale)
        {
            if (begun || boss == null || playerMotor == null)
            {
                return;
            }

            begun = true;
            luna = boss;
            player = playerMotor;
            input = player.GetComponent<PlayerInputReader>();
            arsenal = player.GetComponent<BladeArsenal>();
            floor = floorHeight;
            home = boss.transform.position;

            if (boss.Arena == null)
            {
                Debug.LogWarning("[LunaCaptives] Luna has no arena, so the weapons fight round where she landed. "
                                 + "Drag the ArenaBounds into Luna Awakening's Arena slot.", this);
            }

            StartCoroutine(Play(fullScale));
        }

        /// <summary>
        /// For the dev shortcut: skips every weapon and starts Luna's own
        /// fight at once. <paramref name="boss"/> is already full size and on
        /// a floor at <paramref name="floorHeight"/>; the player keeps the
        /// blades they hold.
        /// </summary>
        public void BeginLunaFight(Enemy boss, PlayerMotor playerMotor, float floorHeight)
        {
            if (begun || boss == null || playerMotor == null)
            {
                return;
            }

            begun = true;
            luna = boss;
            player = playerMotor;
            input = player.GetComponent<PlayerInputReader>();
            arsenal = player.GetComponent<BladeArsenal>();
            floor = floorHeight;
            home = boss.transform.position;

            StartLunaFight();
        }

        private IEnumerator Play(Vector3 fullScale)
        {
            Lock(true);

            yield return Approach();
            yield return Materialise();
            yield return Bind();
            yield return Grow(fullScale);
            yield return PullAway();
            yield return Say(opening);

            List<Captive> owned = captives.FindAll(captive => captive.Weapon.owned);

            if (owned.Count > 0)
            {
                Captive first = null;
                yield return Choose(firstQuestion, owned, picked => first = picked);
                DialogueBox.Instance.Close();

                yield return Struggle(first);
                yield return Save(first);
            }

            while (true)
            {
                List<Captive> held = captives.FindAll(captive => captive.State == State.Orbiting);

                if (held.Count == 0)
                {
                    break;
                }

                Captive next = null;
                yield return Choose(nextQuestion, held, picked => next = picked);
                DialogueBox.Instance.Close();

                yield return Fight(next);

                if (next.Weapon.owned)
                {
                    yield return Save(next);
                }
                else
                {
                    yield return Shatter(next);
                }
            }

            DialogueBox.Instance.Close();
            StartLunaFight();
        }

        private void Update()
        {
            if (luna == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            spin = Mathf.Repeat(spin + orbitSpeed * deltaTime, 360f);

            int count = 0;

            foreach (Captive captive in captives)
            {
                if (captive.State == State.Orbiting)
                {
                    count++;
                }
            }

            int index = 0;

            foreach (Captive captive in captives)
            {
                if (captive.State != State.Orbiting)
                {
                    continue;
                }

                float share = index * 360f / count;
                index++;

                captive.Slot = Mathf.MoveTowardsAngle(captive.Slot, share, respaceSpeed * deltaTime);
                OrbitPose(captive.Slot, out Vector3 position, out Quaternion rotation);

                float pull = Smooth(captive.Pull);
                captive.Blade.transform.SetPositionAndRotation(Vector3.Lerp(captive.HeldPosition, position, pull),
                                                               Quaternion.Slerp(captive.HeldRotation, rotation, pull));
            }
        }

        /// <summary>A spot round Luna, <paramref name="slot"/> degrees on from the orbit's turning start, flat turned toward her, tip down.</summary>
        private void OrbitPose(float slot, out Vector3 position, out Quaternion rotation)
        {
            Vector3 centre = new Vector3(luna.transform.position.x, floor + orbitHeight, luna.transform.position.z);
            Vector3 radial = Quaternion.Euler(0f, spin + slot, 0f) * Vector3.forward;

            position = centre + radial * orbitRadius;
            rotation = Quaternion.LookRotation(-radial, Vector3.up);
        }

        // ---- the taking ----------------------------------------------------

        private IEnumerator Approach()
        {
            Vector3 start = luna.transform.position;
            Vector3 away = Flat(start - player.transform.position);

            if (away.sqrMagnitude < 0.0001f)
            {
                away = player.transform.forward;
            }

            Vector3 end = player.transform.position + away.normalized * approachDistance;
            end.y = start.y;

            yield return MoveLuna(end, approachTime);
        }

        private IEnumerator Materialise()
        {
            if (arsenal != null)
            {
                StartCoroutine(FadeAway(arsenal.Disarm(), disarmTime));
            }

            int count = weapons.Count;
            Vector3 centre = player.transform.position;
            centre.y = floor + ringHeight;

            for (int i = 0; i < count; i++)
            {
                Weapon weapon = weapons[i];

                if (weapon == null || weapon.blade == null || weapon.blade.Prefab == null)
                {
                    Debug.LogWarning($"[LunaCaptives] Weapon {i} on {name} has no blade definition with a prefab; left out.", this);
                    continue;
                }

                Vector3 radial = Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward;
                Vector3 position = centre + radial * ringRadius;
                Quaternion rotation = Quaternion.LookRotation(-radial, Vector3.up);

                Blade blade = Instantiate(weapon.blade.Prefab, position, rotation);
                blade.name = weapon.blade.DisplayName;
                blade.Seized = true;
                blade.transform.SetPositionAndRotation(position, rotation);
                blade.Visibility = 0f;

                captives.Add(new Captive
                {
                    Weapon = weapon,
                    Blade = blade,
                    Colour = weapon.chainColour.a > 0f ? weapon.chainColour : BladeColour(blade),
                    State = State.Held,
                    HeldPosition = position,
                    HeldRotation = rotation,
                });
            }

            for (float clock = 0f; clock < appearTime; clock += Time.deltaTime)
            {
                foreach (Captive captive in captives)
                {
                    captive.Blade.Visibility = clock / appearTime;
                }

                yield return null;
            }

            foreach (Captive captive in captives)
            {
                captive.Blade.Visibility = 1f;
            }
        }

        private IEnumerator Bind()
        {
            foreach (Captive captive in captives)
            {
                captive.Chain = WeaponChain.Create(luna.transform, captive.Blade.transform, captive.Colour, chain);
                captive.Chain.Reach = 0f;
            }

            float total = bindTime + bindStagger * Mathf.Max(0, captives.Count - 1);

            for (float clock = 0f; clock < total; clock += Time.deltaTime)
            {
                for (int i = 0; i < captives.Count; i++)
                {
                    captives[i].Chain.Reach = Smooth((clock - i * bindStagger) / bindTime);
                }

                yield return null;
            }

            foreach (Captive captive in captives)
            {
                captive.Chain.Reach = 1f;
            }
        }

        private IEnumerator Grow(Vector3 full)
        {
            Transform body = luna.transform;
            Vector3 position = body.position;
            Vector3 start = body.localScale;
            float drawnIn = 1f - drawIn;

            for (float clock = 0f; clock < trembleTime; clock += Time.deltaTime)
            {
                float t = clock / trembleTime;

                // Builds as it goes, the way something straining would.
                body.position = position + UnityEngine.Random.insideUnitSphere * (trembleAmount * t);
                body.localScale = start * Mathf.Lerp(1f, drawnIn, Smooth(t));
                yield return null;
            }

            body.position = position;

            for (float clock = 0f; clock < growTime; clock += Time.deltaTime)
            {
                body.localScale = Vector3.LerpUnclamped(start * drawnIn, full, BackOut(clock / growTime));
                yield return null;
            }

            body.localScale = full;

            if (growShake > 0f)
            {
                TopDownCamera.Shake(growShake, growShakeDuration);
            }
        }

        private IEnumerator PullAway()
        {
            for (int i = 0; i < captives.Count; i++)
            {
                captives[i].State = State.Orbiting;
                captives[i].Slot = i * 360f / captives.Count;
                captives[i].Pull = 0f;
            }

            for (float clock = 0f; clock < pullTime; clock += Time.deltaTime)
            {
                float t = clock / pullTime;

                foreach (Captive captive in captives)
                {
                    captive.Pull = t;

                    // Yanked taut, slackening as they come in.
                    captive.Chain.Tautness = 1f - Smooth(t);
                }

                yield return null;
            }

            foreach (Captive captive in captives)
            {
                captive.Pull = 1f;
                captive.Chain.Tautness = 0f;
            }
        }

        // ---- the saving ----------------------------------------------------

        /// <summary>Asks which of <paramref name="among"/> to save, under <paramref name="question"/>.</summary>
        private IEnumerator Choose(DialogueLine question, List<Captive> among, Action<Captive> picked)
        {
            List<string> labels = among.ConvertAll(captive => captive.Name);
            int choice = -1;

            DialogueBox.Instance.Ask(question, labels, index => choice = index);

            while (choice < 0)
            {
                yield return null;
            }

            picked(among[choice]);
        }

        /// <summary>The first save: it strains toward the player until its rope snaps.</summary>
        private IEnumerator Struggle(Captive captive)
        {
            captive.State = State.Busy;
            captive.Chain.Tautness = 1f;

            Transform blade = captive.Blade.transform;
            Vector3 start = blade.position;
            Quaternion rotation = blade.rotation;
            Vector3 toward = Flat(player.transform.position - start);
            toward = toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.forward;

            for (float clock = 0f; clock < struggleTime; clock += Time.deltaTime)
            {
                float t = clock / struggleTime;
                Vector3 shake = UnityEngine.Random.insideUnitSphere * (struggleShake * t);

                blade.SetPositionAndRotation(start + toward * (struggleReach * Smooth(t)) + shake, rotation);
                captive.Chain.Strain = struggleShake * t;
                yield return null;
            }

            SnapChain(captive);
        }

        /// <summary>Flies a freed weapon into the player's orbit, equips it, and plays its save dialogue.</summary>
        private IEnumerator Save(Captive captive)
        {
            captive.State = State.Busy;

            Blade blade = captive.Blade;
            Vector3 start = blade.transform.position;
            Quaternion startRotation = blade.transform.rotation;
            float phase = arsenal != null ? arsenal.NextOrbitPhase : 0f;

            for (float clock = 0f; clock < returnTime; clock += Time.deltaTime)
            {
                float t = clock / returnTime;
                float eased = Smooth(t);

                // Aimed at where it will rest this very frame, so it lands in
                // its place in the orbit with nothing left to catch up.
                blade.OrbitPose(player.transform.position, phase, out Vector3 target, out Quaternion targetRotation);
                Vector3 position = Vector3.Lerp(start, target, eased) + Vector3.up * (4f * returnArc * t * (1f - t));

                blade.transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, targetRotation, eased));
                yield return null;
            }

            captive.State = State.Done;

            if (arsenal != null)
            {
                arsenal.Adopt(blade, captive.Weapon.blade);
            }

            yield return Say(captive.Weapon.SaveDialogueForLevel);
        }

        /// <summary>
        /// Luna retreats home and sends the weapon out to fight as its boss;
        /// returns once it is beaten, turned back into the weapon where the
        /// boss fell, its rope snapped.
        /// </summary>
        private IEnumerator Fight(Captive captive)
        {
            captive.State = State.Busy;

            Vector3 homeFloor = new Vector3(home.x, floor, home.z);
            Vector3 toward = Flat(player.transform.position - homeFloor);
            toward = toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.forward;

            // The weapon rests as in her orbit, flat turned toward her; the
            // boss faces the player.
            Quaternion resting = Quaternion.LookRotation(-toward, Vector3.up);
            Quaternion facing = Quaternion.LookRotation(toward, Vector3.up);

            // Straight up out of her orbit, to turn into its boss up there;
            // the boss's own attack brings it down into the arena.
            Transform weapon = captive.Blade.transform;
            Vector3 above = weapon.position + Vector3.up * ascendHeight;
            Vector3 spot = new Vector3(above.x, floor, above.z);

            Coroutine retreat = StartCoroutine(MoveLuna(home, retreatTime));
            yield return Fly(weapon, above, weapon.rotation, ascendTime);
            yield return retreat;

            Vector3 fallen = captive.Blade.transform.position;

            if (captive.Weapon.bossPrefab == null)
            {
                Debug.LogWarning($"[LunaCaptives] {captive.Name} has no boss prefab, so it skips its fight.", this);
            }
            else
            {
                Enemy boss = Instantiate(captive.Weapon.bossPrefab, spot, facing);
                boss.name = captive.Weapon.bossPrefab.name;

                // Lined up on the weapon it replaces, wherever the prefab's
                // pivot is, so the swap cannot be seen.
                boss.transform.position += Middle(captive.Blade.transform, fallen) - Middle(boss.transform, spot);
                boss.ArenaCentre = FightCentre;
                boss.Summoner = luna.transform;

                if (luna.Arena != null)
                {
                    boss.Arena = luna.Arena;
                }

                if (lastHealth.HasValue)
                {
                    boss.Health.SetMaxHealth(lastHealth.Value + healthPerFight);
                }

                lastHealth = boss.Health.MaxHealth;

                captive.Blade.gameObject.SetActive(false);
                captive.Chain.To = boss.transform;
                captive.State = State.Fighting;

                bool dead = false;
                boss.Health.Died += died =>
                {
                    dead = true;
                    fallen = Middle(boss.transform, fallen);
                };

                BossHealthBar.Show(boss.Health, captive.Name);
                Lock(false);
                boss.Wake();

                while (!dead)
                {
                    yield return null;
                }

                Lock(true);
                BossHealthBar.Hide();

                // Gone already if its prefab destroys itself on death.
                if (boss != null)
                {
                    Destroy(boss.gameObject);
                }
            }

            Blade blade = captive.Blade;
            blade.gameObject.SetActive(true);
            blade.Visibility = 1f;
            blade.transform.SetPositionAndRotation(fallen, resting);

            captive.Chain.To = blade.transform;
            SnapChain(captive);
        }

        /// <summary>A weapon the player does not own: it trembles, bursts into shards in its colour, and fades.</summary>
        private IEnumerator Shatter(Captive captive)
        {
            captive.State = State.Done;

            Blade blade = captive.Blade;
            Vector3 position = blade.transform.position;

            for (float clock = 0f; clock < crackTime; clock += Time.deltaTime)
            {
                blade.transform.position = position + UnityEngine.Random.insideUnitSphere * (struggleShake * clock / crackTime);
                yield return null;
            }

            blade.transform.position = position;

            Color glow = captive.Colour * burstIntensity;
            glow.a = 1f;

            Vector3 middle = Middle(blade.transform, position);
            HammerImpact.Create(middle, shards, null, glow, 0f, glow);
            EnergyBurst.Create(middle, Recolour(snapBurst, glow));

            yield return FadeAway(new List<Blade> { blade }, shatterFadeTime);
        }

        private void SnapChain(Captive captive)
        {
            if (captive.Chain == null)
            {
                return;
            }

            Color glow = captive.Colour * burstIntensity;
            glow.a = 1f;

            captive.Chain.Snap(Recolour(snapBurst, glow));
            captive.Chain = null;
        }

        private void StartLunaFight()
        {
            luna.Health.Invulnerable = false;

            if (lunaIntro == null)
            {
                Debug.LogWarning($"[LunaCaptives] {name} has no Luna Intro, so she simply wakes.", this);
                Lock(false);
                luna.StayDormant = false;
                luna.Wake();
                return;
            }

            Vector3 pivot = luna.transform.position;
            lunaIntro.Begin(luna, pivot, false);

            // The intro aims her attacks at her pivot, up off the floor; one
            // set on the intro itself is left alone.
            if (luna.ArenaCentre.HasValue && (luna.ArenaCentre.Value - pivot).sqrMagnitude < 0.0001f)
            {
                luna.ArenaCentre = FightCentre;
            }
        }

        // ---- helpers -------------------------------------------------------

        private void Lock(bool locked)
        {
            if (input != null)
            {
                input.Locked = locked;
            }
        }

        private IEnumerator Say(List<DialogueLine> lines)
        {
            foreach (DialogueLine line in lines)
            {
                bool waiting = true;
                DialogueBox.Instance.Say(line, () => waiting = false);

                while (waiting)
                {
                    yield return null;
                }
            }
        }

        private IEnumerator MoveLuna(Vector3 end, float duration)
        {
            Transform body = luna.transform;
            Vector3 start = body.position;

            for (float clock = 0f; clock < duration; clock += Time.deltaTime)
            {
                body.position = Vector3.Lerp(start, end, Smooth(clock / duration));
                yield return null;
            }

            body.position = end;
        }

        private static IEnumerator Fly(Transform thing, Vector3 end, Quaternion endRotation, float duration)
        {
            Vector3 start = thing.position;
            Quaternion startRotation = thing.rotation;

            for (float clock = 0f; clock < duration; clock += Time.deltaTime)
            {
                float t = Smooth(clock / duration);
                thing.SetPositionAndRotation(Vector3.Lerp(start, end, t), Quaternion.Slerp(startRotation, endRotation, t));
                yield return null;
            }

            thing.SetPositionAndRotation(end, endRotation);
        }

        private static IEnumerator FadeAway(List<Blade> blades, float duration)
        {
            for (float clock = 0f; clock < duration; clock += Time.deltaTime)
            {
                foreach (Blade blade in blades)
                {
                    if (blade != null)
                    {
                        blade.Visibility = 1f - clock / duration;
                    }
                }

                yield return null;
            }

            foreach (Blade blade in blades)
            {
                if (blade != null)
                {
                    Destroy(blade.gameObject);
                }
            }
        }

        /// <summary>
        /// The most strongly coloured of the blade's materials, at full
        /// brightness, so the rope's intensity alone sets how bright it glows.
        /// </summary>
        private static Color BladeColour(Blade blade)
        {
            Color best = Color.white;
            float bestScore = -1f;

            foreach (Renderer part in blade.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in part.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(BaseColorId))
                    {
                        continue;
                    }

                    Color colour = material.GetColor(BaseColorId);
                    Color.RGBToHSV(colour, out _, out float saturation, out float value);
                    float score = saturation * Mathf.Min(value, 1f);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = colour;
                    }
                }
            }

            float brightest = Mathf.Max(best.r, Mathf.Max(best.g, best.b));

            if (brightest > 0.0001f)
            {
                best /= brightest;
            }

            best.a = 1f;
            return best;
        }

        private static EnergyBurst.Settings Recolour(EnergyBurst.Settings source, Color colour)
        {
            return new EnergyBurst.Settings
            {
                flashSize = source.flashSize,
                flashTime = source.flashTime,
                sparkCount = source.sparkCount,
                sparkSize = source.sparkSize,
                sparkSpeed = source.sparkSpeed,
                sparkLife = source.sparkLife,
                colour = colour,
            };
        }

        /// <summary>The middle of everything drawn under <paramref name="root"/>, or <paramref name="fallback"/> if nothing is.</summary>
        private static Vector3 Middle(Transform root, Vector3 fallback)
        {
            bool any = false;
            Bounds bounds = default;

            foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled || part is ParticleSystemRenderer || part is LineRenderer)
                {
                    continue;
                }

                if (any)
                {
                    bounds.Encapsulate(part.bounds);
                }
                else
                {
                    bounds = part.bounds;
                    any = true;
                }
            }

            return any ? bounds.center : fallback;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Eases out past 1 and back, by <see cref="overshoot"/> of the way
        /// at its furthest.
        /// </summary>
        private float BackOut(float t)
        {
            t = Mathf.Clamp01(t);

            if (overshoot <= 0f)
            {
                return 1f - (1f - t) * (1f - t);
            }

            float s = BackConstant(overshoot);
            float u = t - 1f;
            return u * u * ((s + 1f) * u + s) + 1f;
        }

        /// <summary>
        /// The constant for the back ease whose peak passes 1 by exactly
        /// <paramref name="peak"/>. That peak is 4s^3 / 27(s+1)^2, which only
        /// grows with s, so it is found by halving.
        /// </summary>
        private static float BackConstant(float peak)
        {
            float low = 0f;
            float high = 20f;

            for (int i = 0; i < 30; i++)
            {
                float s = (low + high) * 0.5f;
                float reached = 4f * s * s * s / (27f * (s + 1f) * (s + 1f));

                if (reached < peak)
                {
                    low = s;
                }
                else
                {
                    high = s;
                }
            }

            return (low + high) * 0.5f;
        }
    }
}
