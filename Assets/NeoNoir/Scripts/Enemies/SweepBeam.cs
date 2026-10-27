using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A laser held on for as long as whoever fired it wants, steered every
    /// frame, burning the player for as long as they stand in it.
    /// </summary>
    /// <remarks>
    /// Looks like a <see cref="LightningBeam"/> -- a glowing core with
    /// rounded ends, crackling with black lightning -- but does not fade by
    /// itself: it swells to full width over <see cref="Settings.growTime"/>,
    /// stays until <see cref="Stop"/>, then narrows away over
    /// <see cref="Settings.shrinkTime"/> and is gone.
    ///
    /// Whoever fired it moves it with <see cref="Aim"/>, every frame: left
    /// unaimed for a quarter of a second, as when the attack firing it is cut
    /// short, it stops by itself.
    /// It runs <see cref="Settings.length"/> out, or to the first wall on
    /// <see cref="Settings.wallMask"/>, found again every frame, with sparks
    /// thrown where it hits the wall.
    ///
    /// The player in it takes <see cref="Settings.damagePerSecond"/>, dealt
    /// in small hits every <see cref="Settings.tickInterval"/> seconds, the
    /// first the moment they step in. It cannot be parried: a
    /// <see cref="ParryBarrier"/> is ignored. Colliders belonging to whoever
    /// fired it are ignored too.
    ///
    /// Spawned at runtime and built from primitives, so it needs no prefab.
    /// </remarks>
    public sealed class SweepBeam : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Shape")]
            [Tooltip("Furthest the beam reaches, in units. Walls cut it short.")]
            [Min(0.5f)] public float length = 40f;

            [Tooltip("Radius at full width. The beam is twice this across.")]
            [Min(0.01f)] public float radius = 1f;

            [Tooltip("What cuts the beam short. Leave the Enemy and Player layers out.")]
            public LayerMask wallMask = 1;

            [Header("Timing")]
            [Tooltip("Seconds it takes to shoot out to its full length and swell to full width.")]
            [Min(0f)] public float growTime = 0.25f;

            [Tooltip("Seconds it takes to narrow to nothing once stopped.")]
            [Min(0f)] public float shrinkTime = 0.3f;

            [Header("Damage")]
            [Tooltip("Damage a second to the player standing in it.")]
            [Min(0f)] public float damagePerSecond = 10f;

            [Tooltip("Seconds between the small hits the damage is dealt in.")]
            [Min(0.02f)] public float tickInterval = 0.25f;

            [Tooltip("What the beam can hit. Leave the Enemy layer out.")]
            public LayerMask hitMask = ~0;

            [Header("Look")]
            [Tooltip("Colour of the beam. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Burst at the origin as it fires.")]
            public EnergyBurst.Settings flash = new EnergyBurst.Settings
            {
                flashSize = 2.5f,
                flashTime = 0.2f,
                sparkCount = 12,
                sparkSize = 0.35f,
                sparkSpeed = new Vector2(4f, 9f),
                sparkLife = 0.35f,
            };

            [Tooltip("Sparks where a wall cuts it short, thrown again every Wall Spark Interval.")]
            public EnergyBurst.Settings wallSparks = new EnergyBurst.Settings
            {
                flashSize = 1.2f,
                flashTime = 0.1f,
                sparkCount = 4,
                sparkSize = 0.3f,
                sparkSpeed = new Vector2(3f, 7f),
                sparkLife = 0.3f,
            };

            [Tooltip("Seconds between one throw of wall sparks and the next.")]
            [Min(0.02f)] public float wallSparkInterval = 0.1f;

            [Header("Lightning")]
            [Tooltip("Bolts crackling round the beam. 0 draws none.")]
            [Range(0, 12)] public int boltCount = 5;

            [Tooltip("Colour of the bolts.")]
            [ColorUsage(false, true)] public Color boltColour = Color.black;

            [Tooltip("Width of each bolt's line at full beam width.")]
            [Min(0.005f)] public float boltWidth = 0.12f;

            [Tooltip("Nearest and furthest a bolt strays from the beam's middle, as a multiple of its radius.")]
            public Vector2 boltReach = new Vector2(0.9f, 1.6f);

            [Tooltip("Length of each straight piece of a bolt, in units. Shorter is more jagged.")]
            [Min(0.1f)] public float boltStep = 0.8f;

            [Tooltip("Seconds between one shape of the bolts and the next.")]
            [Min(0.01f)] public float flicker = 0.04f;
        }

        // Stops by itself once nothing has aimed it for this long: whatever fired it was interrupted.
        private const float AbandonedAfter = 0.25f;

        private static readonly Collider[] touching = new Collider[16];
        private static readonly RaycastHit[] walls = new RaycastHit[16];

        private readonly List<LineRenderer> bolts = new List<LineRenderer>();
        private readonly List<float> boltAngles = new List<float>();

        private Settings settings;
        private Transform owner;
        private Transform core;
        private Transform startCap;
        private Transform endCap;
        private Vector3 origin;
        private Vector3 direction;
        private float age;
        private float stoppedAge = -1f;
        private float flickerAge;
        private float tickWait;
        private float sparkWait;
        private float sinceAimed;

        /// <summary>
        /// Fires a beam from <paramref name="from"/> along
        /// <paramref name="aim"/>. <paramref name="firedBy"/>'s colliders are
        /// passed through.
        /// </summary>
        public static SweepBeam Fire(Vector3 from, Vector3 aim, Transform firedBy, Settings settings)
        {
            GameObject host = new GameObject("SweepBeam");
            SweepBeam beam = host.AddComponent<SweepBeam>();
            beam.Begin(from, aim, firedBy, settings);
            return beam;
        }

        /// <summary>Moves the beam to start at <paramref name="from"/> and run along <paramref name="aim"/>.</summary>
        public void Aim(Vector3 from, Vector3 aim)
        {
            sinceAimed = 0f;
            origin = from;

            if (aim.sqrMagnitude > 0.000001f)
            {
                direction = aim.normalized;
            }

            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        }

        /// <summary>Narrows the beam away and destroys it. It does no harm meanwhile.</summary>
        public void Stop()
        {
            if (stoppedAge < 0f)
            {
                stoppedAge = 0f;
            }
        }

        private void Begin(Vector3 from, Vector3 aim, Transform firedBy, Settings beamSettings)
        {
            settings = beamSettings;
            owner = firedBy;
            direction = Vector3.forward;
            Aim(from, aim);

            EnergyBurst.Create(origin, settings.flash);
            BuildCore();

            for (int i = 0; i < settings.boltCount; i++)
            {
                BuildBolt();
                boltAngles.Add(360f * i / settings.boltCount);
            }

            Shape();
            LateUpdate();
        }

        private void BuildCore()
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(piece.GetComponent<Collider>());
            piece.name = "Core";

            // A cylinder runs along its local Y, two units long: tipped
            // forward and pushed out half way, it runs from the origin.
            core = piece.transform;
            core.SetParent(transform, false);
            core.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Paint(piece);

            startCap = BuildCap("StartCap");
            endCap = BuildCap("EndCap");
        }

        private Transform BuildCap(string capName)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(piece.GetComponent<Collider>());
            piece.name = capName;
            piece.transform.SetParent(transform, false);
            Paint(piece);
            return piece.transform;
        }

        private void Paint(GameObject piece)
        {
            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;
            FlatMaterial.Tint(renderer, settings.colour, new MaterialPropertyBlock());
        }

        private void BuildBolt()
        {
            GameObject host = new GameObject("Bolt");
            host.transform.SetParent(transform, false);

            LineRenderer bolt = host.AddComponent<LineRenderer>();
            bolt.useWorldSpace = false;
            bolt.positionCount = Mathf.Max(2, Mathf.CeilToInt(settings.length / settings.boltStep) + 1);
            bolt.numCapVertices = 0;
            bolt.numCornerVertices = 0;
            bolt.alignment = LineAlignment.View;
            bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bolt.receiveShadows = false;
            bolt.sharedMaterial = FlatMaterial.DoubleSided;

            if (bolt.sharedMaterial != null)
            {
                FlatMaterial.Tint(bolt, settings.boltColour, new MaterialPropertyBlock());
            }

            bolts.Add(bolt);
        }

        // After everything has moved this frame, the enemy's turn included.
        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;
            sinceAimed += deltaTime;

            if (sinceAimed > AbandonedAfter)
            {
                Stop();
            }

            float width;
            float extent = 1f;

            if (stoppedAge >= 0f)
            {
                stoppedAge += deltaTime;

                if (stoppedAge >= settings.shrinkTime)
                {
                    Destroy(gameObject);
                    return;
                }

                width = 1f - stoppedAge / Mathf.Max(0.0001f, settings.shrinkTime);
            }
            else if (age < settings.growTime)
            {
                float grown = age / settings.growTime;
                width = grown;

                // Out to full length a little ahead of full width, quick
                // at first and easing in to the end.
                float shot = Mathf.Clamp01(grown * 1.5f);
                extent = 1f - (1f - shot) * (1f - shot);
            }
            else
            {
                width = 1f;
            }

            width = Mathf.SmoothStep(0f, 1f, width);

            float length = WallDistance(out RaycastHit wall, out bool walled);
            float reach = length * extent;

            if (walled && extent >= 1f && stoppedAge < 0f)
            {
                sparkWait -= deltaTime;

                if (sparkWait <= 0f)
                {
                    sparkWait = settings.wallSparkInterval;
                    EnergyBurst.Create(wall.point, settings.wallSparks);
                }
            }

            float across = settings.radius * 2f * width;
            core.localScale = new Vector3(across, reach * 0.5f, across);
            core.localPosition = Vector3.forward * (reach * 0.5f);
            startCap.localScale = Vector3.one * across;
            endCap.localScale = Vector3.one * across;
            endCap.localPosition = Vector3.forward * reach;

            flickerAge += deltaTime;

            if (flickerAge >= settings.flicker)
            {
                flickerAge = 0f;
                Shape();
            }

            // The bolts are drawn the full length and squashed to the reach.
            float squash = reach / settings.length;

            foreach (LineRenderer bolt in bolts)
            {
                bolt.widthMultiplier = settings.boltWidth * width;
                bolt.transform.localScale = new Vector3(width, width, squash);
            }

            tickWait -= deltaTime;

            if (stoppedAge < 0f && tickWait <= 0f && Hurt(reach, settings.radius * width))
            {
                tickWait = settings.tickInterval;
            }
        }

        /// <summary>How far the beam runs before a wall, or its full length.</summary>
        private float WallDistance(out RaycastHit wall, out bool walled)
        {
            wall = default;
            walled = false;

            // Every hit, not just the first: the first is often the enemy firing it.
            int count = Physics.RaycastNonAlloc(origin, direction, walls, settings.length, settings.wallMask,
                                                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (owner != null && walls[i].collider.transform.IsChildOf(owner))
                {
                    continue;
                }

                if (!walled || walls[i].distance < wall.distance)
                {
                    wall = walls[i];
                    walled = true;
                }
            }

            return walled ? Mathf.Max(0.1f, wall.distance) : settings.length;
        }

        /// <summary>Draws every bolt again in a new jagged shape round the beam.</summary>
        private void Shape()
        {
            for (int b = 0; b < bolts.Count; b++)
            {
                LineRenderer bolt = bolts[b];
                int count = bolt.positionCount;

                // Each keeps to its own side of the beam, wandering round it.
                boltAngles[b] += UnityEngine.Random.Range(-40f, 40f);
                float angle = boltAngles[b];

                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / (count - 1);
                    angle += UnityEngine.Random.Range(-35f, 35f);

                    float reach = settings.radius * UnityEngine.Random.Range(settings.boltReach.x, settings.boltReach.y);
                    Vector3 around = Quaternion.AngleAxis(angle, Vector3.forward) * Vector3.up * reach;

                    bolt.SetPosition(i, around + Vector3.forward * (t * settings.length));
                }
            }
        }

        /// <summary>One small hit to the player in the beam. True if there was one.</summary>
        private bool Hurt(float reach, float radius)
        {
            if (reach <= 0f || radius <= 0f)
            {
                return false;
            }

            // An overlap rather than a cast, so a player standing right at
            // the origin, already inside the beam, is hit too.
            Vector3 end = origin + direction * reach;
            int count = Physics.OverlapCapsuleNonAlloc(origin, end, radius, touching,
                                                       settings.hitMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (owner != null && touching[i].transform.IsChildOf(owner))
                {
                    continue;
                }

                PlayerHealth health = touching[i].GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(settings.damagePerSecond * settings.tickInterval, origin);
                    return true;
                }
            }

            return false;
        }
    }
}
