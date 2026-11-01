using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// A white circle drawn on the floor that notices the player stepping in
    /// and out. The base of the <see cref="LoadoutStation"/> and the
    /// <see cref="TestingGrounds"/>.
    /// </summary>
    /// <remarks>
    /// The circle is centred on this object and drawn at its height, so put
    /// the object on the floor. Inside means within <see cref="radius"/>
    /// across the floor, ignoring height.
    /// </remarks>
    public abstract class AfterworldCircle : MonoBehaviour
    {
        private const int Segments = 96;

        [Header("Circle")]
        [SerializeField] [Min(0.1f)] private float radius = 3f;

        [SerializeField]
        [Tooltip("Width of the white outline.")]
        [Min(0.005f)]
        private float lineWidth = 0.08f;

        [SerializeField]
        [Tooltip("HDR: above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color lineColour = Color.white;

        [SerializeField]
        [Tooltip("Height of the outline above this object, so it sits just on the floor.")]
        private float lift = 0.02f;

        private Transform player;
        private Mesh mesh;

        public float Radius => radius;

        /// <summary>True while the player stands inside the circle.</summary>
        public bool PlayerInside { get; private set; }

        protected Transform Player => player;

        protected virtual void Awake()
        {
            BuildOutline();
        }

        protected virtual void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        protected virtual void Update()
        {
            if (player == null)
            {
                PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
                player = motor != null ? motor.transform : null;
            }

            bool inside = false;

            if (player != null)
            {
                Vector3 offset = player.position - transform.position;
                offset.y = 0f;
                inside = offset.sqrMagnitude <= radius * radius;
            }

            if (inside != PlayerInside)
            {
                PlayerInside = inside;

                if (inside)
                {
                    Entered();
                }
                else
                {
                    Left();
                }
            }
        }

        protected abstract void Entered();

        protected abstract void Left();

        private void BuildOutline()
        {
            GameObject outline = new GameObject("Outline");
            outline.transform.SetParent(transform, false);
            outline.transform.localPosition = Vector3.up * lift;
            outline.transform.localRotation = Quaternion.identity;

            Vector3[] vertices = new Vector3[(Segments + 1) * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[Segments * 6];
            float half = lineWidth * 0.5f;

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * (radius + half);
                vertices[i * 2 + 1] = direction * Mathf.Max(0f, radius - half);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            for (int i = 0; i < Segments; i++)
            {
                int at = i * 2;
                int t = i * 6;
                triangles[t] = at; triangles[t + 1] = at + 2; triangles[t + 2] = at + 1;
                triangles[t + 3] = at + 1; triangles[t + 4] = at + 2; triangles[t + 5] = at + 3;
            }

            mesh = new Mesh { name = "Circle Outline", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();

            outline.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = outline.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.DoubleSided;
            FlatMaterial.Tint(renderer, lineColour, new MaterialPropertyBlock());
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = Color.white;
            Vector3 centre = transform.position;
            Vector3 last = centre + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= 48; i++)
            {
                float angle = i / 48f * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(last, next);
                last = next;
            }
        }
    }
}
