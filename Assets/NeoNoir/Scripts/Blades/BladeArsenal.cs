using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Spawns the player's loadout and runs the secondary blade rotation.
    /// </summary>
    /// <remarks>
    /// The main blade is always out. Of the secondaries, one is shown at a
    /// time, and right click uses its secondary ability. When the ability
    /// finishes, that blade goes on cooldown and fades out while the next one
    /// in order fades in.
    ///
    /// The order never changes. Advancing walks forward from the blade just
    /// used and shows the first one that is off cooldown, skipping any that
    /// are not. If every secondary is cooling down, nothing is shown, and the
    /// rotation waits for the blade immediately after the one just used --
    /// even if one further down the list happens to come off cooldown first.
    ///
    /// The swap key skips the shown blade without using it, moving on the
    /// same way but with no cooldown.
    ///
    /// Goes on the player. Blades are spawned unparented, since they must not
    /// turn with the player.
    /// </remarks>
    public sealed class BladeArsenal : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Used when the player owns no blades yet (no Afterworld visit), such as pressing Play straight into a level. Otherwise the setup picked at the Afterworld's Loadout Station is equipped.")]
        private Loadout loadout;

        [SerializeField]
        [Tooltip("Every blade there is (Luna, Rose, Scarlett, ...). The setup picked in the Afterworld is saved by blade name, and this is where the names are looked up.")]
        private List<BladeDefinition> allBlades = new List<BladeDefinition>();

        [SerializeField]
        [Tooltip("For the Afterworld: spawn no blades, and the player can only walk, dash and interact -- no attack, secondary or swap.")]
        private bool unarmed;

        [SerializeField]
        [Tooltip("Seconds a used secondary takes to fade out once its ability is over.")]
        [Min(0.01f)]
        private float fadeOutTime = 0.25f;

        [SerializeField]
        [Tooltip("Seconds the next secondary takes to fade in. It is usable from the moment it starts.")]
        [Min(0.01f)]
        private float fadeInTime = 0.15f;

        [SerializeField]
        [Tooltip("Degrees around the orbit the shown secondary sits from the main blade.")]
        private float secondaryOrbitPhase = 180f;

        private sealed class Entry
        {
            public BladeDefinition Definition;
            public Blade Blade;
            public float Cooldown;
            public float Visibility;
            public bool Shown;

            public bool Ready => Cooldown <= 0f && !Blade.SecondaryActive;
        }

        private readonly List<Entry> secondaries = new List<Entry>();

        private PlayerInputReader input;

        private BladeDefinition mainDefinition;

        private int shown = -1;
        private int waitingFor = -1;
        private bool abilityRunning;

        /// <summary>Raised when a different secondary blade begins showing, or none is. For HUDs.</summary>
        public event Action<Blade> ShownSecondaryChanged;

        public Blade MainBlade { get; private set; }

        /// <summary>The secondary currently in rotation, or null while every one is cooling down.</summary>
        public Blade ShownSecondary => shown >= 0 ? secondaries[shown].Blade : null;

        public Loadout Loadout => loadout;

        /// <summary>Degrees around the orbit the shown secondary sits from the main blade.</summary>
        public float SecondaryOrbitPhase => secondaryOrbitPhase;

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
        }

        private void Start()
        {
            // Start rather than Awake, so every component on the player is
            // already initialised when the blades bind to it.
            Equip();
        }

        /// <summary>Destroys any spawned blades and spawns the loadout afresh.</summary>
        public void Equip()
        {
            Unequip();

            if (input != null)
            {
                input.Unarmed = unarmed;
            }

            if (unarmed)
            {
                return;
            }

            if (TryChosenLoadout(out BladeDefinition main, out List<BladeDefinition> chosen))
            {
                SpawnAll(main, chosen);
            }
            else if (loadout != null)
            {
                SpawnAll(loadout.MainBlade, loadout.SecondaryBlades);
            }
            else
            {
                Debug.LogWarning("[BladeArsenal] No loadout assigned and no blades owned, so no blades were spawned.", this);
                return;
            }

            if (secondaries.Count > 0)
            {
                // The first secondary starts out already in hand, not fading in.
                secondaries[0].Visibility = 1f;
                secondaries[0].Blade.Visibility = 1f;
            }
        }

        /// <summary>
        /// The setup picked at the <see cref="LoadoutStation"/>, from the
        /// blades in <see cref="allBlades"/> the player owns. False when they
        /// own none.
        /// </summary>
        private bool TryChosenLoadout(out BladeDefinition main, out List<BladeDefinition> chosen)
        {
            List<BladeDefinition> owned = new List<BladeDefinition>();

            foreach (BladeDefinition blade in allBlades)
            {
                if (blade != null && BladeUnlocks.IsAcquired(blade) && !owned.Contains(blade))
                {
                    owned.Add(blade);
                }
            }

            if (owned.Count == 0)
            {
                main = null;
                chosen = null;

                if (allBlades.Count == 0)
                {
                    Debug.LogWarning("[BladeArsenal] All Blades is empty, so the setup picked in the Afterworld can't be found; using the Loadout instead.", this);
                }

                return false;
            }

            ChosenLoadout.Resolve(owned);
            main = ChosenLoadout.Primary;
            chosen = ChosenLoadout.Secondaries;
            return main != null;
        }

        /// <summary>
        /// Arms the player with <paramref name="main"/> and
        /// <paramref name="secondaryBlades"/> in place of whatever they hold,
        /// abilities and all, the blades fading in. For the Afterworld's
        /// <see cref="TestingGrounds"/>, which arms an Unarmed player.
        /// </summary>
        public void Arm(BladeDefinition main, IReadOnlyList<BladeDefinition> secondaryBlades)
        {
            Unequip();

            if (input != null)
            {
                input.Unarmed = false;
            }

            SpawnAll(main, secondaryBlades);

            if (MainBlade != null)
            {
                MainBlade.Visibility = 0f;
            }
        }

        /// <summary>
        /// Takes the player's blades away, abilities and all, the blades in
        /// view fading out. Undoes <see cref="Arm"/>.
        /// </summary>
        public void Unarm()
        {
            List<Blade> leaving = Disarm();

            if (input != null)
            {
                input.Unarmed = true;
            }

            StartCoroutine(FadeAway(leaving));
        }

        private System.Collections.IEnumerator FadeAway(List<Blade> leaving)
        {
            float clock = 0f;

            while (clock < fadeOutTime)
            {
                clock += Time.deltaTime;

                foreach (Blade blade in leaving)
                {
                    if (blade != null)
                    {
                        blade.Visibility = Mathf.Min(blade.Visibility, 1f - clock / fadeOutTime);
                    }
                }

                yield return null;
            }

            foreach (Blade blade in leaving)
            {
                if (blade != null)
                {
                    Destroy(blade.gameObject);
                }
            }
        }

        private void SpawnAll(BladeDefinition main, IReadOnlyList<BladeDefinition> secondaryBlades)
        {
            if (main != null)
            {
                MainBlade = Spawn(main, BladeSlot.Main, 0f);
                mainDefinition = main;
            }

            foreach (BladeDefinition definition in secondaryBlades)
            {
                Blade blade = definition != null ? Spawn(definition, BladeSlot.Secondary, secondaryOrbitPhase) : null;

                if (blade == null)
                {
                    continue;
                }

                blade.SecondaryFinished += OnSecondaryFinished;
                blade.Visibility = 0f;
                blade.gameObject.SetActive(false);

                secondaries.Add(new Entry { Definition = definition, Blade = blade });
            }

            if (secondaries.Count > 0)
            {
                Show(0);
            }
        }

        private Blade Spawn(BladeDefinition definition, BladeSlot slot, float orbitPhase)
        {
            if (definition.Prefab == null)
            {
                Debug.LogWarning($"[BladeArsenal] {definition.name} has no prefab.", definition);
                return null;
            }

            Blade blade = Instantiate(definition.Prefab, transform.position, Quaternion.identity);
            blade.name = definition.DisplayName;
            blade.Definition = definition;
            blade.Bind(transform, slot, orbitPhase);
            return blade;
        }

        private void Unequip()
        {
            if (MainBlade != null)
            {
                Destroy(MainBlade.gameObject);
                MainBlade = null;
            }

            mainDefinition = null;

            foreach (Entry entry in secondaries)
            {
                if (entry.Blade != null)
                {
                    entry.Blade.SecondaryFinished -= OnSecondaryFinished;
                    Destroy(entry.Blade.gameObject);
                }
            }

            secondaries.Clear();
            shown = -1;
            waitingFor = -1;
            abilityRunning = false;
        }

        private void Update()
        {
            // A blade promoted to main fades in where it now orbits.
            if (MainBlade != null && MainBlade.Visibility < 1f)
            {
                MainBlade.Visibility = Mathf.MoveTowards(MainBlade.Visibility, 1f, Time.deltaTime / fadeInTime);
            }

            if (secondaries.Count == 0)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            foreach (Entry entry in secondaries)
            {
                // The clock only starts once the ability is over.
                if (!entry.Blade.SecondaryActive)
                {
                    entry.Cooldown = Mathf.Max(0f, entry.Cooldown - deltaTime);
                }
            }

            if (shown < 0 && waitingFor >= 0 && secondaries[waitingFor].Ready)
            {
                Show(waitingFor);
            }

            if (input != null && input.SecondaryPressed)
            {
                TryUseSecondary();
            }
            else if (input != null && input.SwapPressed)
            {
                TrySwap();
            }

            UpdateFades(deltaTime);
        }

        private void TryUseSecondary()
        {
            if (abilityRunning || shown < 0)
            {
                return;
            }

            Entry entry = secondaries[shown];

            // No wait for the fade-in: the incoming blade is usable at once.
            if (!entry.Ready)
            {
                return;
            }

            // Set before activating: an ability that does nothing finishes
            // inside this call, and its handler must see the state it expects.
            abilityRunning = true;
            entry.Blade.ActivateSecondary();
        }

        /// <summary>
        /// Puts the shown secondary away unused and shows the next ready one
        /// in order, skipping any cooling down. The one put away keeps its
        /// place and goes on no cooldown. Does nothing with no secondary
        /// shown, mid-ability, or when no other one is ready.
        /// </summary>
        private void TrySwap()
        {
            if (abilityRunning || shown < 0)
            {
                return;
            }

            int count = secondaries.Count;

            // Stops one short of a full lap: swapping never lands back on
            // the blade already shown.
            for (int step = 1; step < count; step++)
            {
                int index = (shown + step) % count;

                if (secondaries[index].Ready)
                {
                    secondaries[shown].Shown = false;
                    Show(index);
                    return;
                }
            }
        }

        /// <summary>Takes <paramref name="seconds"/> off every secondary's cooldown, stopping at zero.</summary>
        public void ReduceCooldowns(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);

            foreach (Entry entry in secondaries)
            {
                entry.Cooldown = Mathf.Max(0f, entry.Cooldown - seconds);
            }
        }

        /// <summary>
        /// Multiplies the time left on every secondary's cooldown by
        /// <paramref name="factor"/>: 0.5 halves what remains. Blades not
        /// cooling down are unaffected.
        /// </summary>
        public void ScaleCooldowns(float factor)
        {
            factor = Mathf.Max(0f, factor);

            foreach (Entry entry in secondaries)
            {
                entry.Cooldown *= factor;
            }
        }

        /// <summary>
        /// Takes every copy of <paramref name="definition"/> out of the
        /// player's hands for the rest of the level; the loadout asset itself
        /// is left alone. If it was the main blade, the first other secondary
        /// in order leaves the rotation and becomes the main blade.
        /// </summary>
        /// <returns>
        /// The copy that was in view -- the main blade, or the shown
        /// secondary -- now <see cref="Blade.Seized"/> and no longer the
        /// arsenal's to move or destroy. Null if none was in view; any copies
        /// out of view are destroyed.
        /// </returns>
        public Blade Surrender(BladeDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            Blade taken = null;

            bool mainLost = MainBlade != null && mainDefinition == definition;

            if (mainLost)
            {
                taken = MainBlade;
                MainBlade = null;
                mainDefinition = null;
            }

            Entry promoted = mainLost ? secondaries.Find(entry => entry.Definition != definition) : null;

            Entry shownEntry = shown >= 0 ? secondaries[shown] : null;
            Entry waitingEntry = waitingFor >= 0 ? secondaries[waitingFor] : null;

            // Where the walk picks up again if the shown or awaited blade is
            // among those leaving: the next one along that stays.
            int resumeShown = 0;
            int resumeWaiting = 0;

            List<Entry> kept = new List<Entry>();

            foreach (Entry entry in secondaries)
            {
                if (entry == shownEntry)
                {
                    resumeShown = kept.Count;
                }

                if (entry == waitingEntry)
                {
                    resumeWaiting = kept.Count;
                }

                if (entry == promoted)
                {
                    entry.Blade.SecondaryFinished -= OnSecondaryFinished;
                    continue;
                }

                if (entry.Definition != definition)
                {
                    kept.Add(entry);
                    continue;
                }

                entry.Blade.SecondaryFinished -= OnSecondaryFinished;

                if (taken == null && entry == shownEntry && entry.Blade.gameObject.activeSelf)
                {
                    taken = entry.Blade;
                }
                else
                {
                    Destroy(entry.Blade.gameObject);
                }
            }

            secondaries.Clear();
            secondaries.AddRange(kept);

            if (promoted != null)
            {
                MainBlade = promoted.Blade;
                mainDefinition = promoted.Definition;
                MainBlade.Bind(transform, BladeSlot.Main, 0f);
                MainBlade.gameObject.SetActive(true);
                MainBlade.Visibility = 0f;
            }

            abilityRunning = secondaries.Exists(entry => entry.Blade.SecondaryActive);

            if (secondaries.Count == 0)
            {
                bool hadShown = shown >= 0;
                shown = -1;
                waitingFor = -1;

                if (hadShown)
                {
                    ShownSecondaryChanged?.Invoke(null);
                }
            }
            else if (shownEntry != null)
            {
                int index = secondaries.IndexOf(shownEntry);

                if (index >= 0)
                {
                    shown = index;
                }
                else
                {
                    ShowFrom(resumeShown);
                }
            }
            else if (waitingEntry != null)
            {
                int index = secondaries.IndexOf(waitingEntry);
                waitingFor = index >= 0 ? index : resumeWaiting % secondaries.Count;
            }

            if (taken != null)
            {
                taken.Seized = true;
                taken.Visibility = 1f;
            }

            return taken;
        }

        /// <summary>
        /// Takes every blade out of the player's hands for the rest of the
        /// level, abilities and all; the loadout asset itself is left alone.
        /// </summary>
        /// <returns>
        /// The blades that were in view, still orbiting the player but no
        /// longer the arsenal's: the caller fades and destroys them. Any out
        /// of view are destroyed here.
        /// </returns>
        public List<Blade> Disarm()
        {
            List<Blade> shownBlades = new List<Blade>();

            if (MainBlade != null)
            {
                shownBlades.Add(MainBlade);
                MainBlade = null;
            }

            mainDefinition = null;

            foreach (Entry entry in secondaries)
            {
                entry.Blade.SecondaryFinished -= OnSecondaryFinished;

                if (entry.Blade.gameObject.activeSelf)
                {
                    shownBlades.Add(entry.Blade);
                }
                else
                {
                    Destroy(entry.Blade.gameObject);
                }
            }

            bool hadShown = shown >= 0;

            secondaries.Clear();
            shown = -1;
            waitingFor = -1;
            abilityRunning = false;

            if (hadShown)
            {
                ShownSecondaryChanged?.Invoke(null);
            }

            return shownBlades;
        }

        /// <summary>
        /// The orbit phase <see cref="Adopt"/> would put a blade at now: the
        /// main blade's with no main blade, otherwise the shown secondary's.
        /// </summary>
        public float NextOrbitPhase => MainBlade == null ? 0f : secondaryOrbitPhase;

        /// <summary>
        /// Equips <paramref name="blade"/>, an instance made elsewhere, as
        /// <paramref name="definition"/>: the main blade if there is none,
        /// otherwise the last secondary in the rotation. It is un-seized and
        /// snapped into orbit, so fly it to its
        /// <see cref="Blade.OrbitPose"/> at <see cref="NextOrbitPhase"/>
        /// first for a seamless hand-over. A secondary is shown at once if
        /// none is; otherwise it fades out into its place in the rotation.
        /// </summary>
        public void Adopt(Blade blade, BladeDefinition definition)
        {
            if (blade == null)
            {
                return;
            }

            blade.Seized = false;
            blade.Definition = definition;
            blade.gameObject.SetActive(true);

            if (MainBlade == null)
            {
                blade.Bind(transform, BladeSlot.Main, 0f);
                blade.Visibility = 1f;
                MainBlade = blade;
                mainDefinition = definition;
                return;
            }

            blade.Bind(transform, BladeSlot.Secondary, secondaryOrbitPhase);
            blade.SecondaryFinished += OnSecondaryFinished;

            Entry entry = new Entry { Definition = definition, Blade = blade, Visibility = 1f };
            blade.Visibility = 1f;
            secondaries.Add(entry);

            if (shown < 0)
            {
                Show(secondaries.Count - 1);
            }
        }

        private void OnSecondaryFinished(Blade blade)
        {
            int index = secondaries.FindIndex(entry => entry.Blade == blade);

            if (index < 0)
            {
                return;
            }

            abilityRunning = false;

            Entry used = secondaries[index];
            used.Cooldown = used.Definition.SecondaryCooldown;
            used.Shown = false;

            Advance(index);
        }

        /// <summary>Shows the first ready blade after <paramref name="from"/> in order, or waits for the very next one.</summary>
        private void Advance(int from)
        {
            // The walk ends back at the blade just used, which is how a
            // single secondary comes back to itself.
            ShowFrom(from + 1);
        }

        /// <summary>Shows the first ready blade from <paramref name="start"/> on in order, or waits for that one.</summary>
        private void ShowFrom(int start)
        {
            int count = secondaries.Count;

            for (int step = 0; step < count; step++)
            {
                int index = (start + step) % count;

                if (secondaries[index].Ready)
                {
                    Show(index);
                    return;
                }
            }

            shown = -1;
            waitingFor = start % count;
            ShownSecondaryChanged?.Invoke(null);
        }

        private void Show(int index)
        {
            shown = index;
            waitingFor = -1;

            Entry entry = secondaries[index];
            entry.Shown = true;

            if (!entry.Blade.gameObject.activeSelf)
            {
                entry.Visibility = 0f;
                entry.Blade.Visibility = 0f;
                entry.Blade.gameObject.SetActive(true);
            }

            ShownSecondaryChanged?.Invoke(entry.Blade);
        }

        private void UpdateFades(float deltaTime)
        {
            foreach (Entry entry in secondaries)
            {
                // A blade mid-ability stays fully out until it is done, even
                // though it is no longer the one waiting to be used, and so
                // does one lingering on after its ability.
                float target = entry.Shown || entry.Blade.SecondaryActive || entry.Blade.Lingering ? 1f : 0f;
                float rate = deltaTime / (target > entry.Visibility ? fadeInTime : fadeOutTime);

                entry.Visibility = Mathf.MoveTowards(entry.Visibility, target, rate);
                entry.Blade.Visibility = entry.Visibility;

                if (entry.Visibility <= 0f && target <= 0f && entry.Blade.gameObject.activeSelf)
                {
                    entry.Blade.gameObject.SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            Unequip();
        }
    }
}
