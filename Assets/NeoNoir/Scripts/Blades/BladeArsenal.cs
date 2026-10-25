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
        [SerializeField] private Loadout loadout;

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

        private int shown = -1;
        private int waitingFor = -1;
        private bool abilityRunning;

        /// <summary>Raised when a different secondary blade begins showing, or none is. For HUDs.</summary>
        public event Action<Blade> ShownSecondaryChanged;

        public Blade MainBlade { get; private set; }

        /// <summary>The secondary currently in rotation, or null while every one is cooling down.</summary>
        public Blade ShownSecondary => shown >= 0 ? secondaries[shown].Blade : null;

        public Loadout Loadout => loadout;

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

            if (loadout == null)
            {
                Debug.LogWarning("[BladeArsenal] No loadout assigned, so no blades were spawned.", this);
                return;
            }

            if (loadout.MainBlade != null)
            {
                MainBlade = Spawn(loadout.MainBlade, BladeSlot.Main, 0f);
            }

            foreach (BladeDefinition definition in loadout.SecondaryBlades)
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
                // The first secondary starts out already in hand, not fading in.
                Show(0);
                secondaries[0].Visibility = 1f;
                secondaries[0].Blade.Visibility = 1f;
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
            int count = secondaries.Count;

            // Walk the whole list once, ending back at the blade just used,
            // which is how a single secondary comes back to itself.
            for (int step = 1; step <= count; step++)
            {
                int index = (from + step) % count;

                if (secondaries[index].Ready)
                {
                    Show(index);
                    return;
                }
            }

            shown = -1;
            waitingFor = (from + 1) % count;
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
