using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The potion held in an enemy's hand: hidden the moment it is thrown,
    /// and grown back in later, ready for the next throw.
    /// </summary>
    /// <remarks>
    /// Added at runtime to the hand's potion by <see cref="PotionLobPattern"/>.
    /// Hiding switches its renderers off; coming back switches them on and
    /// grows it from nothing to its own size. The scale is set in LateUpdate,
    /// after the animator, so an animation cannot fight it.
    ///
    /// The return is scheduled when it is hidden, not by whoever hid it, so
    /// the potion still comes back if the attack is cut short.
    /// </remarks>
    public sealed class HandPotion : MonoBehaviour
    {
        private Renderer[] renderers;
        private Vector3 ownScale;

        // Counts down to the start of the return, then through it.
        private float returnIn;
        private float returnTime;
        private bool hidden;

        /// <summary>The hand potion on <paramref name="potion"/>, added the first time.</summary>
        public static HandPotion On(GameObject potion)
        {
            if (!potion.TryGetComponent(out HandPotion hand))
            {
                hand = potion.AddComponent<HandPotion>();
            }

            return hand;
        }

        /// <summary>Middle of the potion as drawn, for throwing from.</summary>
        public Vector3 Centre
        {
            get
            {
                foreach (Renderer part in renderers)
                {
                    if (part != null)
                    {
                        return part.bounds.center;
                    }
                }

                return transform.position;
            }
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            ownScale = transform.localScale;
        }

        /// <summary>
        /// Hides the potion, and starts growing it back in after
        /// <paramref name="delay"/> seconds, taking <paramref name="growTime"/>.
        /// </summary>
        public void Hide(float delay, float growTime)
        {
            hidden = true;
            returnIn = Mathf.Max(0f, delay);
            returnTime = Mathf.Max(0f, growTime);
            SetVisible(false);
            transform.localScale = ownScale;
        }

        private void LateUpdate()
        {
            if (!hidden)
            {
                return;
            }

            returnIn -= Time.deltaTime;

            if (returnIn > 0f)
            {
                return;
            }

            SetVisible(true);

            float grown = returnTime > 0f ? Mathf.Clamp01(-returnIn / returnTime) : 1f;
            float eased = 1f - (1f - grown) * (1f - grown);
            transform.localScale = ownScale * eased;

            if (grown >= 1f)
            {
                hidden = false;
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer part in renderers)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }
        }

        private void OnDestroy()
        {
            SetVisible(true);
        }
    }
}
