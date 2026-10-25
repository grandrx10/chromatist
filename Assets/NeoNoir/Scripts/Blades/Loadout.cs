using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The blades the player brings into a level: one main blade, and any
    /// number of secondaries in the order they rotate.
    /// </summary>
    /// <remarks>
    /// Set by hand in the inspector for now. The pre-level selection screen,
    /// when it exists, only has to write these two fields.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Loadout", fileName = "Loadout")]
    public sealed class Loadout : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Always shown. Its primary attack is on left click.")]
        private BladeDefinition mainBlade;

        [SerializeField]
        [Tooltip("Rotated through in this order. The shown one's secondary ability is on right click. The same blade may appear more than once.")]
        private List<BladeDefinition> secondaryBlades = new List<BladeDefinition>();

        public BladeDefinition MainBlade
        {
            get => mainBlade;
            set => mainBlade = value;
        }

        public List<BladeDefinition> SecondaryBlades => secondaryBlades;
    }
}
