using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Fakes air drag on the player's cloak so it streams out behind them.
    /// </summary>
    /// <remarks>
    /// Unity's Cloth simulates gravity and inertia but has no air resistance,
    /// so at a constant run speed nothing pushes the cloak backward and it only
    /// wobbles on starts and stops. This pushes the cloth opposite the player's
    /// planar velocity every frame, with a little random flutter that scales
    /// with speed. Dashing briefly multiplies the drag for a sharper snap.
    /// </remarks>
    [RequireComponent(typeof(Cloth))]
    public sealed class CloakDrag : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Found in the parents automatically if left empty.")]
        private PlayerMotor motor;

        [SerializeField]
        [Tooltip("How hard the cloak streams back per unit of speed.")]
        private float dragStrength = 4f;

        [SerializeField]
        [Tooltip("Random wind noise per unit of speed. Keeps the hem alive while running.")]
        private float flutter = 2f;

        [Header("Dash")]
        [SerializeField] private float dashDragMultiplier = 2f;

        private Cloth cloth;

        private void Awake()
        {
            cloth = GetComponent<Cloth>();

            if (motor == null)
            {
                motor = GetComponentInParent<PlayerMotor>();
            }
        }

        private void LateUpdate()
        {
            if (motor == null)
            {
                return;
            }

            Vector3 velocity = motor.PlanarVelocity;
            float strength = motor.IsDashing ? dragStrength * dashDragMultiplier : dragStrength;

            cloth.externalAcceleration = -velocity * strength;
            cloth.randomAcceleration = Vector3.one * (velocity.magnitude * flutter);
        }

        private void OnDisable()
        {
            if (cloth != null)
            {
                cloth.externalAcceleration = Vector3.zero;
                cloth.randomAcceleration = Vector3.zero;
            }
        }
    }
}
