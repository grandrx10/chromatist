using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The burst where a hammer hits the floor: a shockwave ring running out
    /// across the floor, and chunks of debris thrown up out of it.
    /// </summary>
    /// <remarks>
    /// Purely visual; the damage is the ripple's.
    ///
    /// Nothing fades. The ring thins to nothing as it spreads and the debris
    /// shrinks away as it falls, so every pixel stays full colour and the
    /// spot colour threshold never crushes a half-faded edge unpredictably.
    /// Like <see cref="AmberSlam"/>, brightness comes from scaling the
    /// material's own colour through a property block, never from editing
    /// the material.
    ///
    /// Spawned at runtime and builds its own meshes, so it needs no prefab.
    /// Finished bursts are pooled and reused, debris and all, since some
    /// attacks set off dozens a second.
    /// </remarks>
    public sealed class HammerImpact : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Ring")]
            [Tooltip("Radius the shockwave ring spreads out to.")]
            [Min(0f)] public float ringRadius = 3.5f;

            [Tooltip("Thickness of the ring as it starts out. It thins to nothing as it spreads.")]
            [Min(0.01f)] public float ringWidth = 0.6f;

            [Tooltip("Seconds the ring takes to spread out and vanish.")]
            [Min(0.01f)] public float ringTime = 0.35f;

            [Tooltip("Height above the floor the ring is drawn at.")]
            [Range(0f, 1f)] public float floorOffset = 0.05f;

            [Tooltip("Colour intensity of the ring. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float ringIntensity = 3f;

            [Header("Debris")]
            [Tooltip("Chunks thrown up from the impact.")]
            [Min(0)] public int debrisCount = 8;

            [Tooltip("Size of each chunk as it is thrown. They shrink to nothing as they fall.")]
            [Min(0.01f)] public float debrisSize = 0.4f;

            [Tooltip("Slowest and fastest upward launch speed.")]
            public Vector2 debrisUpSpeed = new Vector2(5f, 9f);

            [Tooltip("Slowest and fastest outward launch speed.")]
            public Vector2 debrisOutSpeed = new Vector2(2f, 5f);

            [Tooltip("Downward pull on the chunks, in units per second squared.")]
            [Min(0f)] public float debrisGravity = 25f;

            [Tooltip("Seconds each chunk lives.")]
            [Min(0.01f)] public float debrisTime = 0.6f;

            [Tooltip("Colour intensity of the debris.")]
            [Min(0f)] public float debrisIntensity = 1.5f;
        }

        private const int RingSegments = 48;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Stack<HammerImpact> Pool = new Stack<HammerImpact>();

        // The plain material a primitive cube comes with, for bursts given none.
        private static Material defaultMaterial;

        private struct Chunk
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 spinAxis;
            public float spinSpeed;
        }

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<int> edgeTriangles = new List<int>();
        private readonly List<Chunk> chunks = new List<Chunk>();

        private Settings settings;
        private float age;

        // Every chunk this burst has ever made; only the first debrisCount
        // are in use this time round.
        private readonly List<Transform> chunkPool = new List<Transform>();

        private Mesh ringMesh;
        private MeshRenderer ringRenderer;
        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;

        // Bands of a second colour along both edges of the ring, drawn as
        // the ring mesh's second submesh. 0 wide for none.
        private float outlineWidth;
        private Color outlineColour = Color.white;

        private float Lifetime => Mathf.Max(settings.ringTime, settings.debrisCount > 0 ? settings.debrisTime : 0f);

        /// <summary>Bursts at <paramref name="floorPoint"/>, a point on the floor.</summary>
        public static HammerImpact Create(Vector3 floorPoint, Settings settings, Material material)
        {
            HammerImpact impact = Take();
            impact.outlineWidth = 0f;
            impact.Begin(floorPoint, settings, material, null);
            return impact;
        }

        /// <summary>
        /// Bursts at <paramref name="floorPoint"/> in <paramref name="colour"/>
        /// rather than a material's own colour, its ring edged on both sides
        /// with <paramref name="edgeWidth"/>-wide bands of
        /// <paramref name="edgeColour"/>. Without a <paramref name="material"/>
        /// it uses a plain NeoNoir/Flat one.
        /// </summary>
        public static HammerImpact Create(Vector3 floorPoint, Settings settings, Material material,
                                          Color colour, float edgeWidth, Color edgeColour)
        {
            HammerImpact impact = Take();
            impact.outlineWidth = Mathf.Max(0f, edgeWidth);
            impact.outlineColour = edgeColour;
            impact.Begin(floorPoint, settings, material != null ? material : FlatMaterial.Shared, colour);
            return impact;
        }

        /// <summary>
        /// Makes ready <paramref name="count"/> bursts of up to
        /// <paramref name="debrisCount"/> chunks each ahead of time, so an
        /// attack that sets off a flurry of them does not build them all
        /// mid-fight. Bursts already pooled count toward it.
        /// </summary>
        public static void Prewarm(int count, int debrisCount)
        {
            // A scene change destroys pooled bursts with it.
            HammerImpact[] pooled = Pool.ToArray();
            Pool.Clear();

            foreach (HammerImpact impact in pooled)
            {
                if (impact != null)
                {
                    impact.EnsureChunks(debrisCount);
                    Pool.Push(impact);
                }
            }

            for (int i = Pool.Count; i < count; i++)
            {
                HammerImpact impact = Make();
                impact.EnsureChunks(debrisCount);
                impact.PutAway();
            }
        }

        private static HammerImpact Take()
        {
            // A scene change destroys pooled bursts with it.
            while (Pool.Count > 0)
            {
                HammerImpact pooled = Pool.Pop();

                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            return Make();
        }

        private static HammerImpact Make()
        {
            GameObject host = new GameObject("HammerImpact");
            HammerImpact impact = host.AddComponent<HammerImpact>();
            impact.Build();
            return impact;
        }

        private void PutAway()
        {
            gameObject.SetActive(false);
            Pool.Push(this);
        }

        /// <summary>The parts every burst has, made once and kept through reuse.</summary>
        private void Build()
        {
            block = new MaterialPropertyBlock();

            GameObject ring = new GameObject("Ring");
            ring.transform.SetParent(transform, false);

            ringMesh = new Mesh { name = "Hammer impact ring" };
            ringMesh.MarkDynamic();
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;

            ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
        }

        private void EnsureChunks(int count)
        {
            while (chunkPool.Count < count)
            {
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());

                piece.name = "Debris";
                piece.transform.SetParent(transform, false);

                MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
                pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (defaultMaterial == null)
                {
                    defaultMaterial = pieceRenderer.sharedMaterial;
                }

                chunkPool.Add(piece.transform);
            }
        }

        private void Begin(Vector3 floorPoint, Settings impactSettings, Material material, Color? colour)
        {
            settings = impactSettings;
            age = 0f;
            // Flat on the floor; a pooled burst may have been turned onto a wall last time.
            transform.SetPositionAndRotation(floorPoint, Quaternion.identity);
            ringRenderer.transform.localPosition = Vector3.up * settings.floorOffset;

            baseColour = colour.HasValue ? colour.Value
                : material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId)
                : Color.white;

            EnsureChunks(settings.debrisCount);

            // Without a material everything uses a primitive cube's default
            // one, so it at least shows.
            if (material == null)
            {
                material = defaultMaterial;
            }

            chunks.Clear();

            for (int i = 0; i < chunkPool.Count; i++)
            {
                bool used = i < settings.debrisCount;
                chunkPool[i].gameObject.SetActive(used);

                if (used)
                {
                    SpawnChunk(chunkPool[i], material);
                }
            }

            // The second slot draws the edge bands, in their own colour.
            ringRenderer.sharedMaterials = new[] { material, material };

            Paint(ringRenderer, settings.ringIntensity, 0);
            SetColour(ringRenderer, outlineColour, 1);
            BuildRing();
        }

        private void SpawnChunk(Transform piece, Material material)
        {
            piece.localPosition = Vector3.up * (settings.debrisSize * 0.5f);
            piece.localRotation = UnityEngine.Random.rotationUniform;
            piece.localScale = Vector3.one * settings.debrisSize;

            MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
            pieceRenderer.sharedMaterial = material;

            Paint(pieceRenderer, settings.debrisIntensity, -1);

            Vector2 outward = UnityEngine.Random.insideUnitCircle.normalized;
            float upSpeed = UnityEngine.Random.Range(settings.debrisUpSpeed.x, settings.debrisUpSpeed.y);
            float outSpeed = UnityEngine.Random.Range(settings.debrisOutSpeed.x, settings.debrisOutSpeed.y);

            chunks.Add(new Chunk
            {
                transform = piece,
                velocity = new Vector3(outward.x * outSpeed, upSpeed, outward.y * outSpeed),
                spinAxis = UnityEngine.Random.onUnitSphere,
                spinSpeed = UnityEngine.Random.Range(360f, 900f),
            });
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= Lifetime)
            {
                PutAway();
                return;
            }

            BuildRing();
            UpdateDebris(deltaTime);
        }

        private void UpdateDebris(float deltaTime)
        {
            float life = Mathf.Clamp01(age / settings.debrisTime);

            // Holds full size for the first half, then shrinks away.
            float scale = settings.debrisSize * Mathf.Clamp01((1f - life) * 2f);

            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i];
                chunk.velocity.y -= settings.debrisGravity * deltaTime;

                Vector3 position = chunk.transform.position + chunk.velocity * deltaTime;

                // Comes to rest on the floor rather than sinking through it.
                float floor = transform.position.y + scale * 0.5f;
                if (position.y < floor)
                {
                    position.y = floor;
                    chunk.velocity = Vector3.zero;
                }

                chunk.transform.position = position;
                chunk.transform.Rotate(chunk.spinAxis, chunk.spinSpeed * deltaTime, Space.World);
                chunk.transform.localScale = Vector3.one * scale;
                chunks[i] = chunk;
            }
        }

        /// <summary>A flat ring spreading out fast and slowing, thinning to nothing as it goes.</summary>
        private void BuildRing()
        {
            vertices.Clear();
            triangles.Clear();
            edgeTriangles.Clear();

            float t = Mathf.Clamp01(age / settings.ringTime);
            float spread = 1f - (1f - t) * (1f - t) * (1f - t);
            float outer = settings.ringRadius * spread;
            float width = settings.ringWidth * (1f - t);
            float inner = Mathf.Max(0f, outer - width);

            if (outer > 0.0001f && width > 0f)
            {
                AddBand(inner, outer, triangles);

                // Thinning with the ring, so the edges never outlast it.
                float edge = outlineWidth * (1f - t);

                if (edge > 0f)
                {
                    AddBand(outer, outer + edge, edgeTriangles);

                    if (inner > 0f)
                    {
                        AddBand(Mathf.Max(0f, inner - edge), inner, edgeTriangles);
                    }
                }
            }

            ringMesh.Clear();
            ringMesh.subMeshCount = 2;
            ringMesh.SetVertices(vertices);
            ringMesh.SetTriangles(triangles, 0);
            ringMesh.SetTriangles(edgeTriangles, 1);
            ringMesh.RecalculateBounds();
        }

        /// <summary>A flat band on the floor from <paramref name="inner"/> to <paramref name="outer"/> radius.</summary>
        private void AddBand(float inner, float outer, List<int> into)
        {
            int start = vertices.Count;

            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices.Add(radial * inner);
                vertices.Add(radial * outer);
            }

            for (int i = 0; i < RingSegments; i++)
            {
                int at = start + i * 2;

                // Wound both ways, so it shows from either side.
                AddTriangle(into, at, at + 2, at + 1);
                AddTriangle(into, at + 1, at + 2, at + 3);
                AddTriangle(into, at, at + 1, at + 2);
                AddTriangle(into, at + 1, at + 3, at + 2);
            }
        }

        private static void AddTriangle(List<int> into, int a, int b, int c)
        {
            into.Add(a);
            into.Add(b);
            into.Add(c);
        }

        /// <summary>Colours <paramref name="target"/> scaled by <paramref name="intensity"/>, in material slot <paramref name="slot"/>, or all of it for -1.</summary>
        private void Paint(Renderer target, float intensity, int slot)
        {
            Color colour = baseColour * intensity;
            colour.a = baseColour.a;
            SetColour(target, colour, slot);
        }

        private void SetColour(Renderer target, Color colour, int slot)
        {
            if (slot < 0)
            {
                target.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                target.SetPropertyBlock(block);
                return;
            }

            target.GetPropertyBlock(block, slot);
            block.SetColor(BaseColorId, colour);
            target.SetPropertyBlock(block, slot);
        }

        private void OnDestroy()
        {
            Destroy(ringMesh);
        }
    }
}
