using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Lavender's debuff indicator: bubbles that keep forming around an
    /// enemy and floating upward for as long as it is vulnerable.
    /// </summary>
    /// <remarks>
    /// One per enemy, found through <see cref="Show"/>, which hitting the
    /// same enemy again only updates. It watches the enemy's own
    /// <see cref="EnemyHealth.VulnerabilityRemaining"/> rather than keeping
    /// a timer of its own, so the bubbles stop exactly when the debuff does,
    /// whatever applied or refreshed it. Bubbles already rising when it ends
    /// finish their climb; then it removes itself.
    ///
    /// A stronger vulnerability makes more bubbles: the rate is for a 1.25x
    /// debuff, and scales with how far above 1 the multiplier is.
    ///
    /// Bubbles rise up the screen, not up the world: the camera looks almost
    /// straight down, so world-up movement would barely show. They face the
    /// camera, drawn as a lavender ring with a white glint through
    /// NeoNoir/Tear's solid branches.
    ///
    /// Lives on its own object, not the enemy, so an enemy's scale never
    /// distorts it, and builds its own mesh, so it needs no prefab.
    ///
    /// <see cref="Burst"/> runs the same bubbles with no enemy at all, round
    /// a fixed area for a fixed time: the shrine uses it when Lavender is
    /// acquired.
    /// </remarks>
    public sealed class LavenderBubbles : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Bubbles formed per second under a 1.25x debuff. Stronger debuffs form proportionally more.")]
            [Min(0f)] public float rate = 9f;

            [Tooltip("Smallest bubble radius.")]
            [Min(0.01f)] public float minSize = 0.08f;

            [Tooltip("Largest bubble radius.")]
            [Min(0.01f)] public float maxSize = 0.18f;

            [Tooltip("Units per second a bubble floats up.")]
            [Min(0f)] public float riseSpeed = 1.3f;

            [Tooltip("How far a bubble sways side to side as it rises.")]
            [Min(0f)] public float wobble = 0.12f;

            [Tooltip("Seconds a bubble lasts before it pops.")]
            [Min(0.1f)] public float lifetime = 0.9f;

            [Tooltip("Thickness of a bubble's ring, as a fraction of its radius.")]
            [Range(0.05f, 1f)] public float ringThickness = 0.3f;
        }

        // Strength the rate is quoted for: a 1.25x debuff.
        private const float ReferenceStrength = 0.25f;

        // Seconds a new bubble takes to swell to full size.
        private const float GrowTime = 0.15f;

        private const int RingSegments = 14;

        private struct Bubble
        {
            public Vector3 Start;
            public float Size;
            public float Age;
            public float Lifetime;
            public float Speed;
            public float Phase;
        }

        private static readonly Dictionary<EnemyHealth, LavenderBubbles> Live =
            new Dictionary<EnemyHealth, LavenderBubbles>();

        private readonly List<Bubble> bubbles = new List<Bubble>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        private EnemyHealth enemy;
        private Settings settings;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private float owed;

        // Set for a burst: bubbles round a fixed area for a fixed time, with
        // no enemy behind them.
        private bool isBurst;
        private Bounds burstArea;
        private float burstRemaining;

        private bool Emitting => isBurst
            ? burstRemaining > 0f
            : enemy != null && !enemy.IsDead && enemy.VulnerabilityRemaining > 0f;

        // A burst forms bubbles at the plain rate, as if under a 1.25x debuff.
        private float Strength => isBurst
            ? 1f
            : Mathf.Max(0f, enemy.DamageTakenMultiplier - 1f) / ReferenceStrength;

        private Bounds Area => isBurst ? burstArea : enemy.BodyBounds();

        /// <summary>
        /// Forms bubbles round <paramref name="area"/> for
        /// <paramref name="duration"/> seconds, then lets the last ones finish
        /// rising and removes itself. For set pieces, where there is no enemy
        /// or debuff to follow.
        /// </summary>
        public static LavenderBubbles Burst(Bounds area, float duration, Settings settings, Material material)
        {
            if (settings == null)
            {
                return null;
            }

            LavenderBubbles bubbles = new GameObject("LavenderBubbles").AddComponent<LavenderBubbles>();
            bubbles.Build(null);
            bubbles.isBurst = true;
            bubbles.burstArea = area;
            bubbles.burstRemaining = duration;
            bubbles.settings = settings;

            if (material != null)
            {
                bubbles.meshRenderer.sharedMaterial = material;
            }

            return bubbles;
        }

        /// <summary>
        /// Starts bubbles on <paramref name="target"/>, or keeps the ones
        /// already there going. Call after applying the vulnerability: the
        /// bubbles last exactly as long as it does.
        /// </summary>
        public static void Show(EnemyHealth target, Settings settings, Material material)
        {
            if (target == null || settings == null)
            {
                return;
            }

            if (!Live.TryGetValue(target, out LavenderBubbles bubbles) || bubbles == null)
            {
                bubbles = new GameObject("LavenderBubbles").AddComponent<LavenderBubbles>();
                bubbles.Build(target);
                Live[target] = bubbles;
            }

            bubbles.settings = settings;

            if (material != null)
            {
                bubbles.meshRenderer.sharedMaterial = material;
            }
        }

        private void Build(EnemyHealth target)
        {
            enemy = target;

            mesh = new Mesh { name = "LavenderBubbles" };
            mesh.MarkDynamic();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            Camera camera = Camera.main;
            bool vulnerable = Emitting;

            if (isBurst)
            {
                burstRemaining -= deltaTime;
            }

            if (vulnerable && camera != null)
            {
                owed += settings.rate * Strength * deltaTime;

                while (owed >= 1f)
                {
                    owed -= 1f;
                    Spawn(camera);
                }
            }
            else
            {
                owed = 0f;
            }

            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = bubbles[i];
                bubble.Age += deltaTime;

                if (bubble.Age >= bubble.Lifetime)
                {
                    bubbles.RemoveAt(i);
                    continue;
                }

                bubbles[i] = bubble;
            }

            if (!vulnerable && bubbles.Count == 0)
            {
                Destroy(gameObject);
                return;
            }

            Rebuild(camera);
        }

        /// <summary>Forms a bubble somewhere round the lower half of the enemy's body, as seen on screen.</summary>
        private void Spawn(Camera camera)
        {
            Bounds body = Area;
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;

            // Across the body's width, and from its middle down, so they rise
            // up through it and past the top.
            float width = Mathf.Max(body.extents.x, body.extents.z);
            float height = Mathf.Max(0.1f, body.extents.y);

            Vector3 start = body.center
                            + right * (UnityEngine.Random.Range(-1f, 1f) * width)
                            - up * (UnityEngine.Random.value * height);

            bubbles.Add(new Bubble
            {
                Start = start,
                Size = UnityEngine.Random.Range(settings.minSize, Mathf.Max(settings.minSize, settings.maxSize)),
                Lifetime = settings.lifetime * UnityEngine.Random.Range(0.75f, 1.25f),
                Speed = settings.riseSpeed * UnityEngine.Random.Range(0.75f, 1.25f),
                Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            });
        }

        private void Rebuild(Camera camera)
        {
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            triangles.Clear();

            if (camera != null)
            {
                Vector3 right = camera.transform.right;
                Vector3 up = camera.transform.up;
                Vector3 facing = -camera.transform.forward;

                foreach (Bubble bubble in bubbles)
                {
                    // Swells in, drifts up swaying, and grows a touch as it
                    // rises, so the pop at the end reads as bursting.
                    float swell = Mathf.Clamp01(bubble.Age / GrowTime);
                    float radius = bubble.Size * (1f - (1f - swell) * (1f - swell))
                                   * Mathf.Lerp(1f, 1.2f, bubble.Age / bubble.Lifetime);

                    Vector3 centre = bubble.Start
                                     + up * (bubble.Speed * bubble.Age)
                                     + right * (Mathf.Sin(bubble.Phase + bubble.Age * 6f) * settings.wobble);

                    AddRing(centre, radius, right, up, facing);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>A lavender ring facing the camera, with a white glint at its upper left.</summary>
        private void AddRing(Vector3 centre, float radius, Vector3 right, Vector3 up, Vector3 facing)
        {
            float inner = radius * (1f - settings.ringThickness);
            int first = vertices.Count;

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                Vector3 direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);

                // u = 2 is NeoNoir/Tear's solid branch, the lavender.
                AddVertex(centre + direction * radius, facing, 2f);
                AddVertex(centre + direction * inner, facing, 2f);
            }

            for (int i = 0; i < RingSegments; i++)
            {
                int next = (i + 1) % RingSegments;
                ScarlettClaw.AddQuad(triangles, first + i * 2, first + i * 2 + 1, first + next * 2, first + next * 2 + 1);
            }

            // Glint after the ring, so it draws over it.
            Vector3 glint = centre + (up - right).normalized * (radius * 0.45f);
            float glintSize = radius * 0.2f;
            int at = vertices.Count;

            // u = 1 is NeoNoir/Tear's outline branch, the white.
            AddVertex(glint + up * glintSize, facing, 1f);
            AddVertex(glint + right * glintSize, facing, 1f);
            AddVertex(glint - right * glintSize, facing, 1f);
            AddVertex(glint - up * glintSize, facing, 1f);
            ScarlettClaw.AddQuad(triangles, at, at + 1, at + 2, at + 3);
        }

        private void AddVertex(Vector3 position, Vector3 normal, float kind)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(new Vector2(kind, 0f));
        }

        private void OnDestroy()
        {
            // By reference: a dead enemy compares equal to null in Unity, but
            // is still the key it was stored under.
            if (!ReferenceEquals(enemy, null) && Live.TryGetValue(enemy, out LavenderBubbles bubbles) && bubbles == this)
            {
                Live.Remove(enemy);
            }

            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
