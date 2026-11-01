using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Luna's sword rain: she leaps to the middle of the arena and lands tip
    /// first in a huge explosion, then rings of warning circles close in on
    /// her, over and over, each circle in a colour of its own with a sword of
    /// that colour dropping into it, while she throws a turning ring of
    /// daggers that fly out and boomerang back to her.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The leap: unless she is already within
    ///      <see cref="centreTolerance"/> of the enemy's
    ///      <see cref="Enemy.ArenaCentre"/>, she arcs <see cref="leapHeight"/>
    ///      high over <see cref="leapTime"/> to it, as the captive swords do,
    ///      somersaulting <see cref="leapSpin"/> degrees edge first, while a
    ///      <see cref="WarningCircle"/> of <see cref="landingRadius"/> fills
    ///      where she will land. She comes down tip first, sunk
    ///      <see cref="sinkDepth"/> into the floor, in a heavy impact -- a
    ///      <see cref="HammerImpact"/>, a <see cref="BlastSphere"/> of
    ///      <see cref="blastRadius"/> and a jolt of the camera -- and
    ///      <see cref="landingDamage"/> to the player inside the circle.
    ///      Without an arena centre, or already there, she only settles tip
    ///      first into the floor where she is over <see cref="stabTime"/>,
    ///      keeping the way she faces: no warning, no explosion, no damage,
    ///      and no wait after. Already stuck there, she does not move. Her
    ///      colliders are off in the air.
    ///   2. After a leap she stays stuck for <see cref="landingHold"/>; then the rings
    ///      start round her: every <see cref="ringInterval"/> seconds a whole
    ///      ring of circles, the first <see cref="startRadius"/> out, each
    ///      next one
    ///      <see cref="ringStep"/> closer and turned <see cref="ringTurn"/>
    ///      degrees further, down to no closer than <see cref="endRadius"/>.
    ///      The innermost ring has <see cref="circlesPerRing"/> circles, and
    ///      each ring further out more in proportion to its size, so the
    ///      circles are as far apart on every ring.
    ///      After the innermost, the next ring is the outermost again, round
    ///      and round for <see cref="duration"/> seconds. Each circle is
    ///      moved up to <see cref="circleJitter"/> any way off its even
    ///      place, at random.
    ///
    ///      Halfway through the duration, everything stops for
    ///      <see cref="reversePause"/> -- never less than the circles' Warn
    ///      Time, so every circle out has struck and the floor is clear
    ///      before a new one appears. Then it all goes the other way: the
    ///      rings sweep outward, from the innermost to the outermost and
    ///      back to the innermost, turning the other way, and the daggers'
    ///      spread and spin turn the other way too.
    ///   3. Every circle takes the next colour round the hue wheel, a golden
    ///      ratio step on from the last, so neighbours never look alike. One
    ///      sword in that colour, brightened by <see cref="swordIntensity"/>,
    ///      drops into its middle as it fills; the circle bursts in the same
    ///      colour and a player in it takes the circle's damage, once.
    ///   4. All the while the rings run, every <see cref="throwInterval"/>
    ///      seconds she throws <see cref="daggersPerThrow"/> daggers evenly
    ///      spread all round, <see cref="daggerHeight"/> off the floor, the
    ///      spread turned a further <see cref="throwTurn"/> degrees each
    ///      throw. Each flies <see cref="daggerReach"/> out over
    ///      <see cref="daggerOutTime"/> seconds, slowing to a stop, spins
    ///      round 180 degrees over <see cref="daggerSpinTime"/>, and comes
    ///      back to her, speeding up. Each dagger, and its trail, is a colour
    ///      <see cref="daggerHueStep"/> further round the rainbow than the
    ///      one thrown before it, so they slide smoothly through it.
    ///
    /// The attack ends once the last circle's sword is down; daggers still
    /// out come home by themselves. She stays stuck in the floor where she
    /// landed, at ground level. The circles, swords and bursts are as in
    /// <see cref="SpearShotBase"/>: set the sword model as its Spear Prefab,
    /// standing upright, tip down, with NeoNoir/Flat materials so it takes the
    /// colour. Spears Per Circle is 1. The dagger's own speed and lifetime are
    /// ignored; its damage, size and trail are its own.
    ///
    /// Her art is posed tip down local -Y, flat facing local +Z, as the
    /// player's blades are. Her Rigidbody, if she has one, is kinematic for
    /// the attack and put back as it was at the end.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Luna/Luna Sword Rain", fileName = "LunaSwordRain")]
    public sealed class LunaSwordRainPattern : SpearShotBase
    {
        private const float GoldenRatio = 0.618034f;

        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        [Header("Leap to the middle")]
        [SerializeField]
        [Tooltip("Already this close to the arena centre, measured flat, she stabs down where she is.")]
        [Min(0f)]
        private float centreTolerance = 1f;

        [SerializeField]
        [Tooltip("How high the arc rises above the higher of its two ends.")]
        [Min(0f)]
        private float leapHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the leap takes.")]
        [Min(0.01f)]
        private float leapTime = 0.9f;

        [SerializeField]
        [Tooltip("Degrees she somersaults over the leap, edge first. Whole turns land her tip down.")]
        private float leapSpin = 360f;

        [SerializeField]
        [Tooltip("Seconds the stab straight down takes, when she does not leap.")]
        [Min(0.01f)]
        private float stabTime = 0.15f;

        [SerializeField]
        [Tooltip("How far the tip sinks under the floor.")]
        [Min(0f)]
        private float sinkDepth = 0.5f;

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [SerializeField]
        [Tooltip("Seconds she stays stuck after landing before the rings start.")]
        [Min(0f)]
        private float landingHold = 0.5f;

        [SerializeField]
        [Tooltip("Radius of the warning circle where she lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 10f;

        [SerializeField]
        [Tooltip("Width of the landing warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.2f;

        [SerializeField]
        [Tooltip("Colour of the landing warning circle.")]
        private Color landingWarningColour = new Color(1f, 0.04f, 0.08f, 1f);

        [SerializeField]
        [Tooltip("Height above the floor the landing warning circle is drawn at.")]
        [Min(0f)]
        private float landingWarningHeight = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as she lands.")]
        [Min(0f)]
        private float landingDamage = 25f;

        [Header("Heavy impact")]
        [SerializeField]
        [Tooltip("Material for the landing's burst, e.g. NeoNoir/Flat in the colour you want. Empty draws no burst.")]
        private Material landingMaterial;

        [SerializeField]
        private HammerImpact.Settings landing = new HammerImpact.Settings
        {
            ringRadius = 12f,
            ringWidth = 1.5f,
            ringTime = 0.5f,
            debrisCount = 24,
            debrisSize = 0.6f,
            debrisUpSpeed = new Vector2(7f, 12f),
            debrisOutSpeed = new Vector2(5f, 12f),
            debrisTime = 0.9f,
        };

        [SerializeField]
        [Tooltip("Radius of the explosion sphere the landing bursts into. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 10f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.18f,
            fadeTime = 0.5f,
            colour = new Color(2.5f, 2.5f, 3f, 1f),
            outlineWidth = 0.3f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 1.2f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.6f;

        [Header("Rings")]
        [SerializeField]
        [Tooltip("Circles in the innermost ring, evenly spaced round her. Rings further out have more in proportion to their size, so the gaps stay the same.")]
        [Min(1)]
        private int circlesPerRing = 12;

        [SerializeField]
        [Tooltip("Distance from her of the outermost ring.")]
        [Min(0f)]
        private float startRadius = 50f;

        [SerializeField]
        [Tooltip("No ring comes closer to her than this. After the innermost, the outermost comes again.")]
        [Min(0f)]
        private float endRadius = 15f;

        [SerializeField]
        [Tooltip("How much closer each ring is than the last.")]
        [Min(0.1f)]
        private float ringStep = 5f;

        [SerializeField]
        [Tooltip("Seconds between one ring appearing and the next.")]
        [Min(0.01f)]
        private float ringInterval = 0.03f;

        [SerializeField]
        [Tooltip("Degrees each ring is turned from the last. 0 lines the circles up as spokes.")]
        private float ringTurn = 15f;

        [SerializeField]
        [Tooltip("Seconds rings keep coming, round from the outermost to the innermost and back out again.")]
        [Min(0f)]
        private float duration = 8f;

        [SerializeField]
        [Tooltip("Seconds everything stops halfway through, before the rings and daggers reverse. Never less than Warn Time, so the floor is clear first; at least twice the Dagger Out Time lets every dagger get home too.")]
        [Min(0f)]
        private float reversePause = 1.6f;

        [SerializeField]
        [Tooltip("Most each circle is moved, any way, off its even place in its ring. 0 keeps the rings neat.")]
        [Min(0f)]
        private float circleJitter = 2f;

        [Header("Colours")]
        [SerializeField]
        [Tooltip("Saturation of the circles' and swords' colours.")]
        [Range(0f, 1f)]
        private float saturation = 1f;

        [SerializeField]
        [Tooltip("Brightness of the swords' colours. Above 1 blooms. The circles keep plain full colour.")]
        [Min(0f)]
        private float swordIntensity = 2.5f;

        [Header("Daggers")]
        [SerializeField]
        private EnemyProjectile daggerPrefab;

        [SerializeField]
        [Tooltip("Daggers in each throw, evenly spaced all round.")]
        [Min(1)]
        private int daggersPerThrow = 4;

        [SerializeField]
        [Tooltip("Seconds between one throw of daggers and the next.")]
        [Min(0.02f)]
        private float throwInterval = 0.12f;

        [SerializeField]
        [Tooltip("Degrees the spread turns after each throw.")]
        private float throwTurn = 9f;

        [SerializeField]
        [Tooltip("How far out each dagger flies before turning back.")]
        [Min(0f)]
        private float daggerReach = 10f;

        [SerializeField]
        [Tooltip("Seconds a dagger takes to fly out and stop. Coming back takes as long again.")]
        [Min(0.05f)]
        private float daggerOutTime = 0.8f;

        [SerializeField]
        [Tooltip("Seconds a dagger takes to spin round 180 degrees at the turn.")]
        [Min(0f)]
        private float daggerSpinTime = 0.2f;

        [SerializeField]
        [Tooltip("Height above the floor the daggers fly at.")]
        [Min(0f)]
        private float daggerHeight = 1f;

        [SerializeField]
        [Tooltip("How far out from her middle each dagger appears.")]
        [Min(0f)]
        private float daggerSpawnDistance = 0.8f;

        [SerializeField]
        [Tooltip("How far round the rainbow each dagger's colour is from the one thrown before it. 1 is the whole way round; small slides smoothly.")]
        [Range(0f, 0.5f)]
        private float daggerHueStep = 0.015f;

        [SerializeField]
        [Tooltip("Brightness of the daggers' colours. Above 1 blooms.")]
        [Min(0f)]
        private float daggerIntensity = 2f;

        private float hue;
        private float daggerHue;
        private float spinWay;
        private float nextThrow;
        private float throwAngle;
        private float floorHeight;
        private float lastRing;

        // The last ring before the pause, and the first after it.
        private float pauseFrom;
        private float reverseAt;

        // Put back as they were once the attack is over.
        private readonly List<Collider> switchedOff = new List<Collider>();
        private Rigidbody body;
        private bool wasKinematic;
        private bool wasPhysicsDriven;

        protected override List<Ring> PlanRings()
        {
            hue = Random.value;
            daggerHue = Random.value;
            spinWay = Random.value < 0.5f ? 1f : -1f;
            nextThrow = 0f;
            throwAngle = Random.Range(0f, 360f);

            // One pass, outermost to innermost; every ring after it starts a pass
            // again. After the pause, the passes run innermost to outermost.
            int perPass = endRadius > startRadius ? 1 : Mathf.FloorToInt((startRadius - endRadius) / ringStep) + 1;
            int inward = Mathf.FloorToInt(duration * 0.5f / ringInterval) + 1;
            int outward = Mathf.Max(1, Mathf.FloorToInt(duration / ringInterval) + 1 - inward);
            float innermost = Mathf.Max(0.1f, startRadius - (perPass - 1) * ringStep);

            pauseFrom = (inward - 1) * ringInterval;
            reverseAt = pauseFrom + Mathf.Max(reversePause, warnTime, ringInterval);
            lastRing = reverseAt + (outward - 1) * ringInterval;

            List<Ring> rings = new List<Ring>(inward + outward);
            float angle = Random.Range(0f, 360f);

            for (int i = 0; i < inward; i++)
            {
                rings.Add(MakeRing(i * ringInterval, startRadius - (i % perPass) * ringStep, angle, innermost));
                angle += ringTurn;
            }

            for (int i = 0; i < outward; i++)
            {
                angle -= ringTurn;
                rings.Add(MakeRing(reverseAt + i * ringInterval, innermost + (i % perPass) * ringStep, angle, innermost));
            }

            return rings;
        }

        private Ring MakeRing(float time, float distance, float angle, float innermost)
        {
            return new Ring
            {
                time = time,
                distance = distance,
                angle = angle,
                circles = Mathf.Max(1, Mathf.RoundToInt(circlesPerRing * distance / innermost)),
            };
        }

        protected override CircleColours? NextCircleColours()
        {
            Color colour = Color.HSVToRGB(hue, saturation, 1f);
            hue = Mathf.Repeat(hue + GoldenRatio, 1f);

            Color sword = colour * swordIntensity;
            sword.a = 1f;
            return new CircleColours { warning = colour, spear = sword };
        }

        protected override Vector3 NextCircleOffset()
        {
            Vector2 offset = Random.insideUnitCircle * circleJitter;
            return new Vector3(offset.x, 0f, offset.y);
        }

        protected override void Striking(Enemy enemy, float clock)
        {
            // Only while rings are still coming, not in the pause, nor while
            // the last ones strike.
            while (clock >= nextThrow && nextThrow <= lastRing)
            {
                if (nextThrow > pauseFrom && nextThrow < reverseAt)
                {
                    nextThrow = reverseAt;
                    continue;
                }

                float way = nextThrow >= reverseAt ? -spinWay : spinWay;
                Throw(enemy, throwAngle, way);
                throwAngle += way * throwTurn;
                nextThrow += throwInterval;
            }
        }

        private void Throw(Enemy enemy, float turn, float way)
        {
            if (daggerPrefab == null)
            {
                return;
            }

            Vector3 middle = enemy.transform.position;
            middle.y = floorHeight + daggerHeight;

            for (int arm = 0; arm < daggersPerThrow; arm++)
            {
                Vector3 direction = Quaternion.Euler(0f, turn + arm * 360f / daggersPerThrow, 0f) * Vector3.forward;
                Vector3 from = middle + direction * daggerSpawnDistance;

                EnemyProjectile dagger = EnemyProjectile.Spawn(daggerPrefab, from, Quaternion.LookRotation(direction, Vector3.up));
                dagger.LaunchBoomerang(direction, daggerReach, daggerOutTime, way * 180f, daggerSpinTime);

                Color colour = Color.HSVToRGB(daggerHue, saturation, 1f) * daggerIntensity;
                colour.a = 1f;
                dagger.Tint(colour);
                daggerHue = Mathf.Repeat(daggerHue + daggerHueStep, 1f);
            }
        }

        // ---- the leap ------------------------------------------------------

        protected override IEnumerator Arrive(Enemy enemy)
        {
            Transform root = enemy.transform;

            // Posed by hand from here to the end of the attack.
            enemy.VelocityOverride = Vector3.zero;
            wasPhysicsDriven = enemy.PhysicsDriven;
            enemy.PhysicsDriven = true;

            body = enemy.GetComponent<Rigidbody>();
            wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            switchedOff.Clear();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            Vector3 heading = Flat(root.forward);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;

            bool leap = enemy.ArenaCentre.HasValue
                        && Flat(enemy.ArenaCentre.Value - root.position).sqrMagnitude > centreTolerance * centreTolerance;

            Vector3 floor = FindGround(root, leap ? enemy.ArenaCentre.Value : root.position);

            if (leap)
            {
                Vector3 flight = Flat(floor - root.position);
                heading = flight.sqrMagnitude > 0.0001f ? flight.normalized : heading;
            }
            else
            {
                // Stood side on to the heading, so this keeps her facing as it is.
                heading = Vector3.Cross(heading, Vector3.up);
            }

            float tipBelow = MeasureTip(root, heading);
            Vector3 across = Vector3.Cross(Vector3.up, heading);
            Quaternion stuckRotation = Quaternion.LookRotation(across, Vector3.up);
            Vector3 stuck = floor + Vector3.up * (tipBelow - sinkDepth);

            Vector3 start = root.position;
            Quaternion launch = root.rotation;

            WarningCircle warning = null;

            if (leap)
            {
                warning = WarningCircle.Create(floor + Vector3.up * landingWarningHeight, landingRadius,
                                               landingOutline, null, landingWarningColour);
                warning.DestroyWhen(() => enemy == null || enemy.Health.IsDead);
            }

            float flightTime = leap ? leapTime : stabTime;
            float peak = Mathf.Max(start.y, stuck.y) + leapHeight;

            for (float clock = 0f; clock < flightTime; )
            {
                yield return null;
                clock += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    yield break;
                }

                float t = Mathf.Clamp01(clock / flightTime);

                if (leap)
                {
                    warning.Fill = t;

                    Vector3 point = Vector3.Lerp(start, stuck, t);
                    point.y = Arc(start.y, peak, stuck.y, t);

                    // Turned side on over the first moments, tumbling all the way.
                    Quaternion tumbling = Quaternion.AngleAxis(leapSpin * Smooth(t), across) * stuckRotation;
                    root.SetPositionAndRotation(point, Quaternion.Slerp(launch, tumbling, Smooth(t * 4f)));
                }
                else
                {
                    float eased = t * t;
                    root.SetPositionAndRotation(Vector3.Lerp(start, stuck, eased), Quaternion.Slerp(launch, stuckRotation, eased));
                }
            }

            root.SetPositionAndRotation(stuck, stuckRotation);
            floorHeight = floor.y;

            if (leap)
            {
                Destroy(warning.gameObject);
                Impact(enemy, floor);
            }

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            switchedOff.Clear();

            if (leap && landingHold > 0f)
            {
                yield return new WaitForSeconds(landingHold);
            }
        }

        protected override void Finished(Enemy enemy)
        {
            enemy.PhysicsDriven = wasPhysicsDriven;
            enemy.VelocityOverride = null;

            if (body != null)
            {
                body.isKinematic = wasKinematic;

                if (!wasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }
        }

        /// <summary>The heavy landing: dust and debris, the explosion, a jolt, and damage inside the circle.</summary>
        private void Impact(Enemy enemy, Vector3 floor)
        {
            if (landingMaterial != null)
            {
                HammerImpact.Create(floor, landing, landingMaterial);
            }

            if (blastRadius > 0f)
            {
                BlastSphere.Create(floor, blastRadius, blast);
            }

            if (landingShake > 0f)
            {
                TopDownCamera.Shake(landingShake, landingShakeDuration);
            }

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            if (player != null && !player.IsDead && landingDamage > 0f
                && Flat(player.transform.position - floor).sqrMagnitude <= landingRadius * landingRadius)
            {
                player.TakeDamage(landingDamage, floor);
            }
        }

        /// <summary>
        /// How far her art reaches below her pivot, stood upright side on to
        /// <paramref name="heading"/>: measured from her renderers by trying
        /// the pose on and putting it back, all within the frame.
        /// </summary>
        private static float MeasureTip(Transform root, Vector3 heading)
        {
            Quaternion rotation = root.rotation;
            root.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, heading), Vector3.up);

            float lowest = float.MaxValue;

            foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
            {
                if (part.enabled && !(part is ParticleSystemRenderer) && !(part is LineRenderer) && !(part is TrailRenderer))
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            float below = lowest < float.MaxValue ? root.position.y - lowest : 1f;
            root.rotation = rotation;
            return Mathf.Max(below, 0f);
        }

        /// <summary>The highest floor under <paramref name="point"/>, never herself; the point itself if there is none.</summary>
        private Vector3 FindGround(Transform root, Vector3 point)
        {
            // From well above, so a raised floor still counts.
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 20f, Vector3.down, GroundHits, 40f,
                                                groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = GroundHits[i];

                if (hit.normal.y >= 0.5f && !hit.transform.IsChildOf(root)
                    && hit.collider.GetComponentInParent<PlayerMotor>() == null && hit.point.y > best)
                {
                    best = hit.point.y;
                }
            }

            if (best > float.NegativeInfinity)
            {
                point.y = best;
            }

            return point;
        }

        /// <summary>
        /// Height at <paramref name="t"/> along a parabola from
        /// <paramref name="from"/> to <paramref name="to"/> whose top is
        /// <paramref name="top"/>.
        /// </summary>
        private static float Arc(float from, float top, float to, float t)
        {
            // As Amber's leap: y = from + b t + c t^2, through both ends,
            // topping out at top.
            float rise = Mathf.Max(top - from, 0.0001f);
            float total = to - from;
            float b = 2f * (rise + Mathf.Sqrt(rise * Mathf.Max(rise - total, 0f)));
            float c = total - b;
            return from + b * t + c * t * t;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        protected override void Reset()
        {
            base.Reset();
            spearsPerCircle = 1;
            groundMask = LayerMask.GetMask("Default");
        }
    }
}
