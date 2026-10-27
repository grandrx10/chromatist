using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A small burst of energy in mid-air: a flash that swells and collapses,
    /// and sparks flung out in every direction.
    /// </summary>
    /// <remarks>
    /// Purely visual. Used for the sceptre's release and for a homing missile
    /// dissipating against a wall, at different sizes.
    ///
    /// Like <see cref="HammerImpact"/>, nothing fades: the flash and sparks
    /// shrink to nothing, so every pixel stays full colour for the spot
    /// colour threshold. The sparks are stretched along the way they fly and
    /// slow as they go, with no gravity.
    ///
    /// Spawned at runtime and built from primitives, so it needs no prefab.
    /// </remarks>
    public sealed class EnergyBurst : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Size the flash swells to.")]
            [Min(0f)] public float flashSize = 1f;

            [Tooltip("Seconds the flash takes to swell and collapse.")]
            [Min(0.01f)] public float flashTime = 0.15f;

            [Tooltip("Sparks flung out.")]
            [Min(0)] public int sparkCount = 10;

            [Tooltip("Length of each spark. They are thin streaks of this.")]
            [Min(0.01f)] public float sparkSize = 0.25f;

            [Tooltip("Slowest and fastest speed the sparks leave at.")]
            public Vector2 sparkSpeed = new Vector2(4f, 9f);

            [Tooltip("Seconds each spark lives.")]
            [Min(0.01f)] public float sparkLife = 0.35f;

            [Tooltip("Colour of the burst. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);
        }

        private struct Spark
        {
            public Transform transform;
            public Vector3 velocity;
        }

        private readonly List<Spark> sparks = new List<Spark>();

        private Settings settings;
        private Transform flash;
        private float age;

        /// <summary>Bursts at <paramref name="point"/>.</summary>
        public static EnergyBurst Create(Vector3 point, Settings settings)
        {
            GameObject host = new GameObject("EnergyBurst");
            host.transform.position = point;

            EnergyBurst burst = host.AddComponent<EnergyBurst>();
            burst.Begin(settings);
            return burst;
        }

        private void Begin(Settings burstSettings)
        {
            settings = burstSettings;
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            if (settings.flashSize > 0f)
            {
                flash = MakePiece("Flash", PrimitiveType.Sphere, block).transform;
                flash.localScale = Vector3.zero;
            }

            for (int i = 0; i < settings.sparkCount; i++)
            {
                Vector3 direction = UnityEngine.Random.onUnitSphere;
                GameObject piece = MakePiece("Spark", PrimitiveType.Cube, block);
                piece.transform.rotation = Quaternion.LookRotation(direction);

                sparks.Add(new Spark
                {
                    transform = piece.transform,
                    velocity = direction * UnityEngine.Random.Range(settings.sparkSpeed.x, settings.sparkSpeed.y),
                });
            }

            Update();
        }

        private GameObject MakePiece(string pieceName, PrimitiveType type, MaterialPropertyBlock block)
        {
            GameObject piece = GameObject.CreatePrimitive(type);
            Destroy(piece.GetComponent<Collider>());

            piece.name = pieceName;
            piece.transform.SetParent(transform, false);

            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;
            FlatMaterial.Tint(renderer, settings.colour, block);

            return piece;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= Mathf.Max(settings.flashTime, settings.sparkCount > 0 ? settings.sparkLife : 0f))
            {
                Destroy(gameObject);
                return;
            }

            if (flash != null)
            {
                // Swells over the first third, then collapses.
                float t = Mathf.Clamp01(age / settings.flashTime);
                float swell = t < 0.33f ? t / 0.33f : 1f - (t - 0.33f) / 0.67f;
                flash.localScale = Vector3.one * (settings.flashSize * Mathf.Max(0f, swell));
            }

            float life = Mathf.Clamp01(age / settings.sparkLife);
            float scale = 1f - life;

            // Slows to about a tenth of its speed over its life.
            float drag = Mathf.Exp(-4.5f * deltaTime / settings.sparkLife);

            for (int i = 0; i < sparks.Count; i++)
            {
                Spark spark = sparks[i];
                spark.velocity *= drag;
                spark.transform.position += spark.velocity * deltaTime;
                spark.transform.localScale = new Vector3(0.2f, 0.2f, 1f) * (settings.sparkSize * scale);
                sparks[i] = spark;
            }
        }
    }
}
