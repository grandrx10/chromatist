using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A beam of light shot dead straight from a fist: shoots out and swells
    /// to full width, holds, and narrows away, crackling with black lightning.
    /// </summary>
    /// <remarks>
    /// Runs <see cref="Settings.length"/> out, or to the first wall on
    /// <see cref="Settings.wallMask"/>, whichever is nearer. Over
    /// <see cref="Settings.growTime"/> it shoots out from the fist to its
    /// full length while widening from nothing to
    /// <see cref="Settings.radius"/>, holds for
    /// <see cref="Settings.holdTime"/>, then narrows to nothing over
    /// <see cref="Settings.shrinkTime"/> and is gone. Both widenings are
    /// eased in and out, and both ends are rounded off with half-spheres,
    /// so nothing about it is sharp. It hurts only as far as it has reached.
    ///
    /// It hurts during its first <see cref="Settings.hurtTime"/> -- by
    /// default all of it -- and hits the player at most once, for <see cref="Settings.damage"/>. A
    /// guarding <see cref="ParryBarrier"/> across it in that time catches it
    /// instead, and then it hurts no one. Colliders belonging to whoever fired
    /// it are ignored.
    ///
    /// The lightning is a few jagged lines wrapped loosely round the beam,
    /// drawn again in new shapes every <see cref="Settings.flicker"/>
    /// seconds, and widening and narrowing with it. Black, which this game's
    /// two-tone look shows as cracks across the light.
    ///
    /// Spawned at runtime and built from primitives, so it needs no prefab.
    /// </remarks>
    public sealed class LightningBeam : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Shape")]
            [Tooltip("Furthest the beam reaches, in units. Walls cut it short.")]
            [Min(0.5f)] public float length = 30f;

            [Tooltip("Radius at full width. The beam is twice this across.")]
            [Min(0.01f)] public float radius = 0.3f;

            [Tooltip("What cuts the beam short. Leave the Enemy and Player layers out.")]
            public LayerMask wallMask = 1;

            [Header("Timing")]
            [Tooltip("Seconds it takes to shoot out to its full length and swell to full width.")]
            [Min(0f)] public float growTime = 0.15f;

            [Tooltip("Seconds it holds at full width.")]
            [Min(0f)] public float holdTime = 0.65f;

            [Tooltip("Seconds it takes to narrow to nothing.")]
            [Min(0f)] public float shrinkTime = 0.2f;

            [Header("Hits")]
            [Tooltip("Damage to the player it hits. It hits them once at most.")]
            [Min(0f)] public float damage = 20f;

            [Tooltip("Seconds from firing during which it can hurt. It still hurts the player only once.")]
            [Min(0f)] public float hurtTime = 1f;

            [Tooltip("What the beam can hit. Leave the Enemy layer out.")]
            public LayerMask hitMask = ~0;

            [Header("Look")]
            [Tooltip("Colour of the beam. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Burst at the fist as it fires.")]
            public EnergyBurst.Settings flash = new EnergyBurst.Settings
            {
                flashSize = 1.2f,
                flashTime = 0.15f,
                sparkCount = 8,
                sparkSize = 0.3f,
                sparkSpeed = new Vector2(4f, 8f),
                sparkLife = 0.3f,
            };

            [Tooltip("Burst where a wall cuts it short.")]
            public EnergyBurst.Settings wallBurst = new EnergyBurst.Settings
            {
                flashSize = 0.9f,
                flashTime = 0.15f,
                sparkCount = 8,
                sparkSize = 0.3f,
                sparkSpeed = new Vector2(3f, 7f),
                sparkLife = 0.3f,
            };

            [Header("Lightning")]
            [Tooltip("Bolts crackling round the beam. 0 draws none.")]
            [Range(0, 12)] public int boltCount = 4;

            [Tooltip("Colour of the bolts.")]
            [ColorUsage(false, true)] public Color boltColour = Color.black;

            [Tooltip("Width of each bolt's line at full beam width.")]
            [Min(0.005f)] public float boltWidth = 0.07f;

            [Tooltip("Nearest and furthest a bolt strays from the beam's middle, as a multiple of its radius.")]
            public Vector2 boltReach = new Vector2(0.9f, 2f);

            [Tooltip("Length of each straight piece of a bolt, in units. Shorter is more jagged.")]
            [Min(0.1f)] public float boltStep = 0.7f;

            [Tooltip("Seconds between one shape of the bolts and the next.")]
            [Min(0.01f)] public float flicker = 0.03f;
        }

        private static readonly Collider[] touching = new Collider[16];

        private readonly List<LineRenderer> bolts = new List<LineRenderer>();
        private readonly List<float> boltAngles = new List<float>();

        private Settings settings;
        private Transform owner;
        private Transform core;
        private Transform startCap;
        private Transform endCap;
        private Vector3 origin;
        private Vector3 direction;
        private float length;
        private float reach;
        private float age;
        private float flickerAge;
        private bool spent;

        /// <summary>
        /// Fires a beam from <paramref name="from"/> along
        /// <paramref name="aim"/>. <paramref name="firedBy"/>'s colliders are
        /// passed through.
        /// </summary>
        public static LightningBeam Fire(Vector3 from, Vector3 aim, Transform firedBy, Settings settings)
        {
            GameObject host = new GameObject("LightningBeam");
            LightningBeam beam = host.AddComponent<LightningBeam>();
            beam.Begin(from, aim, firedBy, settings);
            return beam;
        }

        private void Begin(Vector3 from, Vector3 aim, Transform firedBy, Settings beamSettings)
        {
            settings = beamSettings;
            owner = firedBy;
            origin = from;
            direction = aim.sqrMagnitude > 0.000001f ? aim.normalized : Vector3.forward;
            length = settings.length;

            if (Physics.Raycast(origin, direction, out RaycastHit wall, length, settings.wallMask,
                                QueryTriggerInteraction.Ignore)
                && (owner == null || !wall.collider.transform.IsChildOf(owner)))
            {
                length = Mathf.Max(0.1f, wall.distance);
                EnergyBurst.Create(wall.point, settings.wallBurst);
            }

            EnergyBurst.Create(origin, settings.flash);

            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            BuildCore();

            for (int i = 0; i < settings.boltCount; i++)
            {
                BuildBolt();
                boltAngles.Add(360f * i / settings.boltCount);
            }

            Shape();
            Update();
        }

        private void BuildCore()
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(piece.GetComponent<Collider>());
            piece.name = "Core";

            // A cylinder runs along its local Y, two units long: tipped
            // forward and pushed out half way, it runs from the fist.
            core = piece.transform;
            core.SetParent(transform, false);
            core.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Paint(piece);

            // Spheres as wide as the beam on each end: the half outside the
            // cylinder rounds it off.
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
            bolt.positionCount = Mathf.Max(2, Mathf.CeilToInt(length / settings.boltStep) + 1);
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

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            float open = settings.growTime + settings.holdTime;
            float total = open + settings.shrinkTime;

            if (age >= total)
            {
                Destroy(gameObject);
                return;
            }

            float width;
            float extent = 1f;

            if (age < settings.growTime)
            {
                float grown = age / settings.growTime;
                width = grown;

                // Out to full length a little ahead of full width, quick
                // at first and easing in to the end.
                float shot = Mathf.Clamp01(grown * 1.5f);
                extent = 1f - (1f - shot) * (1f - shot);
            }
            else if (age < open)
            {
                width = 1f;
            }
            else
            {
                width = 1f - (age - open) / Mathf.Max(0.0001f, settings.shrinkTime);
            }

            // Eased in and out, so it swells and narrows smoothly.
            width = Mathf.SmoothStep(0f, 1f, width);
            reach = length * extent;

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

            foreach (LineRenderer bolt in bolts)
            {
                bolt.widthMultiplier = settings.boltWidth * width;
                bolt.transform.localScale = new Vector3(width, width, extent);
            }

            if (!spent && age <= settings.hurtTime)
            {
                Hurt();
            }
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

                    bolt.SetPosition(i, around + Vector3.forward * (t * length));
                }
            }
        }

        private void Hurt()
        {
            Vector3 end = origin + direction * reach;

            if (ParryBarrier.TryCatch(origin, end, settings.radius, direction * 100f))
            {
                spent = true;
                return;
            }

            // An overlap rather than a cast, so a player standing right at
            // the fist, already inside the beam, is hit too.
            int count = Physics.OverlapCapsuleNonAlloc(origin, end, settings.radius, touching,
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
                    health.TakeDamage(settings.damage, origin);
                    spent = true;
                    return;
                }
            }
        }
    }
}
