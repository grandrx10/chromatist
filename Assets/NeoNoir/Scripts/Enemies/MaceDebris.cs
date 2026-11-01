using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A chunk of wall knocked loose by the mace: it arcs out over the hall
    /// and crashes down on a spot marked by a warning circle, hurting the
    /// player if they are still on it.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. A <see cref="WarningCircle"/> appears where it will land and
    ///      fills as it falls. The chunk flies from where it broke off to
    ///      there over its flight time, tumbling, peaking
    ///      <see cref="Settings.arcHeight"/> above the straight line.
    ///   2. It lands: the player within <see cref="Settings.damageRadius"/>
    ///      of the spot, measured flat, takes <see cref="Settings.damage"/>; a
    ///      <see cref="HammerImpact"/> bursts out of the floor and the camera
    ///      jolts.
    ///   3. It lies there for <see cref="Settings.restTime"/>, then shrinks
    ///      away over <see cref="Settings.shrinkTime"/> and is gone.
    ///
    /// During a run (<see cref="ChaseFrame"/>) the flight, the landing spot
    /// and its circle all ride along with the run until it lands, so it
    /// comes down where it was marked relative to the running player.
    ///
    /// Nothing can cut or stop it: it is not a <see cref="PlayerProjectiles"/>
    /// entry and has no health. The art is pure looks: a prefab, or a plain
    /// cube without one. Chunks are copied from its <see cref="ArtTemplate"/>,
    /// so scripts and colliders on it are dropped and a ProBuilder model is
    /// not rebuilt for every chunk. <see cref="Landed"/> tells whoever threw
    /// it where it came down.
    /// </remarks>
    public sealed class MaceDebris : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Look")]
            [Tooltip("The chunk's art. Empty uses a plain cube in Colour.")]
            public GameObject art;

            [Tooltip("Size of the chunk: the cube's width, or a multiple of the art's own scale.")]
            [Min(0.01f)] public float size = 0.7f;

            [Tooltip("Colour of the cube, when there is no art.")]
            public Color colour = new Color(0.85f, 0.85f, 0.85f);

            [Tooltip("Degrees per second the chunk tumbles in flight.")]
            public float tumbleSpeed = 540f;

            [Header("Flight")]
            [Tooltip("Shortest and longest seconds a chunk is in the air, picked at random for each.")]
            public Vector2 flightTime = new Vector2(0.9f, 1.4f);

            [Tooltip("How far the arc rises above the straight line from the wall to the landing spot, at its peak.")]
            [Min(0f)] public float arcHeight = 6f;

            [Header("Landing")]
            [Tooltip("Damage to the player standing within the damage radius when it lands.")]
            [Min(0f)] public float damage = 15f;

            [Tooltip("How close, measured flat, the player has to be to the landing spot to be hit. Also the warning circle's size.")]
            [Min(0f)] public float damageRadius = 1.3f;

            [Tooltip("Colour of the warning circle.")]
            public Color warningColour = new Color(1f, 0.25f, 0.2f);

            [Tooltip("Width of the warning circle's outline.")]
            [Min(0f)] public float warningOutline = 0.08f;

            [Tooltip("Height above the floor the warning circle is drawn at. Raise it to clear a rug or carpet.")]
            [Min(0f)] public float warningHeight = 0.02f;

            [Tooltip("The burst out of the floor where it lands. Ring Radius and Debris Count both 0 for none.")]
            public HammerImpact.Settings landingBurst = new HammerImpact.Settings
            {
                ringRadius = 1.8f,
                ringWidth = 0.4f,
                ringTime = 0.3f,
                debrisCount = 4,
                debrisSize = 0.25f,
                debrisUpSpeed = new Vector2(4f, 7f),
                debrisOutSpeed = new Vector2(2f, 4f),
                debrisTime = 0.5f,
            };

            [Tooltip("How far each landing jolts the camera. 0 for none.")]
            [Min(0f)] public float shakeStrength = 0.2f;

            [Min(0.01f)] public float shakeDuration = 0.15f;

            [Header("After")]
            [Tooltip("Seconds the chunk lies where it landed.")]
            [Min(0f)] public float restTime = 0.6f;

            [Tooltip("Seconds it takes to shrink away.")]
            [Min(0.01f)] public float shrinkTime = 0.3f;
        }

        private Settings settings;
        private PlayerHealth target;
        private Vector3 from;
        private Vector3 landing;
        private float flightTime;
        private float age;
        private bool landed;
        private Vector3 drift;
        private Vector3 baseScale;
        private Vector3 tumbleAxis;
        private WarningCircle warning;

        /// <summary>Raised once, as the chunk lands, with the spot on the floor it landed on.</summary>
        public event Action<Vector3> Landed;

        /// <summary>
        /// Throws a chunk from <paramref name="start"/> to land on
        /// <paramref name="floor"/>, a point on the floor, after
        /// <paramref name="seconds"/>. <paramref name="player"/> is hurt if it
        /// is standing there; null hurts no one.
        /// </summary>
        public static MaceDebris Throw(Vector3 start, Vector3 floor, float seconds, Settings settings, PlayerHealth player)
        {
            GameObject art;

            if (settings.art != null)
            {
                art = Instantiate(ArtTemplate.Of(settings.art), start, UnityEngine.Random.rotationUniform);
                art.transform.localScale = settings.art.transform.localScale * settings.size;
            }
            else
            {
                art = GameObject.CreatePrimitive(PrimitiveType.Cube);
                art.transform.SetPositionAndRotation(start, UnityEngine.Random.rotationUniform);
                art.transform.localScale = Vector3.one * settings.size;

                MeshRenderer cube = art.GetComponent<MeshRenderer>();
                cube.sharedMaterial = FlatMaterial.Shared;
                cube.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                FlatMaterial.Tint(cube, settings.colour, new MaterialPropertyBlock());
            }

            art.name = "MaceDebris";

            foreach (Collider part in art.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            MaceDebris debris = art.AddComponent<MaceDebris>();
            debris.Begin(start, floor, seconds, settings, player);
            return debris;
        }

        private void Begin(Vector3 start, Vector3 floor, float seconds, Settings debrisSettings, PlayerHealth player)
        {
            settings = debrisSettings;
            target = player;
            from = start;
            landing = floor;
            flightTime = Mathf.Max(0.05f, seconds);
            baseScale = transform.localScale;
            tumbleAxis = UnityEngine.Random.onUnitSphere;

            warning = WarningCircle.Create(floor + Vector3.up * settings.warningHeight, settings.damageRadius, settings.warningOutline,
                                           null, settings.warningColour);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (!landed)
            {
                // Carried along with a run until it comes down.
                drift += ChaseFrame.Step(deltaTime);

                float t = Mathf.Clamp01(age / flightTime);
                Vector3 position = Vector3.Lerp(from, landing, t) + Vector3.up * (settings.arcHeight * 4f * t * (1f - t)) + drift;

                transform.position = position;
                transform.Rotate(tumbleAxis, settings.tumbleSpeed * deltaTime, Space.World);

                if (warning != null)
                {
                    warning.transform.position = landing + drift + Vector3.up * 0.02f;
                    warning.Fill = t;
                }

                if (t >= 1f)
                {
                    Land();
                }

                return;
            }

            float after = age - flightTime - settings.restTime;

            if (after >= settings.shrinkTime)
            {
                Destroy(gameObject);
                return;
            }

            if (after > 0f)
            {
                transform.localScale = baseScale * (1f - after / settings.shrinkTime);
            }
        }

        private void Land()
        {
            landed = true;
            Vector3 spot = landing + drift;

            // Sits on the floor rather than half in it.
            transform.position = spot + Vector3.up * (settings.size * 0.4f);

            if (warning != null)
            {
                Destroy(warning.gameObject);
                warning = null;
            }

            if (target != null)
            {
                Vector3 offset = target.transform.position - spot;
                offset.y = 0f;

                if (offset.sqrMagnitude <= settings.damageRadius * settings.damageRadius)
                {
                    target.TakeDamage(settings.damage, spot);
                }
            }

            HammerImpact.Settings burst = settings.landingBurst;

            if (burst.ringRadius > 0f || burst.debrisCount > 0)
            {
                HammerImpact.Create(spot, burst, null);
            }

            TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
            Landed?.Invoke(spot);
        }

        private void OnDestroy()
        {
            if (warning != null)
            {
                Destroy(warning.gameObject);
            }
        }
    }
}
