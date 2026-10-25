using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// An invisible projectile that travels in a fixed straight line, dropping
    /// a rising cube at regular distance intervals, until its lifetime expires.
    /// </summary>
    /// <remarks>
    /// The direction is captured once in <see cref="Launch"/> and never
    /// recomputed, so the trail is a dodgeable straight line rather than a
    /// homing attack.
    ///
    /// Cube spacing is measured in DISTANCE, not time. That means changing the
    /// projectile speed alters how fast the trail arrives without changing how
    /// far apart the cubes stand, so the two can be tuned independently.
    /// </remarks>
    public sealed class RisingCubeProjectile : MonoBehaviour
    {
        private Vector3 direction;
        private float speed;
        private float lifetime;
        private float cubeSpacing;
        private float groundHeight;

        private GameObject cubePrefab;
        private float cubeStartOffset;
        private float cubeRiseHeight;
        private float cubeRiseDuration;
        private float cubeFallDuration;

        private float age;
        private float distanceSinceLastCube;

        /// <summary>
        /// Arms and starts the projectile. The direction is flattened onto the
        /// XZ plane and locked for the rest of its life.
        /// </summary>
        public void Launch(Vector3 travelDirection, RisingCubeLauncher.Settings settings)
        {
            Vector3 flattened = new Vector3(travelDirection.x, 0f, travelDirection.z);

            // A launch straight down the Y axis has no usable heading, so fall
            // back to forward rather than normalising a zero vector.
            direction = flattened.sqrMagnitude > 0.000001f ? flattened.normalized : Vector3.forward;

            speed = settings.Speed;
            lifetime = settings.Lifetime;
            cubeSpacing = Mathf.Max(0.001f, settings.CubeSpacing);
            groundHeight = settings.GroundHeight;

            cubePrefab = settings.CubePrefab;
            cubeStartOffset = settings.CubeStartOffset;
            cubeRiseHeight = settings.CubeRiseHeight;
            cubeRiseDuration = settings.CubeRiseDuration;
            cubeFallDuration = settings.CubeFallDuration;

            age = 0f;
            distanceSinceLastCube = 0f;
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;

            transform.position += direction * step;

            age += Time.deltaTime;
            distanceSinceLastCube += step;

            // A while loop, not an if: at high speed or a long frame a single
            // step can cover several spacings, and every one of them still owes
            // a cube.
            while (distanceSinceLastCube >= cubeSpacing)
            {
                distanceSinceLastCube -= cubeSpacing;
                SpawnCube();
            }

            if (age >= lifetime)
            {
                Destroy(gameObject);
            }
        }

        private void SpawnCube()
        {
            if (cubePrefab == null)
            {
                return;
            }

            Vector3 position = new Vector3(transform.position.x, groundHeight, transform.position.z);

            GameObject cube = Instantiate(cubePrefab, position, cubePrefab.transform.rotation);

            RisingCube motion = cube.GetComponent<RisingCube>();

            if (motion == null)
            {
                motion = cube.AddComponent<RisingCube>();
            }

            motion.Play(groundHeight, cubeStartOffset, cubeRiseHeight, cubeRiseDuration, cubeFallDuration);
        }
    }
}
