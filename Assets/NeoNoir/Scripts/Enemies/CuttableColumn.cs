using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A column the Katana can cut off its root and throw, through a
    /// <see cref="KatanaColumnThrowPattern"/>.
    /// </summary>
    /// <remarks>
    /// Goes on the column itself -- the object whose meshes are the shaft,
    /// not a whole hall piece -- so only the column is cut. Put it in the
    /// hall segment prefab and every copy laid down the hall is a column the
    /// boss can pick.
    ///
    /// <see cref="CutOff"/> splits the real geometry at
    /// <see cref="cutHeight"/> above the column's foot: the stump stays here,
    /// solid, and the top comes away as a new object of its own. Its meshes
    /// need Read/Write enabled in their import settings; ProBuilder meshes
    /// always have it. Each column is cut once.
    /// </remarks>
    public sealed class CuttableColumn : MonoBehaviour
    {
        private static readonly List<CuttableColumn> active = new List<CuttableColumn>();

        [SerializeField]
        [Tooltip("Height above the column's foot where it is cut off. The stump left standing is this tall.")]
        [Min(0f)]
        private float cutHeight = 0.8f;

        [SerializeField]
        [Tooltip("Up to this many degrees the cut leans from level, picked at random, so no two stumps are alike.")]
        [Range(0f, 45f)]
        private float cutTilt = 15f;

        /// <summary>Every column that can still be cut.</summary>
        public static IReadOnlyList<CuttableColumn> Active => active;

        /// <summary>True once the column has been cut off.</summary>
        public bool Taken { get; private set; }

        /// <summary>What is left standing once cut off, or null before then.</summary>
        public GameObject Stump { get; private set; }

        /// <summary>Where the cut goes through: the middle of the column at the cut height.</summary>
        public Vector3 CutPoint
        {
            get
            {
                Bounds bounds = RenderBounds();
                return new Vector3(bounds.center.x, bounds.min.y + cutHeight, bounds.center.z);
            }
        }

        private void OnEnable()
        {
            if (!Taken)
            {
                active.Add(this);
            }
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        /// <summary>
        /// Cuts the column off at its cut height, leaning across
        /// <paramref name="slashDirection"/>, a flat direction, as a blade
        /// passing that way would. The stump stays here; the top is returned
        /// as a new object, its pivot at its middle, belonging to no one.
        /// Null if it has already been cut or cannot be.
        /// </summary>
        public GameObject CutOff(Vector3 slashDirection, KatanaCut.Settings look)
        {
            List<Material> materials = new List<Material>();
            MeshSlicer.Piece top = CutOffGeometry(slashDirection, look, materials);

            if (top == null)
            {
                return null;
            }

            GameObject freed = KatanaCut.BuildPiece(top, materials, look, $"{name} (cut)");
            freed.layer = gameObject.layer;
            return freed;
        }

        /// <summary>
        /// As <see cref="CutOff"/>, but hands back the top as bare geometry,
        /// in world space, with the <paramref name="materials"/> it is drawn
        /// in, for cutting further before anything is built from it.
        /// </summary>
        public MeshSlicer.Piece CutOffGeometry(Vector3 slashDirection, KatanaCut.Settings look, List<Material> materials,
                                               List<Vector3> cutOutline = null)
        {
            if (Taken)
            {
                return null;
            }

            MeshSlicer.Piece whole = KatanaCut.Gather(gameObject, materials);

            if (whole == null || whole.IsEmpty)
            {
                return null;
            }

            Vector3 across = slashDirection;
            across.y = 0f;
            across = across.sqrMagnitude > 0.0001f ? across.normalized : Vector3.right;

            // Leaned about the line the blade travelled along.
            Vector3 normal = Quaternion.AngleAxis(Random.Range(-cutTilt, cutTilt), across) * Vector3.up;
            Plane plane = new Plane(normal, CutPoint);

            if (!MeshSlicer.Slice(whole, plane, look.cutFaceUvScale, out MeshSlicer.Piece top, out MeshSlicer.Piece stump, cutOutline))
            {
                return null;
            }

            Taken = true;
            active.Remove(this);
            KatanaCut.Hide(gameObject);

            // The stump goes wherever the column goes, such as away with its
            // hall piece, and stays solid.
            GameObject standing = KatanaCut.BuildPiece(stump, materials, look, "Stump");
            Stump = standing;
            standing.layer = gameObject.layer;
            standing.transform.SetParent(transform, true);
            standing.AddComponent<BoxCollider>().size = stump.GetBounds().size;

            return top;
        }

        private Bounds RenderBounds()
        {
            bool found = false;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled)
                {
                    continue;
                }

                if (found)
                {
                    bounds.Encapsulate(part.bounds);
                }
                else
                {
                    bounds = part.bounds;
                    found = true;
                }
            }

            return bounds;
        }

        private void OnDrawGizmosSelected()
        {
            Bounds bounds = RenderBounds();
            Vector3 cut = new Vector3(bounds.center.x, bounds.min.y + cutHeight, bounds.center.z);

            Gizmos.color = new Color(1f, 0.62f, 0.08f, 0.9f);
            Gizmos.DrawWireCube(cut, new Vector3(bounds.size.x * 1.3f, 0.02f, bounds.size.z * 1.3f));
        }
    }
}
