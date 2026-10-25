using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Raises the gate once the player comes within range of a detection point.
    /// </summary>
    /// <remarks>
    /// Opening is one-way: once triggered the gate stays up, so the player
    /// cannot get it to drop on them by stepping back out of range.
    ///
    /// Distance is measured on the XZ plane so the player's height (jumps,
    /// stairs, pivot offset) does not affect when the gate opens.
    /// </remarks>
    public sealed class Gate : MonoBehaviour
    {
        [Tooltip("Point the player must get close to. Defaults to the gate itself.")]
        [SerializeField] private Transform detectionPoint;

        [Tooltip("How close (in metres, ignoring height) the player must get to open the gate.")]
        [SerializeField] private float detectionRadius = 5f;

        [Tooltip("How far the gate travels upwards when opening.")]
        [SerializeField] private float raiseHeight = 4f;

        [Tooltip("Seconds the gate takes to fully raise.")]
        [SerializeField] private float raiseDuration = 1.5f;

        [Tooltip("Optional. Found automatically if left empty.")]
        [SerializeField] private Transform player;

        private Vector3 closedPosition;
        private Vector3 openPosition;
        private bool opening;
        private float elapsed;

        private void Awake()
        {
            closedPosition = transform.position;
            openPosition = closedPosition + Vector3.up * raiseHeight;

            if (detectionPoint == null)
            {
                detectionPoint = transform;
            }
        }

        private void Update()
        {
            if (!opening)
            {
                if (player == null)
                {
                    PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
                    if (motor == null)
                    {
                        return;
                    }
                    player = motor.transform;
                }

                Vector3 offset = player.position - detectionPoint.position;
                offset.y = 0f;

                if (offset.sqrMagnitude <= detectionRadius * detectionRadius)
                {
                    opening = true;
                }
                return;
            }

            if (elapsed >= raiseDuration)
            {
                return;
            }

            elapsed += Time.deltaTime;

            // Guard against zero so a mistyped duration snaps open instead of dividing by zero.
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, raiseDuration));
            transform.position = Vector3.Lerp(closedPosition, openPosition, Mathf.SmoothStep(0f, 1f, t));
        }

        private void OnDrawGizmosSelected()
        {
            Transform point = detectionPoint != null ? detectionPoint : transform;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(point.position, detectionRadius);
        }
    }
}
