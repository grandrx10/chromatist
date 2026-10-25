using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

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
    /// Drawn as a wide dim glow with a bright crack down its middle, both
    /// tapering to a point at the front, so the far end reads as the line
    /// still splitting. Shards of light shoot straight up off the line as the
    /// front passes, and a few keep rising off it while it holds. Over the
    /// player, a crescent in the upright plane of the slam shows the path the
    /// axe came down along, and fades fast.
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

            [Header("Line")]
            [Tooltip("Width of the bright crack, as a fraction of the line's.")]
            [Range(0f, 1f)] public float core = 0.2f;

            [Tooltip("How far back from the front the line tapers to its point.")]
            [Min(0.01f)] public float frontTaper = 1.5f;

            [Tooltip("Width the line narrows to at its far end, as a fraction of its width at the player.")]
            [Range(0f, 1f)] public float farWidth = 0.4f;

            [Tooltip("Colour intensity of the crack. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float coreIntensity = 3f;

            [Tooltip("Colour intensity of the glow round the crack. Keep it below the crack's so the crack still reads.")]
            [Min(0f)] public float glowIntensity = 0.7f;

            [Header("Shards")]
            [Tooltip("Shards thrown up per unit of line as the front passes.")]
            [Min(0f)] public float shardsPerUnit = 4f;

            [Tooltip("Shards per second rising off the whole line while it holds.")]
            [Min(0f)] public float lingerShardsPerSecond = 25f;

            [Tooltip("Speed shards rise at.")]
            [Min(0f)] public float shardRiseSpeed = 5f;

            [Tooltip("Shortest and longest a shard grows to.")]
            public Vector2 shardLength = new Vector2(0.5f, 1.6f);

            [Tooltip("Width of a shard.")]
            [Min(0.005f)] public float shardWidth = 0.06f;

            [Tooltip("Seconds a shard lives.")]
            [Min(0.05f)] public float shardLifetime = 0.4f;

            [Tooltip("Colour intensity of the shards.")]
            [Min(0f)] public float shardIntensity = 2f;

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
        private const int MaxShards = 384;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private struct Shard
        {
            public Vector3 Base;
            public float Length;
            public float Age;
            public float Lifetime;
        }

        private readonly List<Shard> shards = new List<Shard>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly HashSet<EnemyHealth> hit = new HashSet<EnemyHealth>();

        private Settings settings;
        private float age;
        private float front;
        private float lingerDebt;

        private Vector3 direction;
        private float damageHeight;
        private Color slashTint;

        // The arc, in this object's local space: from the shoulder, sweeping
        // from overhead behind to the floor in front.
        private float arcCentreHeight;
        private float arcFrom;
        private float arcTo;

        private Mesh glowMesh;
        private Mesh coreMesh;
        private Mesh shardMesh;
        private Mesh arcMesh;

        private MeshRenderer glowRenderer;
        private MeshRenderer coreRenderer;
        private MeshRenderer shardRenderer;
        private MeshRenderer arcRenderer;

        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;
        private bool hasFade;

        private float Lifetime => settings.waveTime + settings.holdTime + settings.fadeTime;

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
                                       Settings settings, Material material, Color tint)
        {
            GameObject host = new GameObject("AmberSlam");
            AmberSlam slam = host.AddComponent<AmberSlam>();
            slam.Begin(floorPoint, slamDirection, hitHeight, shoulderHeight, swingFrom, swingTo, settings, material, tint);
            return slam;
        }

        private void Begin(Vector3 floorPoint, Vector3 slamDirection, float hitHeight,
                           float shoulderHeight, float swingFrom, float swingTo,
                           Settings slamSettings, Material material, Color tint)
        {
            settings = slamSettings;
            slashTint = tint;
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

            // Glow first and a hair lower, so the crack always wins the
            // pixels they share.
            glowRenderer = AddPart("Glow", material, Vector3.down * 0.005f, out glowMesh);
            coreRenderer = AddPart("Crack", material, Vector3.zero, out coreMesh);
            shardRenderer = AddPart("Shards", material, Vector3.zero, out shardMesh);
            arcRenderer = AddPart("Arc", material, Vector3.zero, out arcMesh);

            Advance(0f);
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

            Advance(deltaTime);
            AgeShards(deltaTime);
            UpdateVisuals();
        }

        /// <summary>
        /// Runs the front out to where it should be by now, hurting what it
        /// passes and throwing shards up along the stretch it just split.
        /// </summary>
        private void Advance(float deltaTime)
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
                                  slashTint: slashTint);

                SpawnShards(was, front, Mathf.RoundToInt((front - was) * settings.shardsPerUnit));
            }

            if (front >= settings.length && LineVisibility() >= 1f)
            {
                lingerDebt += settings.lingerShardsPerSecond * deltaTime;
                int count = Mathf.FloorToInt(lingerDebt);
                lingerDebt -= count;

                SpawnShards(0f, settings.length, count);
            }
        }

        private void SpawnShards(float from, float to, int count)
        {
            float halfWidth = settings.width * 0.5f;

            for (int i = 0; i < count && shards.Count < MaxShards; i++)
            {
                float along = Random.Range(from, to);

                // Most hug the crack; a few spray out to the glow's edge.
                float across = Random.Range(-1f, 1f);
                across = across * across * across * halfWidth * Taper(along);

                shards.Add(new Shard
                {
                    Base = new Vector3(across, 0f, along),
                    Length = Random.Range(settings.shardLength.x, Mathf.Max(settings.shardLength.x, settings.shardLength.y)),
                    Lifetime = settings.shardLifetime * Random.Range(0.6f, 1f),
                });
            }
        }

        private void AgeShards(float deltaTime)
        {
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                Shard shard = shards[i];
                shard.Age += deltaTime;

                if (shard.Age >= shard.Lifetime)
                {
                    shards.RemoveAt(i);
                    continue;
                }

                shards[i] = shard;
            }
        }

        /// <summary>1 until the last <see cref="Settings.fadeTime"/> seconds, then down to 0.</summary>
        private float LineVisibility()
        {
            return Mathf.Clamp01((Lifetime - age) / settings.fadeTime);
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

            BuildLine(glowMesh, settings.width * thin);
            BuildLine(coreMesh, settings.width * settings.core * thin);
            BuildShards();

            float arcVisibility = 1f - Mathf.Clamp01(age / settings.arcTime);
            BuildArc(hasFade ? 1f : arcVisibility);

            Paint(glowRenderer, settings.glowIntensity, visibility);
            Paint(coreRenderer, settings.coreIntensity, visibility);
            Paint(shardRenderer, settings.shardIntensity, visibility);
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
        /// Every live shard as an upright strip turned to face the camera:
        /// shoots up fast, grows to full length, and thins away at the end of
        /// its life.
        /// </summary>
        private void BuildShards()
        {
            vertices.Clear();
            triangles.Clear();

            Vector3 side = Vector3.right;
            Camera camera = Camera.main;

            if (camera != null)
            {
                // Across the screen, flattened, so the strips stay upright.
                Vector3 flat = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up);
                if (flat.sqrMagnitude > 0.0001f)
                {
                    side = transform.InverseTransformDirection(flat.normalized);
                }
            }

            foreach (Shard shard in shards)
            {
                float t = shard.Age / shard.Lifetime;
                float grow = Mathf.Clamp01(t / 0.2f);
                float thin = 1f - Mathf.Clamp01((t - 0.5f) / 0.5f);

                // Eased out: burst up off the floor, then slow.
                float rise = settings.shardRiseSpeed * shard.Lifetime * (1f - (1f - t) * (1f - t)) * 0.5f;

                float half = settings.shardWidth * 0.5f * thin;
                Vector3 bottom = shard.Base + Vector3.up * rise;
                Vector3 top = bottom + Vector3.up * (shard.Length * grow);

                int at = vertices.Count;
                vertices.Add(bottom - side * half);
                vertices.Add(bottom + side * half);
                vertices.Add(top - side * half);
                vertices.Add(top + side * half);
                AddDoubleQuad(at, at + 1, at + 2, at + 3);
            }

            Apply(shardMesh);
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
            Destroy(glowMesh);
            Destroy(coreMesh);
            Destroy(shardMesh);
            Destroy(arcMesh);
        }
    }
}
