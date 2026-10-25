using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Scarlett's acquire effect: a rift tears open across the stone beneath
    /// the scythe, holds, and seals shut again.
    /// </summary>
    /// <remarks>
    /// The same <see cref="ScarlettClaw"/> tear her reap opens, with no
    /// damage and no blood. It is raked left to right across the screen, so
    /// it reads the same from the top-down camera whichever way the shrine
    /// is turned.
    /// </remarks>
    public sealed class ScarlettRiftAcquireEffect : BladeAcquireEffect
    {
        [SerializeField]
        [Tooltip("Normally ScarlettTear (NeoNoir/Tear), the one her scythe uses. It is copied and tinted, never edited.")]
        private Material riftMaterial;

        [SerializeField]
        [Tooltip("Colour of the rift itself. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        [ColorUsage(false, true)]
        private Color riftColour = new Color(1f, 0.04f, 0.08f);

        [SerializeField]
        [Tooltip("Colour bright things seen through the rift turn.")]
        [ColorUsage(false, true)]
        private Color overshadowColour = new Color(1f, 0.35f, 0.4f);

        [SerializeField]
        [Tooltip("Offset from the top of the stone the rift opens at.")]
        private Vector3 offset = Vector3.zero;

        [SerializeField]
        private ScarlettClaw.Settings rift = new ScarlettClaw.Settings
        {
            damage = 0f,
            span = 5f,
            speed = 14f,
            thickness = 1.4f,
            bow = 0.6f,
            kinks = 3,
            kinkSize = 0.3f,
            raggedness = 0.6f,
            splinters = 10,
            splinterLength = 1.8f,
            hold = 0.9f,
            fadeTime = 0.8f,
            lift = 0.5f,
        };

        private Material tinted;

        public override void Play(PurchasableBlade blade)
        {
            if (tinted == null)
            {
                tinted = Tinted(riftMaterial, "Scarlett acquire",
                                ("_BaseColor", riftColour),
                                ("_Color", riftColour),
                                ("_BehindColor", overshadowColour),
                                ("_BloodColor", riftColour));
            }

            Vector3 across = Vector3.right;
            Vector3 away = Vector3.forward;
            Camera camera = Camera.main;

            if (camera != null)
            {
                across = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up);
                away = Vector3.Cross(across, Vector3.up);
            }

            Vector3 centre = blade.StoneTop + offset;

            // No blood settings and nothing to tell about damage: the rift
            // is only for show, and the shrine has no enemies to cut anyway.
            ScarlettClaw.Spawn(centre, across, away, centre - away, rift, null, tinted, riftColour, null);
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
