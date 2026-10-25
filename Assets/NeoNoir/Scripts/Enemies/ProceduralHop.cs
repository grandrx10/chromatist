using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Makes an enemy hop along as it moves, instead of sliding: a little arc
    /// per hop, stretching in the air, leaning the way it travels, and
    /// squashing as it lands. No animation needed.
    /// </summary>
    /// <remarks>
    /// Goes on the enemy's visual model -- a child of the <see cref="Enemy"/>,
    /// never the root, which holds the collider and Rigidbody, and never the
    /// transform set as the enemy's turning model, whose rotation it would
    /// fight; a child of that is fine. Everything is
    /// an offset from the pose the model has when this is enabled, put back
    /// exactly when it is disabled.
    ///
    /// Hops are paced by distance, not time: one every
    /// <see cref="hopLength"/> units, so a faster enemy hops more often rather
    /// than sliding further per hop. The lean follows the direction of travel
    /// rather than the facing, since an enemy can walk one way while looking
    /// another. When the enemy stops, the hop in progress is finished, so it
    /// never freezes in mid-air.
    ///
    /// Squash and stretch scale about the model's pivot, so they read best
    /// with the pivot at the feet.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ProceduralHop : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Whose movement drives the hop. Empty finds one in the parents.")]
        private Enemy enemy;

        [Header("Hop")]
        [SerializeField]
        [Tooltip("Distance covered by each hop. Shorter is more, quicker hops.")]
        [Min(0.05f)]
        private float hopLength = 1.8f;

        [SerializeField]
        [Tooltip("Height of each hop at its peak.")]
        [Min(0f)]
        private float hopHeight = 0.1f;

        [SerializeField]
        [Tooltip("Slower than this counts as standing still.")]
        [Min(0f)]
        private float minSpeed = 0.2f;

        [Header("Shape")]
        [SerializeField]
        [Tooltip("How much taller and thinner the model gets at the top of a hop. 0.1 is 10%.")]
        [Range(0f, 0.5f)]
        private float stretch = 0.04f;

        [SerializeField]
        [Tooltip("How much shorter and wider the model gets on landing.")]
        [Range(0f, 0.5f)]
        private float landSquash = 0.06f;

        [SerializeField]
        [Tooltip("Seconds the landing squash takes to spring back.")]
        [Min(0f)]
        private float squashTime = 0.12f;

        [SerializeField]
        [Tooltip("Degrees the model leans toward where it is going, at the top of a hop.")]
        [Range(0f, 30f)]
        private float lean = 4f;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 restScale;

        // Hops done, fractionally: the whole part counts hops, the fraction
        // is how far through the current one. 0 is on the ground.
        private float phase;
        private float hopRate;
        private float squashTimer;
        private Vector3 travel = Vector3.forward;

        private void Awake()
        {
            if (enemy == null)
            {
                enemy = GetComponentInParent<Enemy>();
            }

            // The enemy turns its model to face the player; this would undo
            // that every frame by putting the rotation back to rest.
            if (enemy != null && enemy.Model == transform)
            {
                Debug.LogWarning($"[ProceduralHop] {name} is the enemy's turning model. Put the hop on a child of it instead.", this);
            }
        }

        private void OnEnable()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            restScale = transform.localScale;

            phase = 0f;
            squashTimer = 0f;
        }

        private void OnDisable()
        {
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
            transform.localScale = restScale;
        }

        // LateUpdate, so it lands after the animator and the enemy have both
        // had their turn this frame.
        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            Vector3 velocity = enemy != null && !enemy.Health.IsDead ? enemy.Velocity : Vector3.zero;
            float speed = velocity.magnitude;
            bool moving = speed > minSpeed;

            if (moving)
            {
                hopRate = speed / hopLength;
                travel = velocity / speed;
            }

            // Keeps going while moving, or to finish a hop already in the air.
            if (moving || phase > 0f)
            {
                phase += hopRate * deltaTime;

                if (phase >= 1f)
                {
                    squashTimer = squashTime;

                    // Landed. Straight into the next hop if still moving;
                    // otherwise stay down.
                    phase = moving ? phase - Mathf.Floor(phase) : 0f;
                }
            }

            squashTimer = Mathf.Max(0f, squashTimer - deltaTime);

            // 0 on the ground, 1 at the top of the hop.
            float arc = Mathf.Sin(phase * Mathf.PI);
            float squash = squashTime > 0f ? landSquash * (squashTimer / squashTime) : 0f;

            float tall = 1f + stretch * arc - squash;
            float wide = 1f + (squash - stretch * arc) * 0.5f;

            Transform parent = transform.parent;
            Quaternion parentRotation = parent != null ? parent.rotation : Quaternion.identity;
            Vector3 up = parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up;

            // The lean is about a world axis, since travel is in the world,
            // then brought into the parent's space, which turns to face the
            // player.
            Quaternion worldLean = Quaternion.AngleAxis(lean * arc, Vector3.Cross(Vector3.up, travel));

            transform.localPosition = restPosition + up * (hopHeight * arc);
            transform.localRotation = Quaternion.Inverse(parentRotation) * worldLean * parentRotation * restRotation;
            transform.localScale = Vector3.Scale(restScale, new Vector3(wide, tall, wide));
        }
    }
}
