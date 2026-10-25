using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The player's health bar: a white fill inside a white frame that shrinks
    /// toward the left as health drops, leaving black behind it. A grey drain
    /// bar under the fill shows the health just lost, then eases down to meet
    /// the fill.
    /// </summary>
    /// <remarks>
    /// The fills are plain sprite-less images whose right anchors are set to
    /// a health fraction. Image.fillAmount would need a sprite, and a sprite
    /// brings filtering that can soften the edge. As it is, every edge is a
    /// hard pixel boundary.
    ///
    /// The grey is real grey. The bar is on an overlay canvas, drawn after the
    /// two-tone threshold, so it is never crushed to black or white.
    ///
    /// The white fill always jumps straight to the true health; only the grey
    /// lags behind, so the bar never lies about how much health is left. Each
    /// new hit restarts the pause before the grey drains, so a burst of hits
    /// builds up one long grey chunk that then drains all at once. Healing
    /// pulls the grey up with the fill at once -- there is nothing to drain.
    ///
    /// Read every frame rather than from PlayerHealth's events. It costs
    /// nothing, and it never shows a stale value when the bar wakes up before
    /// the player has set its starting health.
    /// </remarks>
    public sealed class PlayerHealthBar : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The white fill. Its right edge is moved to show remaining health.")]
        private RectTransform fill;

        [SerializeField]
        [Tooltip("The grey bar under the fill. Shows health just lost, then eases down to the fill.")]
        private RectTransform drain;

        [SerializeField]
        [Tooltip("Whose health to show. Empty finds the player automatically.")]
        private PlayerHealth health;

        [Header("Drain")]
        [SerializeField]
        [Tooltip("Seconds the grey holds after a hit before it starts draining. Each new hit restarts it.")]
        [Min(0f)]
        private float drainDelay = 0.3f;

        [SerializeField]
        [Tooltip("How quickly the grey eases down to the fill once it starts. Higher is faster.")]
        [Min(0.01f)]
        private float drainSpeed = 6f;

        private float shown = -1f;
        private float drained = -1f;
        private float holdRemaining;

        private void LateUpdate()
        {
            if (fill == null)
            {
                return;
            }

            if (health == null)
            {
                health = FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Exclude);
            }

            float fraction = health != null ? Mathf.Clamp01(health.Fraction) : 1f;

            if (shown < 0f)
            {
                // First frame: start settled, nothing to drain.
                drained = fraction;
            }
            else if (fraction < shown)
            {
                holdRemaining = drainDelay;
            }

            if (!Mathf.Approximately(fraction, shown))
            {
                shown = fraction;
                SetWidth(fill, fraction);
            }

            UpdateDrain(fraction);
        }

        private void UpdateDrain(float fraction)
        {
            if (drain == null)
            {
                return;
            }

            if (fraction >= drained)
            {
                drained = fraction;
            }
            else if (holdRemaining > 0f)
            {
                holdRemaining -= Time.deltaTime;
            }
            else
            {
                // Frame-rate independent ease: the same fraction of the gap
                // closes per second whatever the frame rate.
                drained = Mathf.Lerp(drained, fraction, 1f - Mathf.Exp(-drainSpeed * Time.deltaTime));

                // An ease never quite arrives on its own; snap the last sliver.
                if (drained - fraction < 0.001f)
                {
                    drained = fraction;
                }
            }

            SetWidth(drain, drained);
        }

        private static void SetWidth(RectTransform bar, float fraction)
        {
            bar.anchorMax = new Vector2(fraction, bar.anchorMax.y);

            // A zero-width image still draws a sliver on some resolutions.
            bar.gameObject.SetActive(fraction > 0f);
        }
    }
}
