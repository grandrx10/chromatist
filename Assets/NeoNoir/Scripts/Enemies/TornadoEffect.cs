using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// A whirlwind round a spinning body: streaks of wind whipping round it
    /// in a funnel that widens as it rises, and dust thrown off the floor at
    /// its foot.
    /// </summary>
    /// <remarks>
    /// Each streak is a trail swept round the body, from
    /// <see cref="Settings.baseRadius"/> at the floor out to
    /// <see cref="Settings.topRadius"/> at <see cref="Settings.height"/>,
    /// and climbs slowly as it goes, starting again at the foot once it
    /// reaches the top. Higher streaks go round a little slower, so the
    /// funnel twists.
    ///
    /// <see cref="Intensity"/>, 0 to 1, is how hard it blows: the funnel's
    /// width, the streaks' width and how much dust comes off all scale with
    /// it, so whoever drives it can spin it up and wind it down. At 0 it
    /// draws nothing.
    ///
    /// Follows the body it is made for, but is not its child, so the body's
    /// own spin never drags the streaks round. Removes itself once
    /// <see cref="Release"/>d and its streaks have faded, or when the body
    /// dies or is gone. Spawned at runtime, so it needs no prefab.
    /// </remarks>
    public sealed class TornadoEffect : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Funnel")]
            [Tooltip("Streaks of wind whipping round.")]
            [Range(0, 24)] public int streaks = 8;

            [Tooltip("How far out the streaks go round at the floor.")]
            [Min(0f)] public float baseRadius = 0.8f;

            [Tooltip("How far out they go round at the top. Wider than the base makes a funnel.")]
            [Min(0f)] public float topRadius = 2f;

            [Tooltip("How high the funnel reaches.")]
            [Min(0.1f)] public float height = 3.5f;

            [Tooltip("Times a second the streaks go round, at the foot.")]
            [Min(0f)] public float turnRate = 2.5f;

            [Tooltip("Units a second the streaks climb as they go round.")]
            [Min(0f)] public float climbSpeed = 1.5f;

            [Header("Look")]
            [Tooltip("Width of each streak at full intensity.")]
            [Min(0.005f)] public float width = 0.08f;

            [Tooltip("Seconds of travel each streak's tail covers. Longer is a longer tail.")]
            [Min(0.01f)] public float tailTime = 0.12f;

            [Tooltip("Colour of the wind. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(2f, 2f, 2f, 1f);

            [Tooltip("Material for the streaks. Empty uses NeoNoir/Flat, double-sided, in the colour above.")]
            public Material material;

            [Header("Dust")]
            [Tooltip("Dust puffs thrown off the floor a second, at full intensity.")]
            [Min(0f)] public float dustRate = 18f;

            [Tooltip("Speed the dust is thrown out at, round and away.")]
            [Min(0f)] public float dustSpeed = 4f;

            public DustTrail.Settings dust = new DustTrail.Settings
            {
                size = 0.4f,
                lifetime = 0.5f,
                rise = 1f,
            };
        }

        private sealed class Streak
        {
            public TrailRenderer trail;
            public float angle;
            public float climb;
        }

        private readonly List<Streak> streaks = new List<Streak>();

        private Settings settings;
        private Transform body;
        private EnemyHealth health;
        private DustTrail.Cloud dust;
        private float dustDebt;
        private float released = -1f;

        /// <summary>How hard it blows, 0 to 1.</summary>
        public float Intensity { get; set; }

        /// <summary>Starts a whirlwind round <paramref name="around"/>, at no intensity.</summary>
        public static TornadoEffect Create(Transform around, Settings settings)
        {
            TornadoEffect tornado = new GameObject("Tornado").AddComponent<TornadoEffect>();
            tornado.Begin(around, settings);
            return tornado;
        }

        /// <summary>Stops blowing: the streaks fade out, then it is gone.</summary>
        public void Release()
        {
            if (released < 0f)
            {
                released = 0f;
                Intensity = 0f;

                if (dust != null)
                {
                    dust.Release();
                    dust = null;
                }
            }
        }

        private void Begin(Transform around, Settings tornadoSettings)
        {
            settings = tornadoSettings;
            body = around;
            health = around.GetComponent<EnemyHealth>();
            transform.position = around.position;

            Material material = settings.material != null ? settings.material : FlatMaterial.DoubleSided;
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            for (int i = 0; i < settings.streaks; i++)
            {
                GameObject host = new GameObject("Streak");
                host.transform.SetParent(transform, false);

                TrailRenderer trail = host.AddComponent<TrailRenderer>();
                trail.time = settings.tailTime;
                trail.minVertexDistance = 0.05f;
                trail.numCapVertices = 2;
                trail.alignment = LineAlignment.View;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.sharedMaterial = material;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                trail.emitting = false;

                if (settings.material == null && material != null)
                {
                    FlatMaterial.Tint(trail, settings.colour, block);
                }

                // Spread evenly up the funnel and round it, so it starts full.
                streaks.Add(new Streak
                {
                    trail = trail,
                    angle = Random.Range(0f, 360f),
                    climb = settings.height * (i + Random.value) / settings.streaks,
                });
            }

            if (settings.dustRate > 0f)
            {
                dust = DustTrail.Cloud.Create("TornadoDust");
            }

            Place(0f);
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            if (released < 0f && (body == null || (health != null && health.IsDead)))
            {
                Release();
            }

            if (released >= 0f)
            {
                released += deltaTime;

                foreach (Streak streak in streaks)
                {
                    streak.trail.emitting = false;
                }

                if (released >= settings.tailTime)
                {
                    Destroy(gameObject);
                }

                return;
            }

            transform.position = body.position;
            Place(deltaTime);
            ThrowDust(deltaTime);
        }

        private void Place(float deltaTime)
        {
            float intensity = Mathf.Clamp01(Intensity);
            bool blowing = intensity > 0.01f;

            foreach (Streak streak in streaks)
            {
                float up = streak.climb / settings.height;

                // Higher goes round slower, so the funnel twists.
                streak.angle += settings.turnRate * 360f * Mathf.Lerp(1f, 0.6f, up) * intensity * deltaTime;
                streak.climb += settings.climbSpeed * deltaTime;

                bool wrapped = streak.climb > settings.height;

                if (wrapped)
                {
                    streak.climb -= settings.height;
                    up = streak.climb / settings.height;
                }

                float radius = Mathf.Lerp(settings.baseRadius, settings.topRadius, up) * intensity;
                float angle = streak.angle * Mathf.Deg2Rad;
                streak.trail.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, streak.climb,
                                                                   Mathf.Sin(angle) * radius);
                streak.trail.widthMultiplier = settings.width * intensity;

                // Back at the foot: start a fresh tail, rather than one drawn
                // straight down through the funnel.
                if (wrapped || !blowing)
                {
                    streak.trail.Clear();
                }

                streak.trail.emitting = blowing;
            }
        }

        private void ThrowDust(float deltaTime)
        {
            if (dust == null)
            {
                return;
            }

            dustDebt += settings.dustRate * Mathf.Clamp01(Intensity) * deltaTime;

            while (dustDebt >= 1f)
            {
                dustDebt -= 1f;

                float angle = Random.Range(0f, 360f);
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 round = Vector3.Cross(Vector3.up, outward);
                Vector3 foot = body.position + outward * settings.baseRadius + Vector3.up * (settings.dust.size * 0.3f);

                dust.Emit(foot, (outward + round) * (settings.dustSpeed * 0.7f)
                                + Vector3.up * (settings.dust.rise * Random.Range(0.6f, 1.2f)),
                          settings.dust);
            }
        }

        private void OnDestroy()
        {
            if (dust != null)
            {
                dust.Release();
            }
        }
    }
}
