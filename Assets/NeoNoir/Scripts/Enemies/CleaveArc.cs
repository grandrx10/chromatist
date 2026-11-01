using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A cleave across the floor in front of a boss: first a warning -- the
    /// outline of a wedge, filling out from the boss as <see cref="Fill"/>
    /// goes from 0 to 1 -- then, on <see cref="Strike"/>, a sweep of light
    /// across the wedge from one side to the other, with a wall of light
    /// riding its leading edge, hurting the player once as it passes them.
    /// </summary>
    /// <remarks>
    /// The wedge is centred on the facing it was made with, spreading
    /// <see cref="Settings"/>-given degrees across, half to either side. The
    /// sweep runs from the left side to the right, seen from above. Measured
    /// flat, so the player's height never matters.
    ///
    /// Builds its own meshes, so it needs no prefab. Coloured through a
    /// property block, never by editing the material; with a _Fade property
    /// the material dissolves it at the end, otherwise it just goes.
    /// <see cref="DestroyWhen"/> covers an owner cut short, such as an attack
    /// ended by a death, before it strikes.
    /// </remarks>
    public sealed class CleaveArc : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Min(0f)] public float damage = 30f;

            [Tooltip("Width of the warning's outline.")]
            [Min(0.01f)] public float outlineWidth = 0.4f;

            [Tooltip("Brightness of the warning's outline, and of its fill. Above 1 blooms.")]
            public Vector2 warningIntensity = new Vector2(2f, 0.6f);

            [Tooltip("Brightness of the swept wedge. Above 1 blooms.")]
            [Min(0f)] public float sweepIntensity = 2.5f;

            [Tooltip("Height of the wall of light on the sweep's leading edge.")]
            [Min(0f)] public float edgeHeight = 3f;

            [Tooltip("Brightness of the wall of light on the leading edge.")]
            [Min(0f)] public float edgeIntensity = 4f;

            [Tooltip("Seconds the swept wedge stays once the sweep is done.")]
            [Min(0f)] public float holdTime = 0.15f;

            [Tooltip("Seconds it takes to fade away after that.")]
            [Min(0.01f)] public float fadeTime = 0.4f;

            [Tooltip("Lift off the floor, so it never flickers into it.")]
            [Range(0f, 1f)] public float floorOffset = 0.05f;
        }

        private const float DegreesPerSegment = 3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Settings settings;
        private PlayerHealth player;
        private float radius;
        private float halfAngle;
        private Color colour;
        private float fill = -1f;
        private Func<bool> expired;

        private MeshRenderer outlineRenderer;
        private MeshRenderer fillRenderer;
        private MeshRenderer sweepRenderer;
        private MeshRenderer edgeRenderer;
        private Transform fillPart;
        private Mesh sweepMesh;
        private Mesh edgeMesh;
        private MaterialPropertyBlock block;
        private bool hasFade;

        // Seconds since the strike; negative before it.
        private float struck = -1f;
        private float sweepTime;
        private bool playerHit;

        /// <summary>How far the warning has filled the wedge, 0 to 1.</summary>
        public float Fill
        {
            get => fill;
            set
            {
                value = Mathf.Clamp01(value);

                if (value != fill)
                {
                    fill = value;

                    float size = Mathf.Max(0f, radius - settings.outlineWidth) * fill;
                    fillRenderer.enabled = size > 0.0001f && struck < 0f;
                    fillPart.localScale = new Vector3(size, 1f, size);
                }
            }
        }

        /// <summary>
        /// Draws the empty warning on the floor at <paramref name="floorPoint"/>:
        /// a wedge <paramref name="radius"/> long and <paramref name="angle"/>
        /// degrees across, centred on <paramref name="facing"/>. Once struck it
        /// hurts <paramref name="target"/>. A null <paramref name="material"/>
        /// uses a shared double-sided NeoNoir/Flat one.
        /// </summary>
        public static CleaveArc Create(Vector3 floorPoint, Vector3 facing, float radius, float angle,
                                       Settings settings, Color colour, Material material, PlayerHealth target)
        {
            GameObject host = new GameObject("CleaveArc");
            CleaveArc arc = host.AddComponent<CleaveArc>();
            arc.player = target;
            arc.Begin(floorPoint, facing, radius, angle, settings, colour, material);
            return arc;
        }

        /// <summary>Destroys the warning by itself the first frame <paramref name="condition"/> is true, unless it has struck.</summary>
        public void DestroyWhen(Func<bool> condition)
        {
            expired = condition;
        }

        /// <summary>
        /// Sweeps across the wedge over <paramref name="seconds"/>, speeding
        /// up as it goes, then fades and removes itself.
        /// </summary>
        public void Strike(float seconds)
        {
            if (struck >= 0f)
            {
                return;
            }

            struck = 0f;
            sweepTime = Mathf.Max(0.01f, seconds);
            expired = null;

            outlineRenderer.enabled = false;
            fillRenderer.enabled = false;
            Sweep();
        }

        private void Begin(Vector3 floorPoint, Vector3 facing, float arcRadius, float angle,
                           Settings arcSettings, Color arcColour, Material material)
        {
            settings = arcSettings;
            radius = Mathf.Max(0.1f, arcRadius);
            halfAngle = Mathf.Clamp(angle, 1f, 360f) * 0.5f;
            colour = arcColour;

            facing.y = 0f;
            facing = facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward;

            // Local +Z is the facing, so every mesh is built flat in its own space.
            transform.SetPositionAndRotation(floorPoint + Vector3.up * settings.floorOffset,
                                             Quaternion.LookRotation(facing, Vector3.up));

            if (material == null)
            {
                material = FlatMaterial.DoubleSided;
            }

            hasFade = material != null && material.HasProperty(FadeId);
            block = new MaterialPropertyBlock();

            Mesh outline = new Mesh { name = "Cleave arc outline" };
            BuildOutline(outline);
            outlineRenderer = AddPart("Outline", outline, material, out _);

            // Built a unit wedge and scaled, so filling rebuilds nothing.
            Mesh unitWedge = new Mesh { name = "Cleave arc fill" };
            BuildWedge(unitWedge, 0f, 1f, -halfAngle, halfAngle);
            fillRenderer = AddPart("Fill", unitWedge, material, out fillPart);

            sweepMesh = new Mesh { name = "Cleave arc sweep" };
            sweepMesh.MarkDynamic();
            sweepRenderer = AddPart("Sweep", sweepMesh, material, out _);
            sweepRenderer.enabled = false;

            edgeMesh = new Mesh { name = "Cleave arc edge" };
            edgeMesh.MarkDynamic();
            edgeRenderer = AddPart("Edge", edgeMesh, material, out _);
            edgeRenderer.enabled = false;

            Paint(outlineRenderer, settings.warningIntensity.x, 1f);
            Paint(fillRenderer, settings.warningIntensity.y, 1f);

            Fill = 0f;
        }

        private MeshRenderer AddPart(string partName, Mesh mesh, Material material, out Transform part)
        {
            GameObject host = new GameObject(partName);
            part = host.transform;
            part.SetParent(transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = host.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sharedMaterial = material;
            return meshRenderer;
        }

        private void Update()
        {
            if (struck < 0f)
            {
                if (expired != null && expired())
                {
                    Destroy(gameObject);
                }

                return;
            }

            struck += Time.deltaTime;

            if (struck >= sweepTime + settings.holdTime + settings.fadeTime)
            {
                Destroy(gameObject);
                return;
            }

            Sweep();
        }

        /// <summary>Draws the sweep as far as it has got, and hurts the player if it has passed them.</summary>
        private void Sweep()
        {
            float progress = Mathf.Clamp01(struck / sweepTime);
            progress *= progress;
            float leading = Mathf.Lerp(-halfAngle, halfAngle, progress);

            float visibility = 1f - Mathf.Clamp01((struck - sweepTime - settings.holdTime) / settings.fadeTime);

            if (leading > -halfAngle)
            {
                // Without a fade property it thins toward the boss instead.
                float inner = hasFade ? 0f : radius * (1f - visibility);
                BuildWedge(sweepMesh, inner, radius, -halfAngle, leading);
                sweepRenderer.enabled = visibility > 0f;
                Paint(sweepRenderer, settings.sweepIntensity, visibility);
            }

            // The wall rides the edge out, then sinks once the sweep is done.
            float sink = Mathf.Clamp01((struck - sweepTime) / Mathf.Max(0.01f, settings.holdTime + settings.fadeTime * 0.5f));
            float height = settings.edgeHeight * (1f - sink * sink);
            edgeRenderer.enabled = height > 0.01f;

            if (edgeRenderer.enabled)
            {
                BuildEdge(edgeMesh, leading, height);
                Paint(edgeRenderer, settings.edgeIntensity, 1f);
            }

            HurtPlayer(leading);
        }

        /// <summary>Hurts the player, once, if they stand anywhere the sweep has crossed.</summary>
        private void HurtPlayer(float leading)
        {
            if (playerHit || player == null || player.IsDead)
            {
                return;
            }

            Vector3 local = transform.InverseTransformPoint(player.transform.position);
            local.y = 0f;

            if (local.sqrMagnitude > radius * radius)
            {
                return;
            }

            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;

            if (angle >= -halfAngle && angle <= leading)
            {
                playerHit = true;
                player.TakeDamage(settings.damage, transform.position);
            }
        }

        private void Paint(MeshRenderer target, float intensity, float visibility)
        {
            Color bright = colour * intensity;
            bright.a = colour.a;

            block.SetColor(BaseColorId, bright);
            block.SetFloat(FadeId, visibility);
            target.SetPropertyBlock(block);
        }

        // ---- meshes -----------------------------------------------------------

        private static Vector3 Radial(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private static int SegmentsFor(float degrees)
        {
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(degrees) / DegreesPerSegment));
        }

        /// <summary>The flat band from <paramref name="inner"/> to <paramref name="outer"/>, between two angles.</summary>
        private void BuildWedge(Mesh mesh, float inner, float outer, float from, float to)
        {
            vertices.Clear();
            triangles.Clear();
            AddBand(inner, outer, from, to);
            Apply(mesh);
        }

        /// <summary>The rim of the wedge, and its two straight sides.</summary>
        private void BuildOutline(Mesh mesh)
        {
            vertices.Clear();
            triangles.Clear();

            float width = settings.outlineWidth;
            AddBand(radius - width, radius, -halfAngle, halfAngle);

            if (halfAngle < 180f)
            {
                AddSide(-halfAngle, width);
                AddSide(halfAngle, -width);
            }

            Apply(mesh);
        }

        private void AddBand(float inner, float outer, float from, float to)
        {
            int segments = SegmentsFor(to - from);
            int first = vertices.Count;

            for (int i = 0; i <= segments; i++)
            {
                Vector3 radial = Radial(Mathf.Lerp(from, to, (float)i / segments));
                vertices.Add(radial * inner);
                vertices.Add(radial * outer);
            }

            for (int i = 0; i < segments; i++)
            {
                AddQuad(first + i * 2, first + i * 2 + 1, first + i * 2 + 3, first + i * 2 + 2);
            }
        }

        /// <summary>A strip from the boss out along <paramref name="degrees"/>, <paramref name="width"/> wide toward the inside of the wedge.</summary>
        private void AddSide(float degrees, float width)
        {
            Vector3 along = Radial(degrees);
            Vector3 inward = Vector3.Cross(Vector3.up, along) * width;
            int first = vertices.Count;

            vertices.Add(Vector3.zero);
            vertices.Add(along * radius);
            vertices.Add(along * radius + inward);
            vertices.Add(inward);

            AddQuad(first, first + 1, first + 2, first + 3);
        }

        /// <summary>An upright slab of light along the radius at <paramref name="degrees"/>.</summary>
        private void BuildEdge(Mesh mesh, float degrees, float height)
        {
            vertices.Clear();
            triangles.Clear();

            Vector3 along = Radial(degrees) * radius;
            vertices.Add(Vector3.zero);
            vertices.Add(along);
            vertices.Add(along + Vector3.up * height);
            vertices.Add(Vector3.up * height);
            AddQuad(0, 1, 2, 3);

            Apply(mesh);
        }

        /// <summary>A quad wound both ways, so it shows from either side.</summary>
        private void AddQuad(int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(a); triangles.Add(d); triangles.Add(c);
        }

        private void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            foreach (MeshFilter part in GetComponentsInChildren<MeshFilter>())
            {
                Destroy(part.sharedMesh);
            }
        }
    }
}
