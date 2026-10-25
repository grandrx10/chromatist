using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// What plays when a <see cref="PurchasableBlade"/> is acquired, on top of
    /// its stone changing colour.
    /// </summary>
    /// <remarks>
    /// Put one on the same object as the PurchasableBlade. A blade with none
    /// (Luna) only has its stone change.
    /// </remarks>
    public abstract class BladeAcquireEffect : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Seconds the player is held in place once the effect starts, so it can be watched.")]
        [Min(0f)]
        private float holdTime = 1.5f;

        public float HoldTime => holdTime;

        public abstract void Play(PurchasableBlade blade);

        /// <summary>
        /// A copy of <paramref name="source"/> with each colour property it
        /// has set. A copy, so the effect never recolours the blades
        /// themselves or anything else sharing the material.
        /// </summary>
        protected static Material Tinted(Material source, string suffix, params (string property, Color colour)[] colours)
        {
            if (source == null)
            {
                return null;
            }

            Material tinted = new Material(source) { name = source.name + " (" + suffix + ")" };

            foreach ((string property, Color colour) in colours)
            {
                if (tinted.HasProperty(property))
                {
                    tinted.SetColor(property, colour);
                }
            }

            return tinted;
        }
    }
}
