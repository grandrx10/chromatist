using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// What plays round a blade in the Afterworld when it is upgraded: a ring
    /// of light sweeping out across the floor from its stone, streaks
    /// spiralling up round the blade, and a flash at its heart.
    /// </summary>
    /// <remarks>
    /// Purely visual. Like <see cref="EnergyBurst"/>, nothing fades: the ring
    /// thins to nothing and the streaks shrink away, so every pixel stays
    /// full colour for the two-tone threshold.
    ///
    /// Spawned at runtime and built from primitives, so it needs no prefab.
    /// </remarks>
    public sealed class BladeUpgradeBurst : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Colour of the ring and streaks. HDR: above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(5f, 5f, 5f, 1f);

            [Header("Floor ring")]
            [Tooltip("Radius the ring starts at, then sweeps out to.")]
            public Vector2 ringRadius = new Vector2(0.4f, 3.5f);

            [Tooltip("Width of the ring as it starts. It thins to nothing as it goes.")]
            [Min(0f)] public float ringWidth = 0.35f;

            [Min(0.01f)] public float ringTime = 0.7f;

            [Header("Streaks")]
            [Min(0)] public int streakCount = 16;

            [Tooltip("Distance from the blade the streaks rise at.")]
            [Min(0f)] public float streakRadius = 0.7f;

            [Tooltip("Metres a second they rise.")]
            public Vector2 streakRise = new Vector2(2.5f, 5f);

            [Tooltip("Degrees a second they wind round the blade.")]
            public float streakSwirl = 240f;

            [Tooltip("Length of each streak.")]
            [Min(0.01f)] public float streakSize = 0.5f;

            [Tooltip("Seconds over which they leave, and how long each lives.")]
            [Min(0f)] public float streakSpread = 0.35f;

            [Min(0.01f)] public float streakLife = 0.7f;

            [Header("Flash")]
            public EnergyBurst.Settings flash = new EnergyBurst.Settings
            {
                flashSize = 1.6f,
                flashTime = 0.3f,
                sparkCount = 20,
                sparkSize = 0.35f,
                sparkSpeed = new Vector2(4f, 9f),
                sparkLife = 0.45f,
                colour = new Color(5f, 5f, 5f, 1f),
            };
        }

        private const int RingSegments = 72;

        private sealed class Streak
        {
            public Transform transform;
            public float angle;
            public float rise;
            public float born;
        }

        private readonly List<Streak> streaks = new List<Streak>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Settings settings;
        private Mesh ring;
        private Vector3 heart;
        private float floor;
        private float age;

        /// <summary>Plays round <paramref name="blade"/>.</summary>
        public static BladeUpgradeBurst Play(PurchasableBlade blade, Settings settings)
        {
            Bounds bounds = blade.BladeBounds;
            Vector3 foot = blade.StoneTop;

            GameObject host = new GameObject("BladeUpgradeBurst");
            host.transform.position = new Vector3(bounds.center.x, SkillTreeLines.Instance.FloorAt(blade), bounds.center.z);

            BladeUpgradeBurst burst = host.AddComponent<BladeUpgradeBurst>();
            burst.Begin(settings, bounds.center, foot.y);
            return burst;
        }

        private void Begin(Settings burstSettings, Vector3 bladeCentre, float stoneTop)
        {
            settings = burstSettings;
            heart = bladeCentre;
            floor = stoneTop;

            MaterialPropertyBlock block = new MaterialPropertyBlock();

            // The ring runs along the floor at the blade's feet.
            GameObject ringObject = new GameObject("Ring");
            ringObject.transform.SetParent(transform, false);
            ringObject.transform.localPosition = Vector3.up * 0.03f;
            ring = new Mesh { name = "Upgrade Ring" };
            ring.MarkDynamic();
            ringObject.AddComponent<MeshFilter>().sharedMesh = ring;
            MeshRenderer ringRenderer = ringObject.AddComponent<MeshRenderer>();
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ringRenderer.sharedMaterial = FlatMaterial.DoubleSided;
            FlatMaterial.Tint(ringRenderer, settings.colour, block);

            for (int i = 0; i < settings.streakCount; i++)
            {
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());
                piece.name = "Streak";
                piece.transform.SetParent(transform, false);
                piece.transform.localScale = Vector3.zero;

                MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = FlatMaterial.Shared;
                FlatMaterial.Tint(renderer, settings.colour, block);

                streaks.Add(new Streak
                {
                    transform = piece.transform,
                    angle = (i / (float)settings.streakCount) * 360f + UnityEngine.Random.Range(-8f, 8f),
                    rise = UnityEngine.Random.Range(settings.streakRise.x, settings.streakRise.y),
                    born = UnityEngine.Random.value * settings.streakSpread,
                });
            }

            EnergyBurst.Create(heart, settings.flash);
            Update();
        }

        private void OnDestroy()
        {
            Destroy(ring);
        }

        private void Update()
        {
            age += Time.deltaTime;

            if (age >= Mathf.Max(settings.ringTime, settings.streakSpread + settings.streakLife))
            {
                Destroy(gameObject);
                return;
            }

            BuildRing(Mathf.Clamp01(age / settings.ringTime));

            for (int i = 0; i < streaks.Count; i++)
            {
                Streak streak = streaks[i];
                float life = (age - streak.born) / settings.streakLife;

                if (life <= 0f || life >= 1f)
                {
                    streak.transform.localScale = Vector3.zero;
                    continue;
                }

                float time = age - streak.born;
                float angle = (streak.angle + settings.streakSwirl * time) * Mathf.Deg2Rad;

                // Drawn in toward the blade as they climb.
                float radius = settings.streakRadius * (1f - life * 0.5f);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 position = new Vector3(heart.x, floor, heart.z) + offset + Vector3.up * (streak.rise * time);

                // Pointed the way they travel: up, and round.
                Vector3 around = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (settings.streakSwirl * Mathf.Deg2Rad * radius);
                Vector3 velocity = around + Vector3.up * streak.rise;

                streak.transform.position = position;
                streak.transform.rotation = Quaternion.LookRotation(velocity);

                float scale = Mathf.Sin(life * Mathf.PI);
                streak.transform.localScale = new Vector3(0.08f, 0.08f, 1f) * (settings.streakSize * scale);
            }
        }

        private void BuildRing(float t)
        {
            vertices.Clear();
            triangles.Clear();

            // Fast out, slowing as it goes; thinning to nothing at the end.
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(settings.ringRadius.x, settings.ringRadius.y, eased);
            float half = settings.ringWidth * 0.5f * (1f - t);

            if (half > 0.0005f)
            {
                for (int i = 0; i <= RingSegments; i++)
                {
                    float angle = i / (float)RingSegments * Mathf.PI * 2f;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(direction * (radius + half));
                    vertices.Add(direction * Mathf.Max(0f, radius - half));
                }

                for (int k = 0; k + 3 < vertices.Count; k += 2)
                {
                    triangles.Add(k); triangles.Add(k + 2); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + 2); triangles.Add(k + 3);
                }
            }

            ring.Clear();
            ring.SetVertices(vertices);
            ring.SetTriangles(triangles, 0);
            ring.RecalculateBounds();
        }
    }
}
