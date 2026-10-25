using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Swaps the enemy for a ragdoll the moment it dies, in exactly the pose
    /// it died in, and knocks it away from the player.
    /// </summary>
    /// <remarks>
    /// The ragdoll is its own prefab: a copy of the enemy's model with
    /// Rigidbodies and joints on its bones, and none of the enemy's scripts.
    /// When spawned, every part of it takes the world position and rotation
    /// of the part of the enemy with the same name, so it picks up mid-hop,
    /// mid-swing or mid-turn rather than snapping back to a rest pose. Parts
    /// with no namesake keep where the prefab put them.
    ///
    /// Any Animator on the ragdoll is switched off, or it would hold the
    /// bones in place and the body would never fall. After
    /// <see cref="fadeDelay"/> the body dissolves away and is removed.
    ///
    /// Goes on the enemy's root, beside <see cref="EnemyHealth"/>. Works
    /// whether or not the enemy is destroyed on death: the swap happens as
    /// it dies, before anything is removed.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class DeathRagdoll : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Spawned in the enemy's place when it dies.")]
        private GameObject ragdollPrefab;

        [SerializeField]
        [Tooltip("Root of the parts the ragdoll copies its pose from. Empty uses the whole enemy.")]
        private Transform poseSource;

        [Header("Knockback")]
        [SerializeField]
        [Tooltip("Speed the body is thrown away from the player at, when the killing blow dealt Reference Damage.")]
        [Min(0f)]
        private float knockback = 10f;

        [SerializeField]
        [Tooltip("Killing blow that throws the body at exactly Knockback.")]
        [Min(0.01f)]
        private float referenceDamage = 10f;

        [SerializeField]
        [Tooltip("How knockback grows with damage. 1 is proportional; lower falls off, so 0.63 makes triple damage throw twice as far.")]
        [Range(0f, 1f)]
        private float damageFalloff = 0.63f;

        [SerializeField]
        [Tooltip("Speed the body is thrown upward at, on top of the knockback.")]
        [Min(0f)]
        private float upward = 2f;

        [SerializeField]
        [Tooltip("Keep the speed the enemy was walking at when it died.")]
        private bool inheritVelocity = true;

        [SerializeField]
        [Tooltip("Least spin, in degrees per second, the body tumbles at. It turns about a random axis.")]
        [Min(0f)]
        private float minSpin = 180f;

        [SerializeField]
        [Tooltip("Most spin, in degrees per second, the body tumbles at.")]
        [Min(0f)]
        private float maxSpin = 540f;

        [Header("Cleanup")]
        [SerializeField]
        [Tooltip("Seconds the body lies there before it starts to fade.")]
        [Min(0f)]
        private float fadeDelay = 3f;

        [SerializeField]
        [Tooltip("Seconds the body takes to dissolve away once it starts. It is removed after.")]
        [Min(0.01f)]
        private float fadeTime = 0.5f;

        private EnemyHealth health;
        private Enemy enemy;

        // Damaged is raised just before Died, so this holds the killing blow.
        private float lastDamage;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            enemy = GetComponent<Enemy>();
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        private void OnDamaged(EnemyHealth damaged, float amount)
        {
            lastDamage = amount;
        }

        private void OnDied(EnemyHealth died)
        {
            if (ragdollPrefab == null)
            {
                Debug.LogWarning($"[DeathRagdoll] {name} has no ragdoll prefab assigned.", this);
                return;
            }

            GameObject ragdoll = Instantiate(ragdollPrefab, transform.position, transform.rotation);
            ragdoll.name = name + " (Ragdoll)";

            foreach (Animator animator in ragdoll.GetComponentsInChildren<Animator>())
            {
                animator.enabled = false;
            }

            if (ragdoll.GetComponentInChildren<Enemy>() != null || ragdoll.GetComponentInChildren<EnemyHealth>() != null)
            {
                Debug.LogWarning($"[DeathRagdoll] {ragdollPrefab.name} still has enemy scripts on it. Remove them, or the body can be attacked and killed again.", ragdollPrefab);
            }

            CopyPose(poseSource != null ? poseSource : transform, ragdoll.transform);
            Throw(ragdoll);

            ragdoll.AddComponent<RagdollFade>().Begin(fadeDelay, fadeTime);
        }

        /// <summary>
        /// Puts every part of the ragdoll where its namesake on the enemy is,
        /// in world space, so the two hierarchies need not match exactly.
        /// </summary>
        private static void CopyPose(Transform source, Transform ragdoll)
        {
            Dictionary<string, Transform> parts = new Dictionary<string, Transform>();

            foreach (Transform part in source.GetComponentsInChildren<Transform>())
            {
                // The first of a name wins, the way Find would pick it.
                parts.TryAdd(part.name, part);
            }

            // Parents come before their children here, so each part is placed
            // after whatever it hangs from and stays exactly where it is put.
            foreach (Transform part in ragdoll.GetComponentsInChildren<Transform>())
            {
                if (parts.TryGetValue(part.name, out Transform match))
                {
                    part.SetPositionAndRotation(match.position, match.rotation);
                }
            }
        }

        private void Throw(GameObject ragdoll)
        {
            Vector3 velocity = inheritVelocity && enemy != null ? enemy.Velocity : Vector3.zero;

            if (enemy != null && enemy.HasTarget)
            {
                Vector3 away = transform.position - enemy.Target.position;
                away.y = 0f;

                if (away.sqrMagnitude > 0.0001f)
                {
                    velocity += away.normalized * Knockback(lastDamage);
                }
            }

            velocity += Vector3.up * upward;

            Rigidbody[] parts = ragdoll.GetComponentsInChildren<Rigidbody>();
            Vector3 spin = Random.onUnitSphere * (Random.Range(minSpin, maxSpin) * Mathf.Deg2Rad);
            Vector3 center = CenterOfMass(parts);

            foreach (Rigidbody part in parts)
            {
                if (!part.isKinematic)
                {
                    // Each part moves as if the whole body were one solid
                    // turning about its center, so it tumbles as one rather
                    // than every limb twirling on its own.
                    part.linearVelocity = velocity + Vector3.Cross(spin, part.worldCenterOfMass - center);
                    part.angularVelocity = spin;
                }
            }
        }

        /// <summary>
        /// Throw speed for a killing blow of <paramref name="damage"/>:
        /// <see cref="knockback"/> at <see cref="referenceDamage"/>, growing
        /// by <see cref="damageFalloff"/> as a power of the ratio between them.
        /// </summary>
        private float Knockback(float damage)
        {
            return knockback * Mathf.Pow(Mathf.Max(0f, damage) / referenceDamage, damageFalloff);
        }

        private static Vector3 CenterOfMass(Rigidbody[] parts)
        {
            Vector3 sum = Vector3.zero;
            float mass = 0f;

            foreach (Rigidbody part in parts)
            {
                if (!part.isKinematic)
                {
                    sum += part.worldCenterOfMass * part.mass;
                    mass += part.mass;
                }
            }

            return mass > 0f ? sum / mass : Vector3.zero;
        }
    }
}
