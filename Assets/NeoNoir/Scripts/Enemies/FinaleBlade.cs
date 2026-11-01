using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of the swords of Luna's finale: drops into the floor far out, then
    /// is dragged round and in to a <see cref="WhiteHole"/> in a tightening
    /// spiral, laying a trail of its colour, and blows up on the player.
    /// </summary>
    /// <remarks>
    /// Appears <see cref="Settings.dropHeight"/> above its spot and falls,
    /// gathering speed, to stand tip down, sunk <see cref="Settings.sinkDepth"/>
    /// into the floor. From there it is dragged: in toward the hole's middle
    /// at <see cref="Settings.inwardSpeed"/>, rising to
    /// <see cref="Settings.inwardSpeedNearCentre"/> close in, and round it at
    /// <see cref="Settings.swirlSpeed"/>, so the nearer it gets, the faster it
    /// circles. The drag eases in over <see cref="Settings.dragEaseIn"/>.
    /// A trail of its colour lies on the floor behind it the whole way.
    ///
    /// The player who comes within <see cref="Settings.touchRadius"/> of it,
    /// once it has landed, blows it up: a burst in its colour, and
    /// <see cref="Settings.damage"/> to them. Reaching the white middle it
    /// is swallowed, shrinking away, harmless; before the hole opens, that is
    /// right at its middle. Gone, its trail is left to
    /// shrink away by itself. It is gone at once if the hole is.
    ///
    /// The model, if given, should stand upright, tip down, with NeoNoir/Flat
    /// materials so it takes the colour; its colliders are removed. Without
    /// one it is a plain thin slab.
    /// </remarks>
    public sealed class FinaleBlade : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Drop")]
            [Tooltip("How far above its spot it appears.")]
            [Min(0f)] public float dropHeight = 25f;

            [Tooltip("Seconds it takes to fall. It speeds up all the way down.")]
            [Min(0.01f)] public float dropTime = 0.6f;

            [Tooltip("How far its tip sinks into the floor.")]
            [Min(0f)] public float sinkDepth = 0.6f;

            [Header("Drag")]
            [Tooltip("Speed it is dragged in toward the middle, far out, in units per second.")]
            [Min(0f)] public float inwardSpeed = 2.5f;

            [Tooltip("Speed it is dragged in at once it nears the middle.")]
            [Min(0f)] public float inwardSpeedNearCentre = 8f;

            [Tooltip("Speed it is swept round the middle, in units per second. The same speed on a smaller circle, so it circles faster the nearer it gets.")]
            [Min(0f)] public float swirlSpeed = 7f;

            [Tooltip("Seconds the drag takes to build up once it lands.")]
            [Min(0f)] public float dragEaseIn = 1f;

            [Tooltip("Seconds it takes to shrink away once swallowed.")]
            [Min(0.01f)] public float swallowTime = 0.25f;

            [Header("Player")]
            [Tooltip("The player this close, measured flat, blows it up.")]
            [Min(0f)] public float touchRadius = 1.2f;

            [Tooltip("Damage to the player who blows it up.")]
            [Min(0f)] public float damage = 10f;

            [Tooltip("Radius of the burst.")]
            [Min(0f)] public float blastRadius = 3f;

            [Tooltip("The burst. Its colour is replaced by the blade's.")]
            public BlastSphere.Settings blast = new BlastSphere.Settings
            {
                growTime = 0.12f,
                fadeTime = 0.35f,
                outlineWidth = 0.2f,
                outlineColour = new Color(2f, 2f, 2f, 1f),
            };

            [Header("Look")]
            [Tooltip("Brightness of the blade's colour. Above 1 blooms.")]
            [Min(0f)] public float intensity = 2.5f;

            [Tooltip("Seconds the trail behind it lasts.")]
            [Min(0.01f)] public float trailTime = 1.5f;

            [Tooltip("Width of the trail.")]
            [Min(0.01f)] public float trailWidth = 0.6f;

            [Tooltip("Height above the floor the trail lies at.")]
            [Range(0f, 0.5f)] public float trailHeight = 0.08f;
        }

        private enum State
        {
            Dropping,
            Dragged,
            Swallowed,
        }

        private Settings settings;
        private WhiteHole hole;
        private PlayerHealth player;
        private TrailRenderer trail;
        private Color colour;
        private Vector3 scale;
        private State state;
        private float floorHeight;
        private float standHeight;
        private float startDistance;
        private float distance;
        private float angle;
        private float spinWay;
        private float clock;

        /// <summary>
        /// Drops a blade of <paramref name="model"/>, in
        /// <paramref name="bladeColour"/>, <paramref name="spawnDistance"/>
        /// from <paramref name="into"/>'s middle at <paramref name="bearing"/>
        /// degrees, to be dragged in round it <paramref name="way"/> (1
        /// anticlockwise seen from above, -1 clockwise) and blow up on
        /// <paramref name="target"/>.
        /// </summary>
        public static FinaleBlade Drop(GameObject model, WhiteHole into, float bearing, float spawnDistance,
                                       Color bladeColour, float way, Transform target, Settings settings)
        {
            GameObject host;

            if (model != null)
            {
                host = Instantiate(model);
            }
            else
            {
                host = GameObject.CreatePrimitive(PrimitiveType.Cube);
                host.transform.localScale = new Vector3(0.3f, 3f, 0.08f);
                host.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial.Shared;
            }

            host.name = "FinaleBlade";

            foreach (Collider part in host.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
                Destroy(part);
            }

            FinaleBlade blade = host.AddComponent<FinaleBlade>();
            blade.Begin(into, bearing, spawnDistance, bladeColour, way, target, settings);
            return blade;
        }

        private void Begin(WhiteHole into, float bearing, float spawnDistance, Color bladeColour, float way,
                           Transform target, Settings bladeSettings)
        {
            settings = bladeSettings;
            hole = into;
            spinWay = way < 0f ? -1f : 1f;
            angle = bearing * Mathf.Deg2Rad;
            startDistance = Mathf.Max(0.01f, spawnDistance);
            distance = startDistance;
            floorHeight = hole.Centre.y;
            player = target != null ? target.GetComponentInParent<PlayerHealth>() : null;

            scale = transform.localScale;
            colour = bladeColour * settings.intensity;
            colour.a = 1f;

            MaterialPropertyBlock block = new MaterialPropertyBlock();

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (part is MeshRenderer || part is SkinnedMeshRenderer)
                {
                    FlatMaterial.Tint(part, colour, block);

                    // Dozens are out at once; drawing each into the shadow map too adds up.
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    part.receiveShadows = false;
                }
            }

            // Measured stood upright, as it stays.
            transform.rotation = Facing();
            standHeight = floorHeight + TipBelowPivot() - settings.sinkDepth;

            BuildTrail();
            Place(standHeight + settings.dropHeight);
        }

        private void BuildTrail()
        {
            GameObject host = new GameObject("FinaleBladeTrail");
            trail = host.AddComponent<TrailRenderer>();
            trail.time = settings.trailTime;
            trail.widthMultiplier = settings.trailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            // Coarse: the spiral is gentle, and every point is rebuilt every frame.
            trail.minVertexDistance = 0.35f;
            trail.numCapVertices = 0;

            // Laid flat on the floor: the strip faces along the object's Z.
            trail.alignment = LineAlignment.TransformZ;
            host.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;

            if (trail.sharedMaterial != null)
            {
                FlatMaterial.Tint(trail, colour, new MaterialPropertyBlock());
            }

            trail.emitting = false;
        }

        private void Update()
        {
            if (hole == null)
            {
                Remove();
                return;
            }

            float deltaTime = Time.deltaTime;
            clock += deltaTime;

            switch (state)
            {
                case State.Dropping:
                {
                    float t = Mathf.Clamp01(clock / settings.dropTime);
                    Place(standHeight + settings.dropHeight * (1f - t * t));

                    if (t >= 1f)
                    {
                        state = State.Dragged;
                        clock = 0f;
                        trail.Clear();
                        trail.emitting = true;
                    }

                    break;
                }

                case State.Dragged:
                {
                    Drag(deltaTime, Mathf.Clamp01(settings.dragEaseIn > 0f ? clock / settings.dragEaseIn : 1f));

                    if (Touching())
                    {
                        Explode();
                        return;
                    }

                    if (distance <= Mathf.Max(hole.CoreRadius, 0.3f))
                    {
                        state = State.Swallowed;
                        clock = 0f;
                    }

                    break;
                }

                case State.Swallowed:
                {
                    Drag(deltaTime, 1f);

                    float t = Mathf.Clamp01(clock / settings.swallowTime);
                    transform.localScale = scale * (1f - t);

                    if (t >= 1f)
                    {
                        Remove();
                        return;
                    }

                    break;
                }
            }
        }

        private void LateUpdate()
        {
            if (trail != null)
            {
                Vector3 under = transform.position;
                under.y = floorHeight + settings.trailHeight;
                trail.transform.position = under;
            }
        }

        /// <summary>One step of the spiral in, the drag at <paramref name="strength"/> of its full pull.</summary>
        private void Drag(float deltaTime, float strength)
        {
            float far = Mathf.Clamp01(distance / startDistance);
            float inward = Mathf.Lerp(settings.inwardSpeedNearCentre, settings.inwardSpeed, far);

            distance = Mathf.Max(0f, distance - inward * strength * deltaTime);
            angle += spinWay * settings.swirlSpeed * strength * deltaTime / Mathf.Max(distance, 0.5f);

            Place(standHeight);
        }

        private void Place(float height)
        {
            Vector3 point = hole.Centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            point.y = height;
            transform.SetPositionAndRotation(point, Facing());
        }

        /// <summary>Upright, its flat toward the middle.</summary>
        private Quaternion Facing()
        {
            Vector3 inward = -new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            return Quaternion.LookRotation(inward, Vector3.up);
        }

        private bool Touching()
        {
            if (!hole.Hurts || player == null || player.IsDead)
            {
                return false;
            }

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= settings.touchRadius * settings.touchRadius;
        }

        private void Explode()
        {
            Vector3 middle = transform.position;
            middle.y = floorHeight + 1f;

            if (settings.blastRadius > 0f)
            {
                BlastSphere.Settings blast = settings.blast;
                BlastSphere.Create(middle, settings.blastRadius, new BlastSphere.Settings
                {
                    growTime = blast.growTime,
                    fadeTime = blast.fadeTime,
                    colour = colour,
                    outlineWidth = blast.outlineWidth,
                    outlineColour = blast.outlineColour,
                    material = blast.material,
                });
            }

            if (settings.damage > 0f)
            {
                player.TakeDamage(settings.damage, middle);
            }

            Remove();
        }

        /// <summary>Goes, leaving its trail to shrink away.</summary>
        private void Remove()
        {
            if (trail != null)
            {
                trail.emitting = false;
                trail.autodestruct = true;

                // Never laid, nothing to shrink.
                if (trail.positionCount == 0)
                {
                    Destroy(trail.gameObject);
                }

                trail = null;
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (trail != null)
            {
                Destroy(trail.gameObject);
            }
        }

        /// <summary>How far its art reaches below its pivot, as it stands now.</summary>
        private float TipBelowPivot()
        {
            float lowest = float.MaxValue;

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (part.enabled && (part is MeshRenderer || part is SkinnedMeshRenderer))
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            return lowest == float.MaxValue ? 0f : Mathf.Max(0f, transform.position.y - lowest);
        }
    }
}
