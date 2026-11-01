using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Leaves fading copies of an enemy behind it as it moves: its meshes,
    /// frozen in the pose it had, each fading away over
    /// <see cref="lifetime"/>.
    /// </summary>
    /// <remarks>
    /// Goes on the enemy's root. Each copy rebuilds the enemy's model out of
    /// bare transforms and renderers only -- its skinned meshes on a copy of
    /// its rig, its plain meshes, its materials -- and nothing else, so a copy
    /// can never be mistaken for an enemy, be hit, or run any of its scripts.
    /// A copy is left every <see cref="interval"/> seconds, but only once the
    /// enemy has moved <see cref="minMove"/> since the last, so standing still
    /// does not stack them up.
    ///
    /// Two ways to fade (<see cref="fadeStyle"/>):
    ///   Darken  The copy stays solid and its colour sinks to black, so it
    ///           melts into the black world with no stipple. The default.
    ///   Dither  The materials' dithered <c>_Fade</c>, a stipple thinning out.
    ///
    /// Copies are pooled: an old one is reused by copying the enemy's current
    /// pose onto it, unless the model has gained or lost parts since, when a
    /// fresh one is built.
    ///
    /// Off until <see cref="Activate"/> unless <see cref="activeOnStart"/> is
    /// set; <see cref="ChromatistPhaseTwo"/> turns it on after the stab. Stops
    /// when the enemy dies.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AfterimageAura : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        public enum FadeStyle
        {
            Darken,
            Dither,
        }

        [SerializeField]
        [Tooltip("On from the start. Off, it waits for Activate, as the Chromatist's phase two calls.")]
        private bool activeOnStart;

        [SerializeField]
        [Tooltip("Seconds between copies.")]
        [Min(0.01f)]
        private float interval = 0.1f;

        [SerializeField]
        [Tooltip("How far the enemy must have moved since the last copy to leave another.")]
        [Min(0f)]
        private float minMove = 0.4f;

        [SerializeField]
        [Tooltip("Seconds each copy takes to fade away.")]
        [Min(0.01f)]
        private float lifetime = 0.5f;

        [SerializeField]
        [Tooltip("Darken: the copy stays solid and sinks to black. Dither: it thins out in a stipple.")]
        private FadeStyle fadeStyle = FadeStyle.Darken;

        [SerializeField]
        [Tooltip("How strong a copy starts, 0 to 1: its brightness for Darken, its solidity for Dither. It fades from here to nothing.")]
        [Range(0f, 1f)]
        private float startOpacity = 0.6f;

        [Header("Colour")]
        [SerializeField]
        [Tooltip("Each copy starts black and pales to white as it ages, so those nearest the enemy are black and those further behind ever whiter. Overrides the colours below.")]
        private bool shadesOfBlack = true;

        [SerializeField]
        [Tooltip("Tints every copy one flat colour. Off keeps the enemy's own colours.")]
        private bool tint;

        [SerializeField]
        [Tooltip("The tint, unless Random Colours is on.")]
        private Color tintColour = new Color(0.5f, 0.5f, 0.5f, 1f);

        [SerializeField]
        [Tooltip("Gives each copy its own random colour, instead of the Tint Colour or the enemy's own.")]
        private bool randomColours = true;

        [SerializeField]
        [Tooltip("Colours to pick from at random. Empty picks any hue at the saturation and brightness below.")]
        private List<Color> palette = new List<Color>();

        [SerializeField]
        [Range(0f, 1f)]
        private float randomSaturation = 0.9f;

        [SerializeField]
        [Tooltip("Brightness of a random hue. Above 1 blooms.")]
        [Min(0f)]
        private float randomBrightness = 1.5f;

        private sealed class Ghost
        {
            public GameObject root;
            public Transform[] bones;
            public Renderer[] sources;
            public Renderer[] renderers;
            public Color[] ownColours;
            public Color? colour;
            public float age;
        }

        private readonly List<Ghost> live = new List<Ghost>();
        private readonly Stack<Ghost> pool = new Stack<Ghost>();
        private readonly List<Renderer> sourceRenderers = new List<Renderer>();

        private Enemy enemy;
        private EnemyHealth health;
        private MaterialPropertyBlock block;
        private bool active;
        private float clock;
        private Vector3 lastPosition;

        /// <summary>True while copies are being left.</summary>
        public bool IsActive => active;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            health = GetComponent<EnemyHealth>();
            block = new MaterialPropertyBlock();
            active = activeOnStart;
            lastPosition = transform.position;
        }

        /// <summary>Starts leaving copies.</summary>
        public void Activate()
        {
            active = true;
            clock = 0f;
            lastPosition = transform.position;
        }

        /// <summary>Stops leaving copies. Those already left finish fading.</summary>
        public void Deactivate()
        {
            active = false;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            if (active && (health == null || !health.IsDead))
            {
                clock += deltaTime;

                if (clock >= interval && (transform.position - lastPosition).sqrMagnitude >= minMove * minMove)
                {
                    clock = 0f;
                    lastPosition = transform.position;
                    Snapshot();
                }
            }

            for (int i = live.Count - 1; i >= 0; i--)
            {
                Ghost ghost = live[i];
                ghost.age += deltaTime;
                float left = 1f - ghost.age / lifetime;

                if (left <= 0f)
                {
                    ghost.root.SetActive(false);
                    live.RemoveAt(i);
                    pool.Push(ghost);
                    continue;
                }

                Shade(ghost, startOpacity * left, 1f - left);
            }
        }

        private void OnDisable()
        {
            foreach (Ghost ghost in live)
            {
                ghost.root.SetActive(false);
                pool.Push(ghost);
            }

            live.Clear();
        }

        private void OnDestroy()
        {
            foreach (Ghost ghost in pool)
            {
                if (ghost.root != null)
                {
                    Destroy(ghost.root);
                }
            }
        }

        private Transform Model => enemy != null ? enemy.Model : transform;

        private void Snapshot()
        {
            Transform model = Model;
            Transform[] source = model.GetComponentsInChildren<Transform>(true);
            sourceRenderers.Clear();
            model.GetComponentsInChildren(true, sourceRenderers);
            sourceRenderers.RemoveAll(part => !(part is SkinnedMeshRenderer || part is MeshRenderer));

            Ghost ghost = null;

            while (pool.Count > 0 && ghost == null)
            {
                Ghost reused = pool.Pop();

                if (reused.root != null && reused.bones.Length == source.Length
                    && reused.sources.Length == sourceRenderers.Count)
                {
                    ghost = reused;
                }
                else if (reused.root != null)
                {
                    Destroy(reused.root);
                }
            }

            if (ghost == null)
            {
                ghost = Build(model, source);
            }

            // The pose as it is this frame, after the animator.
            ghost.root.transform.SetPositionAndRotation(model.position, model.rotation);
            ghost.root.transform.localScale = model.lossyScale;

            for (int i = 1; i < source.Length; i++)
            {
                Transform bone = ghost.bones[i];
                bone.localPosition = source[i].localPosition;
                bone.localRotation = source[i].localRotation;
                bone.localScale = source[i].localScale;
                bone.gameObject.SetActive(source[i].gameObject.activeSelf);
            }

            for (int i = 0; i < ghost.renderers.Length; i++)
            {
                ghost.renderers[i].enabled = ghost.sources[i] != null && ghost.sources[i].enabled;
            }

            ghost.colour = PickColour();
            ghost.age = 0f;
            ghost.root.SetActive(true);
            live.Add(ghost);
            Shade(ghost, startOpacity, 0f);
        }

        /// <summary>
        /// A copy of the model's transforms with its renderers rebuilt on
        /// them, and nothing else.
        /// </summary>
        private Ghost Build(Transform model, Transform[] source)
        {
            Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>(source.Length);
            Transform[] bones = new Transform[source.Length];

            GameObject root = new GameObject($"{name} Afterimage");
            root.SetActive(false);
            copies[model] = root.transform;
            bones[0] = root.transform;

            // Parents come before their children in this list.
            for (int i = 1; i < source.Length; i++)
            {
                Transform copy = new GameObject(source[i].name).transform;
                copy.SetParent(copies[source[i].parent], false);
                copies[source[i]] = copy;
                bones[i] = copy;
            }

            Renderer[] sources = sourceRenderers.ToArray();
            Renderer[] renderers = new Renderer[sources.Length];
            Color[] ownColours = new Color[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                Renderer from = sources[i];
                GameObject host = copies[from.transform].gameObject;
                Renderer made;

                if (from is SkinnedMeshRenderer skinned)
                {
                    SkinnedMeshRenderer to = host.AddComponent<SkinnedMeshRenderer>();
                    to.sharedMesh = skinned.sharedMesh;
                    Transform[] fromBones = skinned.bones;
                    Transform[] toBones = new Transform[fromBones.Length];

                    for (int b = 0; b < fromBones.Length; b++)
                    {
                        toBones[b] = fromBones[b] != null && copies.TryGetValue(fromBones[b], out Transform mapped) ? mapped : null;
                    }

                    to.bones = toBones;
                    to.rootBone = skinned.rootBone != null && copies.TryGetValue(skinned.rootBone, out Transform rootBone) ? rootBone : null;
                    to.localBounds = skinned.localBounds;
                    to.updateWhenOffscreen = true;
                    made = to;
                }
                else
                {
                    MeshFilter filter = from.GetComponent<MeshFilter>();
                    host.AddComponent<MeshFilter>().sharedMesh = filter != null ? filter.sharedMesh : null;
                    made = host.AddComponent<MeshRenderer>();
                }

                made.sharedMaterials = from.sharedMaterials;
                made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                made.receiveShadows = false;
                renderers[i] = made;

                Material material = from.sharedMaterial;
                ownColours[i] = material != null && material.HasProperty(FlatMaterial.BaseColorId)
                    ? material.GetColor(FlatMaterial.BaseColorId)
                    : Color.white;
            }

            return new Ghost
            {
                root = root,
                bones = bones,
                sources = sources,
                renderers = renderers,
                ownColours = ownColours,
            };
        }

        /// <summary>The copy's tint, or null to keep the enemy's own colours.</summary>
        private Color? PickColour()
        {
            if (randomColours)
            {
                if (palette.Count > 0)
                {
                    return palette[Random.Range(0, palette.Count)];
                }

                return Random.ColorHSV(0f, 1f, randomSaturation, randomSaturation, randomBrightness, randomBrightness);
            }

            return tint ? tintColour : (Color?)null;
        }

        /// <summary>
        /// Shows the copy at <paramref name="strength"/>, 1 full and 0 gone,
        /// <paramref name="aged"/> of the way through its life.
        /// </summary>
        private void Shade(Ghost ghost, float strength, float aged)
        {
            for (int i = 0; i < ghost.renderers.Length; i++)
            {
                Renderer part = ghost.renderers[i];

                // Fresh each time, so a reused copy keeps no old tint.
                block.Clear();
                Color colour = ghost.colour ?? ghost.ownColours[i];

                if (shadesOfBlack)
                {
                    // Black to white is the fade; darkening would undo it.
                    block.SetColor(FlatMaterial.BaseColorId, Color.Lerp(Color.black, Color.white, aged));

                    if (fadeStyle == FadeStyle.Dither)
                    {
                        block.SetFloat(FadeId, strength);
                    }
                }
                else if (fadeStyle == FadeStyle.Darken)
                {
                    block.SetColor(FlatMaterial.BaseColorId, new Color(colour.r * strength, colour.g * strength, colour.b * strength, colour.a));
                }
                else
                {
                    block.SetFloat(FadeId, strength);
                    block.SetColor(FlatMaterial.BaseColorId, colour);
                }

                part.SetPropertyBlock(block);
            }
        }
    }
}
