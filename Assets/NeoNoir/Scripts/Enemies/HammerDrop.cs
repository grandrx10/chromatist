using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One hammer falling from the sky: a red circle fills in on the floor,
    /// the hammer drops onto it, and on landing it bursts and fires
    /// projectiles out in fixed directions.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The circle appears, its outline at full size, and fills in from
    ///      the middle over <see cref="Settings.fillTime"/> seconds.
    ///   2. The hammer appears <see cref="Settings.dropHeight"/> above the
    ///      circle and falls, gathering speed, to land
    ///      <see cref="Settings.dropTime"/> seconds later. The circle stays
    ///      full meanwhile.
    ///   3. On landing: the circle goes, a <see cref="HammerImpact"/> bursts,
    ///      the camera shakes, and <see cref="Settings.projectileCount"/>
    ///      projectiles fly out level, evenly spaced around the compass, the
    ///      same world directions every time. The landing itself does no
    ///      damage; only the projectiles do.
    ///   4. The hammer lingers in the floor a moment, then is gone.
    ///
    /// Lives on its own once started, like a fired projectile, so it finishes
    /// even if whoever started it dies.
    ///
    /// The hammer prefab is pure art: every collider on it is switched off.
    /// The circle is a <see cref="WarningCircle"/>, so it needs no prefab.
    /// </remarks>
    public sealed class HammerDrop : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Hammer")]
            [Tooltip("Your falling hammer model. Needs no script or collider. Its pivot lands on the floor, at the rotation it is authored with.")]
            public GameObject hammerPrefab;

            [Tooltip("How far above the floor the hammer starts falling from.")]
            [Min(0f)] public float dropHeight = 15f;

            [Tooltip("Seconds the hammer takes to fall. It speeds up all the way down.")]
            [Min(0.01f)] public float dropTime = 0.5f;

            [Tooltip("Seconds the hammer stays stuck in the floor after landing.")]
            [Min(0f)] public float lingerTime = 0.3f;

            [Header("Indicator")]
            [Tooltip("Radius of the circle on the floor.")]
            [Min(0.1f)] public float radius = 1.5f;

            [Tooltip("Seconds the circle takes to fill, before the hammer starts falling.")]
            [Min(0f)] public float fillTime = 0.5f;

            [Tooltip("Thickness of the circle's outline.")]
            [Min(0f)] public float outlineWidth = 0.1f;

            [Tooltip("Height above the floor the circle is drawn at.")]
            [Range(0f, 0.5f)] public float floorOffset = 0.03f;

            [Tooltip("Material for the circle, e.g. NeoNoir/Flat. Its colour is replaced by the one below. Empty makes a NeoNoir/Flat one.")]
            public Material indicatorMaterial;

            [ColorUsage(false, true)]
            [Tooltip("Colour of the circle. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
            public Color indicatorColour = new Color(1f, 0.04f, 0.08f, 1f);

            [Header("Projectiles")]
            [Tooltip("Straight projectile fired out from the landing.")]
            public EnemyProjectile projectilePrefab;

            [Tooltip("Projectiles fired, evenly spaced around the full circle.")]
            [Range(1, 32)] public int projectileCount = 6;

            [Tooltip("Degrees the whole pattern is turned from world forward (+Z). The directions are the same for every hammer.")]
            public float angleOffset;

            [Tooltip("Height above the floor the projectiles fly at. About the middle of the player.")]
            [Min(0f)] public float projectileHeight = 1f;

            [Header("Impact")]
            [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want.")]
            public Material impactMaterial;

            public HammerImpact.Settings impact = new HammerImpact.Settings
            {
                ringRadius = 2.5f,
                debrisCount = 6,
            };

            [Tooltip("How far the camera shakes on landing. 0 disables it.")]
            [Min(0f)] public float shakeStrength = 0.2f;

            [Tooltip("Seconds the camera shake takes to die away.")]
            [Min(0.01f)] public float shakeDuration = 0.2f;
        }

        private Settings settings;
        private Vector3 floorPoint;
        private float age;
        private bool landed;

        private WarningCircle circle;
        private GameObject hammer;

        /// <summary>Total seconds from the circle appearing to the hammer landing.</summary>
        public static float TimeToLand(Settings settings) => settings.fillTime + settings.dropTime;

        /// <summary>Marks <paramref name="floor"/>, a point on the floor, and drops a hammer on it.</summary>
        public static HammerDrop Create(Vector3 floor, Settings settings)
        {
            GameObject host = new GameObject("HammerDrop");
            HammerDrop drop = host.AddComponent<HammerDrop>();
            drop.Begin(floor, settings);
            return drop;
        }

        private void Begin(Vector3 floor, Settings dropSettings)
        {
            settings = dropSettings;
            floorPoint = floor;
            transform.position = floor;

            circle = WarningCircle.Create(floor + Vector3.up * settings.floorOffset, settings.radius,
                                          settings.outlineWidth, settings.indicatorMaterial,
                                          settings.indicatorColour, transform);
        }

        private void Update()
        {
            age += Time.deltaTime;

            if (!landed)
            {
                circle.Fill = settings.fillTime > 0f ? age / settings.fillTime : 1f;

                if (age >= settings.fillTime)
                {
                    Fall(age - settings.fillTime);
                }

                if (age >= TimeToLand(settings))
                {
                    Land();
                }

                return;
            }

            if (age >= TimeToLand(settings) + settings.lingerTime)
            {
                Destroy(gameObject);
            }
        }

        private void Fall(float falling)
        {
            if (hammer == null && settings.hammerPrefab != null)
            {
                hammer = Instantiate(settings.hammerPrefab, floorPoint + Vector3.up * settings.dropHeight,
                                     settings.hammerPrefab.transform.rotation, transform);

                foreach (Collider part in hammer.GetComponentsInChildren<Collider>())
                {
                    part.enabled = false;
                }
            }

            if (hammer != null)
            {
                // From rest under constant pull, like the boss's own drop.
                float t = Mathf.Clamp01(falling / settings.dropTime);
                hammer.transform.position = floorPoint + Vector3.up * (settings.dropHeight * (1f - t * t));
            }
        }

        private void Land()
        {
            landed = true;
            Fall(settings.dropTime);
            Destroy(circle.gameObject);

            HammerImpact.Create(floorPoint, settings.impact, settings.impactMaterial);

            if (settings.shakeStrength > 0f)
            {
                TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
            }

            Fire();
        }

        private void Fire()
        {
            if (settings.projectilePrefab == null)
            {
                Debug.LogWarning("[HammerDrop] No projectile prefab assigned.");
                return;
            }

            Vector3 origin = floorPoint + Vector3.up * settings.projectileHeight;

            for (int i = 0; i < settings.projectileCount; i++)
            {
                float angle = settings.angleOffset + 360f * i / settings.projectileCount;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

                EnemyProjectile projectile = Instantiate(settings.projectilePrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
                projectile.Launch(direction);
            }
        }
    }
}
