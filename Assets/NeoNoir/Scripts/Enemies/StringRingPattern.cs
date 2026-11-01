using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's string ring: it glides to the middle of the arena and
    /// lays out shapes of string circles round itself -- a cross, a spiral,
    /// a trinity -- then sends a wall of them sweeping across the arena.
    /// </summary>
    /// <remarks>
    /// Every circle is a stationary <see cref="TargetedStrings"/> volley: it
    /// fills where it appears, then strings shoot down into it and snare the
    /// player if they are caught, as the targeted attack's do. Their look,
    /// timing and damage are <see cref="circles"/>.
    ///
    /// Each shape is drawn out from the boss a few circles at a time, a step
    /// every <see cref="stepDelay"/> seconds, and every circle fills on its
    /// own clock, so the strikes follow the drawing. The next shape starts
    /// once every circle of the last has struck. Circles stop at the arena's
    /// bounding square, the gaps of an L- or cross-shaped arena included.
    /// Each shape is turned at random.
    ///
    /// In order:
    ///   1. Glide: it slides to the middle of the arena, as the tornado
    ///      does, easing in as it arrives, for at most
    ///      <see cref="maxGlideTime"/>.
    ///   2. Cross: four arms, circles <see cref="spacing"/> apart, a ring of
    ///      four further out each step, until the arms leave the arena.
    ///   3. Spiral: a circle under the boss, then <see cref="spiralArms"/>
    ///      arms winding out from it,
    ///      <see cref="spiralGap"/> between one turn and the next, circles
    ///      <see cref="spacing"/> apart along each, one more per arm each step.
    ///   4. Trinity: three circles of circles, their middles
    ///      <see cref="trinityOffset"/> from the boss a third of a turn apart,
    ///      each <see cref="trinityRadius"/> across from its middle, so they
    ///      overlap round the boss like the trinity knot. All three are drawn
    ///      at once, round from where they meet the boss. With
    ///      <see cref="fillOutsideTrinity"/> on, everywhere outside the three
    ///      is filled with circles <see cref="fillSpacing"/> apart the moment
    ///      the drawing starts, so the only shelter is inside them.
    ///   5. With <see cref="endWithWall"/> on, a <see cref="StringWall"/>
    ///      starts across the arena in one of four directions, and the attack
    ///      ends; the wall carries on to the far side by itself.
    ///
    /// Needs the arena the <see cref="PuppeteerIntro"/> hands the boss.
    /// Movement above is ignored; the attack moves it itself.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/String Ring", fileName = "StringRing")]
    public sealed class StringRingPattern : AttackPattern
    {
        /// <summary>Close enough to the middle to stop gliding.</summary>
        private const float ArriveDistance = 0.15f;

        /// <summary>Most steps any one shape takes, in case an arena is so large it never seems to end.</summary>
        private const int MaxSteps = 400;

        [Header("Glide")]
        [SerializeField]
        [Tooltip("Fastest it glides to the middle.")]
        [Min(0.1f)]
        private float glideSpeed = 8f;

        [SerializeField]
        [Tooltip("Seconds it glides for at most before starting wherever it is.")]
        [Min(0f)]
        private float maxGlideTime = 4f;

        [Header("Layout")]
        [SerializeField]
        [Tooltip("Distance between neighbouring circles, in every shape and in the wall. Twice the circle radius has them just touching.")]
        [Min(0.1f)]
        private float spacing = 2.5f;

        [SerializeField]
        [Tooltip("Seconds between one step of a shape and the next, further along.")]
        [Min(0f)]
        [FormerlySerializedAs("crossStepDelay")]
        private float stepDelay = 0.03f;

        [SerializeField]
        [Tooltip("Most circles made in one frame. A step with more, such as the trinity's fill, is spread over several frames, so making them never stalls one.")]
        [Min(1)]
        private int maxPerFrame = 30;

        [Header("Spiral")]
        [SerializeField]
        [Tooltip("Arms of the spiral, evenly spaced round the boss.")]
        [Min(1)]
        private int spiralArms = 1;

        [SerializeField]
        [Tooltip("Distance outward between one turn of an arm and the next. The gap the player stands in.")]
        [Min(0.5f)]
        private float spiralGap = 6f;

        [Header("Trinity")]
        [SerializeField]
        [Tooltip("How far from the boss each of the three circles' middles is.")]
        [Min(0f)]
        private float trinityOffset = 5f;

        [SerializeField]
        [Tooltip("Radius of each of the three circles. Equal to the offset, each passes through the boss.")]
        [Min(0.5f)]
        private float trinityRadius = 5f;

        [SerializeField]
        [Tooltip("Also fill everywhere outside the three circles with circles, all at once as the trinity starts, so inside them is the only shelter.")]
        private bool fillOutsideTrinity = true;

        [SerializeField]
        [Tooltip("Distance between the fill's circles. Larger is fewer circles, cheaper to draw, with wider gaps.")]
        [Min(0.5f)]
        private float fillSpacing = 2.5f;

        [Header("Wall")]
        [SerializeField]
        [Tooltip("Finish with a wall of circles sweeping across the arena.")]
        private bool endWithWall = true;

        [SerializeField]
        private TargetedStrings.Settings circles = new TargetedStrings.Settings
        {
            tracking = false,
            fillTime = 1f,
            count = 3,
            lingerTime = 0.1f,
            lingerFade = 0f,

            // Short, quick strings: a few points whip well enough, and there are hundreds.
            recoilSegments = 6,
        };

        public override IEnumerator Perform(Enemy enemy)
        {
            ArenaBounds arena = enemy.Arena;

            if (arena == null || !enemy.HasTarget)
            {
                if (arena == null)
                {
                    Debug.LogWarning($"[StringRing] {enemy.name} has no arena to lay strings in.", enemy);
                }

                yield break;
            }

            Vector3 centre = arena.Centre;
            centre.y = enemy.transform.position.y;
            yield return Glide(enemy, centre);

            yield return Draw(enemy, arena, CrossSteps(arena, enemy.transform.position));
            yield return Draw(enemy, arena, SpiralSteps(arena, enemy.transform.position));
            yield return Draw(enemy, arena, TrinitySteps(arena, enemy.transform.position));

            enemy.VelocityOverride = null;

            if (endWithWall && !enemy.Health.IsDead && enemy.HasTarget)
            {
                StringWall.Sweep(enemy, enemy.Target, arena, spacing, circles);
            }
        }

        /// <summary>Slides to <paramref name="centre"/>, easing in as it arrives.</summary>
        private IEnumerator Glide(Enemy enemy, Vector3 centre)
        {
            float clock = 0f;

            while (clock < maxGlideTime && !enemy.Health.IsDead)
            {
                Vector3 toCentre = centre - enemy.transform.position;
                toCentre.y = 0f;
                float distance = toCentre.magnitude;

                if (distance <= ArriveDistance)
                {
                    break;
                }

                // Slows over the last stretch, so it settles rather than stops dead.
                float speed = Mathf.Min(glideSpeed, distance * 3f);
                enemy.VelocityOverride = toCentre / distance * speed;

                yield return null;
                clock += Time.deltaTime;
            }

            enemy.VelocityOverride = Vector3.zero;
        }

        /// <summary>
        /// Lays a shape a step at a time, every <see cref="stepDelay"/>, then
        /// waits until every circle of it has struck.
        /// </summary>
        private IEnumerator Draw(Enemy enemy, ArenaBounds arena, List<List<Vector3>> steps)
        {
            List<TargetedStrings> laid = new List<TargetedStrings>();

            foreach (List<Vector3> step in steps)
            {
                if (enemy.Health.IsDead || !enemy.HasTarget)
                {
                    yield break;
                }

                enemy.VelocityOverride = Vector3.zero;
                int thisFrame = 0;

                foreach (Vector3 spot in step)
                {
                    // A big step, such as the trinity's fill, is spread over
                    // a few frames rather than made all in one.
                    if (thisFrame >= maxPerFrame)
                    {
                        thisFrame = 0;
                        yield return null;

                        if (enemy.Health.IsDead || !enemy.HasTarget)
                        {
                            yield break;
                        }
                    }

                    TargetedStrings circle = Place(enemy, enemy.Target, arena, spot, circles);
                    thisFrame++;

                    if (circle != null)
                    {
                        laid.Add(circle);
                    }
                }

                if (stepDelay > 0f)
                {
                    yield return new WaitForSeconds(stepDelay);
                }
            }

            while (!enemy.Health.IsDead && laid.Exists(circle => circle != null && !circle.HasStruck))
            {
                enemy.VelocityOverride = Vector3.zero;
                yield return null;
            }
        }

        // ---- shapes ---------------------------------------------------------
        // Each is a list of steps, each step the spots laid at once.

        /// <summary>Four arms from <paramref name="origin"/>, the circle under it first.</summary>
        private List<List<Vector3>> CrossSteps(ArenaBounds arena, Vector3 origin)
        {
            List<List<Vector3>> steps = new List<List<Vector3>> { new List<Vector3> { origin } };
            float turn = Random.Range(0f, 90f);

            for (int ring = 1; ring < MaxSteps; ring++)
            {
                List<Vector3> step = new List<Vector3>();

                for (int arm = 0; arm < 4; arm++)
                {
                    Vector3 spot = origin + Direction(turn + arm * 90f) * (spacing * ring);

                    if (arena.InSquare(spot))
                    {
                        step.Add(spot);
                    }
                }

                if (step.Count == 0)
                {
                    break;
                }

                steps.Add(step);
            }

            return steps;
        }

        /// <summary>
        /// Arms winding out from <paramref name="origin"/>, the circle under
        /// it first: each an Archimedes spiral, its radius growing by
        /// <see cref="spiralGap"/> a turn, with circles <see cref="spacing"/>
        /// apart along it.
        /// </summary>
        private List<List<Vector3>> SpiralSteps(ArenaBounds arena, Vector3 origin)
        {
            // From the boss itself, so the spot it stands on is covered too.
            List<List<Vector3>> steps = new List<List<Vector3>> { new List<Vector3> { origin } };
            float turn = Random.Range(0f, 360f);
            float growth = spiralGap / (2f * Mathf.PI);

            // Wound out until past the square's furthest corner: a turn can
            // leave past one edge and still reach into a corner further on.
            float reach = FurthestCorner(arena, origin);

            // Every arm shares one sweep: they stay evenly spaced round it.
            float sweep = spacing / Mathf.Max(growth, 0.01f);

            for (int i = 0; i < MaxSteps; i++)
            {
                float radius = growth * sweep;

                if (radius > reach)
                {
                    break;
                }

                List<Vector3> step = new List<Vector3>();

                for (int arm = 0; arm < spiralArms; arm++)
                {
                    float angle = turn + arm * 360f / spiralArms + sweep * Mathf.Rad2Deg;
                    Vector3 spot = origin + Direction(angle) * radius;

                    if (arena.InSquare(spot))
                    {
                        step.Add(spot);
                    }
                }

                if (step.Count > 0)
                {
                    steps.Add(step);
                }

                // Equal steps along the curve, however far out it has wound.
                sweep += spacing / Mathf.Sqrt(radius * radius + growth * growth);
            }

            return steps;
        }

        /// <summary>
        /// Three circles round <paramref name="origin"/>, a third of a turn
        /// apart, each drawn round from the point nearest the boss, all three
        /// at once.
        /// </summary>
        private List<List<Vector3>> TrinitySteps(ArenaBounds arena, Vector3 origin)
        {
            List<List<Vector3>> steps = new List<List<Vector3>>();
            float turn = Random.Range(0f, 360f);
            int around = Mathf.Max(3, Mathf.RoundToInt(2f * Mathf.PI * trinityRadius / spacing));

            for (int i = 0; i < around; i++)
            {
                List<Vector3> step = new List<Vector3>();

                for (int loop = 0; loop < 3; loop++)
                {
                    float facing = turn + loop * 120f;
                    Vector3 middle = origin + Direction(facing) * trinityOffset;

                    // Starting from the side facing the boss, going round.
                    float angle = facing + 180f + i * 360f / around;
                    Vector3 spot = middle + Direction(angle) * trinityRadius;

                    if (arena.InSquare(spot))
                    {
                        step.Add(spot);
                    }
                }

                if (step.Count > 0)
                {
                    steps.Add(step);
                }
            }

            // Everywhere outside the three circles, laid all at once as the
            // drawing starts: inside them is the only shelter.
            if (fillOutsideTrinity)
            {
                Vector3[] middles = new Vector3[3];

                for (int loop = 0; loop < 3; loop++)
                {
                    middles[loop] = origin + Direction(turn + loop * 120f) * trinityOffset;
                }

                List<Vector3> fill = OutsideTrinity(arena, middles);

                if (steps.Count == 0)
                {
                    steps.Add(fill);
                }
                else
                {
                    steps[0].AddRange(fill);
                }
            }

            return steps;
        }

        /// <summary>
        /// A grid of spots, <see cref="fillSpacing"/> apart and square to the
        /// arena, over its whole bounding square but for the three circles
        /// round <paramref name="middles"/> and their rims, which the trinity's
        /// own circles cover.
        /// </summary>
        private List<Vector3> OutsideTrinity(ArenaBounds arena, Vector3[] middles)
        {
            List<Vector3> spots = new List<Vector3>();
            arena.Extent(out Vector3 right, out Vector3 forward, out Vector2 min, out Vector2 max);
            Vector3 centre = arena.Centre;

            // Clear of the rims by half a spacing, so the fill never doubles up on them.
            float clear = trinityRadius + spacing * 0.5f;
            float step = Mathf.Max(0.5f, fillSpacing);

            int columns = Mathf.Max(1, Mathf.FloorToInt((max.x - min.x) / step));
            int rows = Mathf.Max(1, Mathf.FloorToInt((max.y - min.y) / step));
            float columnStep = (max.x - min.x) / columns;
            float rowStep = (max.y - min.y) / rows;

            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    Vector3 spot = centre + right * (min.x + columnStep * (c + 0.5f))
                                          + forward * (min.y + rowStep * (r + 0.5f));
                    bool sheltered = false;

                    foreach (Vector3 middle in middles)
                    {
                        Vector3 offset = spot - middle;
                        offset.y = 0f;

                        if (offset.sqrMagnitude < clear * clear)
                        {
                            sheltered = true;
                            break;
                        }
                    }

                    if (!sheltered)
                    {
                        spots.Add(spot);
                    }
                }
            }

            return spots;
        }

        /// <summary>Flat distance from <paramref name="origin"/> to the furthest corner of the arena's bounding square.</summary>
        private static float FurthestCorner(ArenaBounds arena, Vector3 origin)
        {
            arena.Extent(out Vector3 right, out Vector3 forward, out Vector2 min, out Vector2 max);
            Vector3 centre = arena.Centre;
            float furthest = 0f;

            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = centre + right * ((corner & 1) == 0 ? min.x : max.x)
                                       + forward * ((corner & 2) == 0 ? min.y : max.y);
                Vector3 offset = point - origin;
                offset.y = 0f;
                furthest = Mathf.Max(furthest, offset.magnitude);
            }

            return furthest;
        }

        private static Vector3 Direction(float degrees)
        {
            return Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
        }

        /// <summary>
        /// Puts a stationary circle at <paramref name="spot"/>: on the arena
        /// floor there, or, off the arena's cubes, at the height of its middle,
        /// so the strings cover its whole bounding square.
        /// </summary>
        public static TargetedStrings Place(Enemy boss, Transform player, ArenaBounds arena, Vector3 spot,
                                            TargetedStrings.Settings settings)
        {
            spot.y = arena.FloorAt(spot, boss.transform, out float floor) ? floor : arena.Centre.y;
            return TargetedStrings.Create(boss, player, spot, settings);
        }

        private void Reset()
        {
            delayAfter = 1.5f;
            Movement.type = MovementType.StandStill;
        }
    }
}
