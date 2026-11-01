using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Cuts this object to pieces with a <see cref="KatanaCut"/> when its
    /// health runs out.
    /// </summary>
    /// <remarks>
    /// Goes beside the <see cref="EnemyHealth"/> of anything the player can
    /// break apart, such as a column the Katana has thrown. It needs a solid
    /// collider for the player's blades to hit.
    ///
    /// The blow is taken as coming from the player, so the pieces fly on
    /// away from them. Whoever throws the object can hand it the Katana's own
    /// cut through <see cref="Settings"/>, so every column it throws breaks
    /// the same way.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class KatanaSliceOnDeath : MonoBehaviour
    {
        [SerializeField] private KatanaCut.Settings cut = new KatanaCut.Settings();

        private EnemyHealth health;

        /// <summary>How this object is cut apart. Set by whatever throws it, to use its own.</summary>
        public KatanaCut.Settings Settings
        {
            get => cut;
            set => cut = value;
        }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
        }

        private void OnEnable()
        {
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
        }

        private void OnDied(EnemyHealth died)
        {
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
            Vector3 from = player != null ? player.transform.position : transform.position - transform.forward;

            KatanaCut.Cut(gameObject, cut, from);
        }
    }
}
