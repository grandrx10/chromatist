using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// Spawns a <see cref="RisingCubeProjectile"/> at the right-clicked point,
    /// aimed at the player.
    /// </summary>
    /// <remarks>
    /// Holds every tunable value for both the projectile and the cubes it drops
    /// and hands them over at launch, so the whole attack is tuned from one
    /// inspector rather than from a prefab you have to open.
    /// </remarks>
    public sealed class RisingCubeLauncher : MonoBehaviour
    {
        /// <summary>The full parameter set handed to a projectile at launch.</summary>
        public struct Settings
        {
            public float Speed;
            public float Lifetime;
            public float CubeSpacing;
            public float GroundHeight;

            public GameObject CubePrefab;
            public float CubeStartOffset;
            public float CubeRiseHeight;
            public float CubeRiseDuration;
            public float CubeFallDuration;
        }

        [Header("Trigger")]
        [SerializeField]
        [Tooltip("Right click spawns a projectile at the clicked point.")]
        private bool triggerOnRightClick = true;

        [SerializeField]
        [Tooltip("Camera used to resolve the click. Empty uses Camera.main.")]
        private Camera targetCamera;

        [SerializeField] private LayerMask clickMask = ~0;

        [Header("Target")]
        [SerializeField]
        [Tooltip("What the projectile aims at. Empty finds the player automatically.")]
        private Transform target;

        [Header("Projectile")]
        [SerializeField] private float speed = 12f;

        [SerializeField]
        [Tooltip("Seconds before the projectile removes itself.")]
        private float lifetime = 3f;

        [SerializeField]
        [Tooltip("Distance travelled between cubes. Independent of speed.")]
        private float cubeSpacing = 2f;

        [SerializeField]
        [Tooltip("Y position of the floor the cubes rise out of.")]
        private float groundHeight;

        [Header("Cube")]
        [SerializeField]
        [Tooltip("Your rising cube prefab. Motion is added at spawn time, so the prefab needs no script.")]
        private GameObject cubePrefab;

        [SerializeField]
        [Tooltip("Starting height relative to the ground. Negative starts it buried.")]
        private float cubeStartOffset = -1f;

        [SerializeField]
        [Tooltip("Peak height above the ground.")]
        private float cubeRiseHeight = 2f;

        [SerializeField] private float cubeRiseDuration = 0.15f;
        [SerializeField] private float cubeFallDuration = 0.35f;

        private Camera ResolvedCamera => targetCamera != null ? targetCamera : Camera.main;

        private void Update()
        {
            if (!triggerOnRightClick || Mouse.current == null)
            {
                return;
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                FireAtScreenPoint(Mouse.current.position.ReadValue());
            }
        }

        /// <summary>
        /// Resolves a screen point to a world position and launches from there.
        /// Returns false if there is no camera, no target, or nothing to hit.
        /// </summary>
        public bool FireAtScreenPoint(Vector2 screenPoint)
        {
            Camera camera = ResolvedCamera;

            if (camera == null)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(screenPoint);

            if (Physics.Raycast(ray, out RaycastHit hit, camera.farClipPlane, clickMask, QueryTriggerInteraction.Ignore))
            {
                return Fire(hit.point);
            }

            // Nothing was hit, so fall back to the floor plane rather than
            // silently doing nothing when the click lands on empty background.
            Plane ground = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));

            return ground.Raycast(ray, out float enter) && Fire(ray.GetPoint(enter));
        }

        /// <summary>Launches a projectile from a world position toward the target.</summary>
        public bool Fire(Vector3 origin)
        {
            Transform aim = ResolveTarget();

            if (aim == null)
            {
                return false;
            }

            origin.y = groundHeight;

            GameObject projectile = new GameObject("RisingCubeProjectile");
            projectile.transform.position = origin;

            projectile.AddComponent<RisingCubeProjectile>().Launch(aim.position - origin, BuildSettings());

            return true;
        }

        private Settings BuildSettings()
        {
            return new Settings
            {
                Speed = speed,
                Lifetime = lifetime,
                CubeSpacing = cubeSpacing,
                GroundHeight = groundHeight,
                CubePrefab = cubePrefab,
                CubeStartOffset = cubeStartOffset,
                CubeRiseHeight = cubeRiseHeight,
                CubeRiseDuration = cubeRiseDuration,
                CubeFallDuration = cubeFallDuration,
            };
        }

        private Transform ResolveTarget()
        {
            if (target != null)
            {
                return target;
            }

            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (player != null)
            {
                target = player.transform;
            }

            return target;
        }
    }
}
