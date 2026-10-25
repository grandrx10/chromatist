using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// An enemy bullet. Flies in a straight line and damages the player on
    /// contact, unless a <see cref="ParryBarrier"/> catches it first.
    /// </summary>
    /// <remarks>
    /// Moved by its transform and tested with a sphere cast along the path it
    /// covers each frame, rather than a trigger collider. A fast bullet would
    /// otherwise step straight over a thin target between two physics steps,
    /// and this way it needs no rigidbody or collider at all.
    ///
    /// The barrier is checked before anything else, so a parried bullet never
    /// hurts the player, even when both are touched in the same frame.
    ///
    /// Placed in a scene without being launched, it flies along its own
    /// forward.
    ///
    /// The trail is built at spawn unless the prefab already has a
    /// TrailRenderer, which is then left exactly as authored. It narrows to a
    /// point rather than fading out: a fade would blend toward the floor into
    /// mid-values the threshold crushes unpredictably, while a taper keeps
    /// every pixel full colour.
    /// </remarks>
    public sealed class EnemyProjectile : MonoBehaviour
    {
        [SerializeField]
        private float speed = 8f;

        [SerializeField]
        [Min(0f)]
        private float damage = 10f;

        [SerializeField]
        [Tooltip("Size of the bullet for hits and parries. Match it roughly to the model.")]
        [Min(0f)]
        private float radius = 0.15f;

        [SerializeField]
        [Tooltip("Seconds before the bullet removes itself.")]
        [Min(0.01f)]
        private float lifetime = 5f;

        [SerializeField]
        [Tooltip("What stops the bullet. Leave the Enemy layer out, or bullets hit whoever fired them. Trigger colliders, like the blades, are always ignored.")]
        private LayerMask hitMask = ~0;

        [Header("Trail")]
        [SerializeField]
        [Tooltip("Build a trail at spawn. Ignored if the prefab already has its own TrailRenderer.")]
        private bool trail = true;

        [SerializeField]
        [Tooltip("Seconds of travel the trail covers. Longer is a longer tail.")]
        [Min(0.01f)]
        private float trailTime = 0.15f;

        [SerializeField]
        [Tooltip("Width of the trail at the bullet. It narrows to nothing at the tail.")]
        [Min(0.001f)]
        private float trailWidth = 0.35f;

        [SerializeField]
        [Tooltip("Material for the trail. Empty uses the bullet model's own, so the tail matches the shot.")]
        private Material trailMaterial;

        // One double-sided copy per source material, shared by every bullet,
        // rather than a fresh material per shot.
        private static readonly Dictionary<Material, Material> doubleSided = new Dictionary<Material, Material>();
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private Vector3 velocity;
        private bool launched;
        private float age;

        public Vector3 Velocity => velocity;

        /// <summary>Sends the bullet along <paramref name="direction"/> at its own speed.</summary>
        public void Launch(Vector3 direction)
        {
            Vector3 heading = direction.sqrMagnitude > 0.000001f ? direction.normalized : transform.forward;
            velocity = heading * speed;
            launched = true;
            age = 0f;

            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
        }

        private void Awake()
        {
            if (trail && GetComponentInChildren<TrailRenderer>() == null)
            {
                BuildTrail();
            }
        }

        private void BuildTrail()
        {
            TrailRenderer line = gameObject.AddComponent<TrailRenderer>();
            line.time = trailTime;
            line.widthMultiplier = trailWidth;
            line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            line.minVertexDistance = 0.05f;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material source = trailMaterial;

            if (source == null)
            {
                foreach (Renderer model in GetComponentsInChildren<Renderer>())
                {
                    if (model != line)
                    {
                        source = model.sharedMaterial;
                        break;
                    }
                }
            }

            if (source != null)
            {
                line.sharedMaterial = DoubleSided(source);
            }
        }

        /// <summary>
        /// A trail is a camera-facing strip whose winding can come out either
        /// way, and NeoNoir/Flat culls back faces by default, which would hide
        /// it. So the trail draws with a copy that has culling off.
        /// </summary>
        private static Material DoubleSided(Material source)
        {
            if (!source.HasProperty(CullId) || source.GetFloat(CullId) == 0f)
            {
                return source;
            }

            if (!doubleSided.TryGetValue(source, out Material copy) || copy == null)
            {
                copy = new Material(source) { name = source.name + " (trail)" };
                copy.SetFloat(CullId, 0f);
                doubleSided[source] = copy;
            }

            return copy;
        }

        private void Start()
        {
            if (!launched)
            {
                Launch(transform.forward);
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 to = from + velocity * deltaTime;

            if (ParryBarrier.TryCatch(from, to, radius, velocity))
            {
                Destroy(gameObject);
                return;
            }

            float distance = Vector3.Distance(from, to);

            if (distance > 0f && Physics.SphereCast(from, radius, velocity / velocity.magnitude, out RaycastHit hit,
                                                    distance, hitMask, QueryTriggerInteraction.Ignore))
            {
                PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(damage);
                }

                Destroy(gameObject);
                return;
            }

            transform.position = to;

            age += deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
            }
        }

        private void Reset()
        {
            hitMask = LayerMask.GetMask("Default", "Player");
        }

        private void OnValidate()
        {
            speed = Mathf.Max(0f, speed);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
