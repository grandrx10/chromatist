using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A glowing ritual circle on the floor: runes evenly round a circle,
    /// optionally between a doubled outer ring and an inner ring, which
    /// writes itself in stroke by stroke and then pulses.
    /// </summary>
    /// <remarks>
    /// Two ways to draw it:
    ///   <see cref="Ignite"/> at once -- the whole circle is written over
    ///   the draw time, then flashes. <see cref="Create"/> does this unless
    ///   told not to.
    ///   <see cref="Charge"/> bit by bit -- it is written up to the fraction
    ///   given, dimly, and nothing more, as an altar charges it with the
    ///   damage it takes. <see cref="Ignite"/> then finishes it and lights it.
    ///
    /// Once lit it flashes, then breathes gently while turning slowly.
    /// <see cref="Erase"/> undraws it and destroys it.
    ///
    /// The outer rings go round first, the inner ring the other way just
    /// behind it, then each rune is written in turn, then the small diamonds
    /// between the runes. The runes, from <see cref="RuneGlyphs"/>, are
    /// upright as seen from the middle, their tops facing outward, so they
    /// read the same all the way round. Glows by colour: HDR above 1 blooms.
    /// </remarks>
    public sealed class RuneCircle : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Radius of the circle, in metres: of the outer ring with rings, or of the runes' centres without.")]
            [Min(0.5f)] public float radius = 5f;

            [Tooltip("How many runes go round the circle. They cycle through the four: hook, hourglass, bowtie, orbit.")]
            [Min(1)] public int runeCount = 4;

            [Tooltip("Height of each rune, in metres.")]
            [Min(0.1f)] public float runeSize = 1.1f;

            [Tooltip("Draw the rings. Off leaves only the runes, centred on the radius.")]
            public bool showRings = true;

            [Tooltip("Draw a small diamond in each gap between runes.")]
            public bool showDiamonds = true;

            [Tooltip("Radius of the inner ring, as a fraction of the outer. The runes sit between the two.")]
            [Range(0.3f, 0.95f)] public float innerRadius = 0.66f;

            [Tooltip("Width of the rings' lines, in metres.")]
            [Min(0.01f)] public float ringWidth = 0.12f;

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float runeWidth = 0.09f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(4f, 0.35f, 0.3f);

            [Tooltip("Material to draw with. Empty makes a NeoNoir/Flat one.")]
            public Material material;

            [Tooltip("Seconds to write the whole circle once lit. Only what is left is written, so a nearly charged circle finishes quickly.")]
            [Min(0.01f)] public float drawTime = 1.8f;

            [Header("Charging")]
            [Tooltip("Brightness while charging, before it is lit, as a fraction of the full colour.")]
            [Range(0f, 1f)] public float chargingBrightness = 0.3f;

            [Tooltip("Seconds the drawing takes to catch up to a new charge, so a hit writes it in rather than snapping.")]
            [Min(0.01f)] public float chargeCatchUp = 0.35f;

            [Header("Lit")]
            [Tooltip("How much brighter it flashes the moment it completes.")]
            [Min(0f)] public float completeFlash = 1.5f;

            [Tooltip("Seconds the flash takes to fade.")]
            [Min(0.01f)] public float flashTime = 0.6f;

            [Tooltip("How far the brightness swells and dims once lit, as a fraction.")]
            [Range(0f, 1f)] public float pulseAmount = 0.25f;

            [Tooltip("Pulses per second.")]
            [Min(0f)] public float pulseSpeed = 0.6f;

            [Tooltip("Degrees per second the circle turns once lit. Negative turns it the other way.")]
            public float spinSpeed = 4f;

            [Tooltip("Height above the floor, so it does not flicker against it.")]
            [Min(0f)] public float floorOffset = 0.03f;
        }

        private const int RingSegments = 120;

        private Settings settings;
        private RuneDrawing drawing;

        private float target;
        private bool lit;
        private float completedAt = -1f;
        private float eraseTime;
        private bool erasing;
        private float fadeTime = -1f;
        private float faded = 1f;

        /// <summary>True once lit and completely written.</summary>
        public bool IsComplete => completedAt >= 0f && !erasing;

        /// <summary>True once <see cref="Ignite"/> has been called.</summary>
        public bool IsLit => lit;

        /// <summary>
        /// Makes a rune circle flat on the floor around <paramref name="position"/>,
        /// which should be on the floor itself. Lit and written at once if
        /// <paramref name="ignite"/>; otherwise empty, waiting for
        /// <see cref="Charge"/> or <see cref="Ignite"/>.
        /// </summary>
        public static RuneCircle Create(Vector3 position, Settings settings, bool ignite = true, Transform parent = null)
        {
            GameObject host = new GameObject("RuneCircle");
            host.transform.SetParent(parent, true);
            host.transform.position = position + Vector3.up * settings.floorOffset;

            RuneCircle circle = host.AddComponent<RuneCircle>();
            circle.Begin(settings);

            if (ignite)
            {
                circle.Ignite();
            }

            return circle;
        }

        /// <summary>
        /// The floor under <paramref name="point"/>, skipping anything under
        /// any of <paramref name="ignore"/> and anything physics moves, or the
        /// point itself if there is none.
        /// </summary>
        public static Vector3 FloorBelow(Vector3 point, params Transform[] ignore)
        {
            const float above = 1f;
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * above, Vector3.down, above + 20f,
                                                   ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (IsUnder(hit.collider.transform, ignore)
                    || (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                    || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                point.y = hit.point.y;
            }

            return point;
        }

        private static bool IsUnder(Transform part, Transform[] parents)
        {
            if (parents == null)
            {
                return false;
            }

            foreach (Transform parent in parents)
            {
                if (parent != null && part.IsChildOf(parent))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes the circle in, dimly, up to <paramref name="fraction"/> of
        /// the way. Ignored once lit.
        /// </summary>
        public void Charge(float fraction)
        {
            if (!lit)
            {
                target = Mathf.Clamp01(fraction);
            }
        }

        /// <summary>Lights the circle: writes whatever is left, then flashes and glows.</summary>
        public void Ignite()
        {
            lit = true;
            target = 1f;
        }

        /// <summary>Undraws the circle over <paramref name="duration"/> seconds, then destroys it.</summary>
        public void Erase(float duration = 0.8f)
        {
            erasing = true;
            eraseTime = Mathf.Max(0.01f, duration);
        }

        /// <summary>Fades the whole circle out where it is over <paramref name="duration"/> seconds, then destroys it.</summary>
        public void FadeOut(float duration = 0.8f)
        {
            if (fadeTime < 0f)
            {
                fadeTime = Mathf.Max(0.01f, duration);
            }
        }

        private void Begin(Settings circleSettings)
        {
            settings = circleSettings;
            drawing = new RuneDrawing(gameObject, settings.material);

            BuildStrokes();
            drawing.Progress = 0f;
            drawing.SetColour(settings.colour * settings.chargingBrightness);
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (fadeTime > 0f)
            {
                faded -= deltaTime / fadeTime;

                if (faded <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }

                drawing.SetFade(faded);
            }

            if (erasing)
            {
                drawing.Progress -= deltaTime / eraseTime;

                if (drawing.Progress <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else if (drawing.Progress != target)
            {
                float rate = lit ? 1f / settings.drawTime : 1f / settings.chargeCatchUp;
                drawing.Progress = Mathf.MoveTowards(drawing.Progress, target, rate * deltaTime);
            }

            // Checked apart from the drawing, as a circle charged all the way
            // before it was lit has nothing left to write.
            if (lit && completedAt < 0f && !erasing && drawing.Progress >= 1f)
            {
                completedAt = Time.time;
            }

            if (lit)
            {
                transform.Rotate(Vector3.up, settings.spinSpeed * deltaTime, Space.World);
            }

            drawing.SetColour(settings.colour * Brightness());
        }

        private float Brightness()
        {
            if (!lit)
            {
                return settings.chargingBrightness;
            }

            if (completedAt < 0f)
            {
                return 1f;
            }

            float since = Time.time - completedAt;
            float flash = settings.completeFlash * Mathf.Clamp01(1f - since / settings.flashTime);

            // Starts at its midpoint so the pulse begins without a jump.
            float pulse = settings.pulseAmount * Mathf.Sin(since * settings.pulseSpeed * Mathf.PI * 2f);

            return 1f + flash + pulse;
        }

        private void BuildStrokes()
        {
            float outer = settings.radius;
            float inner = settings.radius * settings.innerRadius;
            float runeWidth = settings.runeWidth;

            // Without rings the runes sit on the radius itself, and are
            // written from the start.
            float middle = settings.radius;
            float runesFrom = 0f;

            if (settings.showRings)
            {
                // A thin ring just inside the outer one doubles its edge.
                float trim = outer - Mathf.Max(settings.ringWidth * 2.5f, settings.radius * 0.03f);
                middle = (trim + inner) * 0.5f;
                runesFrom = 0.42f;

                drawing.AddStroke(Ring(outer, 90f, -1f), true, settings.ringWidth, 0f, 0.42f);
                drawing.AddStroke(Ring(trim, 90f, -1f), true, runeWidth * 0.6f, 0.04f, 0.46f);
                drawing.AddStroke(Ring(inner, 90f, 1f), true, settings.ringWidth, 0.08f, 0.5f);
            }

            float runesTo = settings.showDiamonds ? 0.88f : 1f;
            float span = runesTo - runesFrom;

            // Each rune takes a share of the time and they overlap, so a
            // dozen are written as briskly as four.
            int count = Mathf.Max(1, settings.runeCount);
            float each = span * Mathf.Clamp(2f / (count + 1), 0.15f, 1f);
            float stagger = count > 1 ? (span - each) / (count - 1) : 0f;
            float step = 360f / count;

            for (int i = 0; i < count; i++)
            {
                // Clockwise from the top, as seen from above.
                float angle = 90f - i * step;
                float start = runesFrom + i * stagger;

                drawing.AddRune(RuneGlyphs.Rune(i), Outward(angle) * middle, Outward(angle), settings.runeSize,
                                runeWidth, start, start + each);

                if (settings.showDiamonds && count > 1)
                {
                    float gap = angle - step * 0.5f;
                    float diamondStart = 0.88f + 0.08f * i / count;
                    drawing.AddRune(RuneGlyphs.Diamond(), Outward(gap) * middle, Outward(gap), settings.runeSize * 0.4f,
                                    runeWidth, diamondStart, diamondStart + 0.04f);
                }
            }
        }

        private static Vector3 Outward(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
        }

        private static List<Vector3> Ring(float radius, float startDegrees, float direction)
        {
            List<Vector3> points = new List<Vector3>(RingSegments + 1);

            for (int i = 0; i <= RingSegments; i++)
            {
                // The last point is exactly the first, so the ring closes.
                points.Add(Outward(startDegrees + direction * 360f * (i % RingSegments) / RingSegments) * radius);
            }

            return points;
        }

        private void OnDestroy()
        {
            drawing?.Release();
        }
    }
}
