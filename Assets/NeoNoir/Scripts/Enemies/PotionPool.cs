using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A spreading pool of spilt potion: a filled red disc on the floor that
    /// grows out from where the potion shattered and hurts the player
    /// standing in it.
    /// </summary>
    /// <remarks>
    /// In order: it spreads out to <see cref="Settings.maxRadius"/> over
    /// <see cref="Settings.spreadTime"/>, easing out, stays for
    /// <see cref="Settings.duration"/> seconds from the shatter, then shrinks
    /// away over <see cref="Settings.shrinkTime"/>. It hurts at its current
    /// size the whole time, shrinking included.
    ///
    /// The damage comes in ticks: <see cref="Settings.damagePerSecond"/>
    /// split into one every <see cref="Settings.tickInterval"/> seconds. The
    /// first lands the moment the player steps in. Pools do not share a
    /// clock, so two overlapping pools hurt twice as fast.
    ///
    /// The hurt zone is an upright cylinder from the floor up
    /// <see cref="Settings.reachHeight"/>, so a player standing on a ledge
    /// above it is safe.
    ///
    /// On top of the disc, all purely for show:
    ///   Rim     A brighter band round the edge, spreading and shrinking with it.
    ///   Bubbles Swell up at random spots on the surface, then pop, each
    ///           leaving a ring that runs out from where it was, and goes.
    ///   Fumes   Small motes drift up off the surface, turning, and shrink away.
    /// Bubbles and fumes come more often the bigger the pool is, so a pool
    /// shrinking away goes quiet with it. Like the other effects, nothing
    /// fades: everything shrinks to nothing instead, so every pixel stays
    /// full colour for the spot colour threshold.
    ///
    /// Drawn with a <see cref="WarningCircle"/> with no outline and pieces
    /// built from primitives, so it needs no prefab.
    /// </remarks>
    public sealed class PotionPool : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Radius the pool spreads out to.")]
            [Min(0.1f)] public float maxRadius = 5f;

            [Tooltip("Seconds the pool takes to spread to its full size. Fastest at first.")]
            [Min(0.01f)] public float spreadTime = 1.5f;

            [Tooltip("Seconds from the shatter until the pool starts shrinking away.")]
            [Min(0f)] public float duration = 5f;

            [Tooltip("Seconds the pool takes to shrink away at the end.")]
            [Min(0.01f)] public float shrinkTime = 0.6f;

            [Tooltip("Damage per second to the player standing in the pool.")]
            [Min(0f)] public float damagePerSecond = 8f;

            [Tooltip("Seconds between damage ticks. Each deals its share of the damage per second.")]
            [Min(0.02f)] public float tickInterval = 0.25f;

            [Tooltip("How far above the floor the pool hurts.")]
            [Min(0.1f)] public float reachHeight = 2f;

            [Tooltip("Height above the floor the pool is drawn at.")]
            [Range(0f, 0.5f)] public float floorOffset = 0.04f;

            [Tooltip("Colour of the pool, and of the droplets when the potion shatters. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
            [ColorUsage(false, true)] public Color colour = new Color(1f, 0.04f, 0.08f, 1f);

            [Tooltip("Material for the pool and everything on it, e.g. NeoNoir/Flat. Its colour is replaced by the ones here. Empty makes a NeoNoir/Flat one.")]
            public Material material;

            [Header("Rim")]
            [Tooltip("Width of the bright band round the edge, at full size. It narrows with the pool. 0 for none.")]
            [Min(0f)] public float rimWidth = 0.25f;

            [Tooltip("Colour of the rim, bubbles and fumes. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color accentColour = new Color(2.5f, 0.2f, 0.25f, 1f);

            [Header("Bubbles")]
            [Tooltip("Bubbles a second on a full-size pool. Fewer on a smaller one. 0 for none.")]
            [Min(0f)] public float bubblesPerSecond = 6f;

            [Tooltip("Smallest and largest size a bubble swells to before it pops.")]
            public Vector2 bubbleSize = new Vector2(0.2f, 0.45f);

            [Tooltip("Shortest and longest seconds a bubble swells before it pops.")]
            public Vector2 bubbleLife = new Vector2(0.4f, 0.9f);

            [Tooltip("How many times its own size the ring a popping bubble leaves spreads to.")]
            [Min(1f)] public float popSpread = 2.5f;

            [Tooltip("Seconds the pop's ring takes to spread and vanish.")]
            [Min(0.01f)] public float popTime = 0.18f;

            [Header("Fumes")]
            [Tooltip("Fumes a second rising off a full-size pool. Fewer off a smaller one. 0 for none.")]
            [Min(0f)] public float fumesPerSecond = 5f;

            [Tooltip("Size of each mote as it leaves the surface. It shrinks away as it rises.")]
            [Min(0.01f)] public float fumeSize = 0.15f;

            [Tooltip("Slowest and fastest speed the fumes rise at.")]
            public Vector2 fumeRiseSpeed = new Vector2(0.6f, 1.4f);

            [Tooltip("Seconds each mote lives.")]
            [Min(0.01f)] public float fumeLife = 1.2f;
        }

        private sealed class Bubble
        {
            public Transform transform;
            public Vector3 offset;
            public float size;
            public float life;
            public float age;
        }

        private sealed class Pop
        {
            public Transform transform;
            public float size;
            public float age;
        }

        private sealed class Fume
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 spinAxis;
            public float age;
        }

        private const int RingSegments = 32;

        private static readonly Collider[] overlaps = new Collider[16];

        // A flat ring of radius 1, 0.15 wide, shared by every pool's pops.
        // Never destroyed.
        private static Mesh popRingMesh;

        private readonly List<Bubble> bubbles = new List<Bubble>();
        private readonly List<Pop> pops = new List<Pop>();
        private readonly List<Fume> fumes = new List<Fume>();

        private Settings settings;
        private WarningCircle disc;
        private float age;
        private float tickWait;

        private Material pieceMaterial;
        private MaterialPropertyBlock block;
        private Mesh rimMesh;
        private readonly List<Vector3> rimVertices = new List<Vector3>();
        private readonly List<int> rimTriangles = new List<int>();

        private float bubbleDue;
        private float fumeDue;

        /// <summary>Radius the pool hurts and is drawn at this frame.</summary>
        public float Radius { get; private set; }

        /// <summary>Spills a pool on the floor at <paramref name="floorPoint"/>.</summary>
        public static PotionPool Create(Vector3 floorPoint, Settings settings)
        {
            GameObject host = new GameObject("PotionPool");
            host.transform.position = floorPoint;

            PotionPool pool = host.AddComponent<PotionPool>();
            pool.Begin(settings);
            return pool;
        }

        private void Begin(Settings poolSettings)
        {
            settings = poolSettings;
            block = new MaterialPropertyBlock();
            pieceMaterial = settings.material != null ? settings.material : FlatMaterial.DoubleSided;

            disc = WarningCircle.Create(transform.position + Vector3.up * settings.floorOffset, settings.maxRadius, 0f,
                                        settings.material, settings.colour, transform);

            if (settings.rimWidth > 0f)
            {
                rimMesh = new Mesh { name = "Potion pool rim" };
                rimMesh.MarkDynamic();

                GameObject rim = new GameObject("Rim");
                rim.transform.SetParent(transform, false);
                rim.transform.localPosition = Vector3.up * (settings.floorOffset + 0.01f);
                rim.AddComponent<MeshFilter>().sharedMesh = rimMesh;
                Paint(rim.AddComponent<MeshRenderer>(), settings.accentColour);
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;
            tickWait -= deltaTime;

            float end = settings.duration + settings.shrinkTime;

            if (age >= end)
            {
                Destroy(gameObject);
                return;
            }

            float spread = Mathf.Clamp01(age / settings.spreadTime);
            spread = 1f - (1f - spread) * (1f - spread);

            float shrink = age > settings.duration ? Mathf.Clamp01((end - age) / settings.shrinkTime) : 1f;

            float size = spread * shrink;
            Radius = settings.maxRadius * size;
            disc.Fill = size;

            DrawRim(size);
            SpawnBubbles(size, deltaTime);
            SpawnFumes(size, deltaTime);
            UpdateBubbles(deltaTime);
            UpdatePops(deltaTime);
            UpdateFumes(deltaTime);

            if (tickWait <= 0f && Radius > 0f && HurtPlayer())
            {
                tickWait = settings.tickInterval;
            }
        }

        /// <summary>Deals one tick to a player standing in the pool. Returns whether there was one.</summary>
        private bool HurtPlayer()
        {
            float halfHeight = settings.reachHeight * 0.5f;
            Vector3 centre = transform.position + Vector3.up * halfHeight;

            int count = Physics.OverlapBoxNonAlloc(centre, new Vector3(Radius, halfHeight, Radius), overlaps,
                                                   Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth health = overlaps[i].GetComponentInParent<PlayerHealth>();

                if (health == null)
                {
                    continue;
                }

                // The box's corners are outside the circle.
                Vector3 flat = overlaps[i].ClosestPoint(centre) - centre;
                flat.y = 0f;

                if (flat.sqrMagnitude > Radius * Radius)
                {
                    continue;
                }

                health.TakeDamage(settings.damagePerSecond * settings.tickInterval, transform.position);
                return true;
            }

            return false;
        }

        // ---- rim ----------------------------------------------------------

        private void DrawRim(float size)
        {
            if (rimMesh == null)
            {
                return;
            }

            rimVertices.Clear();
            rimTriangles.Clear();

            float outer = Radius;
            float inner = Mathf.Max(0f, outer - settings.rimWidth * size);

            if (outer - inner > 0.0001f)
            {
                AddRing(rimVertices, rimTriangles, inner, outer);
            }

            rimMesh.Clear();
            rimMesh.SetVertices(rimVertices);
            rimMesh.SetTriangles(rimTriangles, 0);
            rimMesh.RecalculateBounds();
        }

        private static void AddRing(List<Vector3> vertices, List<int> triangles, float inner, float outer)
        {
            int first = vertices.Count;

            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices.Add(radial * inner);
                vertices.Add(radial * outer);
            }

            for (int i = 0; i < RingSegments; i++)
            {
                int at = first + i * 2;

                // Wound both ways, so it shows from either side.
                triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
                triangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
            }
        }

        // ---- bubbles ------------------------------------------------------

        private void SpawnBubbles(float size, float deltaTime)
        {
            if (settings.bubblesPerSecond <= 0f)
            {
                return;
            }

            bubbleDue += settings.bubblesPerSecond * size * deltaTime;

            while (bubbleDue >= 1f)
            {
                bubbleDue -= 1f;

                GameObject piece = MakePrimitive("Bubble", PrimitiveType.Sphere);

                // Kept off the very edge, so a bubble never hangs over it.
                Vector2 spot = UnityEngine.Random.insideUnitCircle * (Radius * 0.85f);

                bubbles.Add(new Bubble
                {
                    transform = piece.transform,
                    offset = new Vector3(spot.x, settings.floorOffset, spot.y),
                    size = UnityEngine.Random.Range(settings.bubbleSize.x, settings.bubbleSize.y),
                    life = UnityEngine.Random.Range(settings.bubbleLife.x, settings.bubbleLife.y),
                });

                piece.transform.localPosition = bubbles[bubbles.Count - 1].offset;
                piece.transform.localScale = Vector3.zero;
            }
        }

        private void UpdateBubbles(float deltaTime)
        {
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = bubbles[i];
                bubble.age += deltaTime;

                float flat = new Vector2(bubble.offset.x, bubble.offset.z).magnitude;

                // Pops at the end of its life, or as soon as the shrinking
                // edge passes it.
                if (bubble.age >= bubble.life || flat > Radius)
                {
                    PopBubble(bubble);
                    bubbles.RemoveAt(i);
                    continue;
                }

                // Swells fast, then slows, so it hangs at nearly full size
                // a moment before it goes.
                float t = bubble.age / bubble.life;
                float swell = 1f - (1f - t) * (1f - t);
                float width = bubble.size * swell;

                // Half sunk in the surface: a dome, not a ball.
                bubble.transform.localScale = new Vector3(width, width * 0.8f, width);
            }
        }

        private void PopBubble(Bubble bubble)
        {
            Destroy(bubble.transform.gameObject);

            GameObject ring = new GameObject("Pop");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = bubble.offset + Vector3.up * 0.005f;
            ring.transform.localScale = Vector3.one * (bubble.size * 0.5f);
            ring.AddComponent<MeshFilter>().sharedMesh = PopRingMesh();
            Paint(ring.AddComponent<MeshRenderer>(), settings.accentColour);

            pops.Add(new Pop { transform = ring.transform, size = bubble.size * 0.5f });
        }

        private void UpdatePops(float deltaTime)
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                Pop pop = pops[i];
                pop.age += deltaTime;

                float t = pop.age / settings.popTime;

                if (t >= 1f)
                {
                    Destroy(pop.transform.gameObject);
                    pops.RemoveAt(i);
                    continue;
                }

                // Runs out fast and slows, from the bubble's own size.
                float run = 1f - (1f - t) * (1f - t);
                pop.transform.localScale = Vector3.one * (pop.size * Mathf.Lerp(1f, settings.popSpread, run));
            }
        }

        private static Mesh PopRingMesh()
        {
            if (popRingMesh == null)
            {
                List<Vector3> vertices = new List<Vector3>();
                List<int> triangles = new List<int>();
                AddRing(vertices, triangles, 0.85f, 1f);

                popRingMesh = new Mesh { name = "Potion bubble pop" };
                popRingMesh.SetVertices(vertices);
                popRingMesh.SetTriangles(triangles, 0);
                popRingMesh.RecalculateBounds();
            }

            return popRingMesh;
        }

        // ---- fumes --------------------------------------------------------

        private void SpawnFumes(float size, float deltaTime)
        {
            if (settings.fumesPerSecond <= 0f)
            {
                return;
            }

            fumeDue += settings.fumesPerSecond * size * deltaTime;

            while (fumeDue >= 1f)
            {
                fumeDue -= 1f;

                GameObject piece = MakePrimitive("Fume", PrimitiveType.Cube);
                Vector2 spot = UnityEngine.Random.insideUnitCircle * Radius;
                Vector2 drift = UnityEngine.Random.insideUnitCircle * 0.3f;

                piece.transform.localPosition = new Vector3(spot.x, settings.floorOffset, spot.y);
                piece.transform.localRotation = UnityEngine.Random.rotationUniform;
                piece.transform.localScale = Vector3.one * settings.fumeSize;

                fumes.Add(new Fume
                {
                    transform = piece.transform,
                    velocity = new Vector3(drift.x,
                                           UnityEngine.Random.Range(settings.fumeRiseSpeed.x, settings.fumeRiseSpeed.y),
                                           drift.y),
                    spinAxis = UnityEngine.Random.onUnitSphere,
                });
            }
        }

        private void UpdateFumes(float deltaTime)
        {
            for (int i = fumes.Count - 1; i >= 0; i--)
            {
                Fume fume = fumes[i];
                fume.age += deltaTime;

                float t = fume.age / settings.fumeLife;

                if (t >= 1f)
                {
                    Destroy(fume.transform.gameObject);
                    fumes.RemoveAt(i);
                    continue;
                }

                fume.transform.localPosition += fume.velocity * deltaTime;
                fume.transform.Rotate(fume.spinAxis, 180f * deltaTime, Space.World);
                fume.transform.localScale = Vector3.one * (settings.fumeSize * (1f - t));
            }
        }

        // ---- pieces -------------------------------------------------------

        private GameObject MakePrimitive(string pieceName, PrimitiveType type)
        {
            GameObject piece = GameObject.CreatePrimitive(type);
            Destroy(piece.GetComponent<Collider>());

            piece.name = pieceName;
            piece.transform.SetParent(transform, false);

            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = pieceMaterial;
            Paint(renderer, settings.accentColour);

            return piece;
        }

        private void Paint(Renderer target, Color colour)
        {
            if (target.sharedMaterial == null)
            {
                target.sharedMaterial = pieceMaterial;
            }

            target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            target.receiveShadows = false;
            FlatMaterial.Tint(target, colour, block);
        }

        private void OnDestroy()
        {
            if (rimMesh != null)
            {
                Destroy(rimMesh);
            }
        }
    }
}
