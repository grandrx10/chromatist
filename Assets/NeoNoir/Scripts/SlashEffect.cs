using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Fires screen-space slash frames: a thin inverted streak cut across the
    /// screen along the blade's direction of travel when it connects.
    /// </summary>
    /// <remarks>
    /// The directional sibling of <see cref="ImpactFrames"/>, drawn in the same
    /// fullscreen pass and in the same two-tone inversion, so it stays on-style
    /// and blooms the same way.
    ///
    /// <see cref="intensity"/> is the one knob to reach for when hits start to
    /// overwhelm: it scales the size of the streak, how strongly it inverts the
    /// frame and how hard it blows out, all together. Fast multi-hit attacks
    /// want it low; a single heavy blow can take it high.
    ///
    /// The angle is fixed at the moment of the hit, but the position is
    /// reprojected every frame so the streak stays on the enemy that was cut.
    /// </remarks>
    public sealed class SlashEffect : MonoBehaviour
    {
        /// <summary>Must match NEONOIR_MAX_SLASHES in NeoNoirEdges.shader.</summary>
        public const int MaxConcurrent = 4;

        [Header("Power")]
        [SerializeField]
        [Tooltip("Master strength. 0 is off; 1 is a full-size, hard inversion. Scales size, opacity and brightness together.")]
        [Range(0f, 1f)]
        private float intensity = 0.6f;

        [SerializeField]
        [Tooltip("Brightness of the streak at full intensity. Above 1 it blows out through bloom.")]
        [Range(1f, 6f)]
        private float brightness = 1.8f;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Total life of one slash. Keep it short; it should register, not linger.")]
        [Range(0.03f, 0.5f)]
        private float duration = 0.12f;

        [SerializeField]
        [Tooltip("Discrete steps the slash snaps through. 2-4 reads as drawn frames.")]
        [Range(2, 8)]
        private int frameSteps = 3;

        [Header("Shape")]
        [SerializeField]
        [Tooltip("Length of the streak at full intensity, in fractions of screen height.")]
        [Range(0.05f, 1.5f)]
        private float length = 0.4f;

        [SerializeField]
        [Tooltip("Thickness at the middle of the streak at full intensity, in fractions of screen height.")]
        [Range(0.002f, 0.15f)]
        private float width = 0.03f;

        [SerializeField]
        [Tooltip("Random degrees added to each slash's angle, so a flurry does not draw parallel lines.")]
        [Range(0f, 45f)]
        private float angleJitter = 12f;

        [SerializeField]
        [Tooltip("Camera used to project slashes. Empty uses Camera.main.")]
        private Camera targetCamera;

        private static readonly int SlashesId = Shader.PropertyToID("_NeoNoirSlashes");
        private static readonly int StepsId = Shader.PropertyToID("_NeoNoirSlashSteps");
        private static readonly int LengthId = Shader.PropertyToID("_NeoNoirSlashLength");
        private static readonly int WidthId = Shader.PropertyToID("_NeoNoirSlashWidth");
        private static readonly int StrengthId = Shader.PropertyToID("_NeoNoirSlashStrength");
        private static readonly int BoostId = Shader.PropertyToID("_NeoNoirSlashBoost");
        private static readonly int StylesId = Shader.PropertyToID("_NeoNoirSlashStyles");

        private readonly Vector3[] positions = new Vector3[MaxConcurrent];
        private readonly float[] angles = new float[MaxConcurrent];
        private readonly float[] startTimes = new float[MaxConcurrent];
        private readonly bool[] active = new bool[MaxConcurrent];
        private readonly Vector4[] packed = new Vector4[MaxConcurrent];

        // rgb = tint (black = plain inversion), w = size scale.
        private readonly Vector4[] styles = new Vector4[MaxConcurrent];

        private Camera ResolvedCamera => targetCamera != null ? targetCamera : Camera.main;

        /// <summary>The enabled slash effect in the scene, if any. Lets attacks fire it without holding a reference.</summary>
        public static SlashEffect Current { get; private set; }

        private void OnEnable()
        {
            Current = this;

            for (int i = 0; i < MaxConcurrent; i++)
            {
                active[i] = false;
            }

            Upload();
        }

        private void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
            }

            // Leave no slash frozen on screen if this is switched off mid-flash.
            for (int i = 0; i < MaxConcurrent; i++)
            {
                active[i] = false;
            }

            Upload();
        }

        private void Update()
        {
            Upload();
        }

        /// <summary>
        /// Spawns a slash centred on a world position, cut along a world
        /// direction -- normally the blade's velocity at contact.
        /// <paramref name="scale"/> multiplies its length and width.
        /// <paramref name="tint"/>, if not black, draws it in that colour
        /// instead of the two-tone inversion; HDR values bloom harder.
        /// </summary>
        public void TriggerSlash(Vector3 worldPosition, Vector3 worldDirection,
                                 float scale = 1f, Color tint = default)
        {
            Camera camera = ResolvedCamera;

            if (camera == null || intensity <= 0f)
            {
                return;
            }

            // The angle is taken on screen, not in the world: what has to line
            // up with the cut is the line the player saw the blade travel.
            Vector3 from = camera.WorldToViewportPoint(worldPosition);
            Vector3 to = camera.WorldToViewportPoint(worldPosition + worldDirection.normalized);
            Vector2 screen = new Vector2((to.x - from.x) * camera.aspect, to.y - from.y);

            // A blade moving straight at the camera, or not at all, has no
            // direction on screen to cut along.
            float angle = screen.sqrMagnitude > 0.000001f
                ? Mathf.Atan2(screen.y, screen.x)
                : Random.Range(0f, Mathf.PI * 2f);

            angle += Random.Range(-angleJitter, angleJitter) * Mathf.Deg2Rad;

            int slot = ResolveSlot();

            positions[slot] = worldPosition;
            angles[slot] = angle;
            startTimes[slot] = Time.time;
            styles[slot] = new Vector4(tint.r, tint.g, tint.b, Mathf.Max(0.01f, scale));
            active[slot] = true;
        }

        /// <summary>Picks a free slot, or steals the oldest slash when all are busy.</summary>
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
                packed[i] = new Vector4(0f, 0f, -1f, 0f);

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

                packed[i] = new Vector4(viewport.x, viewport.y, progress, angles[i]);
            }

            // Size only shrinks to 40% at the bottom of the range, so a low
            // intensity is a fainter, smaller slash rather than an invisible one.
            float size = Mathf.Lerp(0.4f, 1f, intensity);

            Shader.SetGlobalVectorArray(SlashesId, packed);
            Shader.SetGlobalVectorArray(StylesId, styles);
            Shader.SetGlobalFloat(StepsId, frameSteps);
            Shader.SetGlobalFloat(LengthId, length * size);
            Shader.SetGlobalFloat(WidthId, width * size);
            Shader.SetGlobalFloat(StrengthId, intensity);
            Shader.SetGlobalFloat(BoostId, Mathf.Lerp(1f, brightness, intensity));
        }
    }
}
