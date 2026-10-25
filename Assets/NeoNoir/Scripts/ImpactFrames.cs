using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// Fires screen-space impact frames: the brief, hard-edged two-tone flash
    /// that sells a sword connecting.
    /// </summary>
    /// <remarks>
    /// This is not a particle system and deliberately so. An impact frame is a
    /// full-screen graphic event held for two to four drawn frames, not a cloud
    /// of sprites, so it lives in the fullscreen pass alongside the outlines and
    /// the threshold. That placement also means the burst is inverted BEFORE the
    /// Volume bloom, so its whites blow out on their own.
    ///
    /// Impacts are anchored to a world position and reprojected every frame, so
    /// a burst stays on the guard that was hit rather than sliding with the
    /// camera. Up to <see cref="MaxConcurrent"/> can overlap, which matters once
    /// several guards die inside the same swing.
    ///
    /// Left click is a convenience trigger for feeling the effect out. The real
    /// entry point is <see cref="TriggerImpact"/> -- call it from the weapon hit
    /// resolution and turn the click off.
    /// </remarks>
    public sealed class ImpactFrames : MonoBehaviour
    {
        /// <summary>Must match NEONOIR_MAX_IMPACTS in NeoNoirEdges.shader.</summary>
        public const int MaxConcurrent = 4;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Total life of one burst. Real impact frames are very short.")]
        [Range(0.03f, 0.5f)]
        private float duration = 0.13f;

        [SerializeField]
        [Tooltip("Discrete steps the burst snaps through. 2-4 reads as animation; high values turn it into a smooth ripple.")]
        [Range(2, 8)]
        private int frameSteps = 3;

        [Header("Shape")]
        [SerializeField]
        [Tooltip("Final radius, in fractions of screen height.")]
        [Range(0.02f, 1f)]
        private float radius = 0.28f;

        [SerializeField]
        [Tooltip("Number of spikes. Lower is chunkier and more graphic.")]
        [Range(3, 40)]
        private float sectors = 14f;

        [SerializeField]
        [Tooltip("How deep the valleys between spikes cut. 0 is a clean circle.")]
        [Range(0f, 0.95f)]
        private float jaggedness = 0.7f;

        [SerializeField]
        [Tooltip("Spike thinness. Around 1 gives fat wedges; higher gives thin needles.")]
        [Range(0.3f, 6f)]
        private float sharpness = 2.4f;

        [Header("Punch")]
        [SerializeField]
        [Tooltip("Size of the first held frame, as a multiple of Radius. This is the flash that sells the hit.")]
        [Range(1f, 5f)]
        private float firstFrameScale = 2.4f;

        [SerializeField]
        [Tooltip("Brightness of the inverted flash. Above 1 it blows out through bloom.")]
        [Range(1f, 6f)]
        private float brightness = 2.2f;

        [Header("Debug trigger")]
        [SerializeField]
        [Tooltip("Left click spawns an impact. Turn off once real weapon hits drive it.")]
        private bool triggerOnLeftClick = true;

        [SerializeField]
        [Tooltip("Camera used to place clicks and project bursts. Empty uses Camera.main.")]
        private Camera targetCamera;

        [SerializeField] private LayerMask clickMask = ~0;

        [SerializeField]
        [Tooltip("Height of the fallback plane used when a click hits nothing.")]
        private float fallbackPlaneHeight;

        private static readonly int ImpactsId = Shader.PropertyToID("_NeoNoirImpacts");
        private static readonly int StepsId = Shader.PropertyToID("_NeoNoirImpactSteps");
        private static readonly int SectorsId = Shader.PropertyToID("_NeoNoirImpactSectors");
        private static readonly int JagId = Shader.PropertyToID("_NeoNoirImpactJag");
        private static readonly int SharpId = Shader.PropertyToID("_NeoNoirImpactSharp");
        private static readonly int FlashId = Shader.PropertyToID("_NeoNoirImpactFlash");
        private static readonly int BoostId = Shader.PropertyToID("_NeoNoirImpactBoost");

        private readonly Vector3[] positions = new Vector3[MaxConcurrent];
        private readonly float[] startTimes = new float[MaxConcurrent];
        private readonly bool[] active = new bool[MaxConcurrent];
        private readonly Vector4[] packed = new Vector4[MaxConcurrent];

        private Camera ResolvedCamera => targetCamera != null ? targetCamera : Camera.main;

        private void OnEnable()
        {
            for (int i = 0; i < MaxConcurrent; i++)
            {
                active[i] = false;
            }

            Upload();
        }

        private void OnDisable()
        {
            // Leave no burst frozen on screen if this is switched off mid-flash.
            for (int i = 0; i < MaxConcurrent; i++)
            {
                active[i] = false;
            }

            Upload();
        }

        private void Update()
        {
            if (triggerOnLeftClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TriggerAtScreenPoint(Mouse.current.position.ReadValue());
            }

            Upload();
        }

        /// <summary>
        /// Spawns an impact frame centred on a world position. This is the hook
        /// for weapon hit resolution.
        /// </summary>
        public void TriggerImpact(Vector3 worldPosition)
        {
            int slot = ResolveSlot();

            positions[slot] = worldPosition;
            startTimes[slot] = Time.time;
            active[slot] = true;
        }

        /// <summary>
        /// Spawns an impact frame at whatever a screen point is pointing at.
        /// Returns false only if there is no usable camera.
        /// </summary>
        public bool TriggerAtScreenPoint(Vector2 screenPoint)
        {
            Camera camera = ResolvedCamera;

            if (camera == null)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(screenPoint);

            if (Physics.Raycast(ray, out RaycastHit hit, camera.farClipPlane, clickMask, QueryTriggerInteraction.Ignore))
            {
                TriggerImpact(hit.point);
                return true;
            }

            // Nothing was hit, so fall back to the gameplay plane. Without this
            // a click on empty sky would silently do nothing.
            Plane plane = new Plane(Vector3.up, new Vector3(0f, fallbackPlaneHeight, 0f));

            if (plane.Raycast(ray, out float enter))
            {
                TriggerImpact(ray.GetPoint(enter));
                return true;
            }

            return false;
        }

        /// <summary>Picks a free slot, or steals the oldest burst when all are busy.</summary>
        private int ResolveSlot()
        {
            int oldest = 0;
            float oldestStart = float.MaxValue;

            for (int i = 0; i < MaxConcurrent; i++)
            {
                if (!active[i])
                {
                    return i;
                }

                if (startTimes[i] < oldestStart)
                {
                    oldestStart = startTimes[i];
                    oldest = i;
                }
            }

            return oldest;
        }

        private void Upload()
        {
            Camera camera = ResolvedCamera;

            for (int i = 0; i < MaxConcurrent; i++)
            {
                // Negative progress is the shader's "inactive" signal.
                packed[i] = new Vector4(0f, 0f, -1f, radius);

                if (!active[i] || camera == null)
                {
                    continue;
                }

                float progress = (Time.time - startTimes[i]) / Mathf.Max(0.001f, duration);

                if (progress >= 1f)
                {
                    active[i] = false;
                    continue;
                }

                Vector3 viewport = camera.WorldToViewportPoint(positions[i]);

                // Behind the camera: skip rather than draw a mirrored ghost.
                if (viewport.z <= 0f)
                {
                    continue;
                }

                packed[i] = new Vector4(viewport.x, viewport.y, progress, radius);
            }

            Shader.SetGlobalVectorArray(ImpactsId, packed);
            Shader.SetGlobalFloat(StepsId, frameSteps);
            Shader.SetGlobalFloat(SectorsId, sectors);
            Shader.SetGlobalFloat(JagId, jaggedness);
            Shader.SetGlobalFloat(SharpId, sharpness);
            Shader.SetGlobalFloat(FlashId, firstFrameScale);
            Shader.SetGlobalFloat(BoostId, brightness);
        }
    }
}
