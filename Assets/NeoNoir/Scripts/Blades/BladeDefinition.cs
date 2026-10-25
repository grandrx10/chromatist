using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One weapon the player can own: which prefab it spawns, and the numbers
    /// that belong to the weapon rather than to its behaviour script.
    /// </summary>
    /// <remarks>
    /// Kept apart from the prefab so loadouts, shops and save files can refer
    /// to "Luna" without loading her model. When weapon levels arrive, each
    /// level becomes its own definition (or this grows a list of levels); the
    /// loadout never needs to know.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Blade Definition", fileName = "Blade")]
    public sealed class BladeDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Blade";

        [SerializeField]
        [Tooltip("The prefab spawned when this blade is equipped. Its root must have a Blade component (Luna, Rose, ...).")]
        private Blade prefab;

        [SerializeField]
        [Tooltip("Seconds after the secondary ability ends before this blade can come back into rotation.")]
        [Min(0f)]
        private float secondaryCooldown = 3f;

        public string DisplayName => displayName;

        public Blade Prefab => prefab;

        public float SecondaryCooldown => secondaryCooldown;
    }
}
