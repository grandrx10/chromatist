using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Hangs an enemy from strings like a marionette: one thread from each
    /// chosen joint straight up into the dark, cut when it dies.
    /// </summary>
    /// <remarks>
    /// Each string runs from its joint up to <see cref="height"/> above the
    /// enemy's root, or to the first ceiling on <see cref="ceilingMask"/>
    /// if one is nearer. The top does not sit dead above the joint: it
    /// trails after it by about <see cref="lag"/> seconds, never more than
    /// <see cref="maxLean"/> behind, so the strings lean as the puppet moves
    /// and swing back upright when it stops, as if a hand above were
    /// following it.
    ///
    /// Drawn with the NeoNoir/String shader, which fades each string out
    /// toward its top and runs glints up it. The camera looks down at a
    /// steep angle, so the strings show shortened; keep <see cref="height"/>
    /// well below the camera, or they reach past it.
    ///
    /// When the enemy dies every string is cut: it springs up out of sight,
    /// wobbling, and fades as it goes (see <see cref="CutPuppetString"/>).
    /// <see cref="Cut(int)"/> and <see cref="CutAll"/> do the same at any
    /// other time, for a boss that loses its strings one by one.
    ///
    /// Goes on the enemy's root, beside <see cref="EnemyHealth"/>. The
    /// strings are their own objects, not children of the enemy, so turning
    /// or scaling the model never bends them and a cut string outlives the
    /// enemy it came from.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class PuppetStrings : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LengthId = Shader.PropertyToID("_Length");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        private static Material sharedMaterial;

        [SerializeField]
        [Tooltip("Bones a string is tied to. Wrists, elbows, knees and the head read best.")]
        private Transform[] joints = new Transform[0];

        [Header("Height")]
        [SerializeField]
        [Tooltip("How far above the enemy's root the strings reach. Keep it well below the camera.")]
        [Min(0.5f)]
        private float height = 12f;

        [SerializeField]
        [Tooltip("Ceilings that stop a string short. Nothing ticked lets every string reach its full height.")]
        private LayerMask ceilingMask = 0;

        [Header("Sway")]
        [SerializeField]
        [Tooltip("Roughly how many seconds the top of a string trails behind its joint. 0 keeps it dead above.")]
        [Min(0f)]
        private float lag = 0.3f;

        [SerializeField]
        [Tooltip("Furthest, in units, the top of a string can fall behind its joint sideways.")]
        [Min(0f)]
        private float maxLean = 2.5f;

        [Header("Look")]
        [SerializeField]
        [Tooltip("Material for the strings. Empty makes one from the NeoNoir/String shader.")]
        private Material material;

        [SerializeField]
        [Tooltip("Colour of the strings. Above 1 blooms.")]
        [ColorUsage(true, true)]
        private Color colour = new Color(2f, 2f, 2f, 1f);

        [SerializeField]
        [Tooltip("Width of each string, in units.")]
        [Min(0.001f)]
        private float width = 0.04f;

        [Header("Cutting")]
        [SerializeField]
        [Tooltip("Cut every string the moment the enemy dies.")]
        private bool cutOnDeath = true;

        [SerializeField]
        [Tooltip("Seconds a cut string takes to spring up out of sight.")]
        [Min(0.05f)]
        private float recoilTime = 0.6f;

        [SerializeField]
        [Tooltip("How far, in units, a cut string whips side to side as it springs up.")]
        [Min(0f)]
        private float wobble = 0.5f;

        [SerializeField]
        [Tooltip("Points along a cut string. More makes its wobble smoother.")]
        [Range(4, 32)]
        private int segments = 16;

        private EnemyHealth health;
        private LineRenderer[] lines;
        private Vector3[] tops;
        private Vector3[] topVelocities;
        private MaterialPropertyBlock block;

        /// <summary>How many strings the enemy hangs from, cut or not.</summary>
        public int Count => joints.Length;

        /// <summary>True if string <paramref name="index"/> is still tied on.</summary>
        public bool IsAttached(int index) => lines != null && index >= 0 && index < lines.Length && lines[index] != null;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            block = new MaterialPropertyBlock();

            lines = new LineRenderer[joints.Length];
            tops = new Vector3[joints.Length];
            topVelocities = new Vector3[joints.Length];

            Material stringMaterial = material != null ? material : SharedMaterial();

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                {
                    Debug.LogWarning($"[PuppetStrings] {name} has an empty joint slot ({i}).", this);
                    continue;
                }

                lines[i] = BuildLine(i, stringMaterial);
                tops[i] = Top(i, joints[i].position);
            }
        }

        private void OnEnable()
        {
            health.Died += OnDied;
            ShowLines(true);
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
            ShowLines(false);
        }

        private void OnDestroy()
        {
            // Strings still tied on go with the enemy. Cut ones have been
            // handed over and finish on their own.
            if (lines == null)
            {
                return;
            }

            foreach (LineRenderer line in lines)
            {
                if (line != null)
                {
                    Destroy(line.gameObject);
                }
            }
        }

        /// <summary>
        /// Cuts string <paramref name="index"/>: it springs up out of sight
        /// and is gone. Does nothing if it is already cut.
        /// </summary>
        public void Cut(int index)
        {
            if (!IsAttached(index))
            {
                return;
            }

            LineRenderer line = lines[index];
            lines[index] = null;

            line.enabled = true;
            line.gameObject.AddComponent<CutPuppetString>()
                .Begin(joints[index].position, tops[index], recoilTime, wobble, segments);
        }

        /// <summary>Cuts every string still tied on.</summary>
        public void CutAll()
        {
            for (int i = 0; i < joints.Length; i++)
            {
                Cut(i);
            }
        }

        private void OnDied(EnemyHealth died)
        {
            if (cutOnDeath)
            {
                CutAll();
            }
        }

        // After animation, so the strings follow the pose actually drawn.
        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < lines.Length; i++)
            {
                LineRenderer line = lines[i];

                if (line == null)
                {
                    continue;
                }

                Vector3 joint = joints[i].position;
                Vector3 target = Top(i, joint);
                Vector3 top = tops[i];

                if (lag > 0f && deltaTime > 0f)
                {
                    // Only sideways trails; the height follows at once.
                    Vector3 velocity = topVelocities[i];
                    top.x = Mathf.SmoothDamp(top.x, target.x, ref velocity.x, lag, Mathf.Infinity, deltaTime);
                    top.z = Mathf.SmoothDamp(top.z, target.z, ref velocity.z, lag, Mathf.Infinity, deltaTime);
                    topVelocities[i] = velocity;
                }
                else
                {
                    top.x = target.x;
                    top.z = target.z;
                }

                top.y = target.y;

                Vector3 lean = new Vector3(top.x - target.x, 0f, top.z - target.z);

                if (lean.sqrMagnitude > maxLean * maxLean)
                {
                    lean = lean.normalized * maxLean;
                    top.x = target.x + lean.x;
                    top.z = target.z + lean.z;
                }

                tops[i] = top;

                line.SetPosition(0, joint);
                line.SetPosition(1, top);
                Paint(line, Vector3.Distance(joint, top));
            }
        }

        /// <summary>Where string <paramref name="index"/> would end if it hung dead straight.</summary>
        private Vector3 Top(int index, Vector3 joint)
        {
            float topY = transform.position.y + height;
            float reach = topY - joint.y;

            if (reach > 0f && ceilingMask.value != 0
                && Physics.Raycast(joint, Vector3.up, out RaycastHit ceiling, reach, ceilingMask,
                                   QueryTriggerInteraction.Ignore))
            {
                topY = ceiling.point.y;
            }

            return new Vector3(joint.x, Mathf.Max(topY, joint.y), joint.z);
        }

        private LineRenderer BuildLine(int index, Material stringMaterial)
        {
            GameObject host = new GameObject($"{name} String {index}");
            LineRenderer line = host.AddComponent<LineRenderer>();

            // Tied on it is straight, so two points do. A cut string gets more.
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = stringMaterial;

            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetFloat(AlphaId, 1f);
            line.SetPropertyBlock(block);

            return line;
        }

        private void Paint(LineRenderer line, float length)
        {
            line.GetPropertyBlock(block);
            block.SetFloat(LengthId, length);
            line.SetPropertyBlock(block);
        }

        private void ShowLines(bool shown)
        {
            if (lines == null)
            {
                return;
            }

            foreach (LineRenderer line in lines)
            {
                if (line != null)
                {
                    line.enabled = shown;
                }
            }
        }

        private Material SharedMaterial()
        {
            if (sharedMaterial == null)
            {
                Shader shader = Shader.Find("NeoNoir/String");

                if (shader == null)
                {
                    Debug.LogWarning("[PuppetStrings] NeoNoir/String shader not found. Assign a material made from it, "
                                     + "or add the shader to Always Included Shaders.", this);
                    return null;
                }

                sharedMaterial = new Material(shader) { name = "Runtime String" };
            }

            return sharedMaterial;
        }
    }
}
