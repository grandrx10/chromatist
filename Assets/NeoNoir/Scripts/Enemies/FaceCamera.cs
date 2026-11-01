using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Keeps flat art turned to face the camera, like a sprite, whatever its
    /// parent does, rocking side to side about the axis toward the viewer.
    /// </summary>
    /// <remarks>
    /// Turned in LateUpdate, after whatever moves the parent. Added at runtime
    /// to note art carried by a <see cref="HomingMissile"/>, which turns its
    /// own object along its flight. The art is taken to face -Z, toward the
    /// viewer, as modelled.
    /// </remarks>
    public sealed class FaceCamera : MonoBehaviour
    {
        private float rock;
        private float rockRate;
        private float phase;

        /// <summary>Adds one to <paramref name="art"/>, rocking <paramref name="degrees"/> either way, <paramref name="perSecond"/> times a second.</summary>
        public static FaceCamera Add(GameObject art, float degrees, float perSecond)
        {
            FaceCamera facing = art.AddComponent<FaceCamera>();
            facing.rock = degrees;
            facing.rockRate = perSecond;
            facing.phase = Random.Range(0f, Mathf.PI * 2f);
            facing.LateUpdate();
            return facing;
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            float angle = Mathf.Sin(phase + Time.time * rockRate * Mathf.PI * 2f) * rock;
            transform.rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
