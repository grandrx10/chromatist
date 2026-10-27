using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// A pillar of light from the sky to the floor: it strikes down thin,
    /// widens to its full radius, streaked with lines of light pouring down
    /// it, and dissolves when told to.
    /// </summary>
    /// <remarks>
    /// Opaque by default: a solid column of light on NeoNoir/Flat, hiding
    /// everything inside it, with streaks of a brighter colour falling down
    /// its surface. See-through instead, with <see cref="Settings.opaque"/>
    /// off, it is three parts on NeoNoir/Barrier, each a stipple of solid
    /// pixels rather than real transparency -- which this game's two-tone
    /// look cannot show:
    ///   Shell   A tube at the full radius, rimmed bright at its silhouette
    ///           with a light stipple across its face.
    ///   Core    A narrower, denser column down the middle.
    ///   Streaks Thin lines falling down inside the pillar.
    /// Either way the streaks are turned to the camera and wrap back to the
    /// top when they reach the floor.
    ///
    /// In order: the pillar strikes down from <see cref="Settings.height"/>
    /// to the floor over <see cref="Settings.strikeTime"/>, shaking the
    /// camera as it lands, then widens over <see cref="Settings.growTime"/>.
    /// <see cref="IsFull"/> from then. <see cref="Withdraw"/> narrows it back
    /// to a thin beam, which then pulls up off the floor into the sky, and
    /// destroys it.
    ///
    /// For as long as it touches the floor, dust is thrown out from where
    /// it meets it, with a burst as it first strikes. The dust is the same
    /// as a <see cref="DustTrail"/>'s.
    ///
    /// Built entirely in code: <see cref="Create"/> makes one.
    /// </remarks>
    public sealed class LightPillar : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Radius once fully grown, in metres.")]
            [Min(0.1f)] public float radius = 7f;

            [Tooltip("Radius as it first strikes down, in metres.")]
            [Min(0.05f)] public float strikeRadius = 0.4f;

            [Tooltip("How high above the floor the pillar starts, in metres.")]
            [Min(1f)] public float height = 80f;

            [Tooltip("Seconds the pillar takes to strike down from the sky to the floor.")]
            [Min(0.01f)] public float strikeTime = 0.35f;

            [Tooltip("Seconds it takes to widen to full radius once it has struck.")]
            [Min(0.01f)] public float growTime = 2.5f;

            [Tooltip("Seconds it takes to narrow back to a thin beam when withdrawn. It then pulls up into the sky over the strike time.")]
            [FormerlySerializedAs("fadeTime")]
            [Min(0.01f)] public float shrinkTime = 1.2f;

            [Tooltip("How far the camera shakes as the pillar hits the floor. 0 disables it.")]
            [Min(0f)] public float strikeShake = 0.6f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(4f, 0.3f, 0.25f);

            [Tooltip("Material to draw with. Empty makes a NeoNoir/Barrier one.")]
            public Material material;

            [Header("Shell")]
            [Tooltip("A solid column that hides everything inside it. Off makes it a see-through stipple, with a denser core.")]
            public bool opaque = true;

            [Tooltip("Material for the solid column. Empty makes a NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material opaqueMaterial;

            [Tooltip("See-through only: how densely the shell's face is stippled, 0 clear to 1 solid.")]
            [Range(0f, 1f)] public float shellFill = 0.12f;

            [Tooltip("See-through only: how densely the shell's silhouette is stippled, 0 clear to 1 solid.")]
            [Range(0f, 1f)] public float shellRim = 0.9f;

            [Header("Core (see-through only)")]
            [Tooltip("Radius of the denser column down the middle, as a fraction of the pillar's.")]
            [Range(0f, 1f)] public float coreRadius = 0.3f;

            [Tooltip("How densely the core column is stippled, 0 clear to 1 solid.")]
            [Range(0f, 1f)] public float coreFill = 0.55f;

            [Header("Streaks")]
            [Tooltip("How many lines are falling at once.")]
            [Min(0)] public int streakCount = 90;

            [Tooltip("Shortest and longest streak, in metres.")]
            public Vector2 streakLength = new Vector2(3f, 9f);

            [Tooltip("Slowest and fastest a streak falls, in metres per second.")]
            public Vector2 streakSpeed = new Vector2(25f, 45f);

            [Tooltip("Width of each streak, in metres.")]
            [Min(0.01f)] public float streakWidth = 0.08f;

            [Tooltip("Colour of the falling lines. Brighter than the pillar, so they show against it. HDR: above 1 blooms.")]
            [ColorUsage(false, true)] public Color streakColour = new Color(6f, 4.5f, 4.5f);

            [Header("Dust")]
            [Tooltip("Puffs per second thrown out from where the pillar meets the floor.")]
            [Min(0f)] public float dustRate = 40f;

            [Tooltip("Puffs thrown out at once as the pillar first strikes the floor.")]
            [Min(0)] public int strikeBurst = 30;

            [Tooltip("Slowest and fastest a puff is thrown outward, in metres per second.")]
            public Vector2 dustSpeed = new Vector2(3f, 8f);

            [Tooltip("Slowest and fastest a puff rises, in metres per second.")]
            public Vector2 dustRise = new Vector2(0.5f, 2.5f);

            [Tooltip("How the puffs look: their size, life and colour. Their spacing, speed and drift settings are not used here.")]
            public DustTrail.Settings dust = new DustTrail.Settings
            {
                size = 0.5f,
                lifetime = 0.9f,
            };
        }

        private struct Streak
        {
            public float angle;
            public float radius;
            public float length;
            public float speed;
            public float offset;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FaceFillId = Shader.PropertyToID("_FaceFill");
        private static readonly int RimFillId = Shader.PropertyToID("_RimFill");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");

        private const int TubeSegments = 48;

        private Settings settings;
        private Material ownMaterial;
        private Material ownOpaqueMaterial;
        private MaterialPropertyBlock block;

        private Transform shell;
        private Transform core;
        private MeshRenderer shellRenderer;
        private MeshRenderer coreRenderer;
        private MeshRenderer streakRenderer;
        private Mesh tube;
        private Mesh streakMesh;

        private Streak[] streaks;
        private readonly List<Vector3> streakVertices = new List<Vector3>();
        private readonly List<Vector3> streakNormals = new List<Vector3>();
        private readonly List<int> streakTriangles = new List<int>();

        private float clock;
        private float withdrawClock = -1f;
        private float withdrawFrom;
        private bool struck;
        private DustTrail.Cloud cloud;
        private float dustOwed;

        /// <summary>True once grown to full radius, until withdrawn.</summary>
        public bool IsFull => withdrawClock < 0f && clock >= settings.strikeTime + settings.growTime;

        /// <summary>The pillar's radius right now, in metres.</summary>
        public float Radius { get; private set; }

        /// <summary>Strikes a pillar of light down onto <paramref name="floor"/>, starting at once.</summary>
        public static LightPillar Create(Vector3 floor, Settings settings)
        {
            GameObject host = new GameObject("LightPillar");
            host.transform.position = floor;

            LightPillar pillar = host.AddComponent<LightPillar>();
            pillar.Begin(settings);
            return pillar;
        }

        /// <summary>
        /// Narrows the pillar back to a thin beam over its shrink time, then
        /// pulls it up into the sky and destroys it.
        /// </summary>
        public void Withdraw()
        {
            if (withdrawClock < 0f)
            {
                withdrawClock = 0f;
                withdrawFrom = Radius;
            }
        }

        private void Begin(Settings pillarSettings)
        {
            settings = pillarSettings;
            block = new MaterialPropertyBlock();

            Material material = settings.material;

            if (material == null)
            {
                Shader barrier = Shader.Find("NeoNoir/Barrier");

                if (barrier != null)
                {
                    ownMaterial = new Material(barrier);
                    material = ownMaterial;
                }
            }

            Material shellMaterial = material;

            if (settings.opaque)
            {
                shellMaterial = settings.opaqueMaterial;

                if (shellMaterial == null)
                {
                    Shader flat = Shader.Find("NeoNoir/Flat");

                    if (flat != null)
                    {
                        ownOpaqueMaterial = new Material(flat);
                        shellMaterial = ownOpaqueMaterial;
                    }
                }
            }

            tube = BuildTube();
            shell = MakePart("Shell", tube, shellMaterial, out shellRenderer);

            // A solid shell would hide the core anyway.
            if (!settings.opaque)
            {
                core = MakePart("Core", tube, material, out coreRenderer);
            }

            streakMesh = new Mesh { name = "Pillar streaks" };
            streakMesh.MarkDynamic();
            MakePart("Streaks", streakMesh, material, out streakRenderer);

            streaks = new Streak[settings.streakCount];

            for (int i = 0; i < streaks.Length; i++)
            {
                streaks[i] = new Streak
                {
                    angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f),

                    // Square-rooted, so they spread evenly over the disc
                    // rather than bunching in the middle.
                    radius = Mathf.Sqrt(UnityEngine.Random.Range(0.02f, 1f)),
                    length = UnityEngine.Random.Range(settings.streakLength.x, settings.streakLength.y),
                    speed = UnityEngine.Random.Range(settings.streakSpeed.x, settings.streakSpeed.y),
                    offset = UnityEngine.Random.Range(0f, settings.height),
                };
            }

            Update();
        }

        private Transform MakePart(string label, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            GameObject part = new GameObject(label);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part.transform;
        }

        /// <summary>
        /// An open tube of radius 1 from 0 to 1 high, with outward normals
        /// for the rim and no panel edges: every UV is zero.
        /// </summary>
        private static Mesh BuildTube()
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int i = 0; i <= TubeSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / TubeSegments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices.Add(radial);
                vertices.Add(radial + Vector3.up);
                normals.Add(radial);
                normals.Add(radial);
            }

            for (int i = 0; i < TubeSegments; i++)
            {
                int at = i * 2;
                triangles.AddRange(new[] { at, at + 1, at + 2, at + 2, at + 1, at + 3 });
            }

            Mesh mesh = new Mesh { name = "Pillar tube" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, new Vector2[vertices.Count]);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            clock += deltaTime;

            float bottom;

            if (withdrawClock < 0f)
            {
                // Strikes down from the sky first: the bottom falls to the floor.
                float strike = Mathf.Clamp01(clock / settings.strikeTime);
                bottom = settings.height * (1f - strike * strike);

                if (strike >= 1f && !struck)
                {
                    struck = true;
                    Burst(settings.strikeBurst);

                    if (settings.strikeShake > 0f)
                    {
                        TopDownCamera.Shake(settings.strikeShake, 0.5f);
                    }
                }

                // Then widens, quickly at first and settling into full size.
                float grow = Mathf.Clamp01((clock - settings.strikeTime) / settings.growTime);
                grow = 1f - (1f - grow) * (1f - grow) * (1f - grow);
                Radius = Mathf.Lerp(settings.strikeRadius, settings.radius, grow);
            }
            else
            {
                withdrawClock += deltaTime;

                // Narrows, slowly at first and then quickly...
                float shrink = Mathf.Clamp01(withdrawClock / settings.shrinkTime);
                Radius = Mathf.Lerp(withdrawFrom, settings.strikeRadius, shrink * shrink);

                // ...then the thin beam that is left pulls up off the floor.
                float lift = Mathf.Clamp01((withdrawClock - settings.shrinkTime) / settings.strikeTime);
                bottom = settings.height * lift * lift;

                if (lift >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            float span = settings.height - bottom;
            Place(shell, Radius, bottom, span);
            Paint(shellRenderer, settings.colour, settings.shellFill, settings.shellRim);

            if (core != null)
            {
                Place(core, Radius * settings.coreRadius, bottom, span);
                Paint(coreRenderer, settings.colour * 1.3f, settings.coreFill, 1f);
            }

            Paint(streakRenderer, settings.streakColour, 1f, 1f);

            BuildStreaks(bottom);

            // Dust only while it stands on the floor.
            if (bottom <= 0.01f)
            {
                dustOwed += settings.dustRate * deltaTime;
                int puffs = Mathf.FloorToInt(dustOwed);
                dustOwed -= puffs;
                Burst(puffs);
            }
        }

        /// <summary>Throws <paramref name="count"/> puffs of dust out from round the foot of the pillar.</summary>
        private void Burst(int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (cloud == null)
            {
                cloud = DustTrail.Cloud.Create("Pillar dust");
            }

            Vector3 floor = transform.position;

            for (int i = 0; i < count; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                Vector3 velocity = outward * UnityEngine.Random.Range(settings.dustSpeed.x, settings.dustSpeed.y)
                                   + Vector3.up * UnityEngine.Random.Range(settings.dustRise.x, settings.dustRise.y);

                cloud.Emit(floor + outward * Radius + Vector3.up * (settings.dust.size * 0.3f), velocity, settings.dust);
            }
        }

        private static void Place(Transform part, float radius, float bottom, float span)
        {
            part.localPosition = Vector3.up * bottom;
            part.localScale = new Vector3(radius, Mathf.Max(0.001f, span), radius);
        }

        private void Paint(Renderer target, Color colour, float fill, float rim)
        {
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetFloat(FaceFillId, fill);
            block.SetFloat(RimFillId, rim);
            block.SetFloat(EdgeWidthId, 0f);
            target.SetPropertyBlock(block);
        }

        /// <summary>Lays every falling streak as a thin quad turned to the camera.</summary>
        private void BuildStreaks(float bottom)
        {
            streakVertices.Clear();
            streakNormals.Clear();
            streakTriangles.Clear();

            Camera view = Camera.main;
            Vector3 toCamera = view != null ? -view.transform.forward : Vector3.back;

            // Across the streak, square to the view; looking straight down,
            // fall back to the camera's right.
            Vector3 side = Vector3.Cross(Vector3.up, toCamera);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : (view != null ? view.transform.right : Vector3.right);
            side *= settings.streakWidth * 0.5f;

            float top = settings.height;

            foreach (Streak streak in streaks)
            {
                // The head falls from the top, the tail trailing above it,
                // and both wrap round to the top again.
                float cycle = top + streak.length;
                float head = top - Mathf.Repeat(streak.offset + streak.speed * clock, cycle);
                float tail = head + streak.length;

                head = Mathf.Max(head, bottom);
                tail = Mathf.Min(tail, top);

                if (tail - head < 0.01f)
                {
                    continue;
                }

                // On a solid pillar they run down just outside its surface,
                // and the pillar hides those round the back.
                float distance = settings.opaque ? Radius * 1.02f : streak.radius * Radius;
                Vector3 foot = new Vector3(Mathf.Cos(streak.angle), 0f, Mathf.Sin(streak.angle)) * distance;
                Vector3 low = foot + Vector3.up * head;
                Vector3 high = foot + Vector3.up * tail;

                int at = streakVertices.Count;
                streakVertices.Add(low - side);
                streakVertices.Add(low + side);
                streakVertices.Add(high - side);
                streakVertices.Add(high + side);

                for (int i = 0; i < 4; i++)
                {
                    streakNormals.Add(toCamera);
                }

                streakTriangles.Add(at); streakTriangles.Add(at + 2); streakTriangles.Add(at + 1);
                streakTriangles.Add(at + 1); streakTriangles.Add(at + 2); streakTriangles.Add(at + 3);
            }

            streakMesh.Clear();
            streakMesh.SetVertices(streakVertices);
            streakMesh.SetNormals(streakNormals);
            streakMesh.SetUVs(0, new Vector2[streakVertices.Count]);
            streakMesh.SetTriangles(streakTriangles, 0);
            streakMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            // Puffs already thrown settle on their own.
            if (cloud != null)
            {
                cloud.Release();
            }

            Destroy(tube);
            Destroy(streakMesh);

            if (ownMaterial != null)
            {
                Destroy(ownMaterial);
            }

            if (ownOpaqueMaterial != null)
            {
                Destroy(ownOpaqueMaterial);
            }
        }
    }
}
