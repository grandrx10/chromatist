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
    /// The one exception is a boss arena: give the gate a
    /// <see cref="BossIntro"/> and, the moment that fight is triggered, the
    /// gate slams shut -- open or not -- and stays shut until the boss dies.
    /// After that it goes back to opening when the player comes near.
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

        [Header("Boss lock")]
        [Tooltip("Optional. When this boss fight is triggered the gate shuts and cannot open until the boss is killed.")]
        [SerializeField] private BossIntro lockedByBoss;

        [Tooltip("Seconds the gate takes to slam shut when the boss fight starts.")]
        [SerializeField] private float closeDuration = 0.4f;

        private Vector3 closedPosition;
        private Vector3 openPosition;
        private bool opening;
        private bool locked;

        // 0 is fully closed, 1 fully open.
        private float openness;

        private void Awake()
        {
            closedPosition = transform.position;
            openPosition = closedPosition + Vector3.up * raiseHeight;

            if (detectionPoint == null)
            {
                detectionPoint = transform;
            }
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

        private void OnBossTriggered(BossIntro intro)
        {
            locked = true;
            opening = false;
        }

        private void OnBossDefeated(BossIntro intro)
        {
            locked = false;
        }

        private void Update()
        {
            if (!opening && !locked)
            {
                opening = PlayerInRange();
            }

            float target = opening ? 1f : 0f;

            if (openness == target)
            {
                return;
            }

            // Guard against zero so a mistyped duration snaps instead of dividing by zero.
            float duration = Mathf.Max(0.0001f, opening ? raiseDuration : closeDuration);
            openness = Mathf.MoveTowards(openness, target, Time.deltaTime / duration);
            transform.position = Vector3.Lerp(closedPosition, openPosition, Mathf.SmoothStep(0f, 1f, openness));
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

            if (lockedByBoss != null)
            {
                Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.8f);
                Gizmos.DrawLine(transform.position, lockedByBoss.transform.position);
            }
        }
    }
}
