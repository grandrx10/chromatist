using UnityEngine;

namespace LightAndShadow.Rendering
{
    /// <summary>
    /// The room gone dark, all but what is kept lit: <see cref="Amount"/> 0
    /// is the room as normal, 1 is black everywhere but the kept renderers.
    /// <see cref="NeoNoirPostFeature"/> draws it after the edges.
    /// </summary>
    /// <remarks>
    /// A renderer is kept by setting <see cref="RenderingLayer"/> in its
    /// rendering layer mask, through <see cref="Keep"/>. The bit only ever
    /// means "kept", so it is safe to leave set on something that is going
    /// away anyway. Drawn only in game cameras, through Hidden/NeoNoir/Darkness.
    /// </remarks>
    public static class NeoNoirDarkness
    {
        /// <summary>The rendering layer bit of renderers that stay lit. Next to <see cref="OccluderFader.RenderingLayer"/>.</summary>
        public const uint RenderingLayer = 1u << 30;

        private static float amount;

        /// <summary>How dark the room is, 0 to 1.</summary>
        public static float Amount
        {
            get => amount;
            set => amount = Mathf.Clamp01(value);
        }

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            amount = 0f;
        }

        /// <summary>Keeps every renderer under <paramref name="root"/> lit, or stops keeping them.</summary>
        public static void Keep(GameObject root, bool keep = true)
        {
            if (root == null)
            {
                return;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (keep)
                {
                    renderer.renderingLayerMask |= RenderingLayer;
                }
                else
                {
                    renderer.renderingLayerMask &= ~RenderingLayer;
                }
            }
        }
    }
}
