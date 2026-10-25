using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The rings Blue's notes leave on the floor beneath an enemy. Each note
    /// that lands adds one hollow ring; at the fifth they burst into music
    /// notes for heavy damage. Left alone, the rings wear off one at a time.
    /// </summary>
    /// <remarks>
    /// Rings are concentric, the first hugging the enemy and each new one a
    /// step further out, so the stack reads at a glance. A new ring pops in
    /// slightly oversized and flashing, then settles.
    ///
    /// Wearing off. Once <see cref="Settings.decayDelay"/> seconds pass with
    /// no new ring, the outermost one dissolves over
    /// <see cref="Settings.decayInterval"/> seconds and is gone, then the
    /// next, and so on. A hit mid-dissolve restores that ring at once and
    /// adds the new one on top, restarting the delay.
    ///
    /// Bursting deals its damage to this enemy only, plays a
    /// <see cref="BlueBurst"/>, empties the stack and reports to whoever made
    /// the mark, so the banjo can cut the player's cooldowns.
    ///
    /// Not parented to the enemy, only following it, so the enemy's own
    /// rotation and scale never bend the rings. Removes itself once it is
    /// empty, or when the enemy dies or is destroyed.
    ///
    /// Spawned at runtime and builds its own meshes, so it needs no prefab.
    /// </remarks>
    public sealed class BlueMark : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Rings needed to burst.")]
            [Min(1)] public int ringsToBurst = 5;

            [Tooltip("Damage the burst deals to the marked enemy.")]
            [Min(0f)] public float burstDamage = 40f;

            [Tooltip("What the time left on every cooldown is multiplied by when a mark bursts. 0.5 halves it.")]
            [Range(0f, 1f)] public float cooldownFactor = 0.5f;

            [Tooltip("Seconds without a new ring before the rings start wearing off.")]
            [Min(0f)] public float decayDelay = 2f;

            [Tooltip("Seconds each ring takes to wear off, one after another.")]
            [Min(0.01f)] public float decayInterval = 1f;

            [Tooltip("Gap between the enemy's collider and the first ring.")]
            [Min(0f)] public float gap = 0.15f;

            [Tooltip("Distance between one ring and the next.")]
            [Min(0.01f)] public float spacing = 0.22f;

            [Tooltip("Width of each ring's outline.")]
            [Min(0.01f)] public float thickness = 0.07f;

            [Tooltip("Height above the bottom of the enemy's collider, taken as the floor, that the rings are drawn at.")]
            [Range(0f, 2f)] public float floorOffset = 0.05f;

            [Tooltip("Colour intensity of a settled ring. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float intensity = 1.5f;

            [Tooltip("Extra intensity flashed on a new ring, fading over the pop time.")]
            [Min(0f)] public float hitFlash = 2f;

            [Tooltip("How much larger a new ring starts before it settles. 0.25 is a quarter larger.")]
            [Min(0f)] public float popScale = 0.25f;

            [Tooltip("Seconds a new ring takes to settle.")]
            [Min(0.01f)] public float popTime = 0.15f;

            public BlueBurst.Settings burst = new BlueBurst.Settings();
        }

        private const int Segments = 48;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private sealed class Ring
        {
            public GameObject Host;
            public MeshRenderer Renderer;
            public Mesh Mesh;

            // 1 just landed, settling to 0.
            public float Pop;
        }

        private readonly List<Ring> rings = new List<Ring>();

        private EnemyHealth target;
        private Settings settings;
        private Material material;
        private IReadOnlyList<GameObject> notePrefabs;
        private Action<BlueMark> burst;

        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;
        private bool hasFade;
        private float firstRadius;

        // Rings currently stacked. The meshes past this are kept, hidden, to reuse.
        private int count;
        private float idle;
        private float wear;

        /// <summary>Rings currently on the enemy.</summary>
        public int Count => count;

        public EnemyHealth Target => target;

        /// <summary>
        /// Starts an empty mark under <paramref name="enemy"/>. Call
        /// <see cref="AddRing"/> to stack it. <paramref name="onBurst"/> is
        /// called each time it bursts.
        /// </summary>
        public static BlueMark Create(EnemyHealth enemy, Settings settings, Material material,
                                      IReadOnlyList<GameObject> notePrefabs, Action<BlueMark> onBurst)
        {
            GameObject host = new GameObject("BlueMark");
            BlueMark mark = host.AddComponent<BlueMark>();
            mark.Bind(enemy, settings, material, notePrefabs, onBurst);
            return mark;
        }

        private void Bind(EnemyHealth enemy, Settings markSettings, Material markMaterial,
                          IReadOnlyList<GameObject> prefabs, Action<BlueMark> onBurst)
        {
            target = enemy;
            settings = markSettings;
            material = markMaterial;
            notePrefabs = prefabs;
            burst = onBurst;

            // Sized to the body once. Enemies do not change size.
            Vector3 extents = enemy.BodyBounds().extents;
            firstRadius = Mathf.Max(0.3f, Mathf.Max(extents.x, extents.z)) + settings.gap;

            if (material != null)
            {
                hasFade = material.HasProperty(FadeId);

                if (material.HasProperty(BaseColorId))
                {
                    baseColour = material.GetColor(BaseColorId);
                }
            }

            block = new MaterialPropertyBlock();
            Follow();
        }

        /// <summary>Stacks one more ring. At the burst count, bursts instead and empties the stack.</summary>
        public void AddRing()
        {
            if (target == null || target.IsDead)
            {
                return;
            }

            idle = 0f;
            wear = 0f;

            count++;

            if (count >= settings.ringsToBurst)
            {
                Burst();
                return;
            }

            Ring ring = RingAt(count - 1);
            ring.Pop = 1f;
            ring.Host.SetActive(true);

            ApplyVisuals();
        }

        private void Burst()
        {
            count = 0;

            foreach (Ring ring in rings)
            {
                ring.Host.SetActive(false);
            }

            Bounds body = target.BodyBounds();
            float outer = firstRadius + settings.spacing * (settings.ringsToBurst - 1);

            BlueBurst.Play(body.center, transform.position, outer, settings.burst, material, notePrefabs);

            target.TakeDamage(settings.burstDamage);
            burst?.Invoke(this);
        }

        private void LateUpdate()
        {
            if (target == null || target.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            float deltaTime = Time.deltaTime;

            Follow();

            foreach (Ring ring in rings)
            {
                ring.Pop = Mathf.MoveTowards(ring.Pop, 0f, deltaTime / settings.popTime);
            }

            if (count > 0)
            {
                idle += deltaTime;

                if (idle >= settings.decayDelay)
                {
                    wear += deltaTime;

                    if (wear >= settings.decayInterval)
                    {
                        wear = 0f;
                        count--;
                        RingAt(count).Host.SetActive(false);
                    }
                }
            }

            if (count == 0)
            {
                Destroy(gameObject);
                return;
            }

            ApplyVisuals();
        }

        private void Follow()
        {
            Bounds body = target.BodyBounds();
            transform.position = new Vector3(body.center.x, body.min.y + settings.floorOffset, body.center.z);
        }

        private void ApplyVisuals()
        {
            for (int i = 0; i < count; i++)
            {
                Ring ring = rings[i];

                // Only the outermost ring wears, and only once the delay is up.
                bool wearing = i == count - 1 && idle >= settings.decayDelay;
                float visibility = wearing ? 1f - Mathf.Clamp01(wear / settings.decayInterval) : 1f;

                float pop = ring.Pop * ring.Pop;
                float scale = 1f + settings.popScale * pop;

                if (!hasFade)
                {
                    scale *= visibility;
                }

                ring.Host.transform.localScale = new Vector3(scale, 1f, scale);

                Color colour = baseColour * (settings.intensity + settings.hitFlash * pop);
                colour.a = baseColour.a;

                block.SetColor(BaseColorId, colour);
                block.SetFloat(FadeId, visibility);
                ring.Renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>The ring at <paramref name="index"/>, counting out from the enemy, built the first time it is needed.</summary>
        private Ring RingAt(int index)
        {
            while (rings.Count <= index)
            {
                float radius = firstRadius + settings.spacing * rings.Count;

                GameObject host = new GameObject("Ring " + rings.Count);
                host.transform.SetParent(transform, false);

                Mesh mesh = AnnulusMesh(radius - settings.thickness * 0.5f, radius + settings.thickness * 0.5f, Segments);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;

                MeshRenderer ringRenderer = host.AddComponent<MeshRenderer>();
                ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;

                if (material != null)
                {
                    ringRenderer.sharedMaterial = material;
                }

                host.SetActive(false);
                rings.Add(new Ring { Host = host, Renderer = ringRenderer, Mesh = mesh });
            }

            return rings[index];
        }

        private void OnDestroy()
        {
            foreach (Ring ring in rings)
            {
                if (ring.Mesh != null)
                {
                    Destroy(ring.Mesh);
                }
            }
        }

        /// <summary>
        /// A flat full ring facing up, centred on the pivot, between the two
        /// radii. Shared by the marks, the burst and the perimeter.
        /// </summary>
        public static Mesh AnnulusMesh(float inner, float outer, int segments)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            inner = Mathf.Max(0f, inner);

            for (int i = 0; i <= segments; i++)
            {
                float yaw = Mathf.PI * 2f * i / segments;
                Vector3 direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

                vertices.Add(direction * inner);
                vertices.Add(direction * outer);
            }

            // Clockwise seen from above, so the face points up.
            for (int i = 0; i < segments; i++)
            {
                int innerA = i * 2;
                int outerA = innerA + 1;
                int innerB = innerA + 2;
                int outerB = innerA + 3;

                triangles.AddRange(new[] { innerA, outerA, innerB, outerA, outerB, innerB });
            }

            Mesh ring = new Mesh { name = "Blue ring" };
            ring.SetVertices(vertices);
            ring.SetTriangles(triangles, 0);
            ring.RecalculateNormals();
            ring.RecalculateBounds();
            return ring;
        }
    }
}
