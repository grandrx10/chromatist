using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Keeps the liquid in a bottle level and sloshing: drives a
    /// NeoNoir/Liquid material's fill and wobble from how the bottle moves.
    /// </summary>
    /// <remarks>
    /// Goes on the liquid mesh itself, inside the bottle. The waterline sits
    /// <see cref="fill"/> of the way up the mesh, measured from its lowest
    /// point to its highest in whatever way it is turned, so a bottle on its
    /// side is still the same amount full.
    ///
    /// Moving or turning the bottle tips the waterline, which then rocks back
    /// and forth at <see cref="wobbleSpeed"/> and settles over time. Nothing
    /// is simulated: it is a damped swing, which is all a bottle this size
    /// needs.
    ///
    /// Set through a property block, never by editing the material, so every
    /// bottle sloshes on its own, and the material's own Fill Level and
    /// wobble are ignored. Runs in LateUpdate, after anything that moves the
    /// bottle this frame, an animator included. Runs in the editor too, so
    /// <see cref="fill"/> previews outside play mode, without the slosh.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class PotionLiquid : MonoBehaviour
    {
        private static readonly int FillLevelId = Shader.PropertyToID("_FillLevel");
        private static readonly int WobbleXId = Shader.PropertyToID("_WobbleX");
        private static readonly int WobbleZId = Shader.PropertyToID("_WobbleZ");

        [SerializeField]
        [Tooltip("How full the bottle is, from the bottom of the liquid mesh (0) to the top (1).")]
        [Range(0f, 1f)]
        private float fill = 0.6f;

        [Header("Slosh")]
        [SerializeField]
        [Tooltip("Steepest the waterline ever tips. 1 is 45 degrees.")]
        [Min(0f)]
        private float maxWobble = 0.5f;

        [SerializeField]
        [Tooltip("How much moving the bottle tips the waterline, per unit per second of speed.")]
        [Min(0f)]
        private float moveSensitivity = 0.04f;

        [SerializeField]
        [Tooltip("How much turning the bottle tips the waterline, per degree per second.")]
        [Min(0f)]
        private float turnSensitivity = 0.0008f;

        [SerializeField]
        [Tooltip("Rocks back and forth this many times a second.")]
        [Min(0f)]
        private float wobbleSpeed = 2.5f;

        [SerializeField]
        [Tooltip("How quickly the slosh dies away. Higher settles sooner.")]
        [Min(0f)]
        private float settle = 2f;

        private Renderer target;
        private MaterialPropertyBlock block;

        private Vector3 localCentre;
        private float localRadius;

        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private Vector2 slosh;
        private float clock;

        /// <summary>How full the bottle is, 0 to 1.</summary>
        public float Fill
        {
            get => fill;
            set => fill = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            target = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            MeasureMesh();

            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        /// <summary>
        /// The mesh's centre, and how far its furthest vertex is from it: half
        /// the liquid's height in any direction it can be turned.
        /// </summary>
        private void MeasureMesh()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;

            if (mesh == null)
            {
                localCentre = Vector3.zero;
                localRadius = 0.5f;
                return;
            }

            localCentre = mesh.bounds.center;
            localRadius = 0f;

            foreach (Vector3 vertex in mesh.vertices)
            {
                localRadius = Mathf.Max(localRadius, (vertex - localCentre).magnitude);
            }
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            // Outside play mode it only previews the fill: no slosh from
            // dragging it round the scene.
            if (!Application.isPlaying)
            {
                slosh = Vector2.zero;
            }
            else if (deltaTime > 0f)
            {
                Slosh(deltaTime);
            }

            Vector3 pivot = transform.position;
            Vector3 centre = transform.TransformPoint(localCentre) - pivot;

            Vector3 scale = transform.lossyScale;
            float radius = localRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            float tiltX = slosh.x * Mathf.Sin(clock * Mathf.PI * 2f * wobbleSpeed);
            float tiltZ = slosh.y * Mathf.Sin(clock * Mathf.PI * 2f * wobbleSpeed);

            // The shader measures from the pivot, along the tipped
            // waterline; this puts the line through the mesh's centre, then
            // up or down by the fill.
            float level = centre.y + centre.x * tiltX + centre.z * tiltZ + Mathf.Lerp(-radius, radius, fill);

            target.GetPropertyBlock(block);
            block.SetFloat(FillLevelId, level);
            block.SetFloat(WobbleXId, tiltX);
            block.SetFloat(WobbleZId, tiltZ);
            target.SetPropertyBlock(block);
        }

        /// <summary>Tips the waterline by how the bottle moved this frame, and lets it settle.</summary>
        private void Slosh(float deltaTime)
        {
            clock += deltaTime;
            slosh = Vector2.Lerp(slosh, Vector2.zero, Mathf.Clamp01(deltaTime * settle));

            Vector3 velocity = (transform.position - lastPosition) / deltaTime;

            (transform.rotation * Quaternion.Inverse(lastRotation)).ToAngleAxis(out float angle, out Vector3 axis);

            if (angle > 180f)
            {
                angle -= 360f;
            }

            Vector3 spin = float.IsInfinity(axis.x) ? Vector3.zero : axis * (angle / deltaTime);

            // Liquid lags behind the bottle: moving toward +X piles it up on
            // the -X side, which is a positive tilt. A turn about Z tips it
            // along X, and about X along Z.
            slosh.x += velocity.x * moveSensitivity + spin.z * turnSensitivity;
            slosh.y += velocity.z * moveSensitivity - spin.x * turnSensitivity;
            slosh = Vector2.ClampMagnitude(slosh, maxWobble);

            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        private void OnEnable()
        {
            // Picks up from wherever the bottle is when it comes back, not
            // from where it was left.
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }
    }
}
