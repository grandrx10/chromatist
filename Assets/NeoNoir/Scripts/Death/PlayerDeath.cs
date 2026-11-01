using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// What happens when the player's health reaches zero: everything but
    /// the player goes black, blood bursts out of them, they fall, sink into
    /// a white circle, and rise out of one in the Afterworld.
    /// </summary>
    /// <remarks>
    /// Goes on the player, next to <see cref="PlayerHealth"/>. The settings
    /// live here; the show itself is a <see cref="DeathSequence"/>, which
    /// outlives the scene so it can carry on in the Afterworld.
    ///
    /// The blackout draws only the player (and the blood and circle) on a
    /// black background, and hides every canvas. See
    /// <see cref="DeathSequence"/> for the steps in order.
    /// </remarks>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerDeath : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Fall")]
            [Tooltip("State in the player's Animator Controller played the moment they die, straight away with no transition. Otherwise the controller plays its default (idle) state as usual. Empty plays none.")]
            public string deathState = "Death";

            [Tooltip("Seconds from death until the white circle opens. Match it to the fall animation.")]
            [Min(0f)] public float fallTime = 1.5f;

            [Header("Blood")]
            [Tooltip("Scarlett's blood, thrown all the way round the player. Its Spread is ignored.")]
            public ScarlettBlood.Settings blood = new ScarlettBlood.Settings
            {
                droplets = 30,
                minSpeed = 4f,
                maxSpeed = 13f,
                lifetime = 0.6f,
                size = 0.16f,
            };

            [Tooltip("Material the blood draws with, such as the one on Scarlett's swipe (NeoNoir/Tear). Empty draws it flat in Blood Colour.")]
            public Material bloodMaterial;

            [Tooltip("Colour of the blood when no material is set.")]
            public Color bloodColour = new Color(0.8f, 0.05f, 0.08f, 1f);

            [Tooltip("Height above the player's feet the blood bursts from.")]
            public float bloodHeight = 1f;

            [Header("White circle")]
            [Tooltip("Material for the circle. Empty draws it flat in Circle Colour.")]
            public Material circleMaterial;

            [ColorUsage(false, true)]
            public Color circleColour = Color.white;

            [Tooltip("Radius of the circle when fully open. Wide enough that the camera cannot see under it to the sunk player.")]
            [Min(0.1f)] public float circleRadius = 2.5f;

            [Tooltip("Seconds the circle takes to open.")]
            [Min(0f)] public float circleOpenTime = 0.5f;

            [Tooltip("Seconds the circle takes to close.")]
            [Min(0f)] public float circleCloseTime = 0.35f;

            [Header("Sinking and rising")]
            [Tooltip("How far below the floor the player sinks, and rises from. At least the player's height.")]
            [Min(0f)] public float sinkDepth = 2.5f;

            [Tooltip("Seconds the player takes to sink out of sight.")]
            [Min(0f)] public float sinkTime = 1.5f;

            [Tooltip("Seconds the player takes to rise in the Afterworld.")]
            [Min(0f)] public float riseTime = 1.5f;

            [Header("Reveal")]
            [Tooltip("Seconds the world takes to spread out from the white circle across the screen once the player has risen. The circle closes meanwhile.")]
            [Min(0f)] public float revealTime = 1.2f;

            [Header("Afterworld")]
            [Tooltip("Scene loaded once the screen is black.")]
            public string afterworldScene = "Afterworld";

            [Tooltip("Seconds of black after the circle closes, before the Afterworld loads.")]
            [Min(0f)] public float blackBeforeLoad = 0.6f;

            [Tooltip("Seconds of black in the Afterworld before the circle opens.")]
            [Min(0f)] public float blackAfterLoad = 0.6f;
        }

        [SerializeField] private Settings settings = new Settings();

        private PlayerHealth health;

        /// <summary>The circle and rise timings, also used for arriving in the Afterworld and a level's opening.</summary>
        public Settings Sequence => settings;

        /// <summary>The settings on <paramref name="player"/>, or the defaults if it has no PlayerDeath.</summary>
        public static Settings SettingsOf(Component player)
        {
            PlayerDeath death = player != null ? player.GetComponentInChildren<PlayerDeath>() : null;
            return death != null ? death.settings : new Settings();
        }

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
        }

        private void OnEnable()
        {
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
        }

        private void OnDied(PlayerHealth died)
        {
            DeathSequence.Play(this, settings);
        }
    }
}
