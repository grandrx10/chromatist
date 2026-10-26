using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Turns an enemy's hammer into a projectile for as long as it is on:
    /// glowing, hurting the player in its reach, and parryable.
    /// </summary>
    /// <remarks>
    /// Added to the hammer at runtime by <see cref="HammerWhirlPattern"/> and
    /// destroyed when the attack ends, which puts the hammer's own look
    /// back. It also removes itself if its enemy dies or goes away, so a
    /// hammer never keeps hurting after the attack is cut short.
    ///
    /// The hitbox is not the hammer itself but the whole circle it sweeps:
    /// an upright cylinder round the enemy's pivot, from the bottom of its
    /// body to the top of whichever is taller, the body or the hammer. Its
    /// radius is the furthest the hammer has reached from the pivot so far
    /// this attack, plus a padding -- never less than the body's own width.
    /// It only ever grows, so the swing never feels smaller from one moment
    /// to the next as the animation moves the arm. A spin is so fast that
    /// the hammer is effectively everywhere in that circle at once, so this
    /// is fairer than chasing the hammer's exact pose, and needs no collider
    /// of its own.
    ///
    /// A hit, or a parry, stops the hammer hurting for
    /// <see cref="hitInterval"/> seconds, so a player inside the swing loses
    /// health once a second at most. Parrying has its own, separate wait of
    /// the same length -- one parry turns the boss away once, however many
    /// times the spinning hammer passes through the barrier while it is up --
    /// but a hit never stops a parry: a player just hit can still raise the
    /// barrier and turn the boss away.
    ///
    /// The barrier is checked before the player, so a parried swing never
    /// hurts, even when both are touched in the same frame. A barrier
    /// anywhere inside the circle catches it.
    ///
    /// The glow is a <see cref="HammerGlow"/>: the hammer's own materials
    /// tinted, or a glow material, if given, in their place.
    /// <see cref="GlowIntensity"/> brightens it, for a charge-up. Wind trails stream off the hammer at points along it
    /// from its middle outward, widest at the far end. When the hammer is
    /// switched off they are left behind to shrink away rather than vanish.
    /// </remarks>
    public sealed class WhirlingHammer : MonoBehaviour
    {
        [Serializable]
        public sealed class Trails
        {
            [Tooltip("Trails streaming off the hammer, spaced from its middle to its far end. 0 for none.")]
            [Min(0)] public int count = 3;

            [Tooltip("Width of the outermost trail. Inner ones are narrower.")]
            [Min(0.01f)] public float width = 0.8f;

            [Tooltip("Seconds of travel each trail covers. Longer is a longer tail.")]
            [Min(0.01f)] public float time = 0.25f;

            [Tooltip("Colour of the trails. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(1.5f, 1.5f, 1.5f);

            [Tooltip("Material for the trails. Empty uses NeoNoir/Flat in the colour above.")]
            public Material material;
        }

        private static readonly Collider[] overlaps = new Collider[16];

        private Enemy owner;
        private float damage;
        private float hitInterval;
        private float cooldown;
        private float parryCooldown;

        private Vector3 localCentre;
        private Vector3 localHalfSize = Vector3.one * 0.5f;

        private float padding;
        private float reach;
        private Collider body;

        private Vector3 lastPivot;
        private float zoneBottom;
        private float zoneTop;

        private HammerGlow glow;
        private float glowFadeOut;

        private readonly List<TrailRenderer> trails = new List<TrailRenderer>();

        /// <summary>
        /// Raised when a barrier catches the hammer, with the point on the
        /// rim of the swing nearest the player, and the velocity it came in at.
        /// </summary>
        public event Action<Vector3, Vector3> Parried;

        /// <summary>Multiplies the glow's brightness. 1 is as armed.</summary>
        public float GlowIntensity
        {
            get => glow != null ? glow.GlowIntensity : 1f;
            set
            {
                if (glow != null)
                {
                    glow.GlowIntensity = value;
                }
            }
        }

        /// <summary>
        /// Switches the hammer on. With a <paramref name="material"/>, it
        /// replaces every material on the hammer and <paramref name="colour"/>
        /// is ignored; without one, the hammer's own materials are tinted
        /// <paramref name="colour"/>. Either way it lasts until this is
        /// destroyed. <paramref name="trailSettings"/> may be null for no
        /// trails. <paramref name="reachPadding"/> is added to the hitbox's
        /// radius beyond the hammer's reach.
        /// </summary>
        public void Arm(Enemy enemy, float hitDamage, float interval, float reachPadding,
                        Material material, Color colour, Trails trailSettings,
                        float fadeIn = 0f, float fadeOut = 0f)
        {
            owner = enemy;
            damage = hitDamage;
            hitInterval = Mathf.Max(0f, interval);
            padding = Mathf.Max(0f, reachPadding);
            cooldown = 0f;
            parryCooldown = 0f;

            body = owner.GetComponent<Collider>();
            reach = body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.z) : 0f;

            MeasureLocalBounds();
            glow = HammerGlow.On(gameObject, material, colour, fadeIn);
            glowFadeOut = fadeOut;

            if (trailSettings != null)
            {
                AddTrails(trailSettings);
            }

            lastPivot = owner.Model.position;
            MeasureZone(lastPivot);
        }

        /// <summary>Radius of the hitbox: the furthest reach so far, plus the padding.</summary>
        public float HitRadius => reach + padding;

        /// <summary>
        /// Grows the reach to the hammer's furthest corner from
        /// <paramref name="pivot"/> this frame, flat, and sets the hitbox's
        /// height to cover both the body and the hammer.
        /// </summary>
        private void MeasureZone(Vector3 pivot)
        {
            float top = float.MinValue;
            float bottom = float.MaxValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = transform.TransformPoint(localCentre + Vector3.Scale(localHalfSize,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));

                Vector3 flat = corner - pivot;
                flat.y = 0f;
                reach = Mathf.Max(reach, flat.magnitude);

                top = Mathf.Max(top, corner.y);
                bottom = Mathf.Min(bottom, corner.y);
            }

            if (body != null)
            {
                Bounds bounds = body.bounds;
                top = Mathf.Max(top, bounds.max.y);
                bottom = Mathf.Min(bottom, bounds.min.y);
            }

            zoneTop = top;
            zoneBottom = bottom;
        }

        /// <summary>The hammer's meshes, combined, in this object's local space.</summary>
        private void MeasureLocalBounds()
        {
            bool found = false;
            Bounds combined = default;

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;

                if (mesh == null)
                {
                    continue;
                }

                Bounds meshBounds = mesh.bounds;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));

                    if (!found)
                    {
                        combined = new Bounds(local, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        combined.Encapsulate(local);
                    }
                }
            }

            if (found)
            {
                localCentre = combined.center;
                localHalfSize = combined.extents;
            }
        }

        /// <summary>
        /// Trails at points along the hammer from its middle out to its far
        /// end -- the end furthest from the enemy -- each wider than the last.
        /// </summary>
        private void AddTrails(Trails settings)
        {
            if (settings.count <= 0)
            {
                return;
            }

            // The hammer's long axis, pointed away from the enemy.
            Vector3 axis = Vector3.right;
            float longest = localHalfSize.x;

            if (localHalfSize.y > longest)
            {
                axis = Vector3.up;
                longest = localHalfSize.y;
            }

            if (localHalfSize.z > longest)
            {
                axis = Vector3.forward;
                longest = localHalfSize.z;
            }

            Vector3 towardEnemy = transform.InverseTransformPoint(owner.Model.position) - localCentre;
            if (Vector3.Dot(towardEnemy, axis) > 0f)
            {
                axis = -axis;
            }

            Material material = settings.material != null ? settings.material : FlatMaterial.DoubleSided;
            MaterialPropertyBlock trailBlock = new MaterialPropertyBlock();

            for (int i = 0; i < settings.count; i++)
            {
                float along = settings.count > 1 ? i / (settings.count - 1f) : 1f;

                GameObject point = new GameObject("WindTrail");
                point.transform.SetParent(transform, false);
                point.transform.localPosition = localCentre + axis * (longest * along);

                TrailRenderer trail = point.AddComponent<TrailRenderer>();
                trail.time = settings.time;
                trail.widthMultiplier = settings.width * Mathf.Lerp(0.35f, 1f, along);
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.minVertexDistance = 0.05f;
                trail.numCapVertices = 0;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.sharedMaterial = material;

                if (settings.material == null && material != null)
                {
                    FlatMaterial.Tint(trail, settings.colour, trailBlock);
                }

                trails.Add(trail);
            }
        }

        private void LateUpdate()
        {
            // After the animator has posed the arm this frame.
            if (owner == null || !owner.isActiveAndEnabled || owner.Health.IsDead)
            {
                Destroy(this);
                return;
            }

            float deltaTime = Time.deltaTime;
            cooldown -= deltaTime;
            parryCooldown -= deltaTime;

            Vector3 pivot = owner.Model.position;
            MeasureZone(pivot);

            Strike(pivot, deltaTime);

            lastPivot = pivot;
        }

        /// <summary>Parries, or hurts the player, if either is inside the hitbox this frame.</summary>
        private void Strike(Vector3 pivot, float deltaTime)
        {
            float radius = HitRadius;

            // At the height of the player's body, where a barrier sits.
            float height = owner.HasTarget ? Mathf.Clamp(owner.TargetPoint.y, zoneBottom, zoneTop) : pivot.y;
            Vector3 from = new Vector3(lastPivot.x, height, lastPivot.z);
            Vector3 to = new Vector3(pivot.x, height, pivot.z);

            Vector3 toward = owner.HasTarget ? owner.TargetPoint - to : owner.Model.forward;
            toward.y = 0f;
            Vector3 velocity = toward.sqrMagnitude > 0.0001f ? toward.normalized * 10f : Vector3.zero;

            if (parryCooldown <= 0f && ParryBarrier.TryCatch(from, to, radius, velocity))
            {
                parryCooldown = hitInterval;
                cooldown = hitInterval;

                Vector3 rim = velocity.sqrMagnitude > 0.0001f ? to + velocity.normalized * radius : to;
                Parried?.Invoke(rim, velocity);
                return;
            }

            if (cooldown > 0f)
            {
                return;
            }

            // A square column first, then each candidate's nearest point
            // checked against the circle, so the corners do not count.
            float halfHeight = Mathf.Max(0.01f, (zoneTop - zoneBottom) * 0.5f);
            Vector3 centre = new Vector3(pivot.x, zoneBottom + halfHeight, pivot.z);

            int count = Physics.OverlapBoxNonAlloc(centre, new Vector3(radius, halfHeight, radius), overlaps,
                                                   Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int k = 0; k < count; k++)
            {
                PlayerHealth health = overlaps[k].GetComponentInParent<PlayerHealth>();

                if (health == null)
                {
                    continue;
                }

                Vector3 nearest = overlaps[k].ClosestPoint(centre);
                Vector3 flat = nearest - centre;
                flat.y = 0f;

                if (flat.sqrMagnitude > radius * radius)
                {
                    continue;
                }

                // The rim of the circle on the player's side, so the spray
                // flies away from the swing.
                Vector3 rim = flat.sqrMagnitude > 0.0001f ? centre + flat.normalized * radius : centre;
                health.TakeDamage(damage, centre + (rim - centre) * 0.5f);
                cooldown = hitInterval;
                return;
            }
        }

        private void OnDrawGizmos()
        {
            if (owner == null)
            {
                return;
            }

            Gizmos.color = cooldown > 0f ? new Color(1f, 1f, 1f, 0.4f) : new Color(1f, 0.1f, 0.1f, 0.9f);

            Vector3 pivot = owner.Model.position;
            float radius = HitRadius;
            const int segments = 32;

            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float b = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Vector3 q = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius;

                Gizmos.DrawLine(new Vector3(pivot.x, zoneBottom, pivot.z) + p, new Vector3(pivot.x, zoneBottom, pivot.z) + q);
                Gizmos.DrawLine(new Vector3(pivot.x, zoneTop, pivot.z) + p, new Vector3(pivot.x, zoneTop, pivot.z) + q);
            }
        }

        private void OnDestroy()
        {
            // Left in the world to shrink away; a trail's points are already
            // in world space, so it stays where it was drawn.
            foreach (TrailRenderer trail in trails)
            {
                if (trail != null)
                {
                    trail.transform.SetParent(null, true);
                    trail.emitting = false;
                    Destroy(trail.gameObject, trail.time);
                }
            }

            if (glow != null)
            {
                glow.FadeOut(glowFadeOut);
            }
        }
    }
}
