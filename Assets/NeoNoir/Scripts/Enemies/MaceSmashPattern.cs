using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss's mace smashes one wall of the hall: it leaves the orbit
    /// glistening, cocks back beside that wall, and slams into it, and chunks
    /// of wall fly out over the hall and crash down on marked spots round the
    /// player.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. A wall is picked, left or right of the boss at random -- across
    ///      the run during one, otherwise across the line to the player --
    ///      found by casting out <see cref="wallSearch"/> at
    ///      <see cref="impactHeight"/> on <see cref="wallMask"/>. With no wall
    ///      that side it tries the other; with none either side it smashes
    ///      the air <see cref="fallbackWallDistance"/> out.
    ///   2. The mace is borrowed from the boss's <see cref="BossBlades"/> --
    ///      the one spawned from <see cref="bladePrefab"/> -- and starts to
    ///      glisten (<see cref="BladeGlisten"/>), building to the strike.
    ///      Over <see cref="riseTime"/> it flies to beside the wall, cocked
    ///      back, head up, <see cref="cockAngle"/> past upright away from it.
    ///   3. It hangs there for <see cref="windupTime"/>, leaning further back
    ///      and trembling harder.
    ///   4. Over <see cref="swingTime"/> it swings into the wall, speeding up,
    ///      until its head meets it. A burst spreads across the wall, the
    ///      camera jolts, and <see cref="debrisCount"/> chunks
    ///      (<see cref="MaceDebris"/>) fly out of it: the first
    ///      <see cref="aimedAtPlayer"/> at the player, the rest at random
    ///      spots across the hall, from <see cref="debrisAlong"/>.x to .y
    ///      down the run from the player. Each spot is marked as the chunk
    ///      flies, and the player still on it when it lands is hurt. Nothing
    ///      can cut them.
    ///   5. It stays in the wall for <see cref="holdTime"/>, then stops
    ///      glowing and goes back into the orbit.
    /// The mace rides along with the boss throughout, so it works with the
    /// boss on the move, as in the hall run of <see cref="SpearHallIntro"/>.
    ///
    /// The mace is posed the way <see cref="BossBlades"/> poses it: head down
    /// local -Y, as MaceHead.obj's is, with the entry's Rotation Offset for
    /// one built otherwise. It swings about a grip <see cref="headDistance"/>
    /// back from its head along that line.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Mace Smash", fileName = "MaceSmash")]
    public sealed class MaceSmashPattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("The mace's prefab, as listed in the boss's Boss Blades. The blade spawned from it is the one swung.")]
        private GameObject bladePrefab;

        [SerializeField] private BladeGlisten.Settings glisten = new BladeGlisten.Settings();

        [Header("Wall")]
        [SerializeField]
        [Tooltip("Layers that count as wall. Leave the Enemy and Player layers out.")]
        private LayerMask wallMask = 1;

        [SerializeField]
        [Tooltip("How far out to each side the boss looks for a wall.")]
        [Min(0.5f)]
        private float wallSearch = 15f;

        [SerializeField]
        [Tooltip("Height above the boss's pivot the mace strikes the wall at.")]
        private float impactHeight = 2f;

        [SerializeField]
        [Tooltip("Distance out to the side the mace strikes when there is no wall either side.")]
        [Min(0.5f)]
        private float fallbackWallDistance = 6f;

        [Header("Swing")]
        [SerializeField]
        [Tooltip("Seconds the mace takes to fly from the orbit to beside the wall.")]
        [Min(0.01f)]
        private float riseTime = 0.4f;

        [SerializeField]
        [Tooltip("Seconds it hangs cocked back beside the wall before it swings.")]
        [Min(0f)]
        private float windupTime = 1f;

        [SerializeField]
        [Tooltip("Seconds from cocked back to in the wall.")]
        [Min(0.01f)]
        private float swingTime = 0.12f;

        [SerializeField]
        [Tooltip("Seconds it stays in the wall before going back into the orbit.")]
        [Min(0f)]
        private float holdTime = 0.4f;

        [SerializeField]
        [Tooltip("Distance from the mace's grip, the point it swings about, to the middle of its head, at its spawned scale.")]
        [Min(0.1f)]
        private float headDistance = 1.5f;

        [SerializeField]
        [Tooltip("Distance from the prefab's pivot to the middle of the head, toward the head. 0 when the pivot is the head's middle, as MaceHead.obj's is.")]
        private float pivotToHead;

        [SerializeField]
        [Tooltip("Degrees past upright, away from the wall, the head is cocked back as the windup starts, and as it ends.")]
        private Vector2 cockAngle = new Vector2(20f, 45f);

        [SerializeField]
        [Tooltip("Distance the mace trembles by at the very end of the windup.")]
        [Min(0f)]
        private float tremble = 0.1f;

        [Header("Wall impact")]
        [SerializeField]
        [Tooltip("The burst across the wall where the mace strikes. Its debris is ignored: the wall's own chunks are the Debris below.")]
        private HammerImpact.Settings wallBurst = new HammerImpact.Settings
        {
            ringRadius = 3.5f,
            ringWidth = 0.7f,
            ringTime = 0.35f,
            ringIntensity = 3f,
            debrisCount = 0,
        };

        [SerializeField]
        [Tooltip("Colour of the burst across the wall. Above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color wallBurstColour = new Color(1.6f, 1.6f, 1.6f);

        [SerializeField]
        [Tooltip("How far the strike jolts the camera. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.6f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.35f;

        [Header("Debris")]
        [SerializeField] private MaceDebris.Settings debris = new MaceDebris.Settings();

        [SerializeField]
        [Tooltip("Chunks of wall knocked out by the strike.")]
        [Min(0)]
        private int debrisCount = 10;

        [SerializeField]
        [Tooltip("How many of them land on the player, give or take Aim Scatter.")]
        [Min(0)]
        private int aimedAtPlayer = 2;

        [SerializeField]
        [Tooltip("How far off the player the aimed chunks can land.")]
        [Min(0f)]
        private float aimScatter = 0.6f;

        [SerializeField]
        [Tooltip("Nearest and furthest down the run from the player the rest land; negative is behind them. Outside a run, along the line from the boss to the player.")]
        private Vector2 debrisAlong = new Vector2(-4f, 8f);

        [SerializeField]
        [Tooltip("How far in from each wall chunks can land.")]
        [Min(0f)]
        private float wallMargin = 1f;

        [SerializeField]
        [Tooltip("Layers chunks land on.")]
        private LayerMask floorMask = 1;

        public override bool CanPerform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            return blades != null && blades.IndexOf(bladePrefab) >= 0 && !blades.IsBorrowed(blades.IndexOf(bladePrefab));
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null ? blades.IndexOf(bladePrefab) : -1;

            if (index < 0)
            {
                Debug.LogWarning($"[{name}] {enemy.name} has no Boss Blades blade made from the Blade Prefab.", this);
                yield break;
            }

            Vector3 forward = Forward(enemy);
            Vector3 across = Vector3.Cross(Vector3.up, forward);

            // How far each wall is, out to either side; 0 for none.
            float right = WallDistance(enemy, across);
            float left = WallDistance(enemy, -across);

            bool goRight = Random.value < 0.5f;

            if (goRight ? right <= 0f : left <= 0f)
            {
                goRight = !goRight;
            }

            Vector3 side = goRight ? across : -across;
            float wall = goRight ? right : left;

            if (wall <= 0f)
            {
                wall = fallbackWallDistance;
            }

            Transform mace = blades.Borrow(index, this);
            Quaternion modelOffset = blades.RotationOffset(index);
            BladeGlisten shine = BladeGlisten.Begin(mace, glisten, riseTime + windupTime);

            Vector3 fromPosition = mace.position;
            Quaternion fromRotation = mace.rotation;
            float clock = 0f;

            // 1. Out of the orbit to beside the wall, cocked back.
            while (clock < riseTime)
            {
                Pose(enemy, side, wall, -cockAngle.x, Vector3.zero, modelOffset, out Vector3 position, out Quaternion rotation);

                float eased = Smooth(clock / riseTime);
                mace.SetPositionAndRotation(Vector3.Lerp(fromPosition, position, eased),
                                            Quaternion.Slerp(fromRotation, rotation, eased));
                shine.Tick(clock);

                yield return null;
                clock += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    End(blades, index, shine);
                    yield break;
                }
            }

            // 2. Hanging there, drawing further back.
            float elapsed = 0f;

            while (elapsed < windupTime)
            {
                float charge = windupTime > 0f ? elapsed / windupTime : 1f;
                Vector3 shake = Random.insideUnitSphere * (tremble * charge * charge);

                Pose(enemy, side, wall, -Mathf.Lerp(cockAngle.x, cockAngle.y, charge), shake, modelOffset,
                     out Vector3 position, out Quaternion rotation);
                mace.SetPositionAndRotation(position, rotation);
                shine.Tick(riseTime + elapsed);

                yield return null;
                elapsed += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    End(blades, index, shine);
                    yield break;
                }
            }

            // 3. Into the wall, speeding up all the way.
            shine.Tick(riseTime + windupTime);
            elapsed = 0f;

            while (elapsed < swingTime)
            {
                float swing = elapsed / swingTime;
                swing *= swing;

                Pose(enemy, side, wall, Mathf.Lerp(-cockAngle.y, 90f, swing), Vector3.zero, modelOffset,
                     out Vector3 position, out Quaternion rotation);
                mace.SetPositionAndRotation(position, rotation);

                yield return null;
                elapsed += Time.deltaTime;
            }

            Smash(enemy, forward, across, side, wall, left, right);

            // 4. Left in the wall a moment.
            elapsed = 0f;

            do
            {
                Pose(enemy, side, wall, 90f, Vector3.zero, modelOffset, out Vector3 position, out Quaternion rotation);
                mace.SetPositionAndRotation(position, rotation);

                yield return null;
                elapsed += Time.deltaTime;
            }
            while (elapsed < holdTime && !enemy.Health.IsDead);

            End(blades, index, shine);
        }

        private void Smash(Enemy enemy, Vector3 forward, Vector3 across, Vector3 side, float wall,
                           float left, float right)
        {
            Vector3 impact = enemy.transform.position + side * wall + Vector3.up * impactHeight;

            // Laid flat against the wall, facing back into the hall.
            if (wallBurst.ringRadius > 0f)
            {
                HammerImpact burst = HammerImpact.Create(impact, wallBurst, null, wallBurstColour, 0f, Color.white);
                burst.transform.rotation = Quaternion.FromToRotation(Vector3.up, -side);
            }

            TopDownCamera.Shake(shakeStrength, shakeDuration);

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;
            Vector3 playerAt = enemy.HasTarget ? enemy.Target.position : enemy.transform.position + forward * 5f;

            // Where across the hall chunks can land, from the boss's line:
            // in from each wall, or the fallback width without one.
            float reachRight = (right > 0f ? right : fallbackWallDistance) - wallMargin;
            float reachLeft = (left > 0f ? left : fallbackWallDistance) - wallMargin;

            // Thrown from just off the wall, so they do not start inside it.
            Vector3 start = impact - side * 0.5f;

            for (int i = 0; i < debrisCount; i++)
            {
                Vector3 spot;

                if (i < aimedAtPlayer)
                {
                    Vector2 scatter = Random.insideUnitCircle * aimScatter;
                    spot = playerAt + across * scatter.x + forward * scatter.y;
                }
                else
                {
                    float along = Vector3.Dot(playerAt - enemy.transform.position, forward)
                                  + Random.Range(Mathf.Min(debrisAlong.x, debrisAlong.y), Mathf.Max(debrisAlong.x, debrisAlong.y));
                    float sideways = Random.Range(-Mathf.Max(0f, reachLeft), Mathf.Max(0f, reachRight));
                    spot = enemy.transform.position + forward * along + across * sideways;
                }

                float seconds = Random.Range(Mathf.Min(debris.flightTime.x, debris.flightTime.y),
                                             Mathf.Max(debris.flightTime.x, debris.flightTime.y));

                MaceDebris.Throw(start, FloorUnder(spot, playerAt.y), seconds, debris, player);
            }
        }

        private void End(BossBlades blades, int index, BladeGlisten shine)
        {
            shine?.End();

            if (blades != null)
            {
                blades.Return(index);
            }
        }

        /// <summary>
        /// The mace beside the wall: swung <paramref name="angle"/> degrees
        /// from head straight up toward the wall, about a grip placed so that
        /// at 90 its head meets the wall at <see cref="impactHeight"/>.
        /// </summary>
        private void Pose(Enemy enemy, Vector3 side, float wall, float angle, Vector3 shake, Quaternion modelOffset,
                          out Vector3 position, out Quaternion rotation)
        {
            Vector3 grip = enemy.transform.position + side * (wall - headDistance) + Vector3.up * impactHeight + shake;

            float radians = angle * Mathf.Deg2Rad;
            Vector3 head = Vector3.up * Mathf.Cos(radians) + side * Mathf.Sin(radians);
            Vector3 headCentre = grip + head * headDistance;

            position = headCentre - head * pivotToHead;

            // As built the head is down -Y; it swings in the upright plane across the hall.
            rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, side), -head) * modelOffset;
        }

        /// <summary>How far out along <paramref name="direction"/> the nearest wall is, at strike height; 0 for none.</summary>
        private float WallDistance(Enemy enemy, Vector3 direction)
        {
            Vector3 from = enemy.transform.position + Vector3.up * impactHeight;
            RaycastHit[] hits = Physics.RaycastAll(from, direction, wallSearch, wallMask, QueryTriggerInteraction.Ignore);
            float nearest = 0f;

            foreach (RaycastHit hit in hits)
            {
                // Not the boss, its blades, or the player.
                if (hit.collider.GetComponentInParent<Enemy>() != null || hit.collider.GetComponentInParent<PlayerMotor>() != null)
                {
                    continue;
                }

                if (nearest <= 0f || hit.distance < nearest)
                {
                    nearest = hit.distance;
                }
            }

            return nearest;
        }

        private Vector3 FloorUnder(Vector3 spot, float height)
        {
            Vector3 from = new Vector3(spot.x, height + 3f, spot.z);

            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 8f, floorMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : new Vector3(spot.x, height, spot.z);
        }

        /// <summary>Down the run during one; otherwise from the boss toward the player.</summary>
        private static Vector3 Forward(Enemy enemy)
        {
            if (ChaseFrame.Active)
            {
                return ChaseFrame.Forward;
            }

            Vector3 toPlayer = enemy.HasTarget ? enemy.TargetPoint - enemy.transform.position : enemy.Model.forward;
            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void Reset()
        {
            delayAfter = 1f;
            Movement.type = MovementType.StandStill;
        }
    }
}
