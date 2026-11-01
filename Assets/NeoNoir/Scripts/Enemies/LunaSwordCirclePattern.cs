using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Luna's sword circle: a ring of white swords rises out of the floor
    /// round her, then, one by one in a random order, each takes a colour of
    /// its own, tips its point over to track the player, and fires a laser of
    /// that colour. The last few go all at once. Outside it all, spirals of
    /// sword circles close in on her, again and again.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. <see cref="swordCount"/> <see cref="CircleSword"/>s rise out of
    ///      the floor in a ring <see cref="ringRadius"/> round her, evenly
    ///      spaced from a random angle, handle first, in dust.
    ///   2. Once they are up, a sword at random activates, then another
    ///      <see cref="activationInterval"/> seconds later, counted from the
    ///      last one's activation, not its laser finishing. Each wait after
    ///      that is <see cref="activationSpeedup"/> shorter than the one
    ///      before, down to <see cref="minActivationInterval"/>. The last <see cref="finalTogether"/> activate together,
    ///      a full <see cref="activationInterval"/> after the one before them.
    ///   3. An outer ring of <see cref="outerCount"/> swords, risen with the
    ///      inner one <see cref="outerRadius"/> round her, waits until the
    ///      inner ring's final group activates, then
    ///      <see cref="outerDelay"/> later activates one by one round the
    ///      ring, <see cref="outerInterval"/> apart, from a random sword and
    ///      going a random way round. These do not track the player: each
    ///      fires straight in at the middle of the ring.
    ///   4. Each sword takes a random colour, and goes on as <see cref="CircleSword"/> describes: tracks, holds,
    ///      fires, fades.
    ///   5. All the while, from the start until the last laser stops, rings
    ///      of circles close in on her outside the outer ring, the circles as
    ///      in <see cref="LunaSwordRainPattern"/>: a whole ring at once,
    ///      every <see cref="ringInterval"/> seconds, the first
    ///      <see cref="startRadius"/> out, each <see cref="ringStep"/> closer
    ///      than the last, its circles <see cref="circleSpacing"/> apart
    ///      round it from a random angle, until one would come within
    ///      <see cref="spiralClearance"/> of the outer swords; then the rings
    ///      start from the outside again. Each circle takes the next colour
    ///      round the hue wheel, and one sword of that colour drops into it.
    /// The attack ends once every sword has faded and every circle struck.
    /// The rings stay where they rose. Everything is laid on the floor under
    /// her, found on the Floor Mask; without one, at her pivot.
    ///
    /// The circles, their swords and bursts are as in
    /// <see cref="SpearShotBase"/>: set the sword model as its Spear Prefab,
    /// as for the sword rain, and Spears Per Circle to 1. Without a Spear
    /// Prefab there are no circles, but the swords still go.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Luna Sword Circle", fileName = "LunaSwordCircle")]
    public sealed class LunaSwordCirclePattern : SpearShotBase
    {
        private const float GoldenRatio = 0.618034f;

        // However the spiral is set, never more circles than this.
        private const int MaxCircles = 5000;

        [Header("Ring")]
        [SerializeField]
        [Min(1)]
        private int swordCount = 12;

        [SerializeField]
        [Tooltip("Distance from her of each sword.")]
        [Min(0f)]
        private float ringRadius = 3f;

        [Header("Outer Ring")]
        [SerializeField]
        [Tooltip("Swords in the outer ring. 0 for none.")]
        [Min(0)]
        private int outerCount = 24;

        [SerializeField]
        [Tooltip("Distance from her of each outer sword.")]
        [Min(0f)]
        private float outerRadius = 6f;

        [SerializeField]
        [Tooltip("Seconds from the inner ring's final group activating to the first outer sword activating.")]
        [Min(0f)]
        private float outerDelay = 0f;

        [SerializeField]
        [Tooltip("Seconds from one outer sword activating to the next round the ring.")]
        [Min(0f)]
        private float outerInterval = 0.03f;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Seconds from the first activation to the next, and from the last lone one to the final group.")]
        [Min(0f)]
        private float activationInterval = 1f;

        [SerializeField]
        [Tooltip("Seconds each wait between lone activations is shorter than the one before.")]
        [Min(0f)]
        private float activationSpeedup = 0.1f;

        [SerializeField]
        [Tooltip("The shortest the wait between lone activations gets.")]
        [Min(0f)]
        private float minActivationInterval = 0.1f;

        [SerializeField]
        [Tooltip("How many of the last swords activate together.")]
        [Min(1)]
        private int finalTogether = 3;

        [Header("Closing Rings")]
        [SerializeField]
        [Tooltip("Distance from her of the first, outermost ring of circles each time round.")]
        [Min(0f)]
        private float startRadius = 50f;

        [SerializeField]
        [Tooltip("Gap kept between the outer swords and the edges of the innermost circles.")]
        [Min(0f)]
        private float spiralClearance = 0.5f;

        [SerializeField]
        [Tooltip("How much closer each ring is than the last.")]
        [Min(0.1f)]
        private float ringStep = 5f;

        [SerializeField]
        [Tooltip("Distance round a ring from one circle to the next.")]
        [Min(0.1f)]
        private float circleSpacing = 5f;

        [SerializeField]
        [Tooltip("Seconds between one ring appearing, all its circles at once, and the next.")]
        [Min(0.01f)]
        private float ringInterval = 0.5f;

        [Header("Colours")]
        [SerializeField]
        [Tooltip("Saturation of the swords', lasers' and circles' colours.")]
        [Range(0f, 1f)]
        private float saturation = 1f;

        [SerializeField]
        [Tooltip("Brightness of the lasers' and the circles' swords' colours. Above 1 blooms. The ring swords' glow takes the Glisten's own Glow Intensity.")]
        [Min(0f)]
        private float intensity = 2.5f;

        [SerializeField] private CircleSword.Settings sword = new CircleSword.Settings();

        private float circlesUntil;
        private float circleHue;

        public override IEnumerator Perform(Enemy enemy)
        {
            Vector3 centre = enemy.transform.position;

            if (FindFloor(centre, out float floor))
            {
                centre.y = floor;
            }

            List<CircleSword> swords = RaiseSwords(enemy, centre);

            yield return Circles(enemy);

            while (swords.Exists(s => s != null))
            {
                yield return null;
            }
        }

        // The sword rain's circles, from SpearShotBase, laid by PlanRings.
        private IEnumerator Circles(Enemy enemy) => base.Perform(enemy);

        /// <summary>Both rings of swords, timed. Notes when the last laser stops, for the circles.</summary>
        private List<CircleSword> RaiseSwords(Enemy enemy, Vector3 centre)
        {
            // A random order to go in: each slot round the ring gets a turn.
            int count = swordCount;
            int[] turns = new int[count];

            for (int i = 0; i < count; i++)
            {
                turns[i] = i;
            }

            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (turns[i], turns[j]) = (turns[j], turns[i]);
            }

            // When each turn comes: lone ones quicker and quicker, the final group after a full wait.
            int lastStep = Mathf.Max(0, count - finalTogether);
            float[] stepTimes = new float[lastStep + 1];

            for (int step = 1; step <= lastStep; step++)
            {
                float wait = step == lastStep ? activationInterval : Mathf.Max(minActivationInterval, activationInterval - activationSpeedup * (step - 1));
                stepTimes[step] = stepTimes[step - 1] + Mathf.Max(0f, wait);
            }

            float startAngle = Random.Range(0f, 360f);
            float lastActivation = 0f;
            List<CircleSword> swords = new List<CircleSword>();

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + i * 360f / count;
                float activateAt = sword.riseTime + stepTimes[Mathf.Min(turns[i], lastStep)];
                lastActivation = Mathf.Max(lastActivation, activateAt);
                swords.Add(MakeSword(enemy, centre, angle, ringRadius, activateAt, true, Random.value));
            }

            // The outer ring goes once the inner one is all under way, in order round from a random sword.
            float outerStart = sword.riseTime + stepTimes[lastStep] + outerDelay;
            float outerAngle = Random.Range(0f, 360f);
            float winding = Random.value < 0.5f ? 1f : -1f;

            for (int i = 0; i < outerCount; i++)
            {
                float angle = outerAngle + winding * i * 360f / outerCount;
                float activateAt = outerStart + i * outerInterval;
                lastActivation = Mathf.Max(lastActivation, activateAt);
                swords.Add(MakeSword(enemy, centre, angle, outerRadius, activateAt, false, Random.value));
            }

            circlesUntil = lastActivation + sword.trackTime + sword.holdTime + sword.fireTime;
            return swords;
        }

        /// <summary>
        /// A sword <paramref name="radius"/> out from <paramref name="centre"/>
        /// at <paramref name="angle"/>, in the colour of <paramref name="hue"/>.
        /// It aims at the player if it
        /// <paramref name="tracks"/>, otherwise in at the centre.
        /// </summary>
        private CircleSword MakeSword(Enemy enemy, Vector3 centre, float angle, float radius, float activateAt,
                                      bool tracks, float hue)
        {
            Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

            Color plain = Color.HSVToRGB(hue, saturation, 1f);
            Color colour = plain * intensity;
            colour.a = 1f;

            return CircleSword.Create(enemy, centre + outward * radius, outward, activateAt, colour, plain, tracks, sword);
        }

        protected override List<Ring> PlanRings()
        {
            circleHue = Random.value;
            float innermost = outerRadius + circleRadius + spiralClearance;

            List<Ring> rings = new List<Ring>();
            float time = 0f;

            int circles = 0;

            // Rings closing in from the outside, over and over, until the last laser stops.
            while (time < circlesUntil && circles < MaxCircles)
            {
                float radius = startRadius;

                do
                {
                    // The same spacing round every ring, so inner rings hold fewer circles.
                    int count = Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.PI * radius / circleSpacing));
                    rings.Add(new Ring { time = time, distance = radius, angle = Random.Range(0f, 360f), circles = count });

                    circles += count;
                    radius -= ringStep;
                    time += ringInterval;
                }
                while (radius > innermost && time < circlesUntil && circles < MaxCircles);
            }

            return rings;
        }

        protected override CircleColours? NextCircleColours()
        {
            Color colour = Color.HSVToRGB(circleHue, saturation, 1f);
            circleHue = Mathf.Repeat(circleHue + GoldenRatio, 1f);

            Color spear = colour * intensity;
            spear.a = 1f;
            return new CircleColours { warning = colour, spear = spear };
        }

        protected override void Reset()
        {
            base.Reset();
            delayAfter = 2f;
            spearsPerCircle = 1;
        }
    }
}
