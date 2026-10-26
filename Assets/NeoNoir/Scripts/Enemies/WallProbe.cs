using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Finds walls ahead of a moving enemy, for attacks that drive it
    /// themselves and need to know when it runs into something.
    /// </summary>
    /// <remarks>
    /// A sphere cast at a fixed height above the enemy's pivot, reaching a
    /// whole radius beyond the step asked for, so a wall is seen before the
    /// body is pressed flat against it. Anything facing upward counts as
    /// floor and is skipped, as are the enemy's own colliders and the
    /// player's.
    /// </remarks>
    public static class WallProbe
    {
        /// <summary>Shared by every probe, so a cast allocates nothing.</summary>
        private static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// Whether something solid that is not floor lies within
        /// <paramref name="step"/>, plus <paramref name="radius"/>, along
        /// <paramref name="direction"/>. <paramref name="contact"/> is where
        /// the nearest one was touched, and <paramref name="normal"/> is its
        /// flat surface normal, facing back toward the enemy.
        /// </summary>
        public static bool Cast(Enemy enemy, Vector3 direction, float step, float radius, float height,
                                LayerMask mask, out Vector3 contact, out Vector3 normal)
        {
            Vector3 origin = enemy.transform.position + Vector3.up * height;

            int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, step + radius,
                                                   mask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            contact = Vector3.zero;
            normal = -direction;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                Transform hitTransform = hit.collider.transform;

                if (hitTransform.IsChildOf(enemy.transform)
                    || (enemy.Target != null && hitTransform.IsChildOf(enemy.Target)))
                {
                    continue;
                }

                // Already overlapping at the start: Unity reports no point or
                // normal for those, so take the closest point on its bounds --
                // ClosestPoint itself refuses non-convex mesh colliders.
                bool overlapping = hit.distance <= 0f && hit.point == Vector3.zero;
                Vector3 point = overlapping ? hit.collider.ClosestPointOnBounds(origin) : hit.point;

                if (!overlapping && hit.normal.y > 0.7f)
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    contact = point;

                    Vector3 surface = overlapping ? origin - point : hit.normal;
                    surface.y = 0f;
                    normal = surface.sqrMagnitude > 0.0001f ? surface.normalized : -direction;
                }
            }

            return nearest < float.MaxValue;
        }
    }
}
