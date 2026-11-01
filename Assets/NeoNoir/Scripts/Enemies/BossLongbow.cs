using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Lavender's bow as a boss: lying flat, its string level with the
    /// floor, turned only about the vertical so its arrow points at the
    /// player, with its
    /// string drawn between the bow's ends, as the player's
    /// <see cref="LavenderLongbow"/> does.
    /// </summary>
    /// <remarks>
    /// Goes on the object the bow's art and reference points hang under --
    /// the boss's Model -- since the points are read in its space: the two
    /// ends of the string, the string's resting middle, and the point it is
    /// drawn back to. Arrows would leave from the draw point toward the rest
    /// point, so that is the way the bow aims.
    ///
    /// Its rotation is set every frame after the enemy's own turn, so how the
    /// enemy or this object is turned in the prefab does not matter; only its
    /// position does.
    ///
    /// The string is drawn with a tinted copy of <see cref="effectMaterial"/>,
    /// expected to be NeoNoir/Tear, through its outline branch, as the
    /// player's bow draws its own.
    /// </remarks>
    public sealed class BossLongbow : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        [Header("String points")]
        [SerializeField]
        [Tooltip("Top end of the string, where it meets the bow.")]
        private Transform topString;

        [SerializeField]
        [Tooltip("Bottom end of the string, where it meets the bow.")]
        private Transform bottomString;

        [SerializeField]
        [Tooltip("Middle of the string at rest, undrawn.")]
        private Transform restPoint;

        [SerializeField]
        [Tooltip("Where the middle of the string is pulled to at full draw.")]
        private Transform drawPoint;

        [SerializeField]
        [Tooltip("How far the string is drawn, 0 at rest to 1 at full draw.")]
        [Range(0f, 1f)]
        private float drawn;

        [SerializeField]
        [Tooltip("Thickness of the string.")]
        [Min(0.001f)]
        private float stringWidth = 0.02f;

        [Header("Aim")]
        [SerializeField]
        [Tooltip("Degrees per second the bow turns after the player.")]
        [Min(0f)]
        private float turnSpeed = 360f;

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Material for the string, normally NeoNoir/Tear. It is copied and tinted, never edited.")]
        private Material effectMaterial;

        [SerializeField]
        [Tooltip("Colour of the string.")]
        [ColorUsage(false, true)]
        private Color stringColour = Color.white;

        private Enemy enemy;
        private float yaw;
        private bool aimed;

        private Mesh stringMesh;
        private MeshRenderer stringRenderer;
        private readonly Vector3[] stringVertices = new Vector3[8];
        private Material tintedMaterial;

        // The points in this object's own space, read once: they hang under it.
        private Vector3 localTop;
        private Vector3 localBottom;
        private Vector3 localRest;
        private Vector3 localDraw;
        private Quaternion unturn;

        private void Awake()
        {
            enemy = GetComponentInParent<Enemy>();

            localTop = Local(topString, Vector3.up);
            localBottom = Local(bottomString, Vector3.down);
            localRest = Local(restPoint, (localTop + localBottom) * 0.5f);
            localDraw = Local(drawPoint, localRest - Vector3.right * 0.6f);

            // The way arrows would leave, in the bow's own plane (its local
            // X and Y). Its local +Z, square to that plane, is turned to face
            // up, so the bow lies flat.
            Vector3 forward = localRest - localDraw;
            forward.z = 0f;
            forward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.right;
            unturn = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.forward));

            BuildString();
        }

        private Vector3 Local(Transform point, Vector3 fallback)
        {
            return point != null ? transform.InverseTransformPoint(point.position) : fallback;
        }

        private void LateUpdate()
        {
            Aim();
            UpdateString();
        }

        /// <summary>Flat, turned about the vertical so the arrow would point at the player.</summary>
        private void Aim()
        {
            if (enemy != null && enemy.HasTarget)
            {
                Vector3 toward = enemy.Target.position - transform.position;
                toward.y = 0f;

                if (toward.sqrMagnitude > 0.0001f)
                {
                    float wanted = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;

                    // Straight at them the first time, rather than swinging round.
                    yaw = aimed ? Mathf.MoveTowardsAngle(yaw, wanted, turnSpeed * Time.deltaTime) : wanted;
                    aimed = true;
                }
            }

            if (aimed)
            {
                transform.rotation = Quaternion.Euler(0f, yaw, 0f) * unturn;
            }
        }

        // ---- string ---------------------------------------------------------

        private void BuildString()
        {
            // Its own object, unparented and at the origin, so the vertices
            // can be written straight in world space and no scale of the
            // boss's touches them.
            GameObject line = new GameObject("BossLongbowString");

            stringMesh = new Mesh { name = "BossLongbowString" };
            stringMesh.MarkDynamic();

            line.AddComponent<MeshFilter>().sharedMesh = stringMesh;

            stringRenderer = line.AddComponent<MeshRenderer>();
            stringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stringRenderer.receiveShadows = false;

            if (effectMaterial != null)
            {
                tintedMaterial = new Material(effectMaterial) { name = effectMaterial.name + " (Lavender string)" };
                SetIfPresent(tintedMaterial, BaseColorId, stringColour);
                SetIfPresent(tintedMaterial, OutlineColorId, stringColour);
                stringRenderer.sharedMaterial = tintedMaterial;
            }

            // u = 1 throughout: NeoNoir/Tear's outline branch, the string colour.
            stringMesh.SetVertices(stringVertices);
            stringMesh.SetUVs(0, new[]
            {
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            });

            var triangles = new System.Collections.Generic.List<int>();
            ScarlettClaw.AddQuad(triangles, 0, 1, 2, 3);
            ScarlettClaw.AddQuad(triangles, 4, 5, 6, 7);
            stringMesh.SetTriangles(triangles, 0);
        }

        /// <summary>
        /// Two flat strips, top to nock and nock to bottom, built in world
        /// space each frame and turned to face the camera.
        /// </summary>
        private void UpdateString()
        {
            Vector3 top = transform.TransformPoint(localTop);
            Vector3 bottom = transform.TransformPoint(localBottom);
            Vector3 nock = transform.TransformPoint(Vector3.Lerp(localRest, localDraw, drawn));

            Camera camera = Camera.main;
            Vector3 toCamera = camera != null ? -camera.transform.forward : Vector3.up;

            StringSegment(0, top, nock, toCamera);
            StringSegment(4, nock, bottom, toCamera);

            stringMesh.SetVertices(stringVertices);
            stringMesh.RecalculateBounds();
        }

        private void StringSegment(int at, Vector3 from, Vector3 to, Vector3 toCamera)
        {
            Vector3 side = Vector3.Cross(to - from, toCamera);
            side = side.sqrMagnitude > 0.0000001f ? side.normalized * (stringWidth * 0.5f) : Vector3.zero;

            stringVertices[at] = from + side;
            stringVertices[at + 1] = from - side;
            stringVertices[at + 2] = to + side;
            stringVertices[at + 3] = to - side;
        }

        private static void SetIfPresent(Material material, int id, Color colour)
        {
            if (material.HasProperty(id))
            {
                material.SetColor(id, colour);
            }
        }

        private void OnEnable()
        {
            if (stringRenderer != null)
            {
                stringRenderer.enabled = true;
            }
        }

        private void OnDisable()
        {
            if (stringRenderer != null)
            {
                stringRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (stringRenderer != null)
            {
                Destroy(stringRenderer.gameObject);
            }

            if (stringMesh != null)
            {
                Destroy(stringMesh);
            }

            if (tintedMaterial != null)
            {
                Destroy(tintedMaterial);
            }
        }
    }
}
