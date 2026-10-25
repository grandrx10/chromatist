using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of Rose's travelling slashes: a line of light that curves out from
    /// beside the player, through the cursor and on past it, then dissipates.
    /// </summary>
    /// <remarks>
    /// The path is a circular arc, chosen so that it leaves in the launch
    /// direction and passes exactly through the target. For a start point, a
    /// heading and a target there is exactly one such circle: its curvature is
    /// 2 sin(phi) / distance, where phi is the angle between the heading and
    /// the line to the target. Launching off to one side therefore curves the
    /// slash back inward to cross the cursor, which is the whole shape.
    ///
    /// The curve always leads to the real cursor. A cursor beyond the range
    /// does not change the path, only where along it the slash dissipates.
    ///
    /// Past the target the curvature is relaxed by <see cref="Settings.afterCurl"/>,
    /// otherwise a wide launch would carry on round the circle and hook back
    /// toward the player.
    ///
    /// Spawned at runtime and builds its own trail, so it needs no prefab.
    /// </remarks>
    public sealed class RapierSlash : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Min(0f)] public float damage = 10f;

            [Tooltip("Total distance the slash travels before it dissipates.")]
            [Min(0.5f)] public float range = 7f;

            [Tooltip("Units per second.")]
            [Min(1f)] public float speed = 30f;

            [Tooltip("Cursor distance up to which a slash keeps its full launch angle. Beyond it the launch straightens in proportion, so a far cursor is headed for rather than curved wide of.")]
            [Min(0.1f)] public float fullCurveDistance = 4f;

            [Tooltip("Closest a cursor may be. Prevents a cursor on top of the player from curling the slash into a tight loop.")]
            [Min(0.1f)] public float minThroughDistance = 1.5f;

            [Tooltip("Curvature kept after crossing the cursor. 1 keeps curling on the same circle; 0 flies straight on.")]
            [Range(0f, 1f)] public float afterCurl = 0.35f;

            [Tooltip("Radius of the damage sweep.")]
            [Min(0.01f)] public float radius = 0.35f;

            [Tooltip("Thickness of the line of light.")]
            [Min(0.01f)] public float width = 0.22f;

            [Tooltip("Seconds the line lingers behind the head.")]
            [Min(0.01f)] public float trailTime = 0.12f;
        }

        // Longest straight piece the curve is integrated in. Small enough that
        // a slash crossing the screen in a frame still draws as a curve.
        private const float MaxStep = 0.1f;

        private readonly HashSet<EnemyHealth> hit = new HashSet<EnemyHealth>();

        private Settings settings;
        private Action<EnemyHealth, Vector3> onHit;
        private Color effectTint;
        private Vector3 position;
        private float heading;
        private float curvature;
        private float throughLength;
        private float travelled;
        private bool finished;

        /// <summary>
        /// Fires a slash from <paramref name="origin"/>, leaving along
        /// <paramref name="direction"/> and curving through <paramref name="target"/>.
        /// Only the horizontal parts of the direction and target are used; the
        /// slash travels level at the origin's height. <paramref name="onHit"/>,
        /// if given, hears about every enemy the slash hits and the point the
        /// slash arrived from: where it was the frame before it connected.
        /// <paramref name="effectTint"/> colours the slash effect on a hit.
        /// </summary>
        public static RapierSlash Spawn(Vector3 origin, Vector3 direction, Vector3 target,
                                        Settings settings, Material material,
                                        Action<EnemyHealth, Vector3> onHit = null,
                                        Color effectTint = default)
        {
            GameObject host = new GameObject("RapierSlash");

            // Positioned before the trail exists, so it does not draw a streak
            // in from the world origin on its first frame.
            host.transform.position = origin;

            RapierSlash slash = host.AddComponent<RapierSlash>();
            slash.onHit = onHit;
            slash.effectTint = effectTint;
            slash.Launch(origin, direction, target, settings, material);
            return slash;
        }

        private void Launch(Vector3 origin, Vector3 direction, Vector3 target, Settings launchSettings, Material material)
        {
            settings = launchSettings;
            position = origin;

            Vector2 forward = new Vector2(direction.x, direction.z);
            forward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;

            Vector2 toTarget = new Vector2(target.x - origin.x, target.z - origin.z);

            if (toTarget.sqrMagnitude < 0.000001f)
            {
                toTarget = forward;
            }

            if (toTarget.magnitude < settings.minThroughDistance)
            {
                toTarget = toTarget.normalized * settings.minThroughDistance;
            }

            // A far cursor with a wide launch is a huge circle, and the slash
            // only ever flies the start of it -- which runs off to the side.
            // Straighten the launch toward the cursor in proportion to how far
            // away it is, so whatever part of the curve is visible is visibly
            // heading at it.
            float straighten = Mathf.Min(1f, settings.fullCurveDistance / toTarget.magnitude);
            float offAxis = Vector2.SignedAngle(toTarget, forward) * straighten * Mathf.Deg2Rad;
            Vector2 axis = toTarget.normalized;
            forward = new Vector2(axis.x * Mathf.Cos(offAxis) - axis.y * Mathf.Sin(offAxis),
                                  axis.x * Mathf.Sin(offAxis) + axis.y * Mathf.Cos(offAxis));

            // Arc length to the target along the circle: chord * phi / sin(phi).
            // sin is floored so a target almost directly behind cannot divide
            // by zero.
            //
            // The target is never pulled in to fit the range. The curve is
            // always the one that leads to the real cursor; a cursor out of
            // range just means the slash dissipates on that curve before it
            // gets there, rather than bending off early through a nearer point.
            float phi = Vector2.Angle(forward, toTarget) * Mathf.Deg2Rad;
            float arc = phi > 0.001f
                ? toTarget.magnitude * phi / Mathf.Max(0.05f, Mathf.Sin(phi))
                : toTarget.magnitude;

            float cross = forward.x * toTarget.y - forward.y * toTarget.x;
            curvature = 2f * cross / toTarget.sqrMagnitude;
            throughLength = arc;
            heading = Mathf.Atan2(forward.y, forward.x);

            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = settings.trailTime;
            trail.widthMultiplier = settings.width;
            trail.widthCurve = PointedWidth(0f);
            trail.minVertexDistance = 0.05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            if (material != null)
            {
                trail.sharedMaterial = material;
            }
        }

        /// <summary>
        /// Trail width along its length, head (0) to tail (1): a hard point at
        /// the head, widening quickly to full, then narrowing to
        /// <paramref name="tailWidth"/>. Without the point at the head the
        /// line ends in a blunt or rounded blob where it stops.
        /// </summary>
        public static AnimationCurve PointedWidth(float tailWidth)
        {
            // Fraction of the trail spent sharpening the head to its point.
            const float headTaper = 0.15f;

            // Linear tangents into both points. The default flat tangents
            // ease the width toward zero, which reads as rounded.
            float headSlope = 1f / headTaper;
            float tailSlope = (tailWidth - 1f) / (1f - headTaper);

            return new AnimationCurve(
                new Keyframe(0f, 0f, headSlope, headSlope),
                new Keyframe(headTaper, 1f, 0f, 0f),
                new Keyframe(1f, tailWidth, tailSlope, tailSlope));
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            float step = Mathf.Min(settings.speed * Time.deltaTime, settings.range - travelled);
            Vector3 start = position;

            int pieces = Mathf.Max(1, Mathf.CeilToInt(step / MaxStep));
            float piece = step / pieces;

            for (int i = 0; i < pieces; i++)
            {
                float bend = travelled < throughLength ? curvature : curvature * settings.afterCurl;

                // Midpoint heading, so each straight piece sits on the curve
                // rather than cutting its corner.
                float middle = heading + bend * piece * 0.5f;
                position += new Vector3(Mathf.Cos(middle), 0f, Mathf.Sin(middle)) * piece;

                heading += bend * piece;
                travelled += piece;
            }

            transform.position = position;

            BladeDamage.Sweep(start, position, settings.radius, settings.damage, position - start, hit, onHit, effectTint);

            if (travelled >= settings.range - 0.0001f)
            {
                // Stop, but let the tail finish drawing in before going.
                finished = true;
                Destroy(gameObject, settings.trailTime + 0.05f);
            }
        }
    }
}
