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
    /// every pixel full colour. A built trail streams from the back of the
    /// body (<see cref="length"/>), and when the bullet ends it is left
    /// behind to shrink away rather than vanish with it, as a
    /// <see cref="HomingMissile"/>'s is. Trails are pooled too.
    ///
    /// For a stream of bullets, <see cref="Spawn"/> rather than Instantiate:
    /// it reuses bullets that are done instead of making new ones, and makes
    /// new ones from a plain-mesh copy of the prefab's art through
    /// <see cref="ArtPool"/>, without its ProBuilder parts, which otherwise
    /// rebuild every mesh from scratch on every copy. A bullet from
    /// <see cref="Spawn"/> is put away when it ends; any other is destroyed.
    ///
    /// <see cref="LaunchBoomerang"/> throws it out and back instead: it
    /// slows to a stop, spins round to face home, and speeds back along the
    /// same line, ending where it was thrown from.
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
        [Tooltip("For a long shot, such as an arrow: the length of its body, centred on the pivot along its flight. The player is hit anywhere along it, not only at the front. 0 for a round bullet.")]
        [Min(0f)]
        private float length;

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

        private static readonly Collider[] BodyHits = new Collider[8];

        // Built trails put away once they have shrunk away behind a finished bullet.
        private static readonly Stack<TrailRenderer> spareTails = new Stack<TrailRenderer>();

        private sealed class Pool
        {
            public EnemyProjectile template;
            public readonly Stack<EnemyProjectile> waiting = new Stack<EnemyProjectile>();
        }

        private static readonly Dictionary<EnemyProjectile, Pool> pools = new Dictionary<EnemyProjectile, Pool>();
        private static Transform poolRoot;

        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly List<EnemyProjectile> live = new List<EnemyProjectile>();

        private Vector3 velocity;
        private bool launched;
        private float age;
        private Pool pool;
        private TrailRenderer tail;
        private bool builtTail;

        // Out and back, after LaunchBoomerang.
        private bool boomerang;
        private Vector3 throwVelocity;
        private float turnTime;
        private float spinTime;
        private float spinDegrees;
        private Quaternion outRotation;

        // Recoloured through Tint, and so to be cleared when reused.
        private bool tinted;

        // Fading out harmlessly, after FadeOutAll.
        private Renderer[] parts;
        private MaterialPropertyBlock block;
        private Vector3 fullScale;
        private float fadeTime = -1f;
        private float fadeAge;

        public Vector3 Velocity => velocity;

        /// <summary>
        /// Every bullet in flight stops being able to hurt anyone and fades
        /// away over <paramref name="seconds"/>, flying on as it goes. For
        /// clearing the stage, as for a cutscene.
        /// </summary>
        public static void FadeOutAll(float seconds)
        {
            // A copy: finishing takes bullets off the list.
            foreach (EnemyProjectile shot in live.ToArray())
            {
                if (shot != null)
                {
                    shot.FadeOut(seconds);
                }
            }
        }

        /// <summary>Stops this bullet hurting anyone and fades it away over <paramref name="seconds"/>.</summary>
        public void FadeOut(float seconds)
        {
            if (fadeTime >= 0f)
            {
                return;
            }

            fadeTime = Mathf.Max(0.01f, seconds);
            fadeAge = 0f;
        }

        private void OnEnable()
        {
            live.Add(this);
        }

        private void OnDisable()
        {
            live.Remove(this);
        }

        /// <summary>
        /// One step of the fade: dissolving through _Fade where the material
        /// has it, as NeoNoir/Flat does, and shrinking away where it does not.
        /// </summary>
        private void Fading(float deltaTime)
        {
            transform.position += velocity * deltaTime;
            fadeAge += deltaTime;
            float left = 1f - Mathf.Clamp01(fadeAge / fadeTime);

            if (left <= 0f)
            {
                Finish();
                return;
            }

            foreach (Renderer part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                if (part.sharedMaterial != null && part.sharedMaterial.HasProperty(FadeId))
                {
                    part.GetPropertyBlock(block);
                    block.SetFloat(FadeId, left);
                    part.SetPropertyBlock(block);
                }
                else
                {
                    transform.localScale = fullScale * left;
                }
            }
        }

        /// <summary>Undoes any fade, for a bullet going out again from the pool.</summary>
        private void Unfade()
        {
            if (fadeTime < 0f)
            {
                return;
            }

            fadeTime = -1f;
            transform.localScale = fullScale;

            foreach (Renderer part in parts)
            {
                if (part != null && part.sharedMaterial != null && part.sharedMaterial.HasProperty(FadeId))
                {
                    part.GetPropertyBlock(block);
                    block.SetFloat(FadeId, 1f);
                    part.SetPropertyBlock(block);
                }
            }
        }

        /// <summary>
        /// A bullet like <paramref name="prefab"/> at the given place, ready
        /// to <see cref="Launch"/>: one put away earlier if any is waiting,
        /// otherwise a new one made cheaply. Put away again when it ends.
        /// </summary>
        public static EnemyProjectile Spawn(EnemyProjectile prefab, Vector3 position, Quaternion rotation)
        {
            Pool entry = PoolFor(prefab);
            EnemyProjectile shot = null;

            // Bullets out when a scene unloaded went with it.
            while (shot == null && entry.waiting.Count > 0)
            {
                shot = entry.waiting.Pop();
            }

            if (shot == null)
            {
                shot = Instantiate(entry.template, PoolRoot());
                shot.name = prefab.name;
                shot.pool = entry;
            }

            // Out of the pool, which lives on across scenes, and into this
            // one, so it goes with the scene like any other bullet.
            shot.transform.SetParent(null, false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                shot.gameObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            shot.transform.SetPositionAndRotation(position, rotation);
            shot.launched = false;
            shot.boomerang = false;
            shot.age = 0f;
            shot.gameObject.SetActive(true);
            shot.Unfade();

            // Its last one was left behind when it ended.
            if (shot.builtTail && shot.tail == null)
            {
                shot.tail = shot.TakeTail();
            }

            // Or the tail would be drawn from wherever it last ended.
            if (shot.tail != null)
            {
                shot.tail.Clear();
            }

            if (shot.tinted)
            {
                shot.Tint(null);
            }

            return shot;
        }

        /// <summary>
        /// The pool for <paramref name="prefab"/>, made the first time: a
        /// switched-off bullet with the prefab's settings, carrying a
        /// plain-mesh copy of its art, that every new bullet is copied from.
        /// </summary>
        private static Pool PoolFor(EnemyProjectile prefab)
        {
            if (pools.TryGetValue(prefab, out Pool entry) && entry.template != null)
            {
                return entry;
            }

            // Under the switched-off root, so it never wakes: its trail is
            // built by each copy as it first goes out.
            GameObject host = new GameObject(prefab.name);
            host.layer = prefab.gameObject.layer;
            host.transform.SetParent(PoolRoot(), false);

            EnemyProjectile template = host.AddComponent<EnemyProjectile>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(prefab), template);

            GameObject art = ArtPool.Take(prefab.gameObject, Vector3.zero, Quaternion.identity);
            art.transform.SetParent(host.transform, false);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;

            entry = new Pool { template = template };
            pools[prefab] = entry;
            return entry;
        }

        private static Transform PoolRoot()
        {
            if (poolRoot == null)
            {
                GameObject host = new GameObject("ProjectilePool");
                host.SetActive(false);
                DontDestroyOnLoad(host);
                poolRoot = host.transform;
            }

            return poolRoot;
        }

        /// <summary>Done: put away for reuse if it came from <see cref="Spawn"/>, otherwise destroyed.</summary>
        private void Finish()
        {
            if (builtTail && tail != null)
            {
                LeaveTail();
            }

            if (pool == null || pool.template == null)
            {
                Destroy(gameObject);
                return;
            }

            gameObject.SetActive(false);
            transform.SetParent(PoolRoot(), false);
            pool.waiting.Push(this);
        }

        /// <summary>
        /// Recolours the bullet and its trail to <paramref name="colour"/>,
        /// through property blocks, so its materials need NeoNoir/Flat's
        /// _BaseColor. Null puts back its own colours. A pooled bullet comes
        /// out of <see cref="Spawn"/> untinted.
        /// </summary>
        public void Tint(Color? colour)
        {
            foreach (Renderer part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                if (colour.HasValue)
                {
                    FlatMaterial.Tint(part, colour.Value, block);
                }
                else
                {
                    part.SetPropertyBlock(null);
                }
            }

            if (tail != null)
            {
                if (colour.HasValue)
                {
                    FlatMaterial.Tint(tail, colour.Value, block);
                }
                else
                {
                    tail.SetPropertyBlock(null);
                }
            }

            tinted = colour.HasValue;
        }

        /// <summary>Sends the bullet along <paramref name="direction"/> at its own speed.</summary>
        public void Launch(Vector3 direction)
        {
            Vector3 heading = direction.sqrMagnitude > 0.000001f ? direction.normalized : transform.forward;
            velocity = heading * speed;
            launched = true;
            age = 0f;
            boomerang = false;

            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
        }

        /// <summary>
        /// Throws the bullet out along <paramref name="direction"/> and back:
        /// it slows steadily to a stop <paramref name="reach"/> away after
        /// <paramref name="outTime"/> seconds, turns
        /// <paramref name="spin"/> degrees about the vertical over
        /// <paramref name="spinSeconds"/>, and comes back as it went, ending
        /// where it started. Its own speed and lifetime are ignored.
        /// </summary>
        public void LaunchBoomerang(Vector3 direction, float reach, float outTime, float spin, float spinSeconds)
        {
            Launch(direction);

            turnTime = Mathf.Max(0.01f, outTime);
            spinTime = Mathf.Max(0f, spinSeconds);
            spinDegrees = spin;
            outRotation = transform.rotation;

            // Slowing evenly to a stop covers half what the start speed would.
            throwVelocity = velocity.normalized * (2f * Mathf.Max(0f, reach) / turnTime);
            velocity = throwVelocity;
            boomerang = true;
        }

        /// <summary>The boomerang's velocity and facing for where it is in its flight.</summary>
        private void Boomerang()
        {
            // Out, slowing to nothing at the turn, then back, speeding up.
            velocity = throwVelocity * (1f - age / turnTime);

            if (age <= turnTime)
            {
                return;
            }

            float spun = spinTime > 0f ? Mathf.Clamp01((age - turnTime) / spinTime) : 1f;
            spun = spun * spun * (3f - 2f * spun);
            transform.rotation = Quaternion.AngleAxis(spinDegrees * spun, Vector3.up) * outRotation;
        }

        private void Awake()
        {
            // Measured before the trail exists, so it is never among them.
            parts = GetComponentsInChildren<Renderer>();

            if (trail && GetComponentInChildren<TrailRenderer>() == null)
            {
                builtTail = true;
                tail = TakeTail();
            }
            else
            {
                tail = GetComponentInChildren<TrailRenderer>();
            }

            block = new MaterialPropertyBlock();
            fullScale = transform.localScale;
        }

        /// <summary>
        /// A trail for this bullet, one put away earlier if any is waiting:
        /// set up to its settings and streaming from the back of its body.
        /// </summary>
        private TrailRenderer TakeTail()
        {
            TrailRenderer line = null;

            // Trails left behind when a scene unloaded went with it.
            while (line == null && spareTails.Count > 0)
            {
                line = spareTails.Pop();
            }

            if (line == null)
            {
                line = new GameObject("Trail").AddComponent<TrailRenderer>();
                line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                line.minVertexDistance = 0.05f;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
            }

            line.time = trailTime;
            line.widthMultiplier = trailWidth;

            Material source = trailMaterial;

            if (source == null)
            {
                foreach (Renderer model in parts)
                {
                    if (model != null)
                    {
                        source = model.sharedMaterial;
                        break;
                    }
                }
            }

            line.sharedMaterial = source != null ? DoubleSided(source) : null;

            line.transform.SetParent(transform, false);
            line.transform.localPosition = Vector3.back * (length * 0.5f);
            line.transform.localRotation = Quaternion.identity;
            line.Clear();
            line.emitting = true;

            // A pooled trail may carry another bullet's tint.
            line.SetPropertyBlock(null);
            return line;
        }

        /// <summary>Leaves the trail where it is, to shrink away and then be put away.</summary>
        private void LeaveTail()
        {
            tail.transform.SetParent(null, true);
            tail.emitting = false;

            TailDrain drain = tail.GetComponent<TailDrain>();

            if (drain == null)
            {
                drain = tail.gameObject.AddComponent<TailDrain>();
            }

            drain.Begin(tail);
            tail = null;
        }

        /// <summary>Puts a left-behind trail away once it has shrunk to nothing.</summary>
        private sealed class TailDrain : MonoBehaviour
        {
            private TrailRenderer line;
            private float left;

            public void Begin(TrailRenderer trail)
            {
                line = trail;
                left = trail.time;
                enabled = true;
            }

            private void Update()
            {
                left -= Time.deltaTime;

                if (left > 0f)
                {
                    return;
                }

                enabled = false;
                line.Clear();
                transform.SetParent(PoolRoot(), false);
                spareTails.Push(line);
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

            if (fadeTime >= 0f)
            {
                Fading(deltaTime);
                return;
            }

            if (boomerang)
            {
                Boomerang();
            }

            Vector3 from = transform.position;
            Vector3 to = from + velocity * deltaTime;

            // The front of a long body, half its length ahead of the pivot.
            Vector3 nose = velocity.sqrMagnitude > 0f ? velocity / velocity.magnitude * (length * 0.5f) : Vector3.zero;

            if (ParryBarrier.TryCatch(from + nose, to + nose, radius, velocity))
            {
                Finish();
                return;
            }

            float distance = Vector3.Distance(from, to);

            if (distance > 0f && Physics.SphereCast(from + nose, radius, velocity / velocity.magnitude, out RaycastHit hit,
                                                    distance, hitMask, QueryTriggerInteraction.Ignore))
            {
                PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(damage, from);
                }

                Finish();
                return;
            }

            transform.position = to;

            // The front is swept above; the rest of a long body is checked
            // where it now lies, for a player stepping into its side.
            if (length > 0f && distance > 0f)
            {
                int count = Physics.OverlapCapsuleNonAlloc(to + nose, to - nose, radius, BodyHits, hitMask, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    PlayerHealth health = BodyHits[i].GetComponentInParent<PlayerHealth>();

                    if (health != null)
                    {
                        health.TakeDamage(damage, to);
                        Finish();
                        return;
                    }
                }
            }

            age += deltaTime;
            if (boomerang ? age >= 2f * turnTime : age >= lifetime)
            {
                Finish();
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
            if (length <= 0f)
            {
                Gizmos.DrawWireSphere(transform.position, radius);
            }
            else
            {
                Vector3 half = transform.forward * (length * 0.5f);
                Gizmos.DrawWireSphere(transform.position + half, radius);
                Gizmos.DrawWireSphere(transform.position - half, radius);
                Gizmos.DrawLine(transform.position + half, transform.position - half);
            }
        }
    }
}
