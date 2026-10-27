using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Keeps something walking on the floor from stepping off its edge.
    /// </summary>
    /// <remarks>
    /// Looks for floor just past the body in the direction of travel, with a
    /// ray dropped from well above the feet, so a stair rising ahead is found
    /// from above rather than missed from inside. Floor counts if it faces
    /// upward and lies within a step up or down of the feet, so stairs and
    /// ramps pass and a drop into the void does not. Anything moved by
    /// physics -- the player, other enemies, debris -- is not floor.
    /// </remarks>
    public static class FloorProbe
    {
        /// <summary>Directions tried, either side, when straight ahead would leave the floor.</summary>
        private static readonly float[] SlideAngles = { 15f, 30f, 45f, 60f, 75f, 90f };

        /// <summary>How far above the feet each ray starts.</summary>
        private const float RayHeight = 3f;

        /// <summary>Shared by every probe, so a cast allocates nothing.</summary>
        private static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// Returns <paramref name="velocity"/> unchanged if following it keeps
        /// <paramref name="self"/> on the floor, otherwise bent along the edge,
        /// slowed by how far it had to turn -- so it slides around a curved
        /// edge instead of sticking -- or zero if every way leads off. Does
        /// nothing while <paramref name="self"/> is not on the floor at all.
        /// </summary>
        public static Vector3 KeepOnFloor(Transform self, Vector3 feet, float radius, Vector3 velocity, float deltaTime,
                                          float maxStepUp, float maxStepDown, LayerMask mask)
        {
            velocity.y = 0f;
            float speed = velocity.magnitude;

            if (speed < 0.0001f)
            {
                return velocity;
            }

            // Launched, hopping, or spawned in the air: leave it to physics
            // rather than freezing it in mid-air.
            if (!HasFloor(self, feet, maxStepUp, maxStepDown, mask))
            {
                return velocity;
            }

            Vector3 direction = velocity / speed;
            float reach = radius + speed * deltaTime;

            if (HasFloor(self, feet + direction * reach, maxStepUp, maxStepDown, mask))
            {
                return velocity;
            }

            foreach (float angle in SlideAngles)
            {
                for (float sign = 1f; sign >= -1f; sign -= 2f)
                {
                    Vector3 slide = Quaternion.AngleAxis(angle * sign, Vector3.up) * direction;

                    if (HasFloor(self, feet + slide * reach, maxStepUp, maxStepDown, mask))
                    {
                        return slide * (speed * Mathf.Cos(angle * Mathf.Deg2Rad));
                    }
                }
            }

            return Vector3.zero;
        }

        /// <summary>Whether there is floor under <paramref name="point"/>, within a step up or down of its height.</summary>
        public static bool HasFloor(Transform self, Vector3 point, float maxStepUp, float maxStepDown, LayerMask mask)
        {
            Vector3 origin = point + Vector3.up * RayHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, RayHeight + maxStepDown,
                                                mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];

                if (hit.normal.y < 0.5f || hit.point.y > point.y + maxStepUp)
                {
                    continue;
                }

                if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                {
                    continue;
                }

                if (hit.collider.transform.IsChildOf(self))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
