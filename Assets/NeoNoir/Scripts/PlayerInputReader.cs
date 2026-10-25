using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// Reads movement and dash input and converts it to a world-space direction
    /// on the XZ plane.
    /// </summary>
    /// <remarks>
    /// This polls devices directly rather than depending on an .inputactions
    /// asset, so the demo has no serialized asset to go stale. When the real
    /// control scheme is designed (rebinding, multiple action maps, UI), replace
    /// the body of <see cref="Read"/> with generated action callbacks -- nothing
    /// else in the project reaches past the properties on this class.
    /// </remarks>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Camera relative")]
        [SerializeField]
        [Tooltip("Interpret input relative to the camera's yaw rather than world axes.")]
        private bool cameraRelative = true;

        [SerializeField]
        [Tooltip("Leave empty to use Camera.main.")]
        private Transform cameraTransform;

        [Header("Buffering")]
        [SerializeField]
        [Tooltip("How long a dash press stays queued while it is unavailable. Bullet hells live or die on this.")]
        [Range(0f, 0.4f)]
        private float dashBufferTime = 0.12f;

        private float dashBufferRemaining;

        /// <summary>Raw stick/key input, magnitude clamped to 1.</summary>
        public Vector2 Move { get; private set; }

        /// <summary>Input projected onto the world XZ plane, camera-relative if enabled.</summary>
        public Vector3 MoveWorld { get; private set; }

        /// <summary>True while a dash press is still inside the buffer window.</summary>
        public bool DashQueued => dashBufferRemaining > 0f;

        /// <summary>True for as long as the attack button is held.</summary>
        public bool AttackHeld { get; private set; }

        /// <summary>True only on the frame the secondary ability button is pressed.</summary>
        public bool SecondaryPressed { get; private set; }

        /// <summary>Mouse position in screen pixels. Where the attack is aimed.</summary>
        public Vector2 PointerPosition { get; private set; }

        /// <summary>True only on the frame the interact key (E) is pressed.</summary>
        public bool InteractPressed { get; private set; }

        /// <summary>True only on the frame the swap key (R) is pressed: skip to the next secondary.</summary>
        public bool SwapPressed { get; private set; }

        /// <summary>
        /// While true, every gameplay input reads as idle: no movement, dash,
        /// attack or interact. Set by dialogue and cutscenes. The pointer is
        /// still tracked.
        /// </summary>
        public bool Locked
        {
            get => locked;
            set
            {
                if (locked && !value)
                {
                    // The click that closed a dialogue must not carry on as
                    // an attack the moment control comes back.
                    attackNeedsRelease = true;
                }

                locked = value;
            }
        }

        private bool locked;
        private bool attackNeedsRelease;

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            Read();

            if (dashBufferRemaining > 0f)
            {
                dashBufferRemaining -= Time.deltaTime;
            }
        }

        private void Read()
        {
            Vector2 move = Vector2.zero;
            bool dashPressed = false;
            bool interactPressed = false;
            bool swapPressed = false;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;

                dashPressed |= keyboard.spaceKey.wasPressedThisFrame;
                interactPressed = keyboard.eKey.wasPressedThisFrame;
                swapPressed = keyboard.rKey.wasPressedThisFrame;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > move.sqrMagnitude)
                {
                    move = stick;
                }

                dashPressed |= gamepad.buttonEast.wasPressedThisFrame
                               || gamepad.rightShoulder.wasPressedThisFrame;
            }

            // Mouse only for now: the attack goes to a point on screen, and a
            // gamepad has nothing to aim it with until a reticle exists.
            Mouse mouse = Mouse.current;
            AttackHeld = mouse != null && mouse.leftButton.isPressed;
            SecondaryPressed = mouse != null && mouse.rightButton.wasPressedThisFrame;

            if (mouse != null)
            {
                PointerPosition = mouse.position.ReadValue();
            }

            if (attackNeedsRelease)
            {
                attackNeedsRelease = AttackHeld;
                AttackHeld = false;
            }

            if (locked)
            {
                move = Vector2.zero;
                dashPressed = false;
                interactPressed = false;
                swapPressed = false;
                AttackHeld = false;
                SecondaryPressed = false;
                dashBufferRemaining = 0f;
            }

            InteractPressed = interactPressed;
            SwapPressed = swapPressed;
            Move = Vector2.ClampMagnitude(move, 1f);
            MoveWorld = ToWorld(Move);

            if (dashPressed)
            {
                dashBufferRemaining = dashBufferTime;
            }
        }

        private Vector3 ToWorld(Vector2 input)
        {
            if (input.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (cameraRelative && cameraTransform != null)
            {
                // Flatten onto XZ. A top-down camera pitches steeply, so its raw
                // forward is mostly -Y and useless as a movement axis.
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);

                if (forward.sqrMagnitude < 0.0001f)
                {
                    // Camera pointing straight down: fall back to its up vector,
                    // which still encodes the yaw we want.
                    forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                }

                forward.Normalize();
                right = Vector3.Cross(Vector3.up, forward);
            }

            return right * input.x + forward * input.y;
        }

        /// <summary>Clears the buffered dash. Call when the dash is actually spent.</summary>
        public void ConsumeDash()
        {
            dashBufferRemaining = 0f;
        }
    }
}
