using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// One volley of the Puppeteer's targeted strings: a warning circle that
    /// stalks the player, then a spray of strings from the sky into it that
    /// snare whoever they catch.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The circle appears near the player and chases them with momentum:
    ///      it speeds up toward them, overshoots when they turn, and curves
    ///      back. It fills over <see cref="Settings.fillTime"/>, then stops.
    ///   2. The strings shoot down one after another, a hair apart, each from
    ///      a random direction high in the sky to a random point in the
    ///      circle. When the first lands, a player inside the circle is caught.
    ///      Each strikes the floor with a small ring, chips, a flash and
    ///      sparks; the first shakes the camera a little.
    ///   3. For <see cref="Settings.lingerTime"/> the strings stay, slowly
    ///      fading, and a player touching any part of one is caught. Then
    ///      they let go, each snapping back up the line it came down,
    ///      whipping as it goes, the way a puppet's cut strings do.
    ///
    /// The whole volley is one projectile: it catches once at most. Caught,
    /// the player takes <see cref="Settings.damage"/> and is held by their
    /// <see cref="PlayerSnare"/>; the strings whip over to their bones and
    /// hang on until the snare ends, then snap back. A player already snared is
    /// hurt and held afresh, and the strings that had them let go.
    ///
    /// Strings that meet Luna's barrier, or a player made immune through
    /// <see cref="PlayerSnare.AddImmunity"/>, let go and snap back, doing nothing.
    ///
    /// Each string is drawn twice with the NeoNoir/String shader, a wide
    /// white line under a narrow red one, so it reads as red edged in white.
    /// Its first point is the end in the circle, its last up in the sky, so
    /// the shader fades it out toward the sky. Letting go hands each line to
    /// a <see cref="CutPuppetString"/>, which outlives the volley. If the boss
    /// dies, every volley of its still in the air lets go.
    /// </remarks>
    public sealed class TargetedStrings : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LengthId = Shader.PropertyToID("_Length");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        private static Material sharedMaterial;

        [Serializable]
        public sealed class Settings
        {
            [Header("Circle")]
            [Tooltip("Chase the player. Off, the circle stays where it appears, on the floor there.")]
            public bool tracking = true;

            [Tooltip("Closest and furthest from the player the circle appears.")]
            public Vector2 spawnDistance = new Vector2(3f, 5f);

            [Tooltip("Radius of the circle.")]
            [Min(0.1f)] public float radius = 1.25f;

            [Tooltip("Seconds the circle takes to fill. The strings come down when it is full.")]
            [Min(0.05f)] public float fillTime = 1.5f;

            [Tooltip("How hard the circle speeds up toward the player, in units per second squared.")]
            [Min(0f)] public float acceleration = 16f;

            [Tooltip("Fastest the circle moves.")]
            [Min(0f)] public float maxSpeed = 7f;

            [Tooltip("How quickly the circle loses speed. Lower keeps more momentum, so it overshoots further when the player turns.")]
            [Min(0f)] public float drag = 1.5f;

            [Tooltip("Material for the circle. Empty makes a NeoNoir/Flat one.")]
            public Material circleMaterial;

            [Tooltip("Colour of the circle.")]
            public Color circleColour = new Color(1f, 0.12f, 0.12f, 0.7f);

            [Tooltip("Width of the circle's outline.")]
            [Min(0.01f)] public float circleOutline = 0.1f;

            [Tooltip("How far above the floor the circle sits, so it does not flicker into it.")]
            [Min(0f)] public float circleLift = 0.1f;

            [Header("Strings")]
            [Tooltip("Strings in one volley.")]
            [Min(1)] public int count = 8;

            [Tooltip("Seconds between one string shooting down and the next.")]
            [Min(0f)] public float stagger = 0.03f;

            [Tooltip("Seconds a string takes to shoot from the sky to the floor.")]
            [Min(0.01f)] public float flightTime = 0.12f;

            [Tooltip("Lowest and highest angle above the horizon a string comes down from, in degrees.")]
            public Vector2 elevation = new Vector2(50f, 75f);

            [Tooltip("How far up into the sky a string reaches from where it lands.")]
            [Min(1f)] public float length = 30f;

            [Tooltip("Material for the strings. Empty makes one from the NeoNoir/String shader.")]
            public Material stringMaterial;

            [Tooltip("Colour of a string's core. Above 1 blooms.")]
            [ColorUsage(true, true)] public Color coreColour = new Color(3f, 0.15f, 0.15f, 1f);

            [Tooltip("Colour of a string's edge.")]
            [ColorUsage(true, true)] public Color edgeColour = new Color(2.5f, 2.5f, 2.5f, 1f);

            [Tooltip("Width of a string's red core.")]
            [Min(0.005f)] public float coreWidth = 0.05f;

            [Tooltip("Width of a string, edge included. Also how close the player must come to be caught.")]
            [Min(0.005f)] public float width = 0.11f;

            [Header("Timing")]
            [Tooltip("Seconds the strings stay once down, fading as they go, before flying back up.")]
            [Min(0f)] public float lingerTime = 3f;

            [Tooltip("How faded the strings are by the end of their stay. 0 is not at all, 1 gone.")]
            [Range(0f, 1f)] public float lingerFade = 0.6f;

            [Tooltip("Seconds the strings take to whip over to a caught player's bones.")]
            [Min(0.01f)] public float latchTime = 0.12f;

            [Tooltip("Seconds a string takes to snap back up the line it came down.")]
            [Min(0.05f)] public float recoilTime = 0.6f;

            [Tooltip("How far, in units, a string whips side to side as it snaps back.")]
            [Min(0f)] public float recoilWobble = 0.8f;

            [Tooltip("Length of string that snaps back, from where it was tied, as a puppet's cut string does. The rest, up in the dark, simply goes.")]
            [Min(0.5f)] public float recoilLength = 12f;

            [Tooltip("Points along a snapping string. More makes its whip smoother.")]
            [Range(4, 32)] public int recoilSegments = 16;

            [Header("Impact")]
            [Tooltip("Shockwave ring and chips kicked up where each string lands.")]
            public HammerImpact.Settings impact = new HammerImpact.Settings
            {
                ringRadius = 0.7f,
                ringWidth = 0.15f,
                ringTime = 0.25f,
                floorOffset = 0.1f,
                debrisCount = 2,
                debrisSize = 0.15f,
                debrisUpSpeed = new Vector2(3f, 5f),
                debrisOutSpeed = new Vector2(1f, 3f),
                debrisTime = 0.4f,
            };

            [Tooltip("Material for the ring and chips. Empty uses NeoNoir/Flat, white.")]
            public Material impactMaterial;

            [Tooltip("Flash and sparks where each string lands.")]
            public EnergyBurst.Settings impactBurst = new EnergyBurst.Settings
            {
                flashSize = 0.45f,
                flashTime = 0.1f,
                sparkCount = 4,
                sparkSize = 0.2f,
                sparkSpeed = new Vector2(3f, 6f),
                sparkLife = 0.2f,
                colour = new Color(3f, 0.15f, 0.15f, 1f),
            };

            [Tooltip("Camera shake as the first string lands. 0 disables it.")]
            [Min(0f)] public float impactShake = 0.15f;

            [Tooltip("Seconds the camera shake takes to die away.")]
            [Min(0.01f)] public float impactShakeTime = 0.2f;

            [Header("Hit")]
            [Tooltip("Damage to a caught player.")]
            [Min(0f)] public float damage = 15f;
        }

        private enum Phase { Tracking, Striking, Holding, Released }

        private sealed class Thread
        {
            public LineRenderer edge;
            public LineRenderer core;
            public Vector3 end;
            public Vector3 sky;
            public Vector3 tip;
            public float launchAt;
            public bool landed;
            public Transform bone;
            public Vector3 latchFrom;

            // As last drawn, so a string sitting still is not redrawn.
            public Vector3 drawnTip;
            public Vector3 drawnSky;
            public float drawnAlpha = -1f;
        }

        private static readonly List<TargetedStrings> live = new List<TargetedStrings>();

        private readonly List<Thread> threads = new List<Thread>();

        private Settings settings;
        private Enemy owner;
        private Transform player;
        private Collider playerCollider;
        private PlayerHealth playerHealth;
        private PlayerSnare snare;
        private MaterialPropertyBlock block;

        private Phase phase;
        private WarningCircle circle;
        private Vector3 position;
        private Vector3 velocity;
        private float clock;
        private float alpha = 1f;
        private bool spent;

        /// <summary>
        /// Starts a volley at <paramref name="start"/>, stalking
        /// <paramref name="target"/>, on behalf of <paramref name="boss"/>.
        /// </summary>
        public static TargetedStrings Create(Enemy boss, Transform target, Vector3 start, Settings settings)
        {
            TargetedStrings volley = new GameObject("TargetedStrings").AddComponent<TargetedStrings>();
            volley.Begin(boss, target, start, settings);
            return volley;
        }

        private void Begin(Enemy boss, Transform target, Vector3 start, Settings volley)
        {
            settings = volley;
            owner = boss;
            player = target;
            playerCollider = target.GetComponentInParent<Collider>();
            playerHealth = target.GetComponentInParent<PlayerHealth>();
            snare = PlayerSnare.For(target);
            block = new MaterialPropertyBlock();

            position = start;

            if (settings.tracking)
            {
                position.y = FeetHeight();
            }
            transform.position = position;

            circle = WarningCircle.Create(position + Vector3.up * settings.circleLift, settings.radius, settings.circleOutline,
                                          settings.circleMaterial, settings.circleColour, transform);
            circle.Fill = 0f;
            phase = Phase.Tracking;
        }

        /// <summary>True once the circle has filled and the strings have begun to come down.</summary>
        public bool HasStruck => phase != Phase.Tracking;

        /// <summary>
        /// Lets go of every volley <paramref name="boss"/> has out, wherever
        /// it is: circles vanish, strings snap back, a snared player is freed
        /// when their snare runs out. For a cutscene to clear the stage.
        /// </summary>
        public static void ReleaseAll(Enemy boss)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] != null && live[i].owner == boss)
                {
                    live[i].LetGo();
                }
            }
        }

        private void OnEnable()
        {
            live.Add(this);
        }

        private void OnDisable()
        {
            live.Remove(this);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            clock += deltaTime;

            if (phase != Phase.Released && (owner == null || owner.Health.IsDead || player == null))
            {
                LetGo();
                return;
            }

            switch (phase)
            {
                case Phase.Tracking:
                    Track(deltaTime);
                    break;

                case Phase.Striking:
                    Strike();
                    break;

                case Phase.Holding:
                    Hold();
                    break;
            }
        }

        // ---- circle ---------------------------------------------------------

        private void Track(float deltaTime)
        {
            if (!settings.tracking)
            {
                circle.Fill = clock / settings.fillTime;

                if (clock >= settings.fillTime)
                {
                    Launch();
                }

                return;
            }

            Vector3 toward = player.position - position;
            toward.y = 0f;

            if (toward.sqrMagnitude > 0.0001f)
            {
                velocity += toward.normalized * (settings.acceleration * deltaTime);
            }

            // Air drag, exact for any frame rate, then the speed limit.
            velocity *= Mathf.Exp(-settings.drag * deltaTime);
            velocity = Vector3.ClampMagnitude(velocity, settings.maxSpeed);

            position += velocity * deltaTime;
            position.y = Mathf.Lerp(position.y, FeetHeight(), 1f - Mathf.Exp(-10f * deltaTime));
            circle.transform.position = position + Vector3.up * settings.circleLift;
            circle.Fill = clock / settings.fillTime;

            if (clock >= settings.fillTime)
            {
                Launch();
            }
        }

        private float FeetHeight()
        {
            return playerCollider != null ? playerCollider.bounds.min.y : player.position.y;
        }

        // ---- strings --------------------------------------------------------

        private void Launch()
        {
            phase = Phase.Striking;
            clock = 0f;

            Material material = settings.stringMaterial != null ? settings.stringMaterial : SharedMaterial();

            for (int i = 0; i < settings.count; i++)
            {
                Vector2 spot = Random.insideUnitCircle * settings.radius;
                Vector3 end = position + new Vector3(spot.x, 0.02f, spot.y);

                float bearing = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float pitch = Random.Range(settings.elevation.x, settings.elevation.y) * Mathf.Deg2Rad;
                Vector3 up = new Vector3(Mathf.Cos(bearing) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                                         Mathf.Sin(bearing) * Mathf.Cos(pitch));

                Thread thread = new Thread
                {
                    end = end,
                    sky = end + up * settings.length,
                    launchAt = i * settings.stagger,
                };

                thread.tip = thread.sky;
                thread.edge = BuildLine(i, "Edge", material, settings.width, settings.edgeColour, 0);
                thread.core = BuildLine(i, "Core", material, settings.coreWidth, settings.coreColour, 1);
                thread.edge.enabled = thread.core.enabled = false;
                threads.Add(thread);
            }
        }

        private void Strike()
        {
            float lastLaunch = (settings.count - 1) * settings.stagger;

            foreach (Thread thread in threads)
            {
                float flown = clock - thread.launchAt;

                if (flown < 0f)
                {
                    continue;
                }

                thread.edge.enabled = thread.core.enabled = true;

                // Fast off the mark, as if fired.
                float t = Mathf.Clamp01(flown / settings.flightTime);
                thread.tip = Vector3.Lerp(thread.sky, thread.end, 1f - (1f - t) * (1f - t));

                if (t >= 1f && !thread.landed)
                {
                    thread.landed = true;
                    Impact(thread.end, circle != null);

                    // The first to land springs the trap on the circle itself.
                    if (circle != null)
                    {
                        Destroy(circle.gameObject);
                        circle = null;
                        TryCatchInCircle();

                        if (phase != Phase.Striking)
                        {
                            return;
                        }
                    }
                }
            }

            float lingered = clock - lastLaunch - settings.flightTime;

            if (lingered > 0f)
            {
                alpha = 1f - settings.lingerFade * Mathf.Clamp01(lingered / Mathf.Max(0.01f, settings.lingerTime));
            }

            Draw();

            if (!spent)
            {
                TryCatchOnStrings();
            }

            if (phase == Phase.Striking && lingered >= settings.lingerTime)
            {
                LetGo();
            }
        }

        /// <summary>
        /// A string strikes the floor at <paramref name="point"/>: a ring and
        /// a few chips, a flash and sparks, and for the <paramref name="first"/>
        /// of the volley a small shake.
        /// </summary>
        private void Impact(Vector3 point, bool first)
        {
            HammerImpact.Create(point, settings.impact,
                                settings.impactMaterial != null ? settings.impactMaterial : FlatMaterial.Shared);
            EnergyBurst.Create(point + Vector3.up * settings.impact.floorOffset, settings.impactBurst);

            if (first && settings.impactShake > 0f)
            {
                TopDownCamera.Shake(settings.impactShake, settings.impactShakeTime);
            }
        }

        private void TryCatchInCircle()
        {
            if (spent)
            {
                return;
            }

            Vector3 offset = player.position - position;
            offset.y = 0f;
            float reach = settings.radius + PlayerRadius();

            if (offset.sqrMagnitude <= reach * reach)
            {
                Catch(PlayerCentre());
            }
        }

        private void TryCatchOnStrings()
        {
            Bounds body = PlayerBounds();
            Vector3 feet = new Vector3(body.center.x, body.min.y, body.center.z);
            Vector3 head = new Vector3(body.center.x, body.max.y, body.center.z);
            float reach = PlayerRadius() + settings.width * 0.5f;

            foreach (Thread thread in threads)
            {
                if (!thread.edge.enabled)
                {
                    continue;
                }

                ClosestPoints(feet, head, thread.tip, thread.sky, out Vector3 onPlayer, out Vector3 onString);

                if ((onPlayer - onString).sqrMagnitude <= reach * reach)
                {
                    Catch(onString);
                    return;
                }
            }
        }

        /// <summary>The volley's one hit: snares the player, unless something turns the strings away.</summary>
        private void Catch(Vector3 contact)
        {
            spent = true;

            // Luna's barrier, or anything else that says the player cannot be held.
            if (snare.IsImmune || ParryBarrier.TryCatch(contact, contact + Vector3.down * 0.01f, settings.width, Vector3.down * 10f))
            {
                LetGo();
                return;
            }

            if (playerHealth != null && settings.damage > 0f)
            {
                playerHealth.TakeDamage(settings.damage, position);
            }

            IReadOnlyList<Transform> bones = snare.Points;

            for (int i = 0; i < threads.Count; i++)
            {
                Thread thread = threads[i];
                thread.bone = bones[i % bones.Count];
                thread.latchFrom = thread.tip;
                thread.edge.enabled = thread.core.enabled = true;
            }

            phase = Phase.Holding;
            clock = 0f;
            alpha = 1f;

            if (circle != null)
            {
                Destroy(circle.gameObject);
                circle = null;
            }

            snare.Snare(LetGo);
        }

        private void Hold()
        {
            float t = Mathf.Clamp01(clock / settings.latchTime);
            float eased = 1f - (1f - t) * (1f - t);

            foreach (Thread thread in threads)
            {
                Vector3 bone = thread.bone != null ? thread.bone.position : player.position;
                thread.tip = Vector3.Lerp(thread.latchFrom, bone, eased);
            }

            Draw();
        }

        /// <summary>
        /// Lets go of everything: each string snaps back up the line it came
        /// down, and the volley is gone. Safe to call more than once.
        /// </summary>
        private void LetGo()
        {
            if (phase == Phase.Released)
            {
                return;
            }

            spent = true;
            phase = Phase.Released;

            if (circle != null)
            {
                Destroy(circle.gameObject);
                circle = null;
            }

            foreach (Thread thread in threads)
            {
                // Not yet fired: nothing to snap back.
                if (!thread.edge.enabled)
                {
                    continue;
                }

                // Only a puppet string's length of it snaps back, so it
                // whips and springs the way a cut puppet string does rather
                // than streaking off into the sky.
                Vector3 along = thread.sky - thread.tip;
                Vector3 top = thread.tip + Vector3.ClampMagnitude(along, settings.recoilLength);

                // Both lines of one string whip about the same axis, square
                // to the string, so the edge stays round the core.
                Vector3 axis = Vector3.Cross(along, Random.onUnitSphere);

                Snap(thread.edge, thread.tip, top, axis);
                Snap(thread.core, thread.tip, top, axis);
            }

            Destroy(gameObject);
        }

        private void Snap(LineRenderer line, Vector3 loose, Vector3 top, Vector3 axis)
        {
            // Out from under the volley, which goes now; the snap removes
            // itself when done.
            line.transform.SetParent(null, true);
            line.gameObject.AddComponent<CutPuppetString>()
                .Begin(loose, top, settings.recoilTime, settings.recoilWobble,
                       settings.recoilSegments, axis, alpha);
        }

        // ---- drawing --------------------------------------------------------

        private void Draw()
        {
            foreach (Thread thread in threads)
            {
                if (!thread.edge.enabled)
                {
                    continue;
                }

                // Landed and not fading: nothing to change.
                if (thread.tip == thread.drawnTip && thread.sky == thread.drawnSky && alpha == thread.drawnAlpha)
                {
                    continue;
                }

                thread.drawnTip = thread.tip;
                thread.drawnSky = thread.sky;
                thread.drawnAlpha = alpha;

                float length = Vector3.Distance(thread.tip, thread.sky);
                Place(thread.edge, thread.tip, thread.sky, length);
                Place(thread.core, thread.tip, thread.sky, length);
            }
        }

        private void Place(LineRenderer line, Vector3 tip, Vector3 sky, float length)
        {
            line.SetPosition(0, tip);
            line.SetPosition(1, sky);

            line.GetPropertyBlock(block);
            block.SetFloat(LengthId, length);
            block.SetFloat(AlphaId, alpha);
            line.SetPropertyBlock(block);
        }

        private LineRenderer BuildLine(int index, string part, Material material, float lineWidth, Color colour, int order)
        {
            GameObject host = new GameObject($"String {index} {part}");
            host.transform.SetParent(transform, false);

            LineRenderer line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = lineWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;

            // The core over the edge: both transparent, so order decides.
            line.sortingOrder = order;

            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetFloat(AlphaId, 1f);
            line.SetPropertyBlock(block);

            return line;
        }

        private static Material SharedMaterial()
        {
            if (sharedMaterial == null)
            {
                Shader shader = Shader.Find("NeoNoir/String");

                if (shader == null)
                {
                    Debug.LogWarning("[TargetedStrings] NeoNoir/String shader not found. Assign a string material, "
                                     + "or add the shader to Always Included Shaders.");
                    return null;
                }

                sharedMaterial = new Material(shader) { name = "Runtime Targeted String" };
            }

            return sharedMaterial;
        }

        // ---- player shape ---------------------------------------------------

        private Bounds PlayerBounds()
        {
            return playerCollider != null ? playerCollider.bounds : new Bounds(player.position + Vector3.up, new Vector3(0.6f, 2f, 0.6f));
        }

        private float PlayerRadius()
        {
            Bounds body = PlayerBounds();
            return Mathf.Min(body.extents.x, body.extents.z);
        }

        private Vector3 PlayerCentre()
        {
            return PlayerBounds().center;
        }

        /// <summary>The nearest points to each other on segments a-b and c-d.</summary>
        private static void ClosestPoints(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 onFirst, out Vector3 onSecond)
        {
            Vector3 first = b - a;
            Vector3 second = d - c;
            Vector3 between = a - c;

            float firstLength = Vector3.Dot(first, first);
            float secondLength = Vector3.Dot(second, second);
            float along = Vector3.Dot(second, between);

            float s;
            float t;

            if (firstLength <= 1e-6f && secondLength <= 1e-6f)
            {
                onFirst = a;
                onSecond = c;
                return;
            }

            if (firstLength <= 1e-6f)
            {
                s = 0f;
                t = Mathf.Clamp01(along / secondLength);
            }
            else
            {
                float across = Vector3.Dot(first, between);

                if (secondLength <= 1e-6f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-across / firstLength);
                }
                else
                {
                    float shared = Vector3.Dot(first, second);
                    float denominator = firstLength * secondLength - shared * shared;

                    s = denominator > 1e-6f ? Mathf.Clamp01((shared * along - across * secondLength) / denominator) : 0f;
                    t = (shared * s + along) / secondLength;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-across / firstLength);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((shared - across) / firstLength);
                    }
                }
            }

            onFirst = a + first * s;
            onSecond = c + second * t;
        }
    }
}
