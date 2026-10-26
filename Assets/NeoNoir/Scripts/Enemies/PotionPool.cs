using System;
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
    /// Drawn with a <see cref="WarningCircle"/> with no outline, so it needs
    /// no prefab.
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

            [Tooltip("Material for the pool, e.g. NeoNoir/Flat. Its colour is replaced by the one above. Empty makes a NeoNoir/Flat one.")]
            public Material material;
        }

        private static readonly Collider[] overlaps = new Collider[16];

        private Settings settings;
        private WarningCircle disc;
        private float age;
        private float tickWait;

        /// <summary>Radius the pool hurts and is drawn at this frame.</summary>
        public float Radius { get; private set; }

        /// <summary>Spills a pool on the floor at <paramref name="floorPoint"/>.</summary>
        public static PotionPool Create(Vector3 floorPoint, Settings settings)
        {
            GameObject host = new GameObject("PotionPool");
            host.transform.position = floorPoint;

            PotionPool pool = host.AddComponent<PotionPool>();
            pool.settings = settings;
            pool.disc = WarningCircle.Create(floorPoint + Vector3.up * settings.floorOffset, settings.maxRadius, 0f,
                                             settings.material, settings.colour, host.transform);
            return pool;
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
    }
}
