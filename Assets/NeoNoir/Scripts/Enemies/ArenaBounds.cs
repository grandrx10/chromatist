using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A cube, or several, marking out a boss arena, so anything spawned
    /// into the fight lands somewhere valid: inside it, on floor, and clear
    /// of walls and bodies.
    /// </summary>
    /// <remarks>
    /// Goes on a cube, such as a ProBuilder one, covering the arena: its
    /// footprint (local X by local Z, scale and turn included) is the
    /// arena, and the floor is looked for between its top and bottom.
    /// Easiest is a box sitting on the floor, a few units tall. Keep it
    /// level; tilt is not expected.
    ///
    /// The footprint is the outline of the mesh seen from above -- the
    /// convex hull of its vertices -- so the marker need not be a box: a
    /// ten-sided ProBuilder cylinder marks out a decagon, a round one a
    /// circle. A hollow or L-shaped mesh counts as its hull, filled in; use
    /// extra cubes for those. A mesh that cannot be read at runtime (an
    /// imported model without Read/Write) falls back to its bounding box.
    ///
    /// For a room no one box covers -- an L shape, two halves round an
    /// obstacle -- add more cubes to <see cref="extraCubes"/>. The arena is
    /// then all of them together: a spot over any of them is inside it, and
    /// spots picked anywhere in the arena are spread over all of them by
    /// floor area, so a big cube gets more than a small one. The cubes may
    /// overlap. Each has its own top and bottom, so they can sit at
    /// different heights. The extra cubes need only a MeshFilter, not this
    /// component; give them one and each would be an arena of its own.
    ///
    /// The cubes are only markers. With <see cref="hideInPlay"/> on, their
    /// renderers and colliders switch off when play starts, so they are
    /// neither seen nor walked into; either way they never count as floor or
    /// as something in the way. Their outlines are drawn in the scene view
    /// whether selected or not.
    ///
    /// A spot counts as valid when there is floor on <see cref="floorMask"/>
    /// there, and the body to be placed, padded by <see cref="clearance"/>,
    /// would touch nothing solid -- walls, pillars, the player, other enemies.
    ///
    /// Nor may the body's middle be inside anything. Overlap tests against a
    /// non-convex mesh collider -- every ProBuilder mesh has one -- only
    /// meet its surface, so a body wholly inside a pillar overlaps nothing
    /// and would pass. So rays are also cast out from the middle, up and to
    /// the four sides, counting back faces: from inside a closed mesh, each
    /// one first meets the inside of a face.
    /// </remarks>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class ArenaBounds : MonoBehaviour
    {
        /// <summary>Gap left under a placed body, so it never starts sunk into the floor.</summary>
        private const float FloorLift = 0.05f;

        /// <summary>Random spots tried before giving up on one body.</summary>
        private const int SpotTries = 40;

        /// <summary>How far the rays checking for being inside something reach.</summary>
        private const float EnclosedProbeLength = 200f;

        /// <summary>
        /// Of the rays cast, how many must meet the inside of a face for the
        /// spot to count as inside something. Not all: a lone one-sided
        /// plane overhead, such as a stair's top seen from below, meets one
        /// ray from behind without enclosing anything.
        /// </summary>
        private const int EnclosedProbesNeeded = 3;

        private static readonly Vector3[] enclosedProbes =
        {
            Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        };

        private static readonly Collider[] overlaps = new Collider[16];

        // Each marker mesh's outline from above, in its own space; built once per mesh in play.
        private static readonly Dictionary<Mesh, Vector2[]> footprints = new Dictionary<Mesh, Vector2[]>();

        [SerializeField]
        [Tooltip("Switch the cubes' renderers and colliders off when play starts, so they are only markers.")]
        private bool hideInPlay = true;

        [SerializeField]
        [Tooltip("More cubes that are part of this same arena, for a room one box cannot cover. They need no ArenaBounds of their own.")]
        private List<MeshFilter> extraCubes = new List<MeshFilter>();

        [SerializeField]
        [Tooltip("What counts as floor. Must include the floor's own layer, such as NonOcclude.")]
        private LayerMask floorMask = ~0;

        [SerializeField]
        [Tooltip("Floor this far above a cube's top or below its bottom still counts as arena floor.")]
        [Min(0f)]
        private float floorTolerance = 0.5f;

        [SerializeField]
        [Tooltip("Room kept all round a placed body, clear of walls and anything else.")]
        [Min(0f)]
        private float clearance = 0.5f;

        private readonly List<MeshFilter> cubes = new List<MeshFilter>();
        private MeshFilter filter;

        /// <summary>
        /// Why the last <see cref="TryPlace(EnemyHealth, float)"/> that failed
        /// found nowhere: how many of its tries fell outside the cubes, found
        /// no floor, or were in the way of something. For a warning.
        /// </summary>
        public string LastFailure { get; private set; } = "";

        /// <summary>Middle of this cube's footprint, at its bottom. The extra cubes do not move it.</summary>
        public Vector3 Centre
        {
            get
            {
                Bounds box = LocalBox(Own);
                return transform.TransformPoint(new Vector3(box.center.x, box.min.y, box.center.z));
            }
        }

        /// <summary>
        /// Middle of the box round the whole arena -- this cube, if it has a
        /// mesh, and every extra one -- at the lowest cube's bottom. Unlike
        /// <see cref="Centre"/>, the extra cubes count, and neither pivot
        /// matters. This object's position if there are no cubes at all.
        /// </summary>
        public Vector3 BoundsCentre
        {
            get
            {
                bool any = false;
                Bounds whole = default;

                foreach (MeshFilter cube in Cubes)
                {
                    if (cube == null || cube.sharedMesh == null)
                    {
                        continue;
                    }

                    Bounds box = cube.sharedMesh.bounds;

                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 local = new Vector3((corner & 1) == 0 ? box.min.x : box.max.x,
                                                    (corner & 2) == 0 ? box.min.y : box.max.y,
                                                    (corner & 4) == 0 ? box.min.z : box.max.z);
                        Vector3 point = cube.transform.TransformPoint(local);

                        if (any)
                        {
                            whole.Encapsulate(point);
                        }
                        else
                        {
                            whole = new Bounds(point, Vector3.zero);
                            any = true;
                        }
                    }
                }

                return any ? new Vector3(whole.center.x, whole.min.y, whole.center.z) : transform.position;
            }
        }

        private MeshFilter Own
        {
            get
            {
                if (filter == null)
                {
                    filter = GetComponent<MeshFilter>();
                }

                return filter;
            }
        }

        /// <summary>This cube and every extra one, skipping empty slots.</summary>
        private List<MeshFilter> Cubes
        {
            get
            {
                cubes.Clear();
                cubes.Add(Own);

                foreach (MeshFilter cube in extraCubes)
                {
                    if (cube != null && !cubes.Contains(cube))
                    {
                        cubes.Add(cube);
                    }
                }

                return cubes;
            }
        }

        /// <summary>A cube's mesh bounds, in its own space. A ProBuilder pivot need not be in the middle.</summary>
        private static Bounds LocalBox(MeshFilter cube)
        {
            Mesh mesh = cube != null ? cube.sharedMesh : null;
            return mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        private void Awake()
        {
            if (!hideInPlay)
            {
                return;
            }

            foreach (MeshFilter cube in Cubes)
            {
                foreach (Renderer part in cube.GetComponents<Renderer>())
                {
                    part.enabled = false;
                }

                foreach (Collider part in cube.GetComponents<Collider>())
                {
                    part.enabled = false;
                }
            }
        }

        /// <summary>Whether <paramref name="point"/> lies over any cube's footprint, at any height.</summary>
        public bool Contains(Vector3 point)
        {
            foreach (MeshFilter cube in Cubes)
            {
                if (Over(cube, point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A random point over the arena, at the bottom of the cube it falls
        /// in. Each cube is picked in proportion to its footprint's area, so
        /// points are spread evenly over the whole floor.
        /// </summary>
        public Vector3 RandomPoint()
        {
            List<MeshFilter> all = Cubes;
            float total = 0f;

            foreach (MeshFilter cube in all)
            {
                total += Area(cube);
            }

            MeshFilter picked = all[0];
            float roll = Random.Range(0f, total);

            foreach (MeshFilter cube in all)
            {
                roll -= Area(cube);

                if (roll <= 0f)
                {
                    picked = cube;
                    break;
                }
            }

            // Thrown at the box round the footprint until one lands inside it.
            Bounds box = LocalBox(picked);
            Vector2[] outline = Footprint(picked);
            Vector3 local = new Vector3(box.center.x, box.min.y, box.center.z);

            for (int i = 0; i < 30; i++)
            {
                Vector3 trial = new Vector3(Random.Range(box.min.x, box.max.x), box.min.y, Random.Range(box.min.z, box.max.z));

                if (outline == null || InOutline(outline, trial))
                {
                    local = trial;
                    break;
                }
            }

            return picked.transform.TransformPoint(local);
        }

        /// <summary>
        /// Moves <paramref name="unit"/> to a random valid spot anywhere in
        /// the arena, its body <paramref name="lift"/> above the floor. False,
        /// with the unit left where it was, if no spot would do.
        /// </summary>
        public bool TryPlace(EnemyHealth unit, float lift = 0f)
        {
            return TryPlace(unit, null, 0f, 0f, lift);
        }

        /// <summary>
        /// Moves <paramref name="unit"/> to a random valid spot in the arena
        /// between <paramref name="minRadius"/> and <paramref name="maxRadius"/>
        /// of <paramref name="around"/> (or anywhere, if null), its body
        /// <paramref name="lift"/> above the floor. False, with the unit left
        /// where it was, if no spot would do.
        /// </summary>
        /// <remarks>
        /// Measures the unit where it stands, so instantiate it first and
        /// place it before physics steps. Physics only hears where a new
        /// object is on its next step, so this syncs transforms itself.
        /// </remarks>
        public bool TryPlace(EnemyHealth unit, Vector3? around, float minRadius, float maxRadius, float lift)
        {
            Physics.SyncTransforms();
            return TryPlace(unit, unit.BodyBounds(), around, minRadius, maxRadius, lift);
        }

        /// <summary>
        /// As <see cref="TryPlace(EnemyHealth, Vector3?, float, float, float)"/>,
        /// for a unit whose body was measured some other way, such as a
        /// hidden <see cref="PuppetDescent"/> puppet whose colliders are off.
        /// </summary>
        public bool TryPlace(EnemyHealth unit, Bounds body, Vector3? around, float minRadius, float maxRadius, float lift)
        {
            Physics.SyncTransforms();

            Transform root = unit.transform;
            Vector3 centreOffset = body.center - root.position;
            float bottomOffset = body.min.y - root.position.y;
            Vector3 halfSize = body.extents + Vector3.one * clearance;

            // Never padded below the floor: the body is lifted clear of it,
            // and the padding must not reach down and find it.
            halfSize.y = body.extents.y + Mathf.Min(clearance, FloorLift * 0.5f);

            int outside = 0;
            int noFloor = 0;
            int blocked = 0;

            for (int i = 0; i < SpotTries; i++)
            {
                Vector3 flat = around.HasValue ? RingPoint(around.Value, minRadius, maxRadius) : RandomPoint();

                if (!Contains(flat))
                {
                    outside++;
                    continue;
                }

                if (!TryFindFloor(flat, root, out float floor))
                {
                    noFloor++;
                    continue;
                }

                Vector3 position = new Vector3(flat.x, floor + FloorLift + lift - bottomOffset, flat.z);

                if (Blocked(root, position + centreOffset, halfSize) || Enclosed(root, position + centreOffset))
                {
                    blocked++;
                    continue;
                }

                root.position = position;

                Rigidbody rigidbody = unit.GetComponent<Rigidbody>();

                if (rigidbody != null)
                {
                    rigidbody.position = position;
                }

                Physics.SyncTransforms();
                return true;
            }

            LastFailure = $"of {SpotTries} spots tried, {outside} were outside the arena, {noFloor} found no floor " +
                          $"on the Floor Mask, and {blocked} were in the way of something";
            return false;
        }

        /// <summary>Whether <paramref name="point"/> lies over <paramref name="cube"/>'s footprint, at any height.</summary>
        private static bool Over(MeshFilter cube, Vector3 point)
        {
            Bounds box = LocalBox(cube);
            Vector3 local = cube.transform.InverseTransformPoint(point);

            if (local.x < box.min.x || local.x > box.max.x || local.z < box.min.z || local.z > box.max.z)
            {
                return false;
            }

            Vector2[] outline = Footprint(cube);
            return outline == null || InOutline(outline, local);
        }

        /// <summary>Whether the local point is inside the convex outline, which runs anticlockwise seen from above.</summary>
        private static bool InOutline(Vector2[] outline, Vector3 local)
        {
            Vector2 point = new Vector2(local.x, local.z);

            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % outline.Length];

                if (Cross(b - a, point - a) < -0.0001f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The outline of <paramref name="cube"/>'s mesh seen from above, in
        /// its own space: the convex hull of its vertices on local X and Z.
        /// Null if the mesh cannot be read, or is flat on the floor plane.
        /// </summary>
        private static Vector2[] Footprint(MeshFilter cube)
        {
            Mesh mesh = cube != null ? cube.sharedMesh : null;

            if (mesh == null || !mesh.isReadable)
            {
                return null;
            }

            // Rebuilt every time in the editor, where the mesh may be being reshaped.
            if (Application.isPlaying && footprints.TryGetValue(mesh, out Vector2[] cached))
            {
                return cached;
            }

            Vector3[] vertices = mesh.vertices;
            List<Vector2> points = new List<Vector2>(vertices.Length);

            foreach (Vector3 vertex in vertices)
            {
                points.Add(new Vector2(vertex.x, vertex.z));
            }

            Vector2[] hull = Hull(points);

            if (Application.isPlaying)
            {
                footprints[mesh] = hull;
            }

            return hull;
        }

        /// <summary>Convex hull of the points, anticlockwise, by the monotone chain. Null for fewer than three corners.</summary>
        private static Vector2[] Hull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            List<Vector2> hull = new List<Vector2>();

            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;

                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 point = points[pass == 0 ? i : points.Count - 1 - i];

                    while (hull.Count >= start + 2
                           && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 2]) <= 0.000001f)
                    {
                        hull.RemoveAt(hull.Count - 1);
                    }

                    hull.Add(point);
                }

                // Each half's last point is the next half's first.
                hull.RemoveAt(hull.Count - 1);
            }

            return hull.Count >= 3 ? hull.ToArray() : null;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>Area of <paramref name="cube"/>'s footprint in world units, scale included.</summary>
        private static float Area(MeshFilter cube)
        {
            Bounds box = LocalBox(cube);
            Vector3 scale = cube.transform.lossyScale;
            Vector2[] outline = Footprint(cube);
            float local = box.size.x * box.size.z;

            if (outline != null)
            {
                local = 0f;

                for (int i = 0; i < outline.Length; i++)
                {
                    local += Cross(outline[i], outline[(i + 1) % outline.Length]) * 0.5f;
                }
            }

            return Mathf.Abs(local * scale.x * scale.z);
        }

        /// <summary>Whether <paramref name="part"/> belongs to one of the arena's own cubes.</summary>
        private bool IsArena(Transform part)
        {
            foreach (MeshFilter cube in Cubes)
            {
                if (part.IsChildOf(cube.transform))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Height of the arena floor at <paramref name="spot"/>, ignoring
        /// <paramref name="ignore"/>'s own colliders. False if there is no
        /// floor there on the Floor Mask.
        /// </summary>
        public bool FloorAt(Vector3 spot, Transform ignore, out float floor)
        {
            return TryFindFloor(spot, ignore != null ? ignore : transform, out floor);
        }

        /// <summary>
        /// How far the whole arena -- every cube -- reaches along this cube's
        /// own flat axes, measured from <see cref="Centre"/>, for laying rows
        /// across it square to this cube. <paramref name="right"/> and
        /// <paramref name="forward"/> are this cube's local X and Z, flattened;
        /// <paramref name="min"/> and <paramref name="max"/> hold the reach
        /// along each, x along right and y along forward, in world units.
        /// </summary>
        public void Extent(out Vector3 right, out Vector3 forward, out Vector2 min, out Vector2 max)
        {
            right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);

            Vector3 centre = Centre;

            foreach (MeshFilter cube in Cubes)
            {
                Bounds box = LocalBox(cube);

                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 local = new Vector3((corner & 1) == 0 ? box.min.x : box.max.x, box.min.y,
                                                (corner & 2) == 0 ? box.min.z : box.max.z);
                    Vector3 offset = cube.transform.TransformPoint(local) - centre;
                    Vector2 along = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward));

                    min = Vector2.Min(min, along);
                    max = Vector2.Max(max, along);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="point"/> lies within the arena's bounding
        /// square -- the box round every cube, square to this one -- at any
        /// height. Unlike <see cref="Contains"/>, the gaps between the cubes
        /// of an L- or cross-shaped arena count.
        /// </summary>
        public bool InSquare(Vector3 point)
        {
            Extent(out Vector3 right, out Vector3 forward, out Vector2 min, out Vector2 max);
            Vector3 offset = point - Centre;
            float x = Vector3.Dot(offset, right);
            float z = Vector3.Dot(offset, forward);
            return x >= min.x && x <= max.x && z >= min.y && z <= max.y;
        }

        /// <summary>A random point between the two radii of <paramref name="around"/>, evenly spread over the ring.</summary>
        private static Vector3 RingPoint(Vector3 around, float minRadius, float maxRadius)
        {
            float inner = Mathf.Max(0f, Mathf.Min(minRadius, maxRadius));
            float outer = Mathf.Max(inner, maxRadius);

            // Square-root spread, so the outer part of the ring, which is
            // bigger, gets its share of spots.
            float radius = Mathf.Sqrt(Random.Range(inner * inner, outer * outer));
            float angle = Random.Range(0f, 360f);

            return around + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
        }

        /// <summary>
        /// Height of the highest floor at <paramref name="spot"/> between the
        /// top and bottom of the cubes over it, give or take the tolerance.
        /// Where cubes overlap, the floor may be anywhere from the lowest
        /// bottom to the highest top among them. Nothing above them counts,
        /// so an arch overhead never does.
        /// </summary>
        private bool TryFindFloor(Vector3 spot, Transform unit, out float floor)
        {
            float top = float.MinValue;
            float bottom = float.MaxValue;

            foreach (MeshFilter cube in Cubes)
            {
                if (!Over(cube, spot))
                {
                    continue;
                }

                Bounds box = LocalBox(cube);
                Transform frame = cube.transform;
                top = Mathf.Max(top, frame.TransformPoint(new Vector3(box.center.x, box.max.y, box.center.z)).y);
                bottom = Mathf.Min(bottom, frame.TransformPoint(new Vector3(box.center.x, box.min.y, box.center.z)).y);
            }

            floor = bottom;

            if (top < bottom)
            {
                return false;
            }

            top += floorTolerance;
            bottom -= floorTolerance;

            Vector3 from = new Vector3(spot.x, top, spot.z);
            float nearest = float.MaxValue;
            floor = bottom;

            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, Mathf.Max(0.01f, top - bottom),
                                                          floorMask, QueryTriggerInteraction.Ignore))
            {
                Transform part = hit.collider.transform;

                if (part.IsChildOf(unit) || IsArena(part) || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                floor = hit.point.y;
            }

            return nearest < float.MaxValue;
        }

        /// <summary>Whether anything solid but the unit itself, or the arena's cubes, is inside the box.</summary>
        private bool Blocked(Transform unit, Vector3 centre, Vector3 halfSize)
        {
            int count = Physics.OverlapBoxNonAlloc(centre, halfSize, overlaps, Quaternion.identity,
                                                   Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform part = overlaps[i].transform;

                if (!part.IsChildOf(unit) && !IsArena(part))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="centre"/> is inside something solid: enough
        /// of the rays cast out from it meet a face from behind before they
        /// meet one from the front.
        /// </summary>
        private bool Enclosed(Transform unit, Vector3 centre)
        {
            bool hitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;

            try
            {
                int inside = 0;

                foreach (Vector3 direction in enclosedProbes)
                {
                    RaycastHit[] hits = Physics.RaycastAll(centre, direction, EnclosedProbeLength,
                                                           Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    float nearest = float.MaxValue;
                    Vector3 normal = Vector3.zero;

                    foreach (RaycastHit hit in hits)
                    {
                        Transform part = hit.collider.transform;

                        if (part.IsChildOf(unit) || IsArena(part) || hit.distance >= nearest)
                        {
                            continue;
                        }

                        nearest = hit.distance;
                        normal = hit.normal;
                    }

                    // A face's normal points out of the solid, so one facing
                    // along the ray was met from inside.
                    if (nearest < float.MaxValue && Vector3.Dot(normal, direction) > 0f)
                    {
                        inside++;
                    }
                }

                return inside >= EnclosedProbesNeeded;
            }
            finally
            {
                Physics.queriesHitBackfaces = hitBackfaces;
            }
        }

        // Drawn always, so the arena still shows once the cubes are hidden.
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);

            foreach (MeshFilter cube in Cubes)
            {
                Bounds box = LocalBox(cube);
                Vector2[] outline = Footprint(cube);
                Gizmos.matrix = cube.transform.localToWorldMatrix;

                if (outline == null)
                {
                    Gizmos.DrawWireCube(box.center, box.size);
                    continue;
                }

                for (int i = 0; i < outline.Length; i++)
                {
                    Vector2 a = outline[i];
                    Vector2 b = outline[(i + 1) % outline.Length];
                    Gizmos.DrawLine(new Vector3(a.x, box.min.y, a.y), new Vector3(b.x, box.min.y, b.y));
                    Gizmos.DrawLine(new Vector3(a.x, box.max.y, a.y), new Vector3(b.x, box.max.y, b.y));
                    Gizmos.DrawLine(new Vector3(a.x, box.min.y, a.y), new Vector3(a.x, box.max.y, a.y));
                }
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
