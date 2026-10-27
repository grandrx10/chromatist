using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A stream of glowing runes flowing through the air along an arc from
    /// one point into another, for as long as it exists.
    /// </summary>
    /// <remarks>
    /// The arc is a curve rising <see cref="Settings.arcHeight"/> above the
    /// higher end at its middle. Runes leave the start one after another,
    /// <see cref="Settings.spacing"/> apart, each written as it emerges and
    /// flashing as it finishes, then travel the curve and dissolve into the
    /// end -- and the next comes round again. They cycle through
    /// <see cref="RuneGlyphs"/>, each turned to face the camera so it reads
    /// from wherever the camera is.
    ///
    /// <see cref="IsComplete"/> once the first rune has arrived. Built
    /// entirely in code: <see cref="Create"/> makes one. Glows by colour:
    /// HDR above 1 blooms.
    ///
    /// <see cref="CreateBurst"/> makes a stream of a set number of runes
    /// instead, sped up or slowed down so the last of them arrives a set
    /// number of seconds after the first leaves. Each raises
    /// <see cref="RuneArrived"/> as it reaches the end, and the arc destroys
    /// itself once the last has.
    /// </remarks>
    public sealed class RuneArc : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("How high the middle of the arc rises above the higher end, in metres.")]
            [Min(0f)] public float arcHeight = 4f;

            [Tooltip("Metres between runes along the arc.")]
            [Min(0.2f)] public float spacing = 1.2f;

            [Tooltip("Height of each rune, in metres.")]
            [Min(0.1f)] public float runeSize = 0.7f;

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.06f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(4f, 0.35f, 0.3f);

            [Tooltip("Material to draw with. Empty makes a NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material material;

            [Header("Flow")]
            [Tooltip("Metres per second the runes travel along the arc.")]
            [Min(0.1f)] public float flowSpeed = 5f;

            [Tooltip("Seconds to write each rune as it leaves the start.")]
            [Min(0.01f)] public float runeDrawTime = 0.3f;

            [Tooltip("How much brighter each rune flashes as it finishes being written.")]
            [Min(0f)] public float writeFlash = 1.5f;

            [Tooltip("Seconds the flash takes to fade.")]
            [Min(0.01f)] public float flashTime = 0.4f;

            [Tooltip("Metres before the end over which each rune dissolves into it.")]
            [Min(0f)] public float fadeDistance = 1f;
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
        }

        private const int CurveSamples = 64;

        private readonly List<Rune> runes = new List<Rune>();

        private Settings settings;
        private Vector3[] samples;
        private float[] distances;
        private float length;
        private float loop;
        private float clock;
        private float dissolveTime = -1f;
        private float remaining = 1f;
        private float speed;
        private int limit = -1;
        private int arrivedCount;

        /// <summary>True once the first rune has reached the end.</summary>
        public bool IsComplete => settings != null && clock * speed >= length;

        /// <summary>Where the arc ends.</summary>
        public Vector3 End => samples != null ? samples[samples.Length - 1] : transform.position;

        /// <summary>Raised by a burst each time one of its runes reaches the end, while it is not dissolving.</summary>
        public event Action<RuneArc> RuneArrived;

        /// <summary>Starts a stream of runes from <paramref name="from"/> into <paramref name="to"/>.</summary>
        public static RuneArc Create(Vector3 from, Vector3 to, Settings settings, Transform parent = null)
        {
            GameObject host = new GameObject("RuneArc");
            host.transform.SetParent(parent, true);
            host.transform.position = from;

            RuneArc arc = host.AddComponent<RuneArc>();
            arc.Begin(from, to, settings, -1, 0f);
            return arc;
        }

        /// <summary>
        /// Sends <paramref name="count"/> runes from <paramref name="from"/>
        /// into <paramref name="to"/>, the last arriving
        /// <paramref name="duration"/> seconds from now, then destroys the arc.
        /// The settings' flow speed is ignored; the speed is whatever fits.
        /// </summary>
        public static RuneArc CreateBurst(Vector3 from, Vector3 to, Settings settings, int count, float duration,
                                          Transform parent = null)
        {
            GameObject host = new GameObject("RuneArc");
            host.transform.SetParent(parent, true);
            host.transform.position = from;

            RuneArc arc = host.AddComponent<RuneArc>();
            arc.Begin(from, to, settings, Mathf.Max(1, count), Mathf.Max(0.01f, duration));
            return arc;
        }

        /// <summary>Dissolves every rune over <paramref name="duration"/> seconds, then destroys the arc.</summary>
        public void Dissolve(float duration)
        {
            if (dissolveTime < 0f)
            {
                dissolveTime = Mathf.Max(0.01f, duration);
            }
        }

        private void Begin(Vector3 from, Vector3 to, Settings arcSettings, int runeLimit, float duration)
        {
            settings = arcSettings;
            limit = runeLimit;

            // A quadratic curve, its control point set so the middle of the
            // curve peaks at the arc height above the higher end.
            Vector3 middle = (from + to) * 0.5f;
            middle.y = Mathf.Max(from.y, to.y) + settings.arcHeight;
            Vector3 control = 2f * middle - (from + to) * 0.5f;

            samples = new Vector3[CurveSamples + 1];
            distances = new float[CurveSamples + 1];

            for (int i = 0; i <= CurveSamples; i++)
            {
                float t = (float)i / CurveSamples;
                samples[i] = (1f - t) * (1f - t) * from + 2f * (1f - t) * t * control + t * t * to;

                if (i > 0)
                {
                    distances[i] = distances[i - 1] + Vector3.Distance(samples[i - 1], samples[i]);
                }
            }

            length = distances[CurveSamples];

            int count;

            if (limit > 0)
            {
                // Each rune once, the last leaving (count - 1) spacings behind
                // the first and arriving at the end of the duration.
                count = limit;
                speed = Mathf.Max(0.1f, (length + (count - 1) * settings.spacing) / duration);
                loop = float.PositiveInfinity;
            }
            else
            {
                // Enough runes to fill the arc, going round and round: one that
                // reaches the end comes back in at the start.
                count = Mathf.Max(1, Mathf.CeilToInt(length / settings.spacing));
                speed = settings.flowSpeed;
                loop = count * settings.spacing;
            }

            for (int i = 0; i < count; i++)
            {
                runes.Add(MakeRune(i));
            }

            Update();
        }

        private Rune MakeRune(int index)
        {
            GameObject host = new GameObject("Rune");
            host.transform.SetParent(transform, false);

            RuneDrawing drawing = new RuneDrawing(host, settings.material);
            drawing.AddRune(RuneGlyphs.Rune(index), Vector3.zero, Vector3.forward, settings.runeSize,
                            settings.lineWidth, 0f, 1f);
            drawing.Progress = 0f;

            return new Rune { transform = host.transform, drawing = drawing };
        }

        private Vector3 PointAt(float distance)
        {
            for (int i = 1; i < samples.Length; i++)
            {
                if (distances[i] >= distance)
                {
                    float segment = distances[i] - distances[i - 1];
                    float t = segment > 0f ? (distance - distances[i - 1]) / segment : 0f;
                    return Vector3.Lerp(samples[i - 1], samples[i], t);
                }
            }

            return samples[samples.Length - 1];
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            clock += Time.deltaTime;

            if (dissolveTime > 0f)
            {
                remaining -= Time.deltaTime / dissolveTime;

                if (remaining <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            Quaternion facing = RuneDrawing.FacingCamera();
            float travelled = clock * speed;
            float writeDistance = settings.runeDrawTime * speed;
            float flashDistance = settings.flashTime * speed;

            for (int i = 0; i < runes.Count; i++)
            {
                Rune rune = runes[i];

                // How far along the arc this rune is. Each leaves one spacing
                // after the last, and none has left before its turn.
                float along = travelled - i * settings.spacing;

                if (along < 0f)
                {
                    rune.drawing.Progress = 0f;
                    continue;
                }

                if (limit < 0)
                {
                    along %= loop;
                }

                // In the gap between the end and the start, as a loop of
                // runes is a little longer than the arc.
                if (along > length)
                {
                    rune.drawing.Progress = 0f;
                    continue;
                }

                rune.transform.SetPositionAndRotation(PointAt(along), facing);

                // Rewritten every time round, so each emerges fresh.
                float written = along / writeDistance;
                rune.drawing.Progress = written;

                float flash = written >= 1f
                    ? settings.writeFlash * Mathf.Clamp01(1f - (along - writeDistance) / flashDistance)
                    : 0f;

                rune.drawing.SetColour(settings.colour * (1f + flash));
                float arriving = settings.fadeDistance > 0f ? Mathf.Clamp01((length - along) / settings.fadeDistance) : 1f;
                rune.drawing.SetFade(arriving * remaining);
            }

            if (limit > 0)
            {
                CountArrivals(travelled);
            }
        }

        /// <summary>Raises an arrival for every burst rune that has reached the end since last frame, and ends the arc after the last.</summary>
        private void CountArrivals(float travelled)
        {
            while (arrivedCount < limit && travelled - arrivedCount * settings.spacing >= length)
            {
                arrivedCount++;

                if (dissolveTime < 0f)
                {
                    RuneArrived?.Invoke(this);
                }
            }

            if (arrivedCount >= limit)
            {
                Destroy(gameObject);
            }
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
