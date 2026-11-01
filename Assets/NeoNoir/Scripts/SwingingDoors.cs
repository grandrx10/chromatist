using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Swings a pair of double doors open once the player comes within range
    /// of a detection point.
    /// </summary>
    /// <remarks>
    /// Door pivots do not matter: each door's hinge is worked out from its
    /// rendered bounds as the outer edge, the side farthest from the other
    /// door. The doors turn about this object's up axis.
    ///
    /// "Forward" is the side facing away from someone standing in front of the
    /// doors with the left door on their left. Tick <see cref="swingBackward"/>
    /// to swing towards them instead. The gizmo shows both hinges and the
    /// swing direction.
    ///
    /// Triggering is one-time only -- once open the doors stay open. Distance
    /// is measured on the XZ plane so the player's height does not matter.
    ///
    /// The one exception is a boss arena: give the doors a
    /// <see cref="PuppeteerIntro"/> and, the moment that fight is triggered,
    /// they swing shut -- open or not -- over <see cref="closeDuration"/>, and
    /// stay shut until the boss dies. After that they go back to opening when
    /// the player comes near, as the boss <see cref="Gate"/> does.
    /// </remarks>
    public sealed class SwingingDoors : MonoBehaviour
    {
        [Tooltip("The left door. Its pivot can be anywhere.")]
        [SerializeField] private Transform leftDoor;

        [Tooltip("The right door. Its pivot can be anywhere.")]
        [SerializeField] private Transform rightDoor;

        [Tooltip("Point the player must get close to. Defaults to this object.")]
        [SerializeField] private Transform detectionPoint;

        [Tooltip("How close (in metres, ignoring height) the player must get to open the doors.")]
        [SerializeField] private float detectionRadius = 5f;

        [Tooltip("How far each door swings open, in degrees.")]
        [SerializeField] private float openAngle = 90f;

        [Tooltip("Seconds the doors take to fully open.")]
        [SerializeField] private float openDuration = 3f;

        [Tooltip("Swing the doors backward instead of forward (see the gizmo).")]
        [SerializeField] private bool swingBackward;

        [Tooltip("Optional. Found automatically if left empty.")]
        [SerializeField] private Transform player;

        [Header("Boss lock")]
        [Tooltip("Optional. When this boss fight is triggered the doors swing shut and cannot open until the boss is killed.")]
        [SerializeField] private PuppeteerIntro lockedByBoss;

        [Tooltip("Seconds the doors take to swing shut when the boss fight starts.")]
        [SerializeField] private float closeDuration = 0.6f;

        private DoorSwing left;
        private DoorSwing right;
        private bool triggered;
        private bool locked;

        // 0 is fully closed, 1 fully open.
        private float progress;

        /// <summary>A door's closed pose and how it turns about its hinge.</summary>
        private struct DoorSwing
        {
            public Transform Door;
            public Vector3 Hinge;
            public Vector3 ClosedPosition;
            public Quaternion ClosedRotation;
            public float Angle;

            public void Apply(Vector3 axis, float t)
            {
                if (Door == null)
                {
                    return;
                }

                Quaternion turn = Quaternion.AngleAxis(Angle * t, axis);
                Door.SetPositionAndRotation(Hinge + turn * (ClosedPosition - Hinge), turn * ClosedRotation);
            }
        }

        private void Awake()
        {
            if (detectionPoint == null)
            {
                detectionPoint = transform;
            }

            ComputeHinges(out Vector3 leftHinge, out Vector3 rightHinge, out Vector3 swingDirection);
            left = CreateSwing(leftDoor, leftHinge, swingDirection);
            right = CreateSwing(rightDoor, rightHinge, swingDirection);
        }

        private void OnEnable()
        {
            if (lockedByBoss != null)
            {
                lockedByBoss.Triggered += OnBossTriggered;
                lockedByBoss.Defeated += OnBossDefeated;
            }
        }

        private void OnDisable()
        {
            if (lockedByBoss != null)
            {
                lockedByBoss.Triggered -= OnBossTriggered;
                lockedByBoss.Defeated -= OnBossDefeated;
            }
        }

        private void OnBossTriggered(PuppeteerIntro intro)
        {
            locked = true;
            triggered = false;
        }

        private void OnBossDefeated(PuppeteerIntro intro)
        {
            locked = false;
        }

        private void Update()
        {
            if (!triggered && !locked)
            {
                triggered = PlayerInRange();
            }

            float target = triggered && !locked ? 1f : 0f;

            if (Mathf.Approximately(progress, target))
            {
                return;
            }

            // Guard against zero so a mistyped duration snaps instead of dividing by zero.
            float duration = target > progress ? openDuration : closeDuration;
            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.0001f, duration));
            float t = Mathf.SmoothStep(0f, 1f, progress);

            left.Apply(transform.up, t);
            right.Apply(transform.up, t);
        }

        private DoorSwing CreateSwing(Transform door, Vector3 hinge, Vector3 swingDirection)
        {
            var swing = new DoorSwing { Door = door, Hinge = hinge };
            if (door == null)
            {
                return swing;
            }

            swing.ClosedPosition = door.position;
            swing.ClosedRotation = door.rotation;

            // Turn whichever way carries the door's body towards the swing direction.
            Vector3 arm = Vector3.ProjectOnPlane(GetBounds(door).center - hinge, transform.up);
            Vector3 motion = Vector3.Cross(transform.up, arm);
            swing.Angle = Vector3.Dot(motion, swingDirection) >= 0f ? openAngle : -openAngle;

            return swing;
        }

        /// <summary>
        /// Finds each door's hinge (its outer edge) and the horizontal direction
        /// the doors swing towards.
        /// </summary>
        private void ComputeHinges(out Vector3 leftHinge, out Vector3 rightHinge, out Vector3 swingDirection)
        {
            Vector3 up = transform.up;
            Bounds leftBounds = leftDoor != null ? GetBounds(leftDoor) : new Bounds(transform.position, Vector3.zero);
            Bounds rightBounds = rightDoor != null ? GetBounds(rightDoor) : new Bounds(transform.position, Vector3.zero);

            // Left-to-right across the doorway; fall back to this object's right
            // if only one door is assigned.
            Vector3 across = Vector3.ProjectOnPlane(rightBounds.center - leftBounds.center, up);
            across = across.sqrMagnitude > 0.0001f ? across.normalized : transform.right;

            leftHinge = leftBounds.center - across * ExtentAlong(leftBounds, across);
            rightHinge = rightBounds.center + across * ExtentAlong(rightBounds, across);

            swingDirection = Vector3.Cross(across, up);
            if (swingBackward)
            {
                swingDirection = -swingDirection;
            }
        }

        /// <summary>Combined world bounds of every renderer on or under the door.</summary>
        private static Bounds GetBounds(Transform door)
        {
            Renderer[] renderers = door.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(door.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        /// <summary>Half-width of an axis-aligned box measured along a direction.</summary>
        private static float ExtentAlong(Bounds bounds, Vector3 direction)
        {
            Vector3 e = bounds.extents;
            return Mathf.Abs(direction.x) * e.x + Mathf.Abs(direction.y) * e.y + Mathf.Abs(direction.z) * e.z;
        }

        private bool PlayerInRange()
        {
            if (player == null)
            {
                PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
                if (motor == null)
                {
                    return false;
                }
                player = motor.transform;
            }

            Vector3 offset = player.position - detectionPoint.position;
            offset.y = 0f;

            return offset.sqrMagnitude <= detectionRadius * detectionRadius;
        }

        private void OnDrawGizmosSelected()
        {
            Transform point = detectionPoint != null ? detectionPoint : transform;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(point.position, detectionRadius);

            // Show the hinges and which way the doors will swing (only meaningful in edit mode).
            if (!Application.isPlaying && (leftDoor != null || rightDoor != null))
            {
                ComputeHinges(out Vector3 leftHinge, out Vector3 rightHinge, out Vector3 swingDirection);
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);

                if (leftDoor != null)
                {
                    Gizmos.DrawWireSphere(leftHinge, 0.25f);
                    Gizmos.DrawLine(leftHinge, leftHinge + swingDirection * 1.5f);
                }

                if (rightDoor != null)
                {
                    Gizmos.DrawWireSphere(rightHinge, 0.25f);
                    Gizmos.DrawLine(rightHinge, rightHinge + swingDirection * 1.5f);
                }
            }
        }
    }
}
