using System;
using LightAndShadow.Rendering;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A black hole turned inside out, lying on the floor: a white middle
    /// ringed by swirling rainbow arms, growing to its full size, pulling the
    /// player in and burning them in the white.
    /// </summary>
    /// <remarks>
    /// Lies there unseen, doing nothing, until <see cref="Open"/>; then it
    /// opens from nothing and grows to <see cref="Settings.maxRadius"/> over
    /// <see cref="Settings.growTime"/>, quickly at first and easing in. The
    /// inner <see cref="Settings.coreFraction"/> of it is the white middle;
    /// the rest is <see cref="Settings.armCount"/> arms, each a colour round
    /// the rainbow, curled <see cref="Settings.armTwist"/> degrees from the
    /// white to the rim, the whole turning at <see cref="Settings.spinSpeed"/>.
    ///
    /// The player is pulled toward the middle, harder as it grows, up to
    /// <see cref="Settings.pullStrength"/> at full size, from anywhere within
    /// <see cref="Settings.pullRange"/>. Standing in the white takes
    /// <see cref="Settings.damagePerSecond"/>, in small hits every
    /// <see cref="Settings.tickInterval"/> seconds, the first the moment they
    /// step in. Neither happens while <see cref="Hurts"/> is off.
    ///
    /// Space bends round it: a <see cref="NeoNoirLens"/>
    /// <see cref="Settings.lensScale"/> times its size squeezes the world in
    /// toward the white, swirls it round, ripples it and splits its colours,
    /// growing with it.
    ///
    /// Gone by itself if the enemy that made it is. Spawned at runtime and
    /// built from meshes of its own on the shared NeoNoir/Flat material, so it
    /// needs no prefab.
    /// </remarks>
    public sealed class WhiteHole : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Shape")]
            [Tooltip("Radius it grows to, rainbow and all.")]
            [Min(0.1f)] public float maxRadius = 22f;

            [Tooltip("Seconds it takes to grow from nothing to full size.")]
            [Min(0.01f)] public float growTime = 15f;

            [Tooltip("Share of the radius that is the white middle. The rainbow fills the rest.")]
            [Range(0.05f, 0.95f)] public float coreFraction = 0.45f;

            [Tooltip("Height above the floor it is drawn at.")]
            [Range(0f, 0.5f)] public float floorOffset = 0.05f;

            [Header("Look")]
            [Tooltip("Colour of the middle. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color coreColour = new Color(4f, 4f, 4f, 1f);

            [Tooltip("Rainbow arms round the middle, one colour each.")]
            [Range(3, 48)] public int armCount = 12;

            [Tooltip("Degrees each arm curls round from the white to the rim.")]
            public float armTwist = 120f;

            [Tooltip("Degrees per second the rainbow turns.")]
            public float spinSpeed = 90f;

            [Tooltip("Saturation of the rainbow. Keep it high, or the spot colour threshold crushes it.")]
            [Range(0f, 1f)] public float saturation = 1f;

            [Tooltip("Brightness of the rainbow. Above 1 blooms.")]
            [Min(0f)] public float rainbowIntensity = 2.5f;

            [Header("Warp")]
            [Tooltip("How far out space bends, as a multiple of the hole's radius. Nothing past it is touched.")]
            [Min(1f)] public float lensScale = 2.5f;

            [Tooltip("How far the world is squeezed in toward the white, as a share of the bent area's radius. Higher shows more of the world crammed round the middle.")]
            [Range(0f, 1f)] public float lensPull = 0.35f;

            [Tooltip("Degrees the world is twisted round the middle, at the white, easing off outward. Turns the same way as the hole.")]
            public float lensSwirl = 100f;

            [Tooltip("Degrees the ripples running in toward the middle twist the world by.")]
            public float lensRipple = 12f;

            [Tooltip("Ripples across the bent area.")]
            [Min(0f)] public float lensRipples = 4f;

            [Tooltip("How fast the ripples run in.")]
            public float lensRippleSpeed = 4f;

            [Tooltip("Share the squeeze throbs by.")]
            [Range(0f, 1f)] public float lensPulse = 0.2f;

            [Tooltip("How far red and blue split apart near the middle, as a share of the squeeze. 0 keeps colours together.")]
            [Range(0f, 0.5f)] public float lensChroma = 0.08f;

            [Header("Player")]
            [Tooltip("Damage a second to the player in the white middle.")]
            [Min(0f)] public float damagePerSecond = 20f;

            [Tooltip("Seconds between the small hits the damage is dealt in.")]
            [Min(0.02f)] public float tickInterval = 0.25f;

            [Tooltip("How hard the player is pulled toward the middle at full size, in units per second per second. It grows with the hole. 0 pulls nothing.")]
            [Min(0f)] public float pullStrength = 12f;

            [Tooltip("The player is pulled from no further than this from the middle.")]
            [Min(0f)] public float pullRange = 60f;
        }

        private const int CoreSegments = 64;
        private const int ArmRadialSteps = 16;
        private const int ArmAngularSteps = 4;

        private static Mesh coreMesh;
        private static Mesh armMesh;
        private static float meshCoreFraction = -1f;
        private static float meshTwist;
        private static int meshArms;

        private Settings settings;
        private Enemy owner;
        private PlayerHealth player;
        private PlayerMotor motor;
        private Transform swirl;
        private Renderer[] pieces;
        private float spinWay;
        private bool opened;
        private float age;
        private float tickWait;

        /// <summary>Where it lies, on the floor.</summary>
        public Vector3 Centre { get; private set; }

        /// <summary>Its radius now, rainbow and all.</summary>
        public float Radius { get; private set; }

        /// <summary>Radius of the white middle now.</summary>
        public float CoreRadius => Radius * settings.coreFraction;

        /// <summary>Whether it has grown to full size.</summary>
        public bool Full => opened && age >= settings.growTime;

        /// <summary>Whether it pulls and burns the player. On from the start.</summary>
        public bool Hurts { get; set; } = true;

        /// <summary>
        /// Opens a hole on the floor at <paramref name="floorPoint"/>, made
        /// by <paramref name="madeBy"/>, pulling and burning
        /// <paramref name="target"/>. <paramref name="way"/> is 1 to turn
        /// anticlockwise seen from above, -1 clockwise.
        /// </summary>
        public static WhiteHole Create(Vector3 floorPoint, Enemy madeBy, Transform target, float way, Settings settings)
        {
            GameObject host = new GameObject("WhiteHole");
            host.transform.position = floorPoint + Vector3.up * settings.floorOffset;

            WhiteHole hole = host.AddComponent<WhiteHole>();
            hole.Begin(floorPoint, madeBy, target, way, settings);
            return hole;
        }

        private void Begin(Vector3 floorPoint, Enemy madeBy, Transform target, float way, Settings holeSettings)
        {
            settings = holeSettings;
            owner = madeBy;
            spinWay = way < 0f ? -1f : 1f;
            Centre = floorPoint;

            if (target != null)
            {
                player = target.GetComponentInParent<PlayerHealth>();
                motor = target.GetComponentInParent<PlayerMotor>();
            }

            BuildMeshes();

            swirl = new GameObject("Swirl").transform;
            swirl.SetParent(transform, false);

            pieces = new Renderer[settings.armCount + 1];
            pieces[0] = Piece("Core", transform, coreMesh, settings.coreColour);

            // Mirrored for a clockwise turn, so the arms trail behind it either way.
            swirl.localScale = new Vector3(1f, 1f, spinWay);

            for (int i = 0; i < settings.armCount; i++)
            {
                Color colour = Color.HSVToRGB((float)i / settings.armCount, settings.saturation, 1f) * settings.rainbowIntensity;
                colour.a = 1f;

                Renderer arm = Piece("Arm", swirl, armMesh, colour);
                arm.transform.localRotation = Quaternion.AngleAxis(360f * i / settings.armCount, Vector3.up);
                pieces[i + 1] = arm;
            }

            Grow();
        }

        /// <summary>Starts it growing, if it has not already.</summary>
        public void Open()
        {
            opened = true;
        }

        private static Renderer Piece(string pieceName, Transform parent, Mesh mesh, Color colour)
        {
            GameObject piece = new GameObject(pieceName);
            piece.transform.SetParent(parent, false);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.DoubleSided;

            if (renderer.sharedMaterial != null)
            {
                FlatMaterial.Tint(renderer, colour, new MaterialPropertyBlock());
            }

            return renderer;
        }

        private void Update()
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            if (!opened)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            Grow();

            // A positive turn about up is clockwise seen from above.
            swirl.Rotate(Vector3.up, -spinWay * settings.spinSpeed * deltaTime, Space.World);

            if (!Hurts || player == null || player.IsDead)
            {
                tickWait = 0f;
                return;
            }

            Vector3 offset = Centre - player.transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            float grown = Radius / settings.maxRadius;

            if (motor != null && distance > 0.05f && distance <= settings.pullRange)
            {
                motor.AddImpulse(offset / distance * (settings.pullStrength * grown * deltaTime));
            }

            tickWait -= deltaTime;

            if (distance <= CoreRadius)
            {
                if (tickWait <= 0f)
                {
                    player.TakeDamage(settings.damagePerSecond * settings.tickInterval, Centre);
                    tickWait = settings.tickInterval;
                }
            }
            else
            {
                // Hurt the moment they step back in.
                tickWait = Mathf.Min(tickWait, 0f);
            }
        }

        private void LateUpdate()
        {
            if (!opened || Radius <= 0.01f)
            {
                return;
            }

            float lensRadius = Radius * settings.lensScale;

            NeoNoirLens.Show(this, new NeoNoirLens.Lens
            {
                centre = Centre,
                radius = lensRadius,
                coreFraction = CoreRadius / lensRadius,
                pull = settings.lensPull,
                swirl = settings.lensSwirl * spinWay,
                chroma = settings.lensChroma,
                ripple = settings.lensRipple,
                ripples = settings.lensRipples,
                rippleSpeed = settings.lensRippleSpeed,
                pulse = settings.lensPulse,
                coreColour = settings.coreColour,
            });
        }

        private void OnDestroy()
        {
            NeoNoirLens.Hide(this);
        }

        private void Grow()
        {
            float t = opened ? Mathf.Clamp01(age / settings.growTime) : 0f;
            Radius = settings.maxRadius * (1f - (1f - t) * (1f - t));

            bool shown = Radius > 0.01f;

            foreach (Renderer piece in pieces)
            {
                piece.enabled = shown;
            }

            transform.localScale = new Vector3(Radius, 1f, Radius);
        }

        /// <summary>
        /// The white disc and one rainbow arm, at a radius of 1. Shared by
        /// every hole with the same shape; the arm is turned for each of them.
        /// </summary>
        private void BuildMeshes()
        {
            if (coreMesh != null && armMesh != null && Mathf.Approximately(meshCoreFraction, settings.coreFraction)
                && Mathf.Approximately(meshTwist, settings.armTwist) && meshArms == settings.armCount)
            {
                return;
            }

            meshCoreFraction = settings.coreFraction;
            meshTwist = settings.armTwist;
            meshArms = settings.armCount;

            coreMesh = BuildCore(settings.coreFraction);
            armMesh = BuildArm(settings.coreFraction, 2f * Mathf.PI / settings.armCount, settings.armTwist * Mathf.Deg2Rad);
        }

        private static Mesh BuildCore(float radius)
        {
            Vector3[] vertices = new Vector3[CoreSegments + 1];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[CoreSegments * 3];

            normals[0] = Vector3.up;

            for (int i = 0; i < CoreSegments; i++)
            {
                float angle = 2f * Mathf.PI * i / CoreSegments;

                // A hair above the arms, so the two never fight where they meet.
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0.002f, Mathf.Sin(angle) * radius);
                normals[i + 1] = Vector3.up;

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % CoreSegments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            vertices[0] = new Vector3(0f, 0.002f, 0f);

            Mesh mesh = new Mesh { name = "WhiteHoleCore" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// One arm: a band from <paramref name="inner"/> out to the rim,
        /// <paramref name="width"/> radians wide, curling
        /// <paramref name="twist"/> radians on the way out.
        /// </summary>
        private static Mesh BuildArm(float inner, float width, float twist)
        {
            int across = ArmAngularSteps + 1;
            Vector3[] vertices = new Vector3[(ArmRadialSteps + 1) * across];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[ArmRadialSteps * ArmAngularSteps * 6];

            for (int r = 0; r <= ArmRadialSteps; r++)
            {
                float out01 = (float)r / ArmRadialSteps;
                float radius = Mathf.Lerp(inner, 1f, out01);
                // Curled back against an anticlockwise turn, so the arms trail.
                float curl = -twist * out01;

                for (int a = 0; a < across; a++)
                {
                    float angle = curl + width * a / ArmAngularSteps;
                    vertices[r * across + a] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    normals[r * across + a] = Vector3.up;
                }
            }

            int at = 0;

            for (int r = 0; r < ArmRadialSteps; r++)
            {
                for (int a = 0; a < ArmAngularSteps; a++)
                {
                    int corner = r * across + a;
                    triangles[at++] = corner;
                    triangles[at++] = corner + 1;
                    triangles[at++] = corner + across;
                    triangles[at++] = corner + 1;
                    triangles[at++] = corner + across + 1;
                    triangles[at++] = corner + across;
                }
            }

            Mesh mesh = new Mesh { name = "WhiteHoleArm" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
