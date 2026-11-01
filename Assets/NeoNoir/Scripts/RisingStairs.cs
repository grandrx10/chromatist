using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Raises a staircase out of the ground once the player comes within range
    /// of a detection point.
    /// </summary>
    /// <remarks>
    /// Place the stairs where they should end up: on load they are sunk by
    /// <see cref="sinkDepth"/> and rise back to that placed position when
    /// triggered. Triggering is one-time only -- once risen the stairs stay up.
    ///
    /// Distance is measured on the XZ plane so the player's height (jumps,
    /// stairs, pivot offset) does not affect when the stairs rise.
    /// </remarks>
    public sealed class RisingStairs : MonoBehaviour
    {
        [Tooltip("Point the player must get close to. Defaults to the stairs themselves.")]
        [SerializeField] private Transform detectionPoint;

        [Tooltip("How close (in metres, ignoring height) the player must get to raise the stairs.")]
        [SerializeField] private float detectionRadius = 5f;

        [Tooltip("How far below their placed position the stairs start.")]
        [SerializeField] private float sinkDepth = 4f;

        [Tooltip("Seconds the stairs take to fully rise.")]
        [SerializeField] private float riseDuration = 2f;

        [Tooltip("Optional. Found automatically if left empty.")]
        [SerializeField] private Transform player;

        private Vector3 loweredPosition;
        private Vector3 raisedPosition;
        private bool triggered;

        // 0 is fully lowered, 1 fully raised.
        private float progress;

        private void Awake()
        {
            raisedPosition = transform.position;
            loweredPosition = raisedPosition + Vector3.down * sinkDepth;
            transform.position = loweredPosition;

            if (detectionPoint == null)
            {
                detectionPoint = transform;
            }
        }

        private void Update()
        {
            if (!triggered)
            {
                triggered = PlayerInRange();
                return;
            }

            if (progress >= 1f)
            {
                // Fully risen; nothing left to do.
                enabled = false;
                return;
            }

            // Guard against zero so a mistyped duration snaps instead of dividing by zero.
            progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / Mathf.Max(0.0001f, riseDuration));
            transform.position = Vector3.Lerp(loweredPosition, raisedPosition, Mathf.SmoothStep(0f, 1f, progress));
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

            // Show where the stairs start before rising (only meaningful in edit mode).
            if (!Application.isPlaying)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
                Gizmos.DrawLine(transform.position, transform.position + Vector3.down * sinkDepth);
            }
        }
    }
}
