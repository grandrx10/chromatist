using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A column standing in the way of <see cref="ChromatistPhaseThree"/>'s
    /// run. The player cuts it apart, steers round it, or runs into it, which
    /// shatters it and hurts them.
    /// </summary>
    /// <remarks>
    /// Goes on each column in the hall segment prefab, at the places one can
    /// stand, all switched off: as each piece is laid, the run switches on at
    /// most one of them. The column needs a collider for blades to hit.
    ///
    /// It is put on the Enemy layer with an <see cref="EnemyHealth"/>, so
    /// blades hit it and homing ones seek it out. The player passes through
    /// it rather than snagging on it. Either way it is broken, it is split
    /// where it stands with the Katana's <see cref="KatanaCut"/>.
    /// </remarks>
    public sealed class HallColumn : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Damage the column takes before it is cut apart.")]
        [Min(1f)]
        private float health = 30f;

        [SerializeField]
        [Tooltip("Damage to the player if they run into it.")]
        [Min(0f)]
        private float contactDamage = 30f;

        [SerializeField]
        [Tooltip("How far past the column's colliders it reaches the player.")]
        [Min(0f)]
        private float contactPadding = 0.1f;

        [SerializeField] private KatanaCut.Settings cut = new KatanaCut.Settings();

        private EnemyHealth life;
        private Collider[] parts;
        private Collider[] playerParts;
        private PlayerHealth player;
        private bool broken;

        private void Awake()
        {
            int layer = LayerMask.NameToLayer("Enemy");

            if (layer >= 0)
            {
                gameObject.layer = layer;
            }

            parts = GetComponentsInChildren<Collider>();

            life = gameObject.AddComponent<EnemyHealth>();
            life.SetMaxHealth(health);
            life.DestroyOnDeath = false;
            life.Unmarked = true;
            life.Died += OnDied;
        }

        private void OnEnable()
        {
            PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (motor == null)
            {
                return;
            }

            player = motor.GetComponent<PlayerHealth>();
            playerParts = motor.GetComponentsInChildren<Collider>();

            // Run through, not stopped dead against.
            foreach (Collider part in parts)
            {
                foreach (Collider other in playerParts)
                {
                    Physics.IgnoreCollision(part, other);
                }
            }
        }

        private void Update()
        {
            if (broken || playerParts == null || !Touching())
            {
                return;
            }

            Vector3 centre = Centre();

            if (player != null && contactDamage > 0f)
            {
                player.TakeDamage(contactDamage, centre);
            }

            Break(centre);
        }

        /// <summary>True if any solid part of the player overlaps the column.</summary>
        private bool Touching()
        {
            foreach (Collider part in parts)
            {
                if (!part.enabled || part.isTrigger)
                {
                    continue;
                }

                Bounds reach = part.bounds;
                reach.Expand(contactPadding * 2f);

                foreach (Collider other in playerParts)
                {
                    if (other != null && other.enabled && !other.isTrigger && reach.Intersects(other.bounds))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private Vector3 Centre()
        {
            return parts.Length > 0 ? parts[0].bounds.center : transform.position;
        }

        private void OnDied(EnemyHealth died)
        {
            PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
            Break(motor != null ? motor.transform.position : Centre());
        }

        /// <summary>Splits it where it stands, as a blow from <paramref name="from"/>, and takes it out of play.</summary>
        private void Break(Vector3 from)
        {
            if (broken)
            {
                return;
            }

            broken = true;

            // Gone from homing's list, though it outlives this with its hall piece.
            life.enabled = false;
            if (KatanaCut.Cut(gameObject, cut, from, Vector3.zero) == null)
            {
                KatanaCut.Hide(gameObject);
            }
        }
    }
}
