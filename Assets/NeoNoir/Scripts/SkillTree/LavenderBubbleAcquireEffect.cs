using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Lavender's acquire effect: her debuff bubbles welling up around the
    /// longbow for a while.
    /// </summary>
    /// <remarks>
    /// The same <see cref="LavenderBubbles"/> her arrows leave on enemies,
    /// run as a <see cref="LavenderBubbles.Burst"/> round the blade.
    /// </remarks>
    public sealed class LavenderBubbleAcquireEffect : BladeAcquireEffect
    {
        [SerializeField]
        [Tooltip("Normally LavenderEffect (NeoNoir/Tear), the one her longbow uses. It is copied and tinted, never edited.")]
        private Material bubbleMaterial;

        [SerializeField]
        [Tooltip("Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        [ColorUsage(false, true)]
        private Color lavenderColour = new Color(0.75f, 0.4f, 1f);

        [SerializeField]
        [Tooltip("Colour of the bubbles' glint.")]
        [ColorUsage(false, true)]
        private Color glintColour = Color.white;

        [SerializeField]
        [Tooltip("Seconds bubbles keep forming. The last ones finish rising after.")]
        [Min(0f)]
        private float duration = 1.4f;

        [SerializeField]
        [Tooltip("Added round the blade's bounds, so bubbles form beside it and not only on it.")]
        private Vector3 areaPadding = new Vector3(0.6f, 0.2f, 0.6f);

        [SerializeField]
        private LavenderBubbles.Settings bubbles = new LavenderBubbles.Settings
        {
            rate = 40f,
            minSize = 0.1f,
            maxSize = 0.24f,
            riseSpeed = 1.2f,
            wobble = 0.15f,
            lifetime = 1.1f,
        };

        private Material tinted;

        public override void Play(PurchasableBlade blade)
        {
            if (tinted == null)
            {
                tinted = Tinted(bubbleMaterial, "Lavender acquire",
                                ("_BaseColor", lavenderColour),
                                ("_BloodColor", lavenderColour),
                                ("_OutlineColor", glintColour));
            }

            Bounds area = blade.BladeBounds;
            area.Expand(areaPadding * 2f);

            LavenderBubbles.Burst(area, duration, bubbles, tinted);
        }

        private void OnDestroy()
        {
            if (tinted != null)
            {
                Destroy(tinted);
            }
        }
    }
}
