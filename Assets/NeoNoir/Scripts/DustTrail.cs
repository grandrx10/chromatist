using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// Dust kicked up at a mover's feet, left behind in its wake.
    /// </summary>
    /// <remarks>
    /// Emits only on frames something calls <see cref="KeepEmitting"/>, so
    /// whatever drives the movement -- an attack, a dash -- just calls it
    /// every frame it wants dust, and the dust stops by itself the moment
    /// that stops, even if an attack is cut off partway. It also stays quiet
    /// below <see cref="Settings.minSpeed"/>, measured from how the mover
    /// really moves.
    ///
    /// A puff is kicked up every <see cref="Settings.spacing"/> travelled,
    /// spread across the feet: the bottom of the mover's collider. Each is a
    /// small cube that swells, drifts back and out and up, then shrinks to
    /// nothing. Nothing fades, so the threshold keeps them solid.
    ///
    /// Every puff of one mover is drawn as a single mesh on an object of its
    /// own, not parented, so a turning or spinning mover never drags them
    /// round. Puffs already kicked up finish even if the mover is destroyed.
    ///
    /// Added at runtime by whatever wants dust. Add one by hand to change
    /// its settings; <see cref="On"/> finds it.
    /// </remarks>
    public sealed class DustTrail : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Distance travelled between one kick of dust and the next.")]
            [Min(0.05f)] public float spacing = 0.4f;

            [Tooltip("Puffs per kick, spread across the feet.")]
            [Min(1)] public int puffsPerStep = 2;

            [Tooltip("Slower than this, in units per second, kicks up nothing.")]
            [Min(0f)] public float minSpeed = 3f;

            [Tooltip("Size a puff swells to.")]
            [Min(0.01f)] public float size = 0.35f;

            [Tooltip("Seconds each puff lives.")]
            [Min(0.05f)] public float lifetime = 0.5f;

            [Tooltip("Upward drift, in units per second.")]
            [Min(0f)] public float rise = 0.8f;

            [Tooltip("Fraction of the mover's speed a puff is flung backward with.")]
            [Range(0f, 1f)] public float drag = 0.15f;

            [Tooltip("Sideways drift, in units per second, away from the line of travel.")]
            [Min(0f)] public float spread = 1.2f;

            [Tooltip("Colour of the dust. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(1f, 1f, 1f);

            [Tooltip("Material for the dust. Empty uses NeoNoir/Flat in the colour above.")]
            public Material material;
        }

        /// <summary>Most kicks in one frame, so a teleport does not lay a line of dust.</summary>
        private const int MaxStepsPerFrame = 6;

        [SerializeField] private Settings settings = new Settings();

        private Collider body;
        private Cloud cloud;
        private Vector3 lastPosition;
        private float travelled;
        private int keptFrame = -1;

        /// <summary>
        /// Finds or adds the dust on <paramref name="mover"/>. Given
        /// <paramref name="dustSettings"/>, uses those from now on; otherwise
        /// keeps its own.
        /// </summary>
        public static DustTrail On(GameObject mover, Settings dustSettings = null)
        {
            DustTrail trail = mover.GetComponent<DustTrail>();

            if (trail == null)
            {
                trail = mover.AddComponent<DustTrail>();
            }

            if (dustSettings != null)
            {
                trail.settings = dustSettings;
            }

            return trail;
        }

        /// <summary>Kicks up dust this frame, if moving fast enough. Call every frame dust is wanted.</summary>
        public void KeepEmitting()
        {
            keptFrame = Time.frameCount;
        }

        private void Awake()
        {
            body = GetComponent<Collider>();
            lastPosition = transform.position;

            cloud = new GameObject("Dust (" + name + ")").AddComponent<Cloud>();
        }

        private void LateUpdate()
        {
            Vector3 position = transform.position;
            Vector3 moved = position - lastPosition;
            moved.y = 0f;
            lastPosition = position;

            float deltaTime = Time.deltaTime;
            float distance = moved.magnitude;

            if (keptFrame != Time.frameCount || deltaTime <= 0f || distance / deltaTime < settings.minSpeed)
            {
                // Primed, so the first frame of the next run kicks at once.
                travelled = settings.spacing;
                return;
            }

            Vector3 direction = moved / distance;
            Vector3 velocity = moved / deltaTime;
            travelled += distance;

            Bounds bounds = body != null ? body.bounds : new Bounds(position, Vector3.zero);
            Vector3 feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float footWidth = Mathf.Max(0.1f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.7f);

            int steps = 0;

            while (travelled >= settings.spacing && steps < MaxStepsPerFrame)
            {
                travelled -= settings.spacing;
                steps++;

                // Laid where the feet were when that spacing was crossed.
                cloud.Kick(feet - direction * travelled, direction, velocity, footWidth, settings);
            }

            travelled = Mathf.Min(travelled, settings.spacing);
        }

        private void OnDestroy()
        {
            if (cloud != null)
            {
                cloud.Release();
            }
        }

        /// <summary>Every live puff of one mover, drawn as one mesh.</summary>
        private sealed class Cloud : MonoBehaviour
        {
            private struct Puff
            {
                public Vector3 position;
                public Vector3 velocity;
                public Quaternion rotation;
                public Vector3 spinAxis;
                public float spinSpeed;
                public float size;
                public float lifetime;
                public float age;
            }

            private static readonly Vector3[] CubeVertices = BuildCubeVertices(out CubeNormals);
            private static readonly Vector3[] CubeNormals;

            private readonly List<Puff> puffs = new List<Puff>();
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();

            private Mesh mesh;
            private MeshRenderer meshRenderer;
            private MaterialPropertyBlock block;
            private Material material;
            private Color colour;
            private bool released;

            public void Kick(Vector3 feet, Vector3 direction, Vector3 velocity, float footWidth, Settings settings)
            {
                Setup(settings);

                Vector3 side = Vector3.Cross(Vector3.up, direction);

                for (int i = 0; i < settings.puffsPerStep; i++)
                {
                    float across = Random.Range(-1f, 1f);
                    float sign = across < 0f ? -1f : 1f;

                    puffs.Add(new Puff
                    {
                        position = feet + side * (across * footWidth) + Vector3.up * (settings.size * 0.3f),
                        velocity = -velocity * settings.drag
                                   + side * (sign * settings.spread * Random.Range(0.4f, 1f))
                                   + Vector3.up * (settings.rise * Random.Range(0.6f, 1.2f)),
                        rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                        spinAxis = Random.onUnitSphere,
                        spinSpeed = Random.Range(90f, 270f),
                        size = settings.size * Random.Range(0.7f, 1.1f),
                        lifetime = settings.lifetime * Random.Range(0.8f, 1.2f),
                    });
                }
            }

            /// <summary>The mover is gone: finish the puffs in flight, then go.</summary>
            public void Release()
            {
                released = true;
            }

            private void Setup(Settings settings)
            {
                if (mesh == null)
                {
                    mesh = new Mesh { name = "Dust" };
                    mesh.MarkDynamic();
                    gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

                    meshRenderer = gameObject.AddComponent<MeshRenderer>();
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                    block = new MaterialPropertyBlock();
                }

                // Settings can be swapped between kicks, as one attack hands
                // over to another with dust of its own.
                Material wanted = settings.material != null ? settings.material : FlatMaterial.Shared;

                if (wanted != material)
                {
                    material = wanted;
                    meshRenderer.sharedMaterial = material;
                    colour = new Color(-1f, -1f, -1f);
                }

                if (settings.material == null && colour != settings.colour && material != null)
                {
                    colour = settings.colour;
                    FlatMaterial.Tint(meshRenderer, colour, block);
                }
            }

            private void LateUpdate()
            {
                float deltaTime = Time.deltaTime;
                float damping = Mathf.Exp(-4f * deltaTime);

                for (int i = puffs.Count - 1; i >= 0; i--)
                {
                    Puff puff = puffs[i];
                    puff.age += deltaTime;

                    if (puff.age >= puff.lifetime)
                    {
                        puffs.RemoveAt(i);
                        continue;
                    }

                    puff.velocity.x *= damping;
                    puff.velocity.z *= damping;
                    puff.position += puff.velocity * deltaTime;
                    puff.rotation = Quaternion.AngleAxis(puff.spinSpeed * deltaTime, puff.spinAxis) * puff.rotation;
                    puffs[i] = puff;
                }

                if (puffs.Count == 0)
                {
                    if (released)
                    {
                        Destroy(gameObject);
                    }
                    else if (meshRenderer != null)
                    {
                        meshRenderer.enabled = false;
                    }

                    return;
                }

                meshRenderer.enabled = true;
                Build();
            }

            private void Build()
            {
                vertices.Clear();
                normals.Clear();
                triangles.Clear();

                foreach (Puff puff in puffs)
                {
                    // Swells quickly, then shrinks away for the rest of its life.
                    float t = puff.age / puff.lifetime;
                    float grow = t < 0.2f ? Mathf.Lerp(0.4f, 1f, t / 0.2f) : 1f - (t - 0.2f) / 0.8f;
                    float scale = puff.size * grow;

                    int first = vertices.Count;

                    for (int v = 0; v < CubeVertices.Length; v++)
                    {
                        vertices.Add(puff.position + puff.rotation * (CubeVertices[v] * scale));
                        normals.Add(puff.rotation * CubeNormals[v]);
                    }

                    for (int face = 0; face < 6; face++)
                    {
                        int at = first + face * 4;
                        triangles.Add(at);
                        triangles.Add(at + 1);
                        triangles.Add(at + 2);
                        triangles.Add(at);
                        triangles.Add(at + 2);
                        triangles.Add(at + 3);
                    }
                }

                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
            }

            /// <summary>A unit cube's 24 corners, four per face, wound clockwise seen from outside.</summary>
            private static Vector3[] BuildCubeVertices(out Vector3[] faceNormals)
            {
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                Vector3[] corners = new Vector3[24];
                faceNormals = new Vector3[24];

                for (int face = 0; face < 6; face++)
                {
                    Vector3 normal = axes[face];
                    Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 w = Vector3.Cross(normal, u);

                    Vector3 centre = normal * 0.5f;
                    corners[face * 4 + 0] = centre + (-u - w) * 0.5f;
                    corners[face * 4 + 1] = centre + (u - w) * 0.5f;
                    corners[face * 4 + 2] = centre + (u + w) * 0.5f;
                    corners[face * 4 + 3] = centre + (-u + w) * 0.5f;

                    for (int k = 0; k < 4; k++)
                    {
                        faceNormals[face * 4 + k] = normal;
                    }
                }

                return corners;
            }

            private void OnDestroy()
            {
                Destroy(mesh);
            }
        }
    }
}
