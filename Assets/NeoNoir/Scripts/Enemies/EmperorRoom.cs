using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Emperor's throne room: walking in sheathes the player's blades --
    /// no attack, no secondary; moving, dashing and swapping still work --
    /// and puts up the Emperor's health bar.
    /// </summary>
    /// <remarks>
    /// Goes on a trigger box covering the room's entrance or the whole room.
    /// Plays once. The blades stay sheathed, whether the player stays in the
    /// room or not, until <see cref="Release"/>: the
    /// <see cref="EmperorAudience"/> calls it as Luna's fight begins.
    ///
    /// The Emperor herself needs only an <see cref="EnemyHealth"/>, a
    /// <see cref="DeathRagdoll"/> and an Animator whose default state is her
    /// looping idle -- no <see cref="Enemy"/>, since she never moves or
    /// attacks. She cannot be hurt until Luna strikes her.
    /// </remarks>
    public sealed class EmperorRoom : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The Emperor.")]
        private EnemyHealth emperor;

        [SerializeField]
        [Tooltip("Name shown over her health bar.")]
        private string emperorName = "The Emperor";

        private PlayerInputReader input;
        private bool entered;
        private bool released;

        public EnemyHealth Emperor => emperor;

        private void Awake()
        {
            if (emperor == null)
            {
                Debug.LogWarning($"[EmperorRoom] {name} has no Emperor assigned.", this);
                return;
            }

            emperor.Invulnerable = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (entered || released)
            {
                return;
            }

            PlayerMotor player = other.GetComponentInParent<PlayerMotor>();

            if (player == null)
            {
                return;
            }

            entered = true;
            input = player.GetComponent<PlayerInputReader>();

            if (input != null)
            {
                input.AbilitiesLocked = true;
            }

            if (emperor != null && !emperor.IsDead)
            {
                BossHealthBar.Show(emperor, emperorName);
            }
        }

        /// <summary>Gives the player their blades back.</summary>
        public void Release()
        {
            released = true;

            if (input != null)
            {
                input.AbilitiesLocked = false;
            }
        }
    }
}
