using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Raises a spawned cube out of the ground, drops it back, then destroys it.
    /// </summary>
    /// <remarks>
    /// Added at spawn time by <see cref="RisingCubeProjectile"/> rather than
    /// living on the prefab, so the prefab stays pure art with no behaviour
    /// baked into it.
    ///
    /// Motion is a deterministic lerp, not physics: the cube must finish its
    /// arc in a known time so the trail can be tuned against projectile speed
    /// and spacing without the two drifting apart.
    /// </remarks>
    public sealed class RisingCube : MonoBehaviour
    {
        private Vector3 column;
        private float startY;
        private float peakY;
        private float riseDuration;
        private float fallDuration;
        private float elapsed;

        /// <summary>
        /// Starts the arc. <paramref name="startOffset"/> is normally negative
        /// so the cube begins buried and appears to rise out of the ground.
        /// </summary>
        public void Play(float groundHeight, float startOffset, float riseHeight,
                         float rise, float fall)
        {
            column = transform.position;
            startY = groundHeight + startOffset;
            peakY = groundHeight + riseHeight;

            // Guard against zero so a mistyped duration cannot divide by zero
            // or freeze the cube on screen forever.
            riseDuration = Mathf.Max(0.0001f, rise);
            fallDuration = Mathf.Max(0.0001f, fall);

            elapsed = 0f;
            transform.position = new Vector3(column.x, startY, column.z);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            float height;

            if (elapsed < riseDuration)
            {
                height = Mathf.Lerp(startY, peakY, elapsed / riseDuration);
            }
            else if (elapsed < riseDuration + fallDuration)
            {
                height = Mathf.Lerp(peakY, startY, (elapsed - riseDuration) / fallDuration);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            transform.position = new Vector3(column.x, height, column.z);
        }
    }
}
