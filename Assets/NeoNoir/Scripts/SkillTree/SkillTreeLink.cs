using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// One of the lines along the shrine floor joining a blade to the blades
    /// it unlocks. Dark until <see cref="from"/> is acquired, then it lights.
    /// </summary>
    /// <remarks>
    /// Put it on the line object itself: every renderer on it and its
    /// children is recoloured, through a property block, so the material
    /// asset is never edited. Any mesh works (a thin quad, a ProBuilder
    /// strip); so does a LineRenderer, whose start and end colours are set
    /// too, for shaders that read vertex colour.
    ///
    /// A line whose blade was acquired on an earlier visit loads already lit.
    ///
    /// With a LineRenderer and both <see cref="from"/> and <see cref="to"/>
    /// set, the line lays itself from one blade to the other along the floor
    /// at this object's height, whenever the component is changed in the
    /// editor and again on play. Move a blade and touch any field (or use the
    /// component menu's "Lay Line") to re-lay it.
    /// </remarks>
    public sealed class SkillTreeLink : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField]
        [Tooltip("The blade whose acquiring lights this line. For the lines out of the centre, Luna.")]
        private PurchasableBlade from;

        [SerializeField]
        [Tooltip("The blade this line leads to. Only used to draw it in the editor.")]
        private PurchasableBlade to;

        [SerializeField]
        [ColorUsage(false, true)]
        private Color unlitColour = Color.black;

        [SerializeField]
        [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
        [ColorUsage(false, true)]
        private Color litColour = new Color(2f, 2f, 2f);

        [SerializeField]
        [Tooltip("Seconds after the blade is acquired before the line starts to light, so it can follow the stone.")]
        [Min(0f)]
        private float delay = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the line takes to light fully.")]
        [Min(0f)]
        private float lightTime = 0.8f;

        [Header("Line Renderer only")]
        [SerializeField]
        [Tooltip("Lay the LineRenderer from one blade to the other automatically.")]
        private bool layBetweenBlades = true;

        [SerializeField]
        [Tooltip("Metres the line stops short of each blade, so it meets the stone's edge rather than running under it.")]
        [Min(0f)]
        private float endInset = 0.8f;

        [SerializeField]
        [FormerlySerializedAs("floorLift")]
        [Tooltip("Height of the line relative to this object, in metres. Negative lowers it. The line moves as you change it; select every link to change them together.")]
        private float elevation = 0.02f;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private bool lit;

        public bool IsLit => lit;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
            LayLine();
        }

        private void OnValidate()
        {
            LayLine();
        }

        /// <summary>Lays the LineRenderer, if there is one, from one blade to the other.</summary>
        [ContextMenu("Lay Line")]
        public void LayLine()
        {
            if (!layBetweenBlades || from == null || to == null)
            {
                return;
            }

            LineRenderer line = GetComponent<LineRenderer>();

            if (line == null)
            {
                return;
            }

            float height = transform.position.y + elevation;
            Vector3 start = from.BladeModel.position;
            Vector3 end = to.BladeModel.position;
            start.y = height;
            end.y = height;

            // Stop short of each stone, but never so far the ends cross over.
            Vector3 along = end - start;
            float inset = Mathf.Min(endInset, along.magnitude * 0.45f);
            Vector3 step = along.normalized * inset;

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start + step);
            line.SetPosition(1, end - step);
        }

        private void OnEnable()
        {
            BladeUnlocks.BladeAcquired += OnBladeAcquired;
        }

        private void OnDisable()
        {
            BladeUnlocks.BladeAcquired -= OnBladeAcquired;
        }

        private void Start()
        {
            lit = from != null && from.IsAcquired;
            Apply(lit ? 1f : 0f);
        }

        private void OnBladeAcquired(BladeDefinition blade)
        {
            if (lit || from == null || from.Blade != blade)
            {
                return;
            }

            lit = true;
            StartCoroutine(LightUp());
        }

        private IEnumerator LightUp()
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            float clock = 0f;

            while (clock < lightTime)
            {
                clock += Time.deltaTime;
                float t = Mathf.Clamp01(clock / lightTime);
                Apply(t * t);
                yield return null;
            }

            Apply(1f);
        }

        private void Apply(float t)
        {
            Color colour = Color.Lerp(unlitColour, litColour, t);

            foreach (Renderer target in renderers)
            {
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                block.SetColor(ColorId, colour);
                target.SetPropertyBlock(block);

                if (target is LineRenderer line)
                {
                    line.startColor = colour;
                    line.endColor = colour;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (from == null || to == null)
            {
                return;
            }

            Gizmos.color = lit ? Color.white : Color.gray;
            Gizmos.DrawLine(from.BladeModel.position, to.BladeModel.position);
        }
    }
}
