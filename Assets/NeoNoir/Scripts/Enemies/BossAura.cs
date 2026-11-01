using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// A boss's aura: a filled white circle on the floor beneath it, with
    /// motes of every colour of the rainbow rising up out of it.
    /// </summary>
    /// <remarks>
    /// Goes on the boss's root, beside <see cref="Enemy"/>. Nothing to build
    /// in the scene: everything is made when the boss first appears.
    ///
    /// The circle follows the boss along the floor, found by a ray straight
    /// down, and stays flat on it even while the boss hops. Each colour's
    /// motes share one mesh in NeoNoir/Flat, so the whole aura costs one draw
    /// for the circle and one per colour. The colours are strongly saturated
    /// so they survive the two-tone threshold as spot colour.
    ///
    /// It fades in when the boss appears and out when it dies, with the
    /// game's usual dithered dissolve.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BossAura : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField]
        [Tooltip("Seconds the aura takes to fade in when the boss appears, and out when it dies.")]
        [Min(0f)]
        private float fadeTime = 0.6f;

        [Header("Circle")]
        [SerializeField]
        [Tooltip("Colour of the circle. Above 1 it blooms.")]
        [ColorUsage(false, true)]
        private Color circleColour = Color.white;

        [SerializeField]
        [Tooltip("Radius of the circle. 0 fits it to the boss's body.")]
        [Min(0f)]
        private float circleRadius;

        [SerializeField]
        [Tooltip("With an automatic radius: how much wider than the body the circle is.")]
        [Min(0f)]
        private float circlePadding = 1f;

        [SerializeField]
        [Tooltip("Height above the floor, so the circle never flickers into it.")]
        [Min(0f)]
        private float floorLift = 0.03f;

        [SerializeField]
        [Tooltip("What counts as floor for the ray down. Anything moved by physics never does.")]
        private LayerMask floorMask = ~0;

        [Header("Rising motes")]
        [SerializeField]
        [Tooltip("The colours the motes come in, each mote picking one. Keep them strongly saturated, or the threshold turns them grey. Above 1 they bloom.")]
        [ColorUsage(false, true)]
        private Color[] moteColours =
        {
            new Color(2f, 0f, 0f),
            new Color(2f, 0.8f, 0f),
            new Color(2f, 2f, 0f),
            new Color(0f, 2f, 0f),
            new Color(0f, 0.6f, 2f),
            new Color(0.9f, 0f, 2f),
        };

        [SerializeField]
        [Tooltip("Motes rising at once. 0 disables them.")]
        [Min(0)]
        private int moteCount = 30;

        [SerializeField] [Min(0.01f)] private float moteSize = 0.15f;

        [SerializeField]
        [Tooltip("How high a mote rises before it is gone.")]
        [Min(0f)]
        private float moteHeight = 3.5f;

        [SerializeField]
        [Tooltip("Seconds a mote takes to rise. Each varies a little.")]
        [Min(0.05f)]
        private float moteTime = 1.6f;

        [SerializeField]
        [Tooltip("How far a mote drifts sideways as it rises.")]
        [Min(0f)]
        private float moteSway = 0.25f;

        private const int Segments = 64;
        private const float RayHeight = 3f;

        private struct Mote
        {
            public int colour;
            public float angle;
            public float radius;
            public float age;
            public float life;
            public float phase;
        }

        private sealed class Layer
        {
            public Mesh mesh;
            public MeshRenderer renderer;
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
        }

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        private EnemyHealth health;
        private GameObject root;
        private Layer circle;
        private Layer[] moteLayers;
        private Mote[] motes;
        private MaterialPropertyBlock block;
        private float radius;

        // 0 hidden, 1 fully shown.
        private float fade;
        private float fadeTarget = 1f;

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

            if (root != null)
            {
                root.SetActive(true);
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }

            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private void Start()
        {
            Build();
            fade = fadeTime > 0f ? 0f : 1f;
            fadeTarget = health != null && health.IsDead ? 0f : 1f;
            ApplyColours();
        }

        private void OnDied(EnemyHealth died)
        {
            fadeTarget = 0f;
        }

        private void LateUpdate()
        {
            if (root == null)
            {
                return;
            }

            if (fade != fadeTarget)
            {
                fade = fadeTime > 0f ? Mathf.MoveTowards(fade, fadeTarget, Time.deltaTime / fadeTime) : fadeTarget;
                ApplyColours();
            }

            root.transform.position = FloorBelow() + Vector3.up * floorLift;
            UpdateMotes(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (root != null)
            {
                Destroy(root);
            }

            if (circle != null)
            {
                Destroy(circle.mesh);
            }

            if (moteLayers != null)
            {
                foreach (Layer layer in moteLayers)
                {
                    Destroy(layer.mesh);
                }
            }
        }

        private void Build()
        {
            Material material = FlatMaterial.DoubleSided;

            if (material == null)
            {
                return;
            }

            radius = circleRadius;

            if (radius <= 0f)
            {
                Bounds body = health != null ? health.BodyBounds() : new Bounds(transform.position, Vector3.one);
                radius = Mathf.Max(body.extents.x, body.extents.z) + circlePadding;
            }

            block = new MaterialPropertyBlock();
            root = new GameObject($"{name} Aura");

            circle = MakeLayer("Circle", material);
            AddDisc(circle, radius);
            Commit(circle);

            moteLayers = new Layer[moteColours.Length];

            for (int i = 0; i < moteLayers.Length; i++)
            {
                moteLayers[i] = MakeLayer($"Motes {i}", material);
                moteLayers[i].mesh.MarkDynamic();
            }

            motes = new Mote[moteColours.Length > 0 ? moteCount : 0];

            for (int i = 0; i < motes.Length; i++)
            {
                ResetMote(ref motes[i]);

                // Spread through their lives, so they do not all set off together.
                motes[i].age = Random.value * motes[i].life;
            }
        }

        private Layer MakeLayer(string layerName, Material material)
        {
            GameObject host = new GameObject(layerName);
            host.transform.SetParent(root.transform, false);

            Layer layer = new Layer { mesh = new Mesh { name = $"Aura {layerName}" } };
            host.AddComponent<MeshFilter>().sharedMesh = layer.mesh;

            layer.renderer = host.AddComponent<MeshRenderer>();
            layer.renderer.sharedMaterial = material;
            layer.renderer.shadowCastingMode = ShadowCastingMode.Off;
            layer.renderer.receiveShadows = false;
            return layer;
        }

        private void ResetMote(ref Mote mote)
        {
            mote.colour = Random.Range(0, moteLayers.Length);
            mote.angle = Random.Range(0f, Mathf.PI * 2f);

            // Square-rooted, so they spread evenly over the circle rather
            // than bunching in the middle.
            mote.radius = Mathf.Sqrt(Random.value);
            mote.age = 0f;
            mote.life = moteTime * Random.Range(0.7f, 1.3f);
            mote.phase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void UpdateMotes(float deltaTime)
        {
            foreach (Layer layer in moteLayers)
            {
                layer.vertices.Clear();
                layer.triangles.Clear();
            }

            for (int i = 0; i < motes.Length; i++)
            {
                motes[i].age += deltaTime;

                if (motes[i].age >= motes[i].life)
                {
                    ResetMote(ref motes[i]);
                }

                Mote mote = motes[i];
                float t = mote.age / mote.life;
                float r = mote.radius * radius;
                float sway = Mathf.Sin(t * Mathf.PI * 2f + mote.phase) * moteSway * t;

                Vector3 centre = new Vector3(
                    Mathf.Cos(mote.angle) * r + Mathf.Cos(mote.phase) * sway,
                    t * moteHeight,
                    Mathf.Sin(mote.angle) * r + Mathf.Sin(mote.phase) * sway);

                // Shrinking away to nothing as it rises.
                AddCube(moteLayers[mote.colour], centre, moteSize * (1f - t));
            }

            foreach (Layer layer in moteLayers)
            {
                Commit(layer);
            }
        }

        private static void Commit(Layer layer)
        {
            layer.mesh.Clear();
            layer.mesh.SetVertices(layer.vertices);
            layer.mesh.SetTriangles(layer.triangles, 0);
            layer.mesh.RecalculateBounds();
        }

        /// <summary>
        /// The floor straight below the boss, or the bottom of its body when
        /// there is none. Anything moved by physics, the boss included, is
        /// not floor.
        /// </summary>
        private Vector3 FloorBelow()
        {
            Bounds body = health != null ? health.BodyBounds() : new Bounds(transform.position, Vector3.zero);
            Vector3 feet = new Vector3(transform.position.x, body.min.y, transform.position.z);

            int count = Physics.RaycastNonAlloc(feet + Vector3.up * RayHeight, Vector3.down, hits,
                RayHeight + 50f, floorMask, QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;
            float floor = feet.y;

            for (int i = 0; i < count; i++)
            {
                Collider hit = hits[i].collider;

                if (hit.attachedRigidbody != null || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    floor = hits[i].point.y;
                }
            }

            return new Vector3(feet.x, floor, feet.z);
        }

        private static void AddDisc(Layer layer, float discRadius)
        {
            int centre = layer.vertices.Count;
            layer.vertices.Add(Vector3.zero);

            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                layer.vertices.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * discRadius);
            }

            for (int i = 0; i < Segments; i++)
            {
                layer.triangles.Add(centre);
                layer.triangles.Add(centre + 1 + (i + 1) % Segments);
                layer.triangles.Add(centre + 1 + i);
            }
        }

        private static void AddCube(Layer layer, Vector3 centre, float size)
        {
            int start = layer.vertices.Count;
            float half = size * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                layer.vertices.Add(centre + new Vector3(
                    (i & 1) == 0 ? -half : half,
                    (i & 2) == 0 ? -half : half,
                    (i & 4) == 0 ? -half : half));
            }

            // Drawn double sided, so the winding does not matter.
            int[] faces =
            {
                0, 1, 3, 2,
                4, 5, 7, 6,
                0, 1, 5, 4,
                2, 3, 7, 6,
                0, 2, 6, 4,
                1, 3, 7, 5,
            };

            for (int f = 0; f < faces.Length; f += 4)
            {
                layer.triangles.Add(start + faces[f]);
                layer.triangles.Add(start + faces[f + 1]);
                layer.triangles.Add(start + faces[f + 2]);

                layer.triangles.Add(start + faces[f]);
                layer.triangles.Add(start + faces[f + 2]);
                layer.triangles.Add(start + faces[f + 3]);
            }
        }

        private void ApplyColours()
        {
            if (circle == null)
            {
                return;
            }

            Tint(circle.renderer, circleColour);

            // Colours added while playing wait for the next run.
            for (int i = 0; i < moteLayers.Length && i < moteColours.Length; i++)
            {
                Tint(moteLayers[i].renderer, moteColours[i]);
            }
        }

        private void Tint(MeshRenderer target, Color colour)
        {
            target.enabled = fade > 0f;
            target.GetPropertyBlock(block);
            block.SetColor(FlatMaterial.BaseColorId, colour);
            block.SetFloat(FadeId, fade);
            target.SetPropertyBlock(block);
        }

        private void OnValidate()
        {
            // Live tuning in play mode.
            if (root != null)
            {
                ApplyColours();
            }
        }
    }
}
