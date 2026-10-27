using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A storm of runes that bursts off a body, tumbles loose for a moment,
    /// then swarms through the air from every side into a point somewhere
    /// else. What <see cref="RuneBlink"/> dissolves an enemy into.
    /// </summary>
    /// <remarks>
    /// Each of the <see cref="Settings.count"/> runes has its own random
    /// everything:
    ///   1. Born: at a random moment while the body dissolves, at a random
    ///      spot on it, written in over <see cref="Settings.writeTime"/> and
    ///      flashing as it finishes.
    ///   2. Scatter: it flies off in a random direction, mostly outward and
    ///      up, slowing as it goes.
    ///   3. Swarm: it swoops to a random spot on the body at the far end
    ///      along a curve of its own -- out wide one way, coming in from
    ///      another -- arriving at a random moment in the last
    ///      <see cref="Settings.arriveSpread"/> seconds, and dissolving as
    ///      it lands.
    /// All the while it faces the camera, spinning at its own speed, at its
    /// own size, some of them mirrored. Glows by colour: HDR above 1 blooms.
    ///
    /// Built entirely in code: <see cref="Create"/> makes one. Not parented
    /// to anything, and destroys itself once the last rune has landed.
    /// </remarks>
    public sealed class RuneSwarm : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Runes in the storm.")]
            [Range(1, 200)] public int count = 45;

            [Tooltip("Smallest and largest height of a rune, in metres.")]
            public Vector2 runeSize = new Vector2(0.25f, 0.6f);

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.05f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(4f, 0.35f, 0.3f);

            [Tooltip("Material to draw with. Empty makes a NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material material;

            [Tooltip("Seconds to write each rune as it is born.")]
            [Min(0.01f)] public float writeTime = 0.2f;

            [Tooltip("How much brighter each rune flashes as it finishes being written.")]
            [Min(0f)] public float writeFlash = 2f;

            [Tooltip("Seconds the flash takes to fade.")]
            [Min(0.01f)] public float flashTime = 0.3f;

            [Header("Body")]
            [Tooltip("How far out from the middle of the body runes are born and land.")]
            [Min(0f)] public float bodyRadius = 0.45f;

            [Tooltip("Lowest and highest above the feet runes are born and land.")]
            public Vector2 bodyHeight = new Vector2(0.2f, 2f);

            [Header("Scatter")]
            [Tooltip("Shortest and longest distance a rune flies off the body before it turns to swarm.")]
            public Vector2 scatterDistance = new Vector2(1f, 3.5f);

            [Tooltip("Shortest and longest seconds a rune scatters for.")]
            public Vector2 scatterTime = new Vector2(0.25f, 0.6f);

            [Header("Swarm")]
            [Tooltip("How far a rune's path bulges out to the side, at most, so they come in from every direction.")]
            [Min(0f)] public float swirlRadius = 6f;

            [Tooltip("Lowest and highest a rune's path climbs above the straight line.")]
            public Vector2 lift = new Vector2(1f, 6f);

            [Tooltip("Runes land at random moments within this many seconds of the end, the last right at the end.")]
            [Min(0f)] public float arriveSpread = 0.6f;

            [Tooltip("Fraction of its swarm flight, at the end, a rune spends dissolving into the body.")]
            [Range(0f, 1f)] public float arriveFade = 0.25f;

            [Tooltip("Slowest and fastest a rune spins, in degrees a second, either way round.")]
            public Vector2 spinSpeed = new Vector2(30f, 360f);
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
            public float born;
            public float scattered;
            public float arrives;
            public Vector3 birth;
            public Vector3 scatter;
            public Vector3 control1;
            public Vector3 control2;
            public Vector3 landing;
            public float roll;
            public float spin;
            public bool mirrored;
        }

        private readonly List<Rune> runes = new List<Rune>();

        private Settings settings;
        private float clock;
        private float end;

        /// <summary>
        /// Bursts runes off the body standing at <paramref name="fromFeet"/>,
        /// born over the first <paramref name="birthTime"/> seconds, which
        /// swarm onto the body at <paramref name="toFeet"/>, the last landing
        /// <paramref name="duration"/> seconds from now.
        /// </summary>
        public static RuneSwarm Create(Vector3 fromFeet, Vector3 toFeet, float birthTime, float duration, Settings settings)
        {
            GameObject host = new GameObject("RuneSwarm");
            host.transform.position = fromFeet;

            RuneSwarm swarm = host.AddComponent<RuneSwarm>();
            swarm.Begin(fromFeet, toFeet, Mathf.Max(0f, birthTime), Mathf.Max(0.01f, duration), settings);
            return swarm;
        }

        private void Begin(Vector3 fromFeet, Vector3 toFeet, float birthTime, float duration, Settings swarmSettings)
        {
            settings = swarmSettings;
            end = duration;

            Vector3 across = toFeet - fromFeet;
            across.y = 0f;
            Vector3 side = across.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, across.normalized)
                : Vector3.right;

            for (int i = 0; i < settings.count; i++)
            {
                Rune rune = new Rune();

                rune.born = UnityEngine.Random.Range(0f, birthTime);
                rune.scattered = rune.born + RandomIn(settings.scatterTime);

                // Never lands before it has finished scattering and had a moment in the air.
                float earliest = Mathf.Min(duration, rune.scattered + 0.2f);
                rune.arrives = Mathf.Max(earliest, duration - UnityEngine.Random.Range(0f, settings.arriveSpread));

                rune.birth = OnBody(fromFeet);
                rune.landing = OnBody(toFeet);

                // Off the body, outward and up more than down.
                Vector3 outward = rune.birth - fromFeet;
                outward.y = 0f;
                Vector3 direction = (outward.normalized + UnityEngine.Random.insideUnitSphere * 0.8f
                                     + Vector3.up * UnityEngine.Random.Range(0f, 0.8f)).normalized;
                rune.scatter = rune.birth + direction * RandomIn(settings.scatterDistance);

                // Out wide to one side on the way, coming in from anywhere at the end.
                float bulge = UnityEngine.Random.Range(-1f, 1f) * settings.swirlRadius;
                float height = RandomIn(settings.lift);
                Vector2 inFrom = UnityEngine.Random.insideUnitCircle * settings.swirlRadius;

                rune.control1 = Vector3.Lerp(rune.scatter, rune.landing, 0.33f) + side * bulge + Vector3.up * height;
                rune.control2 = rune.landing + new Vector3(inFrom.x, height * 0.5f, inFrom.y);

                rune.roll = UnityEngine.Random.Range(0f, 360f);
                rune.spin = RandomIn(settings.spinSpeed) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
                rune.mirrored = UnityEngine.Random.value < 0.5f;

                GameObject host = new GameObject("Rune");
                host.transform.SetParent(transform, false);

                RuneDrawing drawing = new RuneDrawing(host, settings.material);
                drawing.AddRune(Glyph(rune.mirrored), Vector3.zero,
                                Vector3.forward, RandomIn(settings.runeSize), settings.lineWidth, 0f, 1f);
                drawing.Progress = 0f;

                rune.transform = host.transform;
                rune.drawing = drawing;
                runes.Add(rune);
            }

            Update();
        }

        /// <summary>A random rune, flipped left to right if <paramref name="mirrored"/>.</summary>
        private static List<List<Vector2>> Glyph(bool mirrored)
        {
            List<List<Vector2>> glyph = RuneGlyphs.Rune(UnityEngine.Random.Range(0, RuneGlyphs.Count));

            if (mirrored)
            {
                foreach (List<Vector2> line in glyph)
                {
                    for (int i = 0; i < line.Count; i++)
                    {
                        line[i] = new Vector2(-line[i].x, line[i].y);
                    }
                }
            }

            return glyph;
        }

        private Vector3 OnBody(Vector3 feet)
        {
            Vector2 round = UnityEngine.Random.insideUnitCircle * settings.bodyRadius;
            return feet + new Vector3(round.x, RandomIn(settings.bodyHeight), round.y);
        }

        private static float RandomIn(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            clock += Time.deltaTime;

            if (clock >= end)
            {
                Destroy(gameObject);
                return;
            }

            Quaternion facing = RuneDrawing.FacingCamera();

            foreach (Rune rune in runes)
            {
                float age = clock - rune.born;

                if (age < 0f || clock >= rune.arrives)
                {
                    rune.drawing.Progress = 0f;
                    continue;
                }

                Vector3 position;
                float fade = 1f;

                if (clock < rune.scattered)
                {
                    // Thrown off fast, slowing to a hang.
                    float t = (clock - rune.born) / Mathf.Max(0.01f, rune.scattered - rune.born);
                    position = Vector3.Lerp(rune.birth, rune.scatter, 1f - (1f - t) * (1f - t));
                }
                else
                {
                    float t = (clock - rune.scattered) / Mathf.Max(0.01f, rune.arrives - rune.scattered);
                    float eased = t * t * (3f - 2f * t);
                    position = Bezier(rune.scatter, rune.control1, rune.control2, rune.landing, eased);

                    if (settings.arriveFade > 0f)
                    {
                        fade = Mathf.Clamp01((1f - t) / settings.arriveFade);
                    }
                }

                float roll = rune.roll + rune.spin * clock;
                rune.transform.SetPositionAndRotation(position, facing * Quaternion.AngleAxis(roll, Vector3.up));

                float written = age / settings.writeTime;
                rune.drawing.Progress = written;

                float flash = written >= 1f
                    ? settings.writeFlash * Mathf.Clamp01(1f - (age - settings.writeTime) / settings.flashTime)
                    : 0f;

                rune.drawing.SetColour(settings.colour * (1f + flash));
                rune.drawing.SetFade(fade);
            }
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        private void OnDestroy()
        {
            foreach (Rune rune in runes)
            {
                rune.drawing.Release();
            }
        }
    }
}
