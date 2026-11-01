using System;
using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss marks a wide circle round the player, leaps into the middle
    /// of it, its mace glistening overhead, and brings it down as it lands:
    /// everything in the circle is hurt, and blocks of rubble burst up out of
    /// the impact and tumble down round it one after another, a sword in a
    /// stone growing out of the floor where each lands.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. A <see cref="WarningCircle"/> of <see cref="impactRadius"/> is laid
    ///      on the floor round the player, where they stand now. It fills
    ///      until the boss lands.
    ///   2. The mace is borrowed from the boss's <see cref="BossBlades"/> --
    ///      the one spawned from <see cref="bladePrefab"/> -- and starts to
    ///      glisten, building to the strike. Over <see cref="windupTime"/> it
    ///      rises overhead, cocked back, and the boss turns to the circle.
    ///   3. The boss leaps into the middle of the circle in an arc
    ///      <see cref="leapHeight"/> high over <see cref="leapTime"/>, the mace
    ///      drawing further back, and over the last <see cref="swingTime"/>
    ///      of it swings over and down, to meet the floor in front of the
    ///      boss just as it lands.
    ///   4. The impact: the player anywhere in the circle takes
    ///      <see cref="impactDamage"/>; a <see cref="HammerImpact"/> bursts
    ///      where the head lands and the camera jolts hard; a web of glowing
    ///      <see cref="FloorCracks"/> (<see cref="cracks"/>) spreads across
    ///      the circle and fades; and
    ///      <see cref="blockCount"/> blocks burst up out of the impact and
    ///      come down on spots round it, <see cref="blockSpread"/>.x to .y
    ///      out, the first after <see cref="firstLanding"/> seconds and the
    ///      rest <see cref="landingInterval"/> apart, give or take
    ///      <see cref="landingJitter"/>. Each is a <see cref="MaceDebris"/>
    ///      (<see cref="blocks"/>): its spot is marked as it falls, and the
    ///      player still on it when it lands is hurt.
    ///   5. Where each block lands, a sword in a stone of
    ///      <see cref="swordPrefab"/> grows up out of the floor over
    ///      <see cref="growTime"/>, turned at random about the vertical, its
    ///      bottom <see cref="swordSink"/> under the floor
    ///      (<see cref="StoneSword.Spawn"/>). Once grown it is pulled and
    ///      thrown like any other. With no Sword Prefab, nothing grows.
    ///   6. The mace stays in the floor for <see cref="holdTime"/>, then goes
    ///      back into the orbit, and the attack is over. Blocks still in the
    ///      air carry on falling into whatever comes next.
    ///
    /// With <see cref="leapCount"/> above 1, steps 1 to 5 repeat that many
    /// times in a row: after each landing but the last the mace stays in the
    /// floor only <see cref="holdBetweenLeaps"/>, then rises straight from
    /// there into the next windup, with a fresh circle round the player.
    ///
    /// The leap moves the boss itself: its body goes kinematic and
    /// <see cref="Enemy.PhysicsDriven"/> holds off its own movement until it
    /// lands. Its feet -- the bottom of its colliders -- land on the floor.
    ///
    /// The blocks start at a random angle and tumble as they fly, at the
    /// blocks' Tumble Speed. The blocks' art and the swords are made ready as
    /// the attack starts, so neither is built mid-fight. Spots outside the boss's
    /// <see cref="Enemy.Arena"/>, if it has one, are tried again elsewhere.
    ///
    /// The mace is posed the way <see cref="BossBlades"/> poses it: head down
    /// local -Y, as MaceHead.obj's is, with the entry's Rotation Offset for
    /// one built otherwise. It swings about a grip <see cref="headDistance"/>
    /// back from its head along that line.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Mace Leap", fileName = "MaceLeap")]
    public sealed class MaceLeapPattern : AttackPattern
    {
        private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

        private const int SpotTries = 6;

        [SerializeField]
        [Tooltip("The mace's prefab, as listed in the boss's Boss Blades. The blade spawned from it is the one swung.")]
        private GameObject bladePrefab;

        [SerializeField]
        private BladeGlisten.Settings glisten = new BladeGlisten.Settings { colour = new Color(0.5943396f, 0f, 0.023271088f) };

        [Header("Target")]
        [SerializeField]
        [Tooltip("Radius of the circle marked round the player, which the boss leaps into the middle of and hurts all of.")]
        [Min(0.1f)]
        private float impactRadius = 10f;

        [SerializeField]
        [Tooltip("Damage to the player anywhere in the circle as the boss lands.")]
        [Min(0f)]
        private float impactDamage = 20f;

        [SerializeField]
        private Color warningColour = new Color(1f, 0.25f, 0.2f, 1f);

        [SerializeField]
        [Tooltip("Thickness of the circle's outline.")]
        [Min(0f)]
        private float warningOutline = 0.15f;

        [Header("Leap")]
        [SerializeField]
        [Tooltip("Leaps in a row. Each marks a fresh circle round the player, where they stand as it starts.")]
        [Min(1)]
        private int leapCount = 1;

        [SerializeField]
        [Tooltip("Seconds the mace rises overhead and the boss turns, before it leaps. The circle shows for this and the leap together.")]
        [Min(0f)]
        private float windupTime = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the leap takes, take-off to landing.")]
        [Min(0.05f)]
        private float leapTime = 0.9f;

        [SerializeField]
        [Tooltip("How high the leap rises above the straight line from take-off to landing, at its peak.")]
        [Min(0f)]
        private float leapHeight = 7f;

        [SerializeField]
        [Tooltip("Degrees per second the boss turns to face the circle through the windup.")]
        [Min(0f)]
        private float turnSpeed = 540f;

        [SerializeField]
        [Tooltip("Layers the boss lands on, and the circle and blocks are laid on.")]
        private LayerMask floorMask = 1;

        [Header("Mace")]
        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot while the mace is overhead.")]
        private float gripHeight = 3.5f;

        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot once the mace has come down.")]
        private float slamGripHeight = 1.2f;

        [SerializeField]
        [Tooltip("How far in front of the boss the grip comes down.")]
        private float slamReach = 1.2f;

        [SerializeField]
        [Tooltip("Distance from the mace's grip, the point it swings about, to the middle of its head, at its spawned scale.")]
        [Min(0.1f)]
        private float headDistance = 1.5f;

        [SerializeField]
        [Tooltip("Distance from the prefab's pivot to the middle of the head, toward the head. 0 when the pivot is the head's middle, as MaceHead.obj's is.")]
        private float pivotToHead;

        [SerializeField]
        [Tooltip("Degrees behind upright the head is cocked back as the leap starts, and as the swing starts.")]
        private Vector2 cockAngle = new Vector2(30f, 70f);

        [SerializeField]
        [Tooltip("Degrees from straight up, toward the front, the head ends at. Past 90 points it into the floor.")]
        private float slamAngle = 120f;

        [SerializeField]
        [Tooltip("Seconds of the swing over and down, ending as the boss lands.")]
        [Min(0.01f)]
        private float swingTime = 0.18f;

        [SerializeField]
        [Tooltip("Distance the mace trembles by at the top of the leap.")]
        [Min(0f)]
        private float tremble = 0.1f;

        [SerializeField]
        [Tooltip("Seconds the mace stays in the floor before going back into the orbit. The attack ends after it.")]
        [Min(0f)]
        private float holdTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the mace stays in the floor between one leap and the next, before rising for the next windup.")]
        [Min(0f)]
        private float holdBetweenLeaps = 0.2f;

        [Header("Impact")]
        [SerializeField]
        [Tooltip("The burst where the mace head lands.")]
        private HammerImpact.Settings impact = new HammerImpact.Settings
        {
            ringRadius = 10f,
            ringWidth = 1.2f,
            ringTime = 0.4f,
            ringIntensity = 3f,
            debrisCount = 14,
            debrisSize = 0.45f,
            debrisUpSpeed = new Vector2(7f, 12f),
            debrisOutSpeed = new Vector2(3f, 7f),
            debrisTime = 0.8f,
        };

        [SerializeField]
        [Tooltip("Colour of the burst. Above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color impactColour = new Color(1.6f, 0.2f, 0.2f);

        [SerializeField]
        [Tooltip("The web of glowing cracks spread across the circle by the impact. The floor itself is untouched.")]
        private FloorCracks.Settings cracks = new FloorCracks.Settings();

        [SerializeField]
        [Tooltip("How far the impact jolts the camera.")]
        [Min(0f)]
        private float shakeStrength = 1f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.5f;

        [Header("Blocks")]
        [SerializeField]
        [Tooltip("Blocks that burst out of the impact.")]
        [Min(0)]
        private int blockCount = 14;

        [SerializeField]
        [Tooltip("Nearest and furthest from the impact the blocks land.")]
        private Vector2 blockSpread = new Vector2(3f, 14f);

        [SerializeField]
        [Tooltip("Seconds after the impact the first block lands.")]
        [Min(0.05f)]
        private float firstLanding = 0.9f;

        [SerializeField]
        [Tooltip("Seconds between one block landing and the next.")]
        [Min(0f)]
        private float landingInterval = 0.2f;

        [SerializeField]
        [Tooltip("Up to this many seconds earlier or later each block lands, so they do not come down like clockwork.")]
        [Min(0f)]
        private float landingJitter = 0.08f;

        [SerializeField]
        [Tooltip("Each block, as it flies and lands: its art (rubble, with no sword), tumble, damage and warning. Its Flight Time is ignored; the landings above set it. Keep Rest Time 0 for it to vanish as the sword grows.")]
        private MaceDebris.Settings blocks = new MaceDebris.Settings
        {
            size = 1f,
            tumbleSpeed = 360f,
            arcHeight = 12f,
            damage = 10f,
            damageRadius = 1.3f,
            restTime = 0f,
            shrinkTime = 0.1f,
        };

        [Header("Swords")]
        [SerializeField]
        [Tooltip("The sword in a stone that grows where each block lands, as for Stone Swords: a Stone Sword on its root. Empty grows nothing.")]
        private GameObject swordPrefab;

        [SerializeField]
        [Tooltip("Seconds each sword in a stone takes to grow to full size. It cannot be pulled until grown.")]
        [Min(0.01f)]
        private float growTime = 0.5f;

        [SerializeField]
        [Tooltip("How far the bottom of each sword in a stone sits under the floor, so the rock looks set in it. Its pivot can be anywhere.")]
        [Min(0f)]
        private float swordSink = 0.3f;

        public override bool CanPerform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            return blades != null && blades.IndexOf(bladePrefab) >= 0 && !blades.IsBorrowed(blades.IndexOf(bladePrefab));
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            return Leaps(enemy, null, leapCount, impactRadius, false, null);
        }

        /// <summary>
        /// One leap into <paramref name="point"/> for a cutscene, such as
        /// <see cref="ChromatistPhaseThree"/>'s: no warning circle, no damage
        /// and no blocks, just the leap, the burst and a web of cracks
        /// <paramref name="radius"/> across. <paramref name="impacted"/> is
        /// called with the centre the moment the mace hits.
        /// </summary>
        public IEnumerator Slam(Enemy enemy, Vector3 point, float radius, Action<Vector3> impacted)
        {
            return Leaps(enemy, point, 1, radius, true, impacted);
        }

        /// <summary>
        /// <paramref name="count"/> leaps, into circles of
        /// <paramref name="radius"/> round <paramref name="point"/> -- or round
        /// the player, without one. <paramref name="quiet"/> leaves out the
        /// warning circle, the damage and the blocks. <paramref name="impacted"/>
        /// is called with the centre the moment the last one hits.
        /// </summary>
        private IEnumerator Leaps(Enemy enemy, Vector3? point, int count, float radius, bool quiet, Action<Vector3> impacted)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null ? blades.IndexOf(bladePrefab) : -1;

            if (index < 0)
            {
                Debug.LogWarning($"[{name}] {enemy.name} has no Boss Blades blade made from the Blade Prefab.", this);
                yield break;
            }

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;
            float warnTime = windupTime + leapTime;

            // Made now, so the first block does not build its art, or its sword, mid-fight.
            ArtTemplate.Of(blocks.art);
            StoneSword.Prepare(swordPrefab);

            Transform mace = blades.Borrow(index, this);
            Quaternion modelOffset = blades.RotationOffset(index);
            BladeGlisten shine = null;

            Rigidbody body = enemy.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            Vector3 forward = Facing(enemy);

            for (int leap = 0; leap < count; leap++)
            {
                Vector3 start = enemy.transform.position;

                // The circle, round the point given, or where the player stands now.
                Vector3 aimAt = point ?? (enemy.HasTarget ? enemy.Target.position : start + forward * 5f);
                Vector3 centre = FloorUnder(aimAt, Mathf.Max(start.y, aimAt.y));
                WarningCircle warning = null;

                if (!quiet)
                {
                    warning = WarningCircle.Create(centre + Vector3.up * 0.03f, radius, warningOutline, null, warningColour);
                    warning.DestroyWhen(() => enemy == null || enemy.Health.IsDead);
                }

                shine?.End();
                shine = BladeGlisten.Begin(mace, glisten, warnTime);

                Vector3 fromPosition = mace.position;
                Quaternion fromRotation = mace.rotation;
                float clock = 0f;

                // 1. Overhead, cocked back, turning to the circle.
                while (clock < windupTime)
                {
                    forward = TurnToward(enemy, forward, centre);
                    Pose(enemy.transform.position, forward, -cockAngle.x, 0f, Vector3.zero, modelOffset,
                         out Vector3 position, out Quaternion rotation);

                    float eased = Smooth(clock / windupTime);
                    mace.SetPositionAndRotation(Vector3.Lerp(fromPosition, position, eased),
                                                Quaternion.Slerp(fromRotation, rotation, eased));
                    shine.Tick(clock);

                    if (warning != null)
                    {
                        warning.Fill = clock / warnTime;
                    }

                    yield return null;
                    clock += Time.deltaTime;

                    if (enemy.Health.IsDead)
                    {
                        End(enemy, blades, index, shine, body, wasKinematic);
                        yield break;
                    }
                }

                // 2. The leap into the middle of the circle, the mace swinging
                // down to meet the landing. Its feet -- the bottom of its
                // colliders -- land on the floor.
                start = enemy.transform.position;
                float footOffset = start.y - enemy.Health.BodyBounds().min.y;
                Vector3 landing = centre + Vector3.up * footOffset;
                Vector3 travel = Flat(landing - start);

                if (travel.sqrMagnitude > 0.0001f)
                {
                    forward = travel.normalized;
                }

                if (body != null)
                {
                    body.isKinematic = true;
                }

                enemy.PhysicsDriven = true;
                enemy.TurnModelToward(forward, 360f);
                float elapsed = 0f;

                while (elapsed < leapTime)
                {
                    float t = elapsed / leapTime;
                    Vector3 at = Vector3.Lerp(start, landing, t) + Vector3.up * (leapHeight * 4f * t * (1f - t));
                    enemy.transform.position = at;

                    float swingFrom = leapTime - swingTime;
                    Vector3 position;
                    Quaternion rotation;

                    if (elapsed < swingFrom)
                    {
                        float charge = swingFrom > 0f ? elapsed / swingFrom : 1f;
                        Vector3 shake = UnityEngine.Random.insideUnitSphere * (tremble * charge * charge);
                        Pose(at, forward, -Mathf.Lerp(cockAngle.x, cockAngle.y, charge), 0f, shake, modelOffset,
                             out position, out rotation);
                    }
                    else
                    {
                        float swing = (elapsed - swingFrom) / swingTime;
                        swing *= swing;
                        Pose(at, forward, Mathf.Lerp(-cockAngle.y, slamAngle, swing), swing, Vector3.zero, modelOffset,
                             out position, out rotation);
                    }

                    mace.SetPositionAndRotation(position, rotation);
                    shine.Tick(windupTime + elapsed);

                    if (warning != null)
                    {
                        warning.Fill = (windupTime + elapsed) / warnTime;
                    }

                    yield return null;
                    elapsed += Time.deltaTime;

                    if (enemy.Health.IsDead)
                    {
                        End(enemy, blades, index, shine, body, wasKinematic);
                        yield break;
                    }
                }

                enemy.transform.position = landing;

                if (body != null)
                {
                    body.isKinematic = wasKinematic;
                    body.linearVelocity = Vector3.zero;
                }

                enemy.PhysicsDriven = false;
                if (warning != null)
                {
                    Destroy(warning.gameObject);
                }

                // 3. The impact.
                Pose(landing, forward, slamAngle, 1f, Vector3.zero, modelOffset, out Vector3 slamPosition, out Quaternion slamRotation);
                mace.SetPositionAndRotation(slamPosition, slamRotation);
                Smash(enemy, quiet ? null : player, centre, forward, radius, quiet ? 0 : blockCount);

                if (leap == count - 1)
                {
                    impacted?.Invoke(centre);
                }

                // 4. Left in the floor a moment: briefly between leaps, longer after the last.
                float hold = leap < count - 1 ? holdBetweenLeaps : holdTime;
                elapsed = 0f;

                do
                {
                    Pose(enemy.transform.position, forward, slamAngle, 1f, Vector3.zero, modelOffset,
                         out Vector3 position, out Quaternion rotation);
                    mace.SetPositionAndRotation(position, rotation);

                    yield return null;
                    elapsed += Time.deltaTime;
                }
                while (elapsed < hold && !enemy.Health.IsDead);

                if (enemy.Health.IsDead)
                {
                    break;
                }
            }

            End(enemy, blades, index, shine, body, wasKinematic);
        }

        private void Smash(Enemy enemy, PlayerHealth player, Vector3 centre, Vector3 forward, float radius, int blockTotal)
        {
            Vector3 head = FloorUnder(centre + forward * (slamReach + headDistance), centre.y);

            if (player != null && !player.IsDead)
            {
                Vector3 offset = Flat(player.transform.position - centre);

                if (offset.sqrMagnitude <= radius * radius)
                {
                    player.TakeDamage(impactDamage, centre);
                }
            }

            if (impact.ringRadius > 0f || impact.debrisCount > 0)
            {
                HammerImpact.Create(head, impact, null, impactColour, 0f, Color.white);
            }

            FloorCracks.Create(centre, radius, cracks);
            TopDownCamera.Shake(shakeStrength, shakeDuration);

            // Burst up out of the impact, coming down one after another.
            Vector3 from = head + Vector3.up * 0.5f;

            for (int i = 0; i < blockTotal; i++)
            {
                Vector3 spot = BlockSpot(enemy, centre);
                float seconds = Mathf.Max(0.05f, firstLanding + i * landingInterval + UnityEngine.Random.Range(-landingJitter, landingJitter));
                ThrowBlock(from, spot, seconds, player);
            }
        }

        /// <summary>
        /// One block, tumbling, from <paramref name="from"/> onto
        /// <paramref name="floor"/>, a sword in a stone growing where it lands.
        /// </summary>
        private void ThrowBlock(Vector3 from, Vector3 floor, float seconds, PlayerHealth player)
        {
            MaceDebris block = MaceDebris.Throw(from, floor, seconds, blocks, player);

            if (swordPrefab == null)
            {
                return;
            }

            GameObject prefab = swordPrefab;
            float grow = growTime;
            float sink = swordSink;
            block.Landed += spot => StoneSword.Spawn(prefab, spot, UnityEngine.Random.Range(0f, 360f), grow, sink);
        }

        /// <summary>A spot on the floor round the impact, inside the arena if there is one.</summary>
        private Vector3 BlockSpot(Enemy enemy, Vector3 centre)
        {
            float near = Mathf.Min(blockSpread.x, blockSpread.y);
            float far = Mathf.Max(blockSpread.x, blockSpread.y);
            Vector3 spot = centre;

            for (int i = 0; i < SpotTries; i++)
            {
                float angle = UnityEngine.Random.Range(0f, 360f);
                Vector3 tried = centre + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * UnityEngine.Random.Range(near, far);

                if (enemy.Arena == null || enemy.Arena.Contains(tried))
                {
                    spot = tried;
                    break;
                }
            }

            return FloorUnder(spot, centre.y);
        }

        private void End(Enemy enemy, BossBlades blades, int index, BladeGlisten shine, Rigidbody body, bool wasKinematic)
        {
            shine?.End();

            if (blades != null)
            {
                blades.Return(index);
            }

            if (body != null)
            {
                body.isKinematic = wasKinematic;
            }

            if (enemy != null)
            {
                enemy.PhysicsDriven = false;
                enemy.FacingOverride = null;
            }
        }

        /// <summary>
        /// The mace about the boss at <paramref name="boss"/>: swung
        /// <paramref name="angle"/> degrees from head straight up toward
        /// <paramref name="forward"/>, about a grip overhead at
        /// <paramref name="swing"/> 0 and down in front at 1.
        /// </summary>
        private void Pose(Vector3 boss, Vector3 forward, float angle, float swing, Vector3 shake, Quaternion modelOffset,
                          out Vector3 position, out Quaternion rotation)
        {
            Vector3 grip = Vector3.Lerp(boss + Vector3.up * gripHeight,
                                        boss + Vector3.up * slamGripHeight + forward * slamReach,
                                        swing) + shake;

            float radians = angle * Mathf.Deg2Rad;
            Vector3 head = Vector3.up * Mathf.Cos(radians) + forward * Mathf.Sin(radians);
            position = grip + head * headDistance - head * pivotToHead;

            // As built the head is down -Y; it swings in the upright plane along the leap.
            rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, forward), -head) * modelOffset;
        }

        /// <summary>Turns the boss, and the way it faces, toward <paramref name="point"/>.</summary>
        private Vector3 TurnToward(Enemy enemy, Vector3 forward, Vector3 point)
        {
            Vector3 toPoint = Flat(point - enemy.transform.position);

            if (toPoint.sqrMagnitude > 0.0001f)
            {
                forward = Flat(Vector3.RotateTowards(forward, toPoint.normalized, turnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f)).normalized;
            }

            enemy.FacingOverride = forward;
            return forward;
        }

        /// <summary>The floor under <paramref name="spot"/>, seen through the boss and the player; at <paramref name="height"/> without one.</summary>
        private Vector3 FloorUnder(Vector3 spot, float height)
        {
            const float Above = 4f;
            const float Below = 20f;

            Vector3 from = new Vector3(spot.x, height + Above, spot.z);
            int hits = Physics.RaycastNonAlloc(from, Vector3.down, FloorHits, Above + Below, floorMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 floor = new Vector3(spot.x, height, spot.z);

            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = FloorHits[i];

                if (hit.distance < best
                    && hit.normal.y > 0.7f
                    && hit.collider.GetComponentInParent<PlayerMotor>() == null
                    && hit.collider.GetComponentInParent<Enemy>() == null)
                {
                    best = hit.distance;
                    floor = hit.point;
                }
            }

            return floor;
        }

        private static Vector3 Facing(Enemy enemy)
        {
            Vector3 forward = Flat(enemy.Model.forward);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
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

        private void Reset()
        {
            delayAfter = 1f;
            Movement.type = MovementType.StandStill;
        }
    }
}
