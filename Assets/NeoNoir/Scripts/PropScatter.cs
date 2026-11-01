using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightAndShadow
{
    /// <summary>
    /// Scatters copies of a prefab across a rectangular area, no two closer
    /// than <see cref="minSpacing"/>, kept clear of a set of blockers (such
    /// as an arena's corner pillars).
    /// </summary>
    /// <remarks>
    /// The area is centred on this transform, <see cref="areaSize"/> wide on
    /// its local X and Z, and drawn as a gizmo when selected. Each copy is
    /// dropped onto the floor below with a raycast against
    /// <see cref="groundMask"/>, and spawned as a child of this object.
    ///
    /// Spots are picked by dart throwing: random points are tried, and any
    /// too close to one already taken, or to a blocker, are thrown away. If
    /// the area is too crowded to fit <see cref="count"/> it places as many
    /// as it can and warns.
    ///
    /// Blockers are kept clear two ways:
    ///   Footprint  No copy within <see cref="blockerClearance"/> of a
    ///              blocker's bounds on the floor plane.
    ///   Sightline  With <see cref="keepVisibleFromCamera"/> on, no copy
    ///              where the blocker would stand between it and the camera,
    ///              so none end up hidden behind one. Uses the camera's
    ///              forward as the view direction, which is exact for a
    ///              fixed-angle follow camera.
    /// Blocker bounds come from their colliders, or their renderers if they
    /// have none, so no physics is needed and both work in edit mode.
    ///
    /// Use "Scatter" from the component's context menu to lay copies down in
    /// the editor (undoable, re-rolls each time), or tick
    /// <see cref="scatterOnStart"/> for a fresh layout every time the scene
    /// loads.
    /// </remarks>
    public sealed class PropScatter : MonoBehaviour
    {
        [Tooltip("Prefab to scatter.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("How many copies to place.")]
        [Min(1)] [SerializeField] private int count = 30;

        [Tooltip("Smallest distance between any two copies, in metres.")]
        [Min(0f)] [SerializeField] private float minSpacing = 3f;

        [Tooltip("Width (X) and depth (Z) of the area, in metres, centred on this object.")]
        [SerializeField] private Vector2 areaSize = new Vector2(40f, 40f);

        [Tooltip("Random seed. 0 picks a new layout every time.")]
        [SerializeField] private int seed;

        [Tooltip("Lay the copies down when the scene starts, replacing any already under this object.")]
        [SerializeField] private bool scatterOnStart;

        [Header("Placement")]
        [Tooltip("Layers the copies are dropped onto. Spots with no floor below are skipped.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the area the floor raycast starts, in metres.")]
        [Min(0f)] [SerializeField] private float rayHeight = 20f;

        [Tooltip("Turn each copy to a random heading.")]
        [SerializeField] private bool randomYaw = true;

        [Tooltip("Most each copy is tipped off upright, in degrees.")]
        [Range(0f, 45f)] [SerializeField] private float maxTilt = 8f;

        [Tooltip("Each copy is scaled by a random amount in this range.")]
        [SerializeField] private Vector2 scaleRange = new Vector2(1f, 1f);

        [Tooltip("Raise or lower each copy so the bottom of its meshes rests on the floor, wherever the prefab's pivot is. Off puts the pivot on the floor.")]
        [SerializeField] private bool sitOnFloor = true;

        [Tooltip("With Sit On Floor, how far each copy is sunk into the floor, in metres, so an uneven base never floats.")]
        [SerializeField] private float sinkDepth = 0.05f;

        [Header("Blockers")]
        [Tooltip("Objects to keep copies away from, such as the corner pillars.")]
        [SerializeField] private Transform[] blockers;

        [Tooltip("Gap kept between a copy and a blocker's footprint, in metres.")]
        [Min(0f)] [SerializeField] private float blockerClearance = 2f;

        [Tooltip("Also skip spots where a blocker would hide the copy from the camera.")]
        [SerializeField] private bool keepVisibleFromCamera = true;

        [Tooltip("Camera to stay visible from. Defaults to the main camera.")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("Height of a copy, in metres. Its base and top must both be in view.")]
        [Min(0f)] [SerializeField] private float propHeight = 1.5f;

        // Dart throws allowed per copy before giving up on fitting the rest.
        private const int AttemptsPerCopy = 60;

        private void Start()
        {
            if (scatterOnStart)
            {
                Scatter();
            }
        }

        /// <summary>
        /// Clears any copies under this object and lays down a fresh set.
        /// </summary>
        [ContextMenu("Scatter")]
        public void Scatter()
        {
            if (prefab == null)
            {
                Debug.LogWarning($"{name}: no prefab to scatter.", this);
                return;
            }

            Clear();

            var random = seed == 0 ? new System.Random() : new System.Random(seed);
            var blockerBounds = GatherBlockerBounds(true);
            Vector3 viewDirection = ViewDirection();
            var taken = new List<Vector3>(count);
            float spacingSqr = minSpacing * minSpacing;
            int attempts = count * AttemptsPerCopy;

            for (int i = 0; i < attempts && taken.Count < count; i++)
            {
                var local = new Vector3(
                    ((float)random.NextDouble() - 0.5f) * areaSize.x,
                    0f,
                    ((float)random.NextDouble() - 0.5f) * areaSize.y);
                Vector3 top = transform.TransformPoint(local) + Vector3.up * rayHeight;

                if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, rayHeight * 2f, groundMask, QueryTriggerInteraction.Ignore)
                    || TooClose(hit.point, taken, spacingSqr)
                    || Blocked(hit.point, blockerBounds, viewDirection))
                {
                    continue;
                }

                taken.Add(hit.point);
                Place(hit.point, random);
            }

            if (taken.Count < count)
            {
                Debug.LogWarning($"{name}: only fit {taken.Count} of {count} copies. Lower the spacing or clearance, or widen the area.", this);
            }
        }

        /// <summary>Destroys every child of this object.</summary>
        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Undo.DestroyObjectImmediate(child);
                    continue;
                }
#endif
                Destroy(child);
            }
        }

        private void Place(Vector3 point, System.Random random)
        {
            float yaw = randomYaw ? (float)random.NextDouble() * 360f : 0f;
            float tiltHeading = (float)random.NextDouble() * 360f;
            float tilt = (float)random.NextDouble() * maxTilt;
            Quaternion rotation = Quaternion.AngleAxis(tiltHeading, Vector3.up)
                * Quaternion.AngleAxis(tilt, Vector3.right)
                * Quaternion.AngleAxis(yaw - tiltHeading, Vector3.up);
            float scale = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble());

            GameObject copy;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform);
                Undo.RegisterCreatedObjectUndo(copy, "Scatter " + prefab.name);
            }
            else
#endif
            {
                copy = Instantiate(prefab, transform);
            }

            copy.transform.SetPositionAndRotation(point, rotation);
            copy.transform.localScale = prefab.transform.localScale * scale;

            if (sitOnFloor)
            {
                SitOnFloor(copy, point.y);
            }
        }

        /// <summary>Moves <paramref name="copy"/> up or down so its lowest mesh point is at <paramref name="floor"/>, less the sink.</summary>
        private void SitOnFloor(GameObject copy, float floor)
        {
            bool found = false;
            float lowest = 0f;

            foreach (Renderer part in copy.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = part.bounds;

                if (bounds.size == Vector3.zero)
                {
                    continue;
                }

                if (!found || bounds.min.y < lowest)
                {
                    lowest = bounds.min.y;
                    found = true;
                }
            }

            if (found)
            {
                copy.transform.position += Vector3.up * (floor - sinkDepth - lowest);
            }
        }

        private static bool TooClose(Vector3 point, List<Vector3> taken, float spacingSqr)
        {
            foreach (Vector3 other in taken)
            {
                float dx = point.x - other.x;
                float dz = point.z - other.z;

                if (dx * dx + dz * dz < spacingSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private bool Blocked(Vector3 point, List<Bounds> blockerBounds, Vector3 viewDirection)
        {
            foreach (Bounds bounds in blockerBounds)
            {
                // Footprint: distance from the point to the bounds on the floor plane.
                float dx = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
                float dz = Mathf.Max(bounds.min.z - point.z, 0f, point.z - bounds.max.z);

                if (dx * dx + dz * dz < blockerClearance * blockerClearance)
                {
                    return true;
                }

                if (keepVisibleFromCamera && viewDirection != Vector3.zero
                    && (bounds.IntersectRay(new Ray(point, -viewDirection))
                        || bounds.IntersectRay(new Ray(point + Vector3.up * propHeight, -viewDirection))))
                {
                    return true;
                }
            }

            return false;
        }

        private List<Bounds> GatherBlockerBounds(bool warn)
        {
            var result = new List<Bounds>();

            if (blockers == null)
            {
                return result;
            }

            foreach (Transform blocker in blockers)
            {
                if (blocker == null)
                {
                    continue;
                }

                bool found = false;
                var bounds = new Bounds();

                foreach (Collider collider in blocker.GetComponentsInChildren<Collider>())
                {
                    if (collider.isTrigger)
                    {
                        continue;
                    }

                    Encapsulate(ref bounds, ref found, collider.bounds);
                }

                if (!found)
                {
                    foreach (Renderer renderer in blocker.GetComponentsInChildren<Renderer>())
                    {
                        Encapsulate(ref bounds, ref found, renderer.bounds);
                    }
                }

                if (found)
                {
                    result.Add(bounds);
                }
                else if (warn)
                {
                    Debug.LogWarning($"{name}: blocker {blocker.name} has no collider or renderer to measure.", blocker);
                }
            }

            return result;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool found, Bounds add)
        {
            if (found)
            {
                bounds.Encapsulate(add);
            }
            else
            {
                bounds = add;
                found = true;
            }
        }

        private Vector3 ViewDirection()
        {
            Camera camera = viewCamera != null ? viewCamera : Camera.main;
            return camera != null ? camera.transform.forward : Vector3.zero;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(areaSize.x, 0f, areaSize.y));
            Gizmos.matrix = Matrix4x4.identity;

            Gizmos.color = new Color(1f, 0.3f, 0.2f);

            foreach (Bounds bounds in GatherBlockerBounds(false))
            {
                Vector3 size = bounds.size + new Vector3(blockerClearance * 2f, 0f, blockerClearance * 2f);
                Gizmos.DrawWireCube(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), new Vector3(size.x, 0f, size.z));
            }
        }
    }
}
