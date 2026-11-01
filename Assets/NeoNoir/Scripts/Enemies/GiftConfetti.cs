using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// A burst of confetti: little paper flakes in bright colours, flung out
    /// and up, then fluttering down, tumbling, and shrinking away.
    /// </summary>
    /// <remarks>
    /// Builds itself, needing no prefab, and removes itself once every flake
    /// is gone. Each flake is a thin flat card with no collider, coloured
    /// through a property block. Air drag is strong, so a flake bursts out
    /// fast, all but stops, and drifts down slowly, swaying, the way paper does.
    /// </remarks>
    public sealed class GiftConfetti : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Flakes in one burst.")]
            [Min(0)] public int count = 40;

            [Tooltip("Width of a flake. Each is about half again as long, and paper thin.")]
            [Min(0.01f)] public float size = 0.18f;

            [Tooltip("Slowest and fastest a flake is flung out, in units per second.")]
            public Vector2 speed = new Vector2(6f, 14f);

            [Tooltip("Extra upward speed on every flake, so the burst fountains up before it falls.")]
            [Min(0f)] public float upward = 6f;

            [Tooltip("How quickly air slows a flake. Higher stops it sooner and lets it fall slower.")]
            [Min(0f)] public float drag = 3.5f;

            [Tooltip("Downward pull, in units per second squared.")]
            [Min(0f)] public float gravity = 9f;

            [Tooltip("How far a flake sways side to side as it drifts down.")]
            [Min(0f)] public float sway = 1.2f;

            [Tooltip("Shortest and longest seconds a flake lives. It shrinks away over the last third.")]
            public Vector2 lifetime = new Vector2(1.4f, 2.4f);

            [Tooltip("Colours picked from at random, one per flake. Above 1 blooms.")]
            [ColorUsage(false, true)]
            public Color[] colours =
            {
                new Color(2.4f, 0.12f, 0.1f),
                new Color(2f, 2f, 2f),
            };
        }

        private struct Flake
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 spinAxis;
            public float spinSpeed;
            public float swayPhase;
            public float lifetime;
        }

        private readonly List<Flake> flakes = new List<Flake>();

        private Settings settings;
        private Vector3 flakeScale;
        private float age;

        /// <summary>
        /// Bursts confetti from <paramref name="origin"/>, everything about it
        /// multiplied by <paramref name="scale"/> but the colours and timing.
        /// A null <paramref name="material"/> uses the shared NeoNoir/Flat one.
        /// </summary>
        public static GiftConfetti Burst(Vector3 origin, Settings settings, Material material, float scale = 1f)
        {
            GameObject host = new GameObject("GiftConfetti");
            host.transform.position = origin;

            GiftConfetti confetti = host.AddComponent<GiftConfetti>();
            confetti.Begin(settings, material != null ? material : FlatMaterial.DoubleSided, Mathf.Max(0.05f, scale));
            return confetti;
        }

        private void Begin(Settings burst, Material material, float scale)
        {
            settings = burst;
            flakeScale = new Vector3(settings.size, 0.01f, settings.size * 1.5f) * scale;

            MaterialPropertyBlock block = new MaterialPropertyBlock();

            for (int i = 0; i < settings.count; i++)
            {
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());

                piece.name = "Flake";
                piece.transform.SetParent(transform, false);
                piece.transform.localRotation = Random.rotationUniform;
                piece.transform.localScale = flakeScale;

                MeshRenderer flakeRenderer = piece.GetComponent<MeshRenderer>();
                flakeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flakeRenderer.receiveShadows = false;

                if (material != null)
                {
                    flakeRenderer.sharedMaterial = material;
                }

                if (settings.colours != null && settings.colours.Length > 0)
                {
                    FlatMaterial.Tint(flakeRenderer, settings.colours[Random.Range(0, settings.colours.Length)], block);
                }

                float shortest = Mathf.Max(0.05f, Mathf.Min(settings.lifetime.x, settings.lifetime.y));

                flakes.Add(new Flake
                {
                    transform = piece.transform,
                    velocity = Random.onUnitSphere * (Random.Range(settings.speed.x, settings.speed.y) * scale)
                               + Vector3.up * (settings.upward * scale),
                    spinAxis = Random.onUnitSphere,
                    spinSpeed = Random.Range(360f, 1080f),
                    swayPhase = Random.Range(0f, Mathf.PI * 2f),
                    lifetime = Random.Range(shortest, Mathf.Max(shortest, settings.lifetime.y)),
                });
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            // Exact for any frame rate: the share of speed air leaves after this step.
            float kept = Mathf.Exp(-settings.drag * deltaTime);
            bool anyLeft = false;

            for (int i = 0; i < flakes.Count; i++)
            {
                Flake flake = flakes[i];

                if (flake.transform == null)
                {
                    continue;
                }

                if (age >= flake.lifetime)
                {
                    Destroy(flake.transform.gameObject);
                    flake.transform = null;
                    flakes[i] = flake;
                    continue;
                }

                anyLeft = true;

                flake.velocity *= kept;
                flake.velocity += Vector3.down * (settings.gravity * deltaTime);

                // Paper falls no faster than air lets it, rocking as it goes.
                float sway = Mathf.Sin(age * 5f + flake.swayPhase) * settings.sway;
                Vector3 drift = new Vector3(Mathf.Cos(flake.swayPhase), 0f, Mathf.Sin(flake.swayPhase)) * sway;

                flake.transform.position += (flake.velocity + drift) * deltaTime;
                flake.transform.Rotate(flake.spinAxis, flake.spinSpeed * deltaTime, Space.World);

                // Full size for the first two thirds, then shrinks away.
                float left = 1f - age / flake.lifetime;
                flake.transform.localScale = flakeScale * Mathf.Clamp01(left * 3f);

                flakes[i] = flake;
            }

            if (!anyLeft)
            {
                Destroy(gameObject);
            }
        }
    }
}
