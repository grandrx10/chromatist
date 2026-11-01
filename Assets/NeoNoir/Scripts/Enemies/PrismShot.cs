using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One coloured sphere split off a <see cref="LightOrb"/>: lobs in an arc
    /// to a spot on the floor, trailing its own colour, and explodes there.
    /// </summary>
    /// <remarks>
    /// The arc is fixed from the split: it does not track the player. It
    /// takes <see cref="Settings.flightTime"/> seconds whatever the distance,
    /// peaking <see cref="Settings.arcHeight"/> above the straight line from
    /// the split to the landing spot. A <see cref="WarningCircle"/> the size
    /// of the explosion marks the landing spot, filling in as the shot flies.
    ///
    /// It explodes on reaching the landing spot, or early if it touches the
    /// player on the way. The explosion hurts a player within
    /// <see cref="Settings.explosionRadius"/> of it, once, for
    /// <see cref="Settings.damage"/>. Walls and props do not stop it.
    ///
    /// Spawned at runtime and drawn as a primitive ball, so it needs no
    /// prefab. Its trail is left behind when it goes, to shrink away rather
    /// than vanish.
    /// </remarks>
    public sealed class PrismShot : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Flight")]
            [Tooltip("Seconds from the split to the landing spot, whatever the distance.")]
            [Min(0.05f)] public float flightTime = 1.2f;

            [Tooltip("How far the arc rises above the straight line from the split to the landing spot, at its peak.")]
            [Min(0f)] public float arcHeight = 5f;

            [Tooltip("Size of the ball, across.")]
            [Min(0.05f)] public float size = 0.6f;

            [Tooltip("Radius for touching the player in flight.")]
            [Min(0.01f)] public float radius = 0.3f;

            [Header("Trail")]
            [Tooltip("Seconds of travel the trail covers. Longer is a longer tail.")]
            [Min(0.01f)] public float trailTime = 0.4f;

            [Tooltip("Width of the trail at the ball. It narrows to nothing at the tail.")]
            [Min(0.001f)] public float trailWidth = 0.35f;

            [Header("Explosion")]
            [Tooltip("Damage to the player caught in the explosion.")]
            [Min(0f)] public float damage = 15f;

            [Tooltip("Radius of the explosion, and of the warning circle.")]
            [Min(0.1f)] public float explosionRadius = 2f;

            [Tooltip("The burst where it explodes. Its colour is replaced by the shot's.")]
            public EnergyBurst.Settings burst = new EnergyBurst.Settings
            {
                flashSize = 4f,
                flashTime = 0.25f,
                sparkCount = 14,
                sparkSize = 0.35f,
                sparkSpeed = new Vector2(5f, 10f),
                sparkLife = 0.4f,
            };

            [Tooltip("How far the camera shakes on an explosion. 0 disables it.")]
            [Min(0f)] public float shakeStrength = 0.15f;

            [Tooltip("Seconds the camera shake takes to die away.")]
            [Min(0.01f)] public float shakeDuration = 0.2f;

            [Header("Indicator")]
            [Tooltip("Thickness of the warning circle's outline.")]
            [Min(0f)] public float outlineWidth = 0.1f;

            [Tooltip("Height above the floor the circle is drawn at.")]
            [Range(0f, 0.5f)] public float floorOffset = 0.03f;

            [Tooltip("Material for the circle, e.g. NeoNoir/Flat. Its colour is replaced by the shot's. Empty makes a NeoNoir/Flat one.")]
            public Material indicatorMaterial;
        }

        private static readonly Collider[] overlaps = new Collider[16];

        private Settings settings;
        private Color colour;
        private Vector3 start;
        private Vector3 end;
        private float age;
        private TrailRenderer trail;
        private WarningCircle circle;

        /// <summary>
        /// Lobs a ball of <paramref name="shotColour"/> from
        /// <paramref name="from"/> to <paramref name="landing"/>, a point on
        /// the floor.
        /// </summary>
        public static PrismShot Launch(Vector3 from, Vector3 landing, Color shotColour, Settings settings)
        {
            GameObject host = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            host.name = "PrismShot";
            Destroy(host.GetComponent<Collider>());
            host.transform.position = from;
            host.transform.localScale = Vector3.one * settings.size;

            MeshRenderer renderer = host.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterial = FlatMaterial.Shared;
            FlatMaterial.Tint(renderer, shotColour, new MaterialPropertyBlock());

            PrismShot shot = host.AddComponent<PrismShot>();
            shot.Begin(from, landing, shotColour, settings);
            return shot;
        }

        private void Begin(Vector3 from, Vector3 landing, Color shotColour, Settings shotSettings)
        {
            settings = shotSettings;
            colour = shotColour;
            start = from;
            end = landing;

            circle = WarningCircle.Create(landing + Vector3.up * settings.floorOffset, settings.explosionRadius,
                                          settings.outlineWidth, settings.indicatorMaterial, colour);
            BuildTrail();
        }

        private void BuildTrail()
        {
            GameObject host = new GameObject("Trail");
            host.transform.SetParent(transform, false);

            trail = host.AddComponent<TrailRenderer>();
            trail.time = settings.trailTime;
            trail.widthMultiplier = settings.trailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;

            if (trail.sharedMaterial != null)
            {
                FlatMaterial.Tint(trail, colour, new MaterialPropertyBlock());
            }
        }

        /// <summary>Where the arc is <paramref name="t"/> of the way along, 0 to 1.</summary>
        private Vector3 PointAt(float t)
        {
            return Vector3.Lerp(start, end, t) + Vector3.up * (settings.arcHeight * 4f * t * (1f - t));
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / settings.flightTime);
            transform.position = PointAt(t);

            if (circle != null)
            {
                circle.Fill = t;
            }

            if (t >= 1f || TouchingPlayer())
            {
                Explode();
            }
        }

        private bool TouchingPlayer()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, settings.radius, overlaps,
                                                      Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (overlaps[i].GetComponentInParent<PlayerHealth>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void Explode()
        {
            Vector3 point = transform.position;
            EnergyBurst.Create(point, Recolour(settings.burst));

            if (settings.shakeStrength > 0f)
            {
                TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
            }

            int count = Physics.OverlapSphereNonAlloc(point, settings.explosionRadius, overlaps,
                                                      Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth player = overlaps[i].GetComponentInParent<PlayerHealth>();

                if (player != null)
                {
                    player.TakeDamage(settings.damage, point);
                    break;
                }
            }

            if (circle != null)
            {
                Destroy(circle.gameObject);
            }

            DropTrail();
            Destroy(gameObject);
        }

        private EnergyBurst.Settings Recolour(EnergyBurst.Settings from)
        {
            return new EnergyBurst.Settings
            {
                flashSize = from.flashSize,
                flashTime = from.flashTime,
                sparkCount = from.sparkCount,
                sparkSize = from.sparkSize,
                sparkSpeed = from.sparkSpeed,
                sparkLife = from.sparkLife,
                colour = colour,
            };
        }

        /// <summary>Leaves the trail in the world to shrink away.</summary>
        private void DropTrail()
        {
            if (trail == null)
            {
                return;
            }

            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(trail.gameObject, trail.time);
            trail = null;
        }

        private void OnDestroy()
        {
            if (circle != null)
            {
                Destroy(circle.gameObject);
            }
        }
    }
}
