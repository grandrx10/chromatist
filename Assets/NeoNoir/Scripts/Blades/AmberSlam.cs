using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Amber's secondary landing: a line split into the floor from the
    /// player's feet out along the slam, damaging every enemy it reaches.
    /// </summary>
    /// <remarks>
    /// The line does not appear all at once. Its front runs out from the
    /// player to its full length over <see cref="Settings.waveTime"/>, and
    /// the damage travels with it: each frame sweeps from where the front was
    /// to where it is now, so an enemy is hit the moment the line reaches it,
    /// and only once. It goes through walls -- the length is fixed.
    ///
    /// Drawn as one flat strip of a single colour, tapering to a point at the
    /// front, so the far end reads as the line still splitting. A wall of
    /// light rises off it right behind the front and falls back into the
    /// floor, so a wave of light runs out with the strike. Over the player, a
    /// crescent in the upright plane of the slam shows the path the axe came
    /// down along, and fades fast.
    ///
    /// Every part uses one material, expected to be NeoNoir/Flat in Amber's
    /// colour, so it dissolves with the same dither as the blades. Brightness
    /// comes from scaling the material's own colour, never from editing it.
    ///
    /// Spawned at runtime and builds its own meshes, so it needs no prefab.
    /// </remarks>
    public sealed class AmberSlam : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Min(0f)] public float damage = 50f;

            [Tooltip("How far the line runs from the player's feet.")]
            [Min(0.5f)] public float length = 20f;

            [Tooltip("Width of the line, and of the strip that takes damage.")]
            [Min(0.1f)] public float width = 1.5f;

            [Tooltip("Seconds the front of the line takes to run its full length.")]
            [Min(0.01f)] public float waveTime = 0.12f;

            [Tooltip("Seconds the line holds at full length before it starts to dissolve.")]
            [Min(0f)] public float holdTime = 0.25f;

            [Tooltip("Seconds the line takes to dissolve.")]
            [Min(0.01f)] public float fadeTime = 0.35f;

            [Tooltip("Height above the floor the line is drawn at.")]
            [Range(0f, 1f)] public float floorOffset = 0.05f;

            [Header("Hit")]
            [Tooltip("Slashes fired together on each enemy the line reaches, fanned so they cross.")]
            [Min(1)] public int hitSlashes = 3;

            [Tooltip("Size of each hit slash. 1 is a normal hit's.")]
            [Min(0.1f)] public float hitSlashSize = 2f;

            [Tooltip("Colour of the hit slashes. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
            [ColorUsage(false, true)] public Color hitColour = new Color(1f, 0.42f, 0.02f);

            [Header("Line")]
            [Tooltip("How far back from the front the line tapers to its point.")]
            [Min(0.01f)] public float frontTaper = 1.5f;

            [Tooltip("Width the line narrows to at its far end, as a fraction of its width at the player.")]
            [Range(0f, 1f)] public float farWidth = 0.4f;

            [Tooltip("Colour intensity of the line. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float lineIntensity = 2f;

            [Header("Wall of light")]
            [Tooltip("Height the wall rises to off the line.")]
            [Min(0f)] public float wallHeight = 3f;

            [Tooltip("Thickness of the wall, as a fraction of the line's width.")]
            [Range(0f, 1f)] public float wallWidth = 0.5f;

            [Tooltip("Seconds each stretch of wall takes to rise once the front has passed it.")]
            [Min(0.01f)] public float wallRiseTime = 0.08f;

            [Tooltip("Seconds each stretch of wall takes to fall back into the floor after rising.")]
            [Min(0.01f)] public float wallFallTime = 0.35f;

            [Tooltip("Colour intensity of the wall.")]
            [Min(0f)] public float wallIntensity = 5f;

            [Header("Swing arc")]
            [Tooltip("Distance from the shoulder to the arc's outer edge: about the reach of the axe.")]
            [Min(0f)] public float arcRadius = 2.2f;

            [Tooltip("Thickness of the arc at its leading end.")]
            [Min(0.01f)] public float arcWidth = 0.6f;

            [Tooltip("Seconds the arc takes to fade.")]
            [Min(0.01f)] public float arcTime = 0.18f;

            [Tooltip("Colour intensity of the arc.")]
            [Min(0f)] public float arcIntensity = 2f;
        }

        private const int LineSegments = 48;
        private const int ArcSegments = 24;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly HashSet<EnemyHealth> hit = new HashSet<EnemyHealth>();

        private Settings settings;
        private float age;
        private float front;

        private Vector3 direction;
        private float damageHeight;

        // The arc, in this object's local space: from the shoulder, sweeping
        // from overhead behind to the floor in front.
        private float arcCentreHeight;
        private float arcFrom;
        private float arcTo;

        private Mesh lineMesh;
        private Mesh wallMesh;
        private Mesh arcMesh;

        private MeshRenderer lineRenderer;
        private MeshRenderer wallRenderer;
        private MeshRenderer arcRenderer;

        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;
        private bool hasFade;

        private float LineLifetime => settings.waveTime + settings.holdTime + settings.fadeTime;

        // The last stretch of wall rises once the front reaches the end.
        private float WallLifetime => settings.waveTime + settings.wallRiseTime + settings.wallFallTime;

        private float Lifetime => Mathf.Max(LineLifetime, WallLifetime);

        /// <summary>
        /// Splits a line into the floor from <paramref name="floorPoint"/>, a
        /// point on the floor at the player's feet, along
        /// <paramref name="slamDirection"/>. Enemies are found at
        /// <paramref name="hitHeight"/>, normally the middle of the player's
        /// body. The swing arc is drawn about a shoulder
        /// <paramref name="shoulderHeight"/> above the floor, from
        /// <paramref name="swingFrom"/> to <paramref name="swingTo"/> degrees
        /// from straight up, positive toward the slam.
        /// </summary>
        public static AmberSlam Create(Vector3 floorPoint, Vector3 slamDirection, float hitHeight,
                                       float shoulderHeight, float swingFrom, float swingTo,
                                       Settings settings, Material material)
        {
            GameObject host = new GameObject("AmberSlam");
            AmberSlam slam = host.AddComponent<AmberSlam>();
            slam.Begin(floorPoint, slamDirection, hitHeight, shoulderHeight, swingFrom, swingTo, settings, material);
            return slam;
        }

        private void Begin(Vector3 floorPoint, Vector3 slamDirection, float hitHeight,
                           float shoulderHeight, float swingFrom, float swingTo,
                           Settings slamSettings, Material material)
        {
            settings = slamSettings;
            damageHeight = hitHeight;

            slamDirection.y = 0f;
            direction = slamDirection.sqrMagnitude > 0.0001f ? slamDirection.normalized : Vector3.forward;

            // Local +Z runs along the line, so every mesh is built flat in
            // its own space and never needs turning.
            transform.SetPositionAndRotation(floorPoint + Vector3.up * settings.floorOffset,
                                             Quaternion.LookRotation(direction, Vector3.up));

            arcCentreHeight = shoulderHeight - settings.floorOffset;
            arcFrom = swingFrom;
            arcTo = swingTo;

            if (material != null)
            {
                hasFade = material.HasProperty(FadeId);

                if (material.HasProperty(BaseColorId))
                {
                    baseColour = material.GetColor(BaseColorId);
                }
            }

            block = new MaterialPropertyBlock();

            lineRenderer = AddPart("Line", material, Vector3.zero, out lineMesh);
            wallRenderer = AddPart("Wall", material, Vector3.zero, out wallMesh);
            arcRenderer = AddPart("Arc", material, Vector3.zero, out arcMesh);

            Advance();
            UpdateVisuals();
        }

        private MeshRenderer AddPart(string partName, Material material, Vector3 offset, out Mesh mesh)
        {
            GameObject part = new GameObject(partName);
            part.transform.SetParent(transform, false);
            part.transform.localPosition = offset;

            mesh = new Mesh { name = "Amber slam " + partName };
            mesh.MarkDynamic();
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            return meshRenderer;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            Advance();
            UpdateVisuals();
        }

        /// <summary>Runs the front out to where it should be by now, hurting what it passes.</summary>
        private void Advance()
        {
            float was = front;
            front = settings.length * Mathf.Clamp01(age / settings.waveTime);

            // The first call sweeps the spot at the player's feet alone.
            if (front > was || age <= 0f)
            {
                Vector3 start = transform.position + direction * was;
                Vector3 end = transform.position + direction * front;
                start.y = damageHeight;
                end.y = damageHeight;

                BladeDamage.Sweep(start, end, settings.width * 0.5f, settings.damage, direction, hit,
                                  slashTint: settings.hitColour, slashCount: settings.hitSlashes,
                                  slashScale: settings.hitSlashSize);
            }
        }

        /// <summary>1 until the line's last <see cref="Settings.fadeTime"/> seconds, then down to 0.</summary>
        private float LineVisibility()
        {
            return Mathf.Clamp01((LineLifetime - age) / settings.fadeTime);
        }

        /// <summary>
        /// Height of the wall at <paramref name="along"/>: nothing until the
        /// front reaches it, then shooting up and falling back into the floor.
        /// </summary>
        private float WallHeight(float along)
        {
            float passed = age - settings.waveTime * along / settings.length;

            if (passed <= 0f)
            {
                return 0f;
            }

            // Eased out going up, eased in coming down, so it bursts up and
            // drops away rather than bobbing.
            if (passed < settings.wallRiseTime)
            {
                float rise = 1f - passed / settings.wallRiseTime;
                return settings.wallHeight * (1f - rise * rise);
            }

            float fall = (passed - settings.wallRiseTime) / settings.wallFallTime;
            return fall >= 1f ? 0f : settings.wallHeight * (1f - fall * fall);
        }

        /// <summary>The line's width at <paramref name="along"/>, as a fraction of its full width: narrowing toward the far end.</summary>
        private float Taper(float along)
        {
            return Mathf.Lerp(1f, settings.farWidth, along / settings.length);
        }

        private void UpdateVisuals()
        {
            float visibility = LineVisibility();

            // Without a fade property the dither cannot dissolve it, so it
            // thins away instead.
            float thin = hasFade ? 1f : visibility;

            BuildLine(lineMesh, settings.width * thin);
            BuildWall();

            float arcVisibility = 1f - Mathf.Clamp01(age / settings.arcTime);
            BuildArc(hasFade ? 1f : arcVisibility);

            Paint(lineRenderer, settings.lineIntensity, visibility);
            Paint(wallRenderer, settings.wallIntensity, 1f);
            Paint(arcRenderer, settings.arcIntensity, arcVisibility);
            arcRenderer.enabled = arcVisibility > 0f;
        }

        private void Paint(MeshRenderer target, float intensity, float visibility)
        {
            Color colour = baseColour * intensity;
            colour.a = baseColour.a;

            block.SetColor(BaseColorId, colour);
            block.SetFloat(FadeId, visibility);
            target.SetPropertyBlock(block);
        }

        /// <summary>A flat strip from the player's feet to the front, narrowing to the far end and coming to a point at the front.</summary>
        private void BuildLine(Mesh mesh, float width)
        {
            vertices.Clear();
            triangles.Clear();

            if (front > 0.0001f && width > 0f)
            {
                for (int i = 0; i <= LineSegments; i++)
                {
                    float along = front * i / LineSegments;
                    float point = Mathf.Clamp01((front - along) / settings.frontTaper);
                    float half = width * 0.5f * Taper(along) * Mathf.Sqrt(point);

                    vertices.Add(new Vector3(-half, 0f, along));
                    vertices.Add(new Vector3(half, 0f, along));
                }

                for (int i = 0; i < LineSegments; i++)
                {
                    int at = i * 2;
                    AddDoubleQuad(at, at + 1, at + 2, at + 3);
                }
            }

            Apply(mesh);
        }

        /// <summary>
        /// The wall of light along the line: a slab with two sides and a top,
        /// so it still reads from a steep camera looking straight down it.
        /// Its height follows <see cref="WallHeight"/>, so it rises just
        /// behind the front and has fallen away again by the time the line
        /// fades.
        /// </summary>
        private void BuildWall()
        {
            vertices.Clear();
            triangles.Clear();

            float halfWidth = settings.width * 0.5f * settings.wallWidth;

            if (front > 0.0001f && halfWidth > 0f && age < WallLifetime)
            {
                for (int i = 0; i <= LineSegments; i++)
                {
                    float along = front * i / LineSegments;
                    float height = WallHeight(along);
                    float half = halfWidth * Taper(along);

                    // Bottom left, top left, top right, bottom right.
                    vertices.Add(new Vector3(-half, 0f, along));
                    vertices.Add(new Vector3(-half, height, along));
                    vertices.Add(new Vector3(half, height, along));
                    vertices.Add(new Vector3(half, 0f, along));
                }

                for (int i = 0; i < LineSegments; i++)
                {
                    int at = i * 4;
                    int next = at + 4;

                    AddDoubleQuad(at, at + 1, next, next + 1);
                    AddDoubleQuad(at + 1, at + 2, next + 1, next + 2);
                    AddDoubleQuad(at + 2, at + 3, next + 2, next + 3);
                }

                // Closed off at the player's end, where it stands tallest last.
                AddDoubleQuad(0, 1, 3, 2);
            }

            Apply(wallMesh);
        }

        /// <summary>
        /// A crescent in the upright plane of the slam, about the shoulder:
        /// thin at the overhead end the axe left and full at the floor end it
        /// arrived at, like a smear of its swing.
        /// </summary>
        private void BuildArc(float thickness)
        {
            vertices.Clear();
            triangles.Clear();

            if (thickness > 0f && settings.arcRadius > 0f)
            {
                Vector3 centre = new Vector3(0f, arcCentreHeight, 0f);
                float outer = settings.arcRadius;

                for (int i = 0; i <= ArcSegments; i++)
                {
                    float t = i / (float)ArcSegments;
                    float angle = Mathf.Lerp(arcFrom, arcTo, t) * Mathf.Deg2Rad;
                    Vector3 radial = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));

                    float width = settings.arcWidth * thickness * t * t;
                    float inner = Mathf.Max(0f, outer - width);

                    vertices.Add(centre + radial * inner);
                    vertices.Add(centre + radial * outer);
                }

                for (int i = 0; i < ArcSegments; i++)
                {
                    int at = i * 2;
                    AddDoubleQuad(at, at + 1, at + 2, at + 3);
                }
            }

            Apply(arcMesh);
        }

        /// <summary>A quad wound both ways, so it shows from either side.</summary>
        private void AddDoubleQuad(int a, int b, int c, int d)
        {
            triangles.AddRange(new[] { a, c, b, b, c, d });
            triangles.AddRange(new[] { a, b, c, b, d, c });
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
            Destroy(lineMesh);
            Destroy(wallMesh);
            Destroy(arcMesh);
        }
    }
}
