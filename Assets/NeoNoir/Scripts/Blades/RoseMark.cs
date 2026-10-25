using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// The arc Rose paints on the floor around an enemy while she is the main
    /// blade. Slashes that come in through the arc charge it; enough of them
    /// and it bursts into petals for heavy damage, then a new arc appears on
    /// another side.
    /// </summary>
    /// <remarks>
    /// The circle round the enemy is split into three fixed 120 degree
    /// sectors in world space, the first centred on world +Z. One is lit at a
    /// time. Fixed in the world rather than turning with the enemy, so the
    /// side to attack from never shifts under the player.
    ///
    /// A hit counts when the slash arrived from inside the lit sector: the
    /// point it was at the frame before it connected, seen from the enemy's
    /// centre, falls within the arc. Each slash hits an enemy at most once,
    /// so one slash is at most one charge. A burst plays a
    /// <see cref="SlashFlurry"/> on the enemy.
    ///
    /// Not parented to the enemy, only following it, so the enemy's own
    /// rotation and scale never bend the arc. Sits at the bottom of the
    /// enemy's collider, which is taken to be the floor.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab.
    /// </remarks>
    public sealed class RoseMark : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Hits through the lit arc needed to burst it.")]
            [Min(1)] public int hitsToBurst = 3;

            [Tooltip("Automatic slashes played on the enemy when the arc bursts, each dealing one slash's damage.")]
            public SlashFlurry.Settings flurry = new SlashFlurry.Settings();

            [Tooltip("Gap between the enemy's collider and the inside edge of the arc.")]
            [Min(0f)] public float gap = 0.15f;

            [Tooltip("Width of the arc's band.")]
            [Min(0.01f)] public float thickness = 0.35f;

            [Tooltip("Height above the bottom of the enemy's collider, taken as the floor, that the arc is drawn at.")]
            [Range(0f, 2f)] public float floorOffset = 0.4f;

            [Tooltip("Colour intensity with no hits landed. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float baseIntensity = 1f;

            [Tooltip("Intensity added for each hit landed.")]
            [Min(0f)] public float intensityPerHit = 0.75f;

            [Tooltip("Extra intensity flashed on each hit, fading over the flash time.")]
            [Min(0f)] public float hitFlash = 1.5f;

            [Min(0.01f)] public float flashTime = 0.15f;

            [Tooltip("Seconds after a burst before the next arc appears.")]
            [Min(0f)] public float rearmDelay = 0.5f;

            [Tooltip("Seconds a new arc takes to dissolve in.")]
            [Min(0f)] public float fadeInTime = 0.3f;

            [Tooltip("Petals released when the arc bursts. Spacing is measured along the arc.")]
            public RosePetal.Settings petals = new RosePetal.Settings
            {
                spacing = 0.15f,
                burstSpeed = 2f,
                carrySpeed = 3f,
            };
        }

        private const int Sectors = 3;
        private const float SectorArc = 360f / Sectors;
        private const int Segments = 24;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private EnemyHealth target;
        private Collider targetCollider;
        private Settings settings;
        private Material material;
        private Blade source;

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;

        private float innerRadius;
        private float outerRadius;

        private int sector = -1;
        private int hits;
        private bool armed;
        private float rearmRemaining;
        private float fade;
        private float flash;

        private float SectorYaw => sector * SectorArc;

        private float MiddleRadius => (innerRadius + outerRadius) * 0.5f;

        /// <summary>
        /// Paints a mark round <paramref name="enemy"/>. It removes itself when
        /// the enemy dies or is destroyed. <paramref name="source"/> is the
        /// blade that marked it, told if the mark's flurry kills.
        /// </summary>
        public static RoseMark Create(EnemyHealth enemy, Settings settings, Material material, Blade source = null)
        {
            GameObject host = new GameObject("RoseMark");
            RoseMark mark = host.AddComponent<RoseMark>();
            mark.Bind(enemy, settings, material, source);
            return mark;
        }

        private void Bind(EnemyHealth enemy, Settings markSettings, Material markMaterial, Blade markSource)
        {
            target = enemy;
            settings = markSettings;
            material = markMaterial;
            source = markSource;
            targetCollider = enemy.GetComponentInChildren<Collider>();

            // Sized to the collider once. Enemies do not change size.
            float reach = 0.5f;
            if (targetCollider != null)
            {
                Vector3 extents = targetCollider.bounds.extents;
                reach = Mathf.Max(extents.x, extents.z);
            }

            innerRadius = reach + settings.gap;
            outerRadius = innerRadius + settings.thickness;

            mesh = ArcMesh(innerRadius, outerRadius);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;

                if (material.HasProperty(BaseColorId))
                {
                    baseColour = material.GetColor(BaseColorId);
                }
            }

            block = new MaterialPropertyBlock();

            Follow();
            Arm();
            ApplyVisuals();
        }

        /// <summary>
        /// Tells the mark a slash arriving from <paramref name="arrivalPoint"/>
        /// hit its enemy. Returns true if it came in through the lit arc and
        /// counted. A burst slashes for <paramref name="slashDamage"/> a strike.
        /// </summary>
        public bool RegisterHit(Vector3 arrivalPoint, float slashDamage)
        {
            if (!armed || target == null || target.IsDead)
            {
                return false;
            }

            // Where the slash actually was, not where it was thrown from:
            // a curved slash can reach the enemy from well round the side.
            Vector3 from = arrivalPoint - transform.position;
            from.y = 0f;

            if (from.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            float yaw = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg;

            if (Mathf.Abs(Mathf.DeltaAngle(yaw, SectorYaw)) > SectorArc * 0.5f)
            {
                return false;
            }

            hits++;
            flash = 1f;

            if (hits >= settings.hitsToBurst)
            {
                Burst(slashDamage);
            }

            return true;
        }

        private void Arm()
        {
            // Any sector to start, then always a different one from the last.
            sector = sector < 0 ? Random.Range(0, Sectors) : (sector + Random.Range(1, Sectors)) % Sectors;
            hits = 0;
            flash = 0f;
            fade = settings.fadeInTime > 0f ? 0f : 1f;
            armed = true;

            transform.rotation = Quaternion.Euler(0f, SectorYaw, 0f);
        }

        private void Burst(float damagePerStrike)
        {
            armed = false;
            rearmRemaining = settings.rearmDelay;

            Vector3 centre = transform.position;
            float middle = MiddleRadius;

            // Even spacing along the arc, each petal flung outward from it.
            float length = middle * SectorArc * Mathf.Deg2Rad;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / settings.petals.spacing));

            for (int i = 0; i < count; i++)
            {
                float yaw = SectorYaw - SectorArc * 0.5f + SectorArc * (i + 0.5f) / count;
                Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                RosePetal.Spawn(centre + outward * middle + Vector3.up * 0.05f, outward, settings.petals, material);
            }

            ApplyVisuals();

            SlashFlurry.Play(target, damagePerStrike, settings.flurry, baseColour, source);
        }

        private void LateUpdate()
        {
            if (target == null || target.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            float deltaTime = Time.deltaTime;

            Follow();

            if (!armed)
            {
                rearmRemaining -= deltaTime;

                if (rearmRemaining <= 0f)
                {
                    Arm();
                }
            }

            if (settings.fadeInTime > 0f)
            {
                fade = Mathf.MoveTowards(fade, 1f, deltaTime / settings.fadeInTime);
            }

            flash = Mathf.MoveTowards(flash, 0f, deltaTime / settings.flashTime);

            ApplyVisuals();
        }

        private void Follow()
        {
            Bounds bounds = targetCollider != null
                ? targetCollider.bounds
                : new Bounds(target.transform.position, Vector3.zero);

            transform.position = new Vector3(bounds.center.x, bounds.min.y + settings.floorOffset, bounds.center.z);
        }

        private void ApplyVisuals()
        {
            meshRenderer.enabled = armed;

            if (!armed)
            {
                return;
            }

            float intensity = settings.baseIntensity
                            + settings.intensityPerHit * hits
                            + settings.hitFlash * flash;

            Color colour = baseColour * intensity;
            colour.a = baseColour.a;

            block.SetColor(BaseColorId, colour);
            block.SetFloat(FadeId, fade);
            meshRenderer.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        /// <summary>
        /// A flat band facing up, centred on local +Z and spanning one
        /// sector, between the two radii.
        /// </summary>
        private static Mesh ArcMesh(float inner, float outer)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int i = 0; i <= Segments; i++)
            {
                float yaw = (-SectorArc * 0.5f + SectorArc * i / Segments) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

                vertices.Add(direction * inner);
                vertices.Add(direction * outer);
            }

            // Clockwise seen from above, so the face points up.
            for (int i = 0; i < Segments; i++)
            {
                int innerA = i * 2;
                int outerA = innerA + 1;
                int innerB = innerA + 2;
                int outerB = innerA + 3;

                triangles.AddRange(new[] { innerA, outerA, innerB, outerA, outerB, innerB });
            }

            Mesh arc = new Mesh { name = "Rose mark" };
            arc.SetVertices(vertices);
            arc.SetTriangles(triangles, 0);
            arc.RecalculateNormals();
            arc.RecalculateBounds();
            return arc;
        }
    }
}
