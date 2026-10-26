using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A wave of earth cubes running out along the floor: one cube rises,
    /// then the next a step further on, and so on, each falling back as the
    /// wave moves past it.
    /// </summary>
    /// <remarks>
    /// The direction and the floor height are fixed at the start, so the
    /// wave runs dead straight and can be sidestepped -- unless
    /// <see cref="Settings.curvePerCube"/> bends it, each cube turning a
    /// little further than the last, so it sweeps round in an arc. It goes
    /// through walls: its length is a fixed number of cubes.
    ///
    /// Cubes come from <see cref="Settings.cubePrefab"/>, which is pure art.
    /// Its own <see cref="RisingCube"/> moves it, or one is added if missing,
    /// and an <see cref="EarthRippleCube"/> is added for damage. Every
    /// collider on it is switched off, so a rising cube never shoves the
    /// player about; hits are tested by the cube itself.
    ///
    /// Each cube is a projectile, so a <see cref="ParryBarrier"/> can catch
    /// it. A caught cube is gone, and with <see cref="Settings.parryStopsWave"/>
    /// so is the rest of the wave.
    ///
    /// Spawned at runtime with no prefab of its own. Removes itself once the
    /// last cube is up; the cubes clean themselves up after.
    /// </remarks>
    public sealed class EarthRipple : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Your earth cube prefab. Needs no script or collider.")]
            public GameObject cubePrefab;

            [Tooltip("Cubes in the wave, the first one included.")]
            [Min(1)] public int count = 10;

            [Tooltip("Distance between one cube and the next. Match the cube's size for an unbroken wall.")]
            [Min(0.1f)] public float spacing = 3f;

            [Tooltip("Seconds between one cube rising and the next.")]
            [Min(0f)] public float interval = 0.1f;

            [Tooltip("Degrees the wave turns with each cube, so it sweeps round in an arc. Positive turns clockwise seen from above, negative anticlockwise; 0 runs straight.")]
            public float curvePerCube;

            [Tooltip("Damage each cube deals to the player it touches. A player caught by several cubes takes it from each.")]
            [Min(0f)] public float damage = 10f;

            [Tooltip("A parried cube stops the rest of the wave too.")]
            public bool parryStopsWave = true;

            [Header("Cube motion")]
            [Tooltip("Height of the cube's pivot relative to the floor before it rises. Low enough to bury it completely.")]
            public float startOffset = -1.6f;

            [Tooltip("Height of the cube's pivot relative to the floor at the top of its rise.")]
            public float riseHeight = 0.5f;

            [Tooltip("Seconds each cube takes to rise.")]
            [Min(0.01f)] public float riseTime = 0.12f;

            [Tooltip("Seconds each cube takes to sink back into the floor.")]
            [Min(0.01f)] public float fallTime = 0.4f;
        }

        private Settings settings;
        private Vector3 origin;
        private Vector3 direction;
        private float groundHeight;
        private float age;
        private int spawned;

        // Where the next cube rises, and the way the wave is heading there.
        private Vector3 cubePosition;
        private Vector3 cubeDirection;

        /// <summary>
        /// Starts a wave whose first cube rises at <paramref name="floorPoint"/>,
        /// a point on the floor, running along <paramref name="travelDirection"/>.
        /// </summary>
        public static EarthRipple Create(Vector3 floorPoint, Vector3 travelDirection, Settings settings)
        {
            GameObject host = new GameObject("EarthRipple");
            EarthRipple ripple = host.AddComponent<EarthRipple>();
            ripple.Begin(floorPoint, travelDirection, settings);
            return ripple;
        }

        /// <summary>Ends the wave where it is. Cubes already up finish rising and falling.</summary>
        public void Stop()
        {
            Destroy(gameObject);
        }

        private void Begin(Vector3 floorPoint, Vector3 travelDirection, Settings rippleSettings)
        {
            settings = rippleSettings;
            origin = floorPoint;
            groundHeight = floorPoint.y;

            travelDirection.y = 0f;
            direction = travelDirection.sqrMagnitude > 0.0001f ? travelDirection.normalized : Vector3.forward;

            transform.position = origin;
            cubePosition = origin;
            cubeDirection = direction;

            if (settings.cubePrefab == null)
            {
                Debug.LogWarning("[EarthRipple] No cube prefab assigned.");
                Destroy(gameObject);
                return;
            }

            // The first cube rises the moment the hammer lands.
            Advance();
        }

        private void Update()
        {
            age += Time.deltaTime;
            Advance();
        }

        /// <summary>Raises every cube that is due by now, catching up if a long frame skipped some.</summary>
        private void Advance()
        {
            if (settings.cubePrefab == null)
            {
                return;
            }

            while (spawned < settings.count && age >= settings.interval * spawned)
            {
                SpawnCube();
                spawned++;
            }

            if (spawned >= settings.count)
            {
                Destroy(gameObject);
            }
        }

        private void SpawnCube()
        {
            Quaternion rotation = Quaternion.LookRotation(cubeDirection, Vector3.up) * settings.cubePrefab.transform.rotation;

            GameObject cube = Instantiate(settings.cubePrefab, cubePosition, rotation);

            foreach (Collider part in cube.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            RisingCube motion = cube.GetComponent<RisingCube>();

            if (motion == null)
            {
                motion = cube.AddComponent<RisingCube>();
            }

            motion.Play(groundHeight, settings.startOffset, settings.riseHeight, settings.riseTime, settings.fallTime);

            // How fast the wave moves, for the parry sparks.
            Vector3 waveVelocity = settings.interval > 0f ? cubeDirection * (settings.spacing / settings.interval) : cubeDirection;

            cube.AddComponent<EarthRippleCube>().Arm(settings.damage, groundHeight, waveVelocity, OnCubeParried);

            cubeDirection = Quaternion.AngleAxis(settings.curvePerCube, Vector3.up) * cubeDirection;
            cubePosition += cubeDirection * settings.spacing;
        }

        private void OnCubeParried()
        {
            if (this != null && settings.parryStopsWave)
            {
                Stop();
            }
        }
    }
}
