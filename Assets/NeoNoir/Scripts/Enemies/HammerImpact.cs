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

        private struct Chunk
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 spinAxis;
            public float spinSpeed;
        }

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Chunk> chunks = new List<Chunk>();

        private Settings settings;
        private float age;

        private Mesh ringMesh;
        private MeshRenderer ringRenderer;
        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;

        private float Lifetime => Mathf.Max(settings.ringTime, settings.debrisCount > 0 ? settings.debrisTime : 0f);

        /// <summary>Bursts at <paramref name="floorPoint"/>, a point on the floor.</summary>
        public static HammerImpact Create(Vector3 floorPoint, Settings settings, Material material)
        {
            GameObject host = new GameObject("HammerImpact");
            HammerImpact impact = host.AddComponent<HammerImpact>();
            impact.Begin(floorPoint, settings, material);
            return impact;
        }

        private void Begin(Vector3 floorPoint, Settings impactSettings, Material material)
        {
            settings = impactSettings;
            transform.position = floorPoint;
            block = new MaterialPropertyBlock();

            if (material != null && material.HasProperty(BaseColorId))
            {
                baseColour = material.GetColor(BaseColorId);
            }

            GameObject ring = new GameObject("Ring");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = Vector3.up * settings.floorOffset;

            ringMesh = new Mesh { name = "Hammer impact ring" };
            ringMesh.MarkDynamic();
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;

            ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;

            for (int i = 0; i < settings.debrisCount; i++)
            {
                SpawnChunk(material);
            }

            // Without a material the ring borrows the debris' default one, so
            // it at least shows.
            if (material == null && chunks.Count > 0)
            {
                material = chunks[0].transform.GetComponent<Renderer>().sharedMaterial;
            }

            ringRenderer.sharedMaterial = material;

            Paint(ringRenderer, settings.ringIntensity);
            BuildRing();
        }

        private void SpawnChunk(Material material)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());

            piece.name = "Debris";
            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = Vector3.up * (settings.debrisSize * 0.5f);
            piece.transform.localRotation = UnityEngine.Random.rotationUniform;
            piece.transform.localScale = Vector3.one * settings.debrisSize;

            MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
            pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (material != null)
            {
                pieceRenderer.sharedMaterial = material;
            }

            Paint(pieceRenderer, settings.debrisIntensity);

            Vector2 outward = UnityEngine.Random.insideUnitCircle.normalized;
            float upSpeed = UnityEngine.Random.Range(settings.debrisUpSpeed.x, settings.debrisUpSpeed.y);
            float outSpeed = UnityEngine.Random.Range(settings.debrisOutSpeed.x, settings.debrisOutSpeed.y);

            chunks.Add(new Chunk
            {
                transform = piece.transform,
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
                Destroy(gameObject);
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

            float t = Mathf.Clamp01(age / settings.ringTime);
            float spread = 1f - (1f - t) * (1f - t) * (1f - t);
            float outer = settings.ringRadius * spread;
            float width = settings.ringWidth * (1f - t);
            float inner = Mathf.Max(0f, outer - width);

            if (outer > 0.0001f && width > 0f)
            {
                for (int i = 0; i <= RingSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / RingSegments;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                    vertices.Add(radial * inner);
                    vertices.Add(radial * outer);
                }

                for (int i = 0; i < RingSegments; i++)
                {
                    int at = i * 2;

                    // Wound both ways, so it shows from either side.
                    triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
                    triangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
                }
            }

            ringMesh.Clear();
            ringMesh.SetVertices(vertices);
            ringMesh.SetTriangles(triangles, 0);
            ringMesh.RecalculateBounds();
        }

        private void Paint(Renderer target, float intensity)
        {
            Color colour = baseColour * intensity;
            colour.a = baseColour.a;

            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            target.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            Destroy(ringMesh);
        }
    }
}
