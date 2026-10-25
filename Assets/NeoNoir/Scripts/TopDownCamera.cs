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

        [Header("Occlusion")]
        [SerializeField] private OccluderFader occlusion = new OccluderFader();

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

            transform.SetPositionAndRotation(
                followSmoothTime > 0f
                    ? Vector3.SmoothDamp(transform.position, desired, ref followVelocity, followSmoothTime)
                    : desired,
                rotation);

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

            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);

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
