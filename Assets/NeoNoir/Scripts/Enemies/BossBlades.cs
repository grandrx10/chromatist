using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Blades circling a boss the way the player's circle the player: around
    /// it at a fixed height, following where it goes but never turning with
    /// it.
    /// </summary>
    /// <remarks>
    /// Goes on the boss's root, beside <see cref="Enemy"/>. Each blade is a
    /// prefab -- pure art, any model -- spawned unparented when the boss
    /// first appears, and spaced evenly around the orbit. Like a player
    /// blade, each rests flat side in and tip down; turn
    /// <see cref="BladeEntry.rotationOffset"/> for a model built another way.
    ///
    /// The orbit follows the boss through a smoothed anchor, so the blades
    /// trail a little behind a dash and catch up, as the player's do.
    ///
    /// An attack can take a blade out of the orbit with <see cref="Borrow"/>,
    /// move it however it likes, and hand it back with <see cref="Return"/>,
    /// after which it blends back into its place over
    /// <see cref="returnTime"/>.
    ///
    /// When the boss dies the blades dissolve away with the game's usual
    /// dithered fade, and they are destroyed along with the boss.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BossBlades : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [System.Serializable]
        public sealed class BladeEntry
        {
            [Tooltip("The blade's art.")]
            public GameObject prefab;

            [Tooltip("Turns the model within its orbit pose, for one not built flat side in and tip down.")]
            public Vector3 rotationOffset;

            [Tooltip("Scales the model.")]
            [Min(0.01f)]
            public float scale = 1f;
        }

        [SerializeField] private List<BladeEntry> blades = new List<BladeEntry>();

        [Header("Orbit")]
        [SerializeField]
        [Tooltip("Degrees per second around the boss. Negative reverses direction.")]
        private float orbitSpeed = 45f;

        [SerializeField]
        [Tooltip("Distance from the boss.")]
        [Min(0f)]
        private float orbitRadius = 2.5f;

        [SerializeField]
        [Tooltip("Height above the boss's pivot.")]
        private float orbitHeight = 1.8f;

        [SerializeField] private float bobAmplitude = 0.1f;
        [SerializeField] private float bobFrequency = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the blades take to catch up when the boss moves. Higher is looser.")]
        [Min(0f)]
        private float followTime = 0.3f;

        [SerializeField]
        [Tooltip("Seconds a returned blade takes to blend back into its place in the orbit.")]
        [Min(0f)]
        private float returnTime = 0.3f;

        [Header("Death")]
        [SerializeField]
        [Tooltip("Seconds the blades take to dissolve when the boss dies.")]
        [Min(0f)]
        private float fadeTime = 0.5f;

        private sealed class Spawned
        {
            public GameObject prefab;
            public Transform transform;
            public Quaternion offset;
            public Renderer[] renderers;
            public bool borrowed;
            public object borrower;

            // 0 is wherever it was handed back, 1 its place in the orbit.
            public float blend = 1f;
            public Vector3 fromPosition;
            public Quaternion fromRotation;
        }

        private readonly List<Spawned> spawned = new List<Spawned>();
        private EnemyHealth health;
        private MaterialPropertyBlock block;
        private Vector3 anchor;
        private Vector3 anchorVelocity;
        private float visibility = 1f;
        private bool dying;

        /// <summary>How many blades circle the boss.</summary>
        public int Count => spawned.Count;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Died += OnDied;
            }

            SetShown(true);
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }

            SetShown(false);
        }

        private void Start()
        {
            block = new MaterialPropertyBlock();
            anchor = transform.position;

            foreach (BladeEntry entry in blades)
            {
                if (entry == null || entry.prefab == null)
                {
                    continue;
                }

                // Unparented: a child would turn with the boss.
                GameObject blade = Instantiate(entry.prefab);
                blade.name = $"{name} {entry.prefab.name}";
                // 0 is what a list entry can start at when added in the
                // inspector; read it as "as built" rather than invisible.
                float scale = entry.scale > 0f ? entry.scale : 1f;
                blade.transform.localScale = entry.prefab.transform.localScale * scale;

                spawned.Add(new Spawned
                {
                    prefab = entry.prefab,
                    transform = blade.transform,
                    offset = Quaternion.Euler(entry.rotationOffset),
                    renderers = blade.GetComponentsInChildren<Renderer>(),
                });
            }

            Place(0f);
        }

        private void OnDestroy()
        {
            foreach (Spawned blade in spawned)
            {
                if (blade.transform != null)
                {
                    Destroy(blade.transform.gameObject);
                }
            }
        }

        private void OnDied(EnemyHealth died)
        {
            dying = true;
        }

        /// <summary>
        /// Takes blade <paramref name="index"/> out of the orbit and returns it,
        /// for an attack to move as it likes. Null if there is no such blade.
        /// <paramref name="borrower"/>, if given, is who has it, until it is
        /// returned or borrowed again; see <see cref="BorrowerOf"/>.
        /// </summary>
        public Transform Borrow(int index, object borrower = null)
        {
            if (index < 0 || index >= spawned.Count)
            {
                return null;
            }

            spawned[index].borrowed = true;
            spawned[index].borrower = borrower;
            return spawned[index].transform;
        }

        /// <summary>Who last borrowed blade <paramref name="index"/>, if it is out and they said; null otherwise.</summary>
        public object BorrowerOf(int index)
        {
            return index >= 0 && index < spawned.Count && spawned[index].borrowed ? spawned[index].borrower : null;
        }

        /// <summary>Hands blade <paramref name="index"/> back; it blends into its place in the orbit from wherever it is.</summary>
        public void Return(int index)
        {
            if (index < 0 || index >= spawned.Count || !spawned[index].borrowed)
            {
                return;
            }

            Spawned blade = spawned[index];
            blade.borrowed = false;
            blade.borrower = null;
            blade.blend = returnTime > 0f ? 0f : 1f;
            blade.fromPosition = blade.transform.position;
            blade.fromRotation = blade.transform.rotation;
        }

        /// <summary>True while blade <paramref name="index"/> is out of the orbit, borrowed by something.</summary>
        public bool IsBorrowed(int index)
        {
            return index >= 0 && index < spawned.Count && spawned[index].borrowed;
        }

        /// <summary>Blade <paramref name="index"/> itself, borrowed or not, without borrowing it. Null if there is no such blade.</summary>
        public Transform BladeAt(int index)
        {
            return index >= 0 && index < spawned.Count ? spawned[index].transform : null;
        }

        /// <summary>The index of the first blade spawned from <paramref name="prefab"/>, or -1 if none is.</summary>
        public int IndexOf(GameObject prefab)
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (prefab != null && spawned[i].prefab == prefab)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Blade <paramref name="index"/>'s Rotation Offset, to apply after posing it as a blade built flat side in and tip down.</summary>
        public Quaternion RotationOffset(int index)
        {
            return index >= 0 && index < spawned.Count ? spawned[index].offset : Quaternion.identity;
        }

        /// <summary>Where blade <paramref name="index"/> rests in the orbit right now.</summary>
        public Vector3 RestingPosition(int index)
        {
            OrbitPose(index, out Vector3 position, out _);
            return position;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            if (deltaTime <= 0f)
            {
                return;
            }

            if (dying && visibility > 0f)
            {
                visibility = fadeTime > 0f ? Mathf.MoveTowards(visibility, 0f, deltaTime / fadeTime) : 0f;
                ApplyVisibility();
            }

            Place(deltaTime);
        }

        private void Place(float deltaTime)
        {
            // Moved by a cutscene rather than walking: no sweeping across the map.
            if ((transform.position - anchor).sqrMagnitude > 100f)
            {
                anchor = transform.position;
                anchorVelocity = Vector3.zero;
            }

            anchor = deltaTime > 0f && followTime > 0f
                ? Vector3.SmoothDamp(anchor, transform.position, ref anchorVelocity, followTime, Mathf.Infinity, deltaTime)
                : transform.position;

            for (int i = 0; i < spawned.Count; i++)
            {
                Spawned blade = spawned[i];

                if (blade.transform == null || blade.borrowed)
                {
                    continue;
                }

                OrbitPose(i, out Vector3 position, out Quaternion rotation);

                if (blade.blend < 1f)
                {
                    blade.blend = Mathf.MoveTowards(blade.blend, 1f, deltaTime / Mathf.Max(0.0001f, returnTime));
                    float eased = blade.blend * blade.blend * (3f - 2f * blade.blend);
                    position = Vector3.Lerp(blade.fromPosition, position, eased);
                    rotation = Quaternion.Slerp(blade.fromRotation, rotation, eased);
                }

                blade.transform.SetPositionAndRotation(position, rotation);
            }
        }

        private void OrbitPose(int index, out Vector3 position, out Quaternion rotation)
        {
            // Off one clock, evenly spaced, the way the player's blades keep
            // their phase apart.
            float spacing = spawned.Count > 0 ? 360f / spawned.Count : 0f;
            float angle = Mathf.Repeat(Time.time * orbitSpeed + index * spacing, 360f);

            Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            float bob = Mathf.Sin(Time.time * bobFrequency * 2f * Mathf.PI) * bobAmplitude;

            position = anchor + radial * orbitRadius + Vector3.up * (orbitHeight + bob);

            // Flat turned inward, tip straight down, as a player blade rests.
            Quaternion offset = index < spawned.Count ? spawned[index].offset : Quaternion.identity;
            rotation = Quaternion.LookRotation(-radial, Vector3.up) * offset;
        }

        private void SetShown(bool shown)
        {
            foreach (Spawned blade in spawned)
            {
                if (blade.transform != null)
                {
                    blade.transform.gameObject.SetActive(shown);
                }
            }
        }

        private void ApplyVisibility()
        {
            foreach (Spawned blade in spawned)
            {
                foreach (Renderer part in blade.renderers)
                {
                    if (part == null)
                    {
                        continue;
                    }

                    Material material = part.sharedMaterial;

                    if (material != null && material.HasProperty(FadeId))
                    {
                        part.GetPropertyBlock(block);
                        block.SetFloat(FadeId, visibility);
                        part.SetPropertyBlock(block);
                    }
                    else
                    {
                        // Not on a shader that can dissolve: just switch off halfway.
                        part.enabled = visibility > 0.5f;
                    }
                }
            }
        }
    }
}
