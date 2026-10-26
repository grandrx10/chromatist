using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// Fixed-angle top-down follow camera with player-controlled yaw.
    /// </summary>
    /// <remarks>
    /// The camera follows the player's POSITION and nothing else. It never
    /// inherits the character's rotation, which is why it must not be parented
    /// to the player: the character spins to face its movement direction, and a
    /// camera that spun with it would be unusable.
    ///
    /// <see cref="pitch"/> is the dial worth playing with. 90 is straight down
    /// and reads flattest; lower values show more of the character silhouette
    /// and will matter more once there is a cape to see.
    ///
    /// Yaw is driven by Q and E. Because movement input is camera-relative,
    /// rotating the camera also rotates what WASD means -- which is what makes
    /// rotation feel like turning the world rather than sliding the view.
    ///
    /// <see cref="Shake"/> jolts the view without disturbing the follow: the
    /// smoothed follow position is kept apart from the shake, so a shake
    /// never drags the camera off its target.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField]
        [Tooltip("Leave empty to find the player automatically, including after a respawn.")]
        private Transform target;

        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1f, 0f);

        [SerializeField]
        [Tooltip("Seconds between attempts to find a player when none exists yet.")]
        private float targetSearchInterval = 0.25f;

        [Header("Framing")]
        [SerializeField]
        [Tooltip("90 is straight down. Lower shows more of the character's silhouette.")]
        [Range(30f, 90f)]
        private float pitch = 70f;

        [SerializeField]
        [Tooltip("Rotation about the world Y axis. Movement input follows this.")]
        private float yaw;

        [SerializeField] private float distance = 20f;

        [Header("Rotation input")]
        [SerializeField]
        [Tooltip("Q and E rotate the camera while held. Right stick X on a gamepad.")]
        private bool allowRotationInput = true;

        [SerializeField]
        [Tooltip("Degrees per second while held. Negative swaps which key goes which way.")]
        private float rotationSpeed = 90f;

        [Header("Follow")]
        [SerializeField]
        [Tooltip("0 locks the camera rigidly to the target.")]
        private float followSmoothTime = 0.12f;

        [Header("Look ahead")]
        [SerializeField]
        [Tooltip("Leads the target by its own velocity. 0 disables it.")]
        private float lookAheadDistance;

        [SerializeField] private float lookAheadSmoothTime = 0.35f;

        [SerializeField]
        [Tooltip("Target speed treated as full lead.")]
        private float lookAheadReferenceSpeed = 9f;

        [Header("Shake")]
        [SerializeField]
        [Tooltip("How many times a second a shake jolts back and forth.")]
        [Min(0.1f)]
        private float shakeFrequency = 25f;

        [Header("Occlusion")]
        [SerializeField] private OccluderFader occlusion = new OccluderFader();

        // Shared by every camera, so whoever triggers a shake needs no
        // reference to one.
        private static float shakeStrength;
        private static float shakeDuration;
        private static float shakeStartTime;

        private Vector3 followPosition;
        private Vector3 followVelocity;
        private Vector3 lookAhead;
        private Vector3 lookAheadVelocity;
        private Rigidbody targetBody;
        private PlayerInputReader targetInput;
        private float nextTargetSearchTime;

        public Transform Target
        {
            get => target;
            set
            {
                target = value;
                targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
                targetInput = target != null ? target.GetComponent<PlayerInputReader>() : null;
                SnapToTarget();
            }
        }

        /// <summary>
        /// Shakes the camera by up to <paramref name="strength"/> units,
        /// dying away over <paramref name="duration"/> seconds. A weaker
        /// shake never cuts a stronger one short.
        /// </summary>
        public static void Shake(float strength, float duration)
        {
            if (strength <= 0f || duration <= 0f || strength < CurrentShake())
            {
                return;
            }

            shakeStrength = strength;
            shakeDuration = duration;
            shakeStartTime = Time.time;
        }

        /// <summary>How far a shake can move the camera right now: its strength, eased down to nothing.</summary>
        private static float CurrentShake()
        {
            if (shakeDuration <= 0f)
            {
                return 0f;
            }

            float remaining = 1f - (Time.time - shakeStartTime) / shakeDuration;
            return remaining > 0f ? shakeStrength * remaining * remaining : 0f;
        }

        private void OnEnable()
        {
            AcquireTarget();
            SnapToTarget();
        }

        private void OnDisable()
        {
            occlusion.RestoreAll();
        }

        private void LateUpdate()
        {
            // Re-runs whenever the target is missing, so the camera picks the
            // player back up after a respawn without anything having to tell it.
            if (target == null)
            {
                AcquireTarget();

                if (target == null)
                {
                    return;
                }

                SnapToTarget();
            }

            if (!Application.isPlaying)
            {
                SnapToTarget();
                return;
            }

            ReadRotationInput(Time.deltaTime);

            Vector3 focus = ResolveFocus(Time.deltaTime);
            Quaternion rotation = ResolveRotation();

            Vector3 desired = focus - rotation * Vector3.forward * distance;

            followPosition = followSmoothTime > 0f
                ? Vector3.SmoothDamp(followPosition, desired, ref followVelocity, followSmoothTime)
                : desired;

            transform.SetPositionAndRotation(followPosition + ShakeOffset(rotation), rotation);

            occlusion.Update(transform.position, target, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Finds a player to follow when no target is assigned. An explicitly
        /// assigned target always wins and is never overwritten.
        /// </summary>
        private void AcquireTarget()
        {
            if (target != null)
            {
                targetBody = target.GetComponent<Rigidbody>();
                targetInput = target.GetComponent<PlayerInputReader>();
                return;
            }

            // Play mode only. Assigning a serialized field from edit mode would
            // change the scene without marking it dirty, which is the kind of
            // thing that silently saves later and confuses everyone.
            if (!Application.isPlaying)
            {
                return;
            }

            // Scanning the scene is not free, so back off between attempts
            // rather than searching on every frame of an empty scene.
            if (Time.unscaledTime < nextTargetSearchTime)
            {
                return;
            }

            nextTargetSearchTime = Time.unscaledTime + Mathf.Max(0.05f, targetSearchInterval);

            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (player == null)
            {
                return;
            }

            target = player.transform;
            targetBody = player.GetComponent<Rigidbody>();
            targetInput = player.GetComponent<PlayerInputReader>();
        }

        private void ReadRotationInput(float deltaTime)
        {
            if (!allowRotationInput)
            {
                return;
            }

            // E is also interact. Hold the view while an interact prompt is
            // up, and while the player's controls are locked by dialogue or an
            // acquire effect, so pressing E to talk never swings the camera.
            if (DialogueBox.IsEngaged || (targetInput != null && targetInput.Locked))
            {
                return;
            }

            float direction = 0f;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.eKey.isPressed) direction += 1f;
                if (keyboard.qKey.isPressed) direction -= 1f;
            }

            // Right stick rather than the shoulders, which PlayerInputReader
            // already uses for the dash.
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                direction += gamepad.rightStick.ReadValue().x;
            }

            if (Mathf.Abs(direction) < 0.001f)
            {
                return;
            }

            yaw = Mathf.Repeat(yaw + Mathf.Clamp(direction, -1f, 1f) * rotationSpeed * deltaTime, 360f);
        }

        /// <summary>Sideways and up-down jolt across the screen for the current shake.</summary>
        private Vector3 ShakeOffset(Quaternion rotation)
        {
            float amount = CurrentShake();

            if (amount <= 0f)
            {
                return Vector3.zero;
            }

            float time = Time.time * shakeFrequency;
            float x = Mathf.PerlinNoise(time, 0.37f) * 2f - 1f;
            float y = Mathf.PerlinNoise(0.71f, time) * 2f - 1f;

            return (rotation * new Vector3(x, y, 0f)) * amount;
        }

        private Quaternion ResolveRotation()
        {
            return Quaternion.Euler(pitch, yaw, 0f);
        }

        private Vector3 ResolveFocus(float deltaTime)
        {
            Vector3 focus = target.position + targetOffset;

            if (lookAheadDistance <= 0f)
            {
                return focus;
            }

            Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            velocity.y = 0f;

            Vector3 desiredLead = Vector3.ClampMagnitude(
                velocity / Mathf.Max(0.0001f, lookAheadReferenceSpeed), 1f) * lookAheadDistance;

            lookAhead = lookAheadSmoothTime > 0f
                ? Vector3.SmoothDamp(lookAhead, desiredLead, ref lookAheadVelocity, lookAheadSmoothTime)
                : desiredLead;

            return focus + lookAhead;
        }

        /// <summary>Jumps straight to the framed position, skipping all smoothing.</summary>
        [ContextMenu("Snap To Target")]
        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            Quaternion rotation = ResolveRotation();
            Vector3 focus = target.position + targetOffset;

            followPosition = focus - rotation * Vector3.forward * distance;
            transform.SetPositionAndRotation(followPosition, rotation);

            followVelocity = Vector3.zero;
            lookAhead = Vector3.zero;
            lookAheadVelocity = Vector3.zero;
        }

        private void OnValidate()
        {
            // Only clamp here. Moving the transform from OnValidate is not safe
            // during deserialization; the ExecuteAlways LateUpdate above already
            // keeps the edit-mode view framed.
            distance = Mathf.Max(0.1f, distance);
            occlusion?.Validate();
        }
    }
}
