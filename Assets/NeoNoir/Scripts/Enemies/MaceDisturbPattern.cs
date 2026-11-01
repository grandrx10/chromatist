using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Mace Smash brought down on the floor instead of a wall: the mace flies
    /// out ahead of the player, hangs there cocked back, and slams the floor
    /// as it does in the hall intro. Swords burst up out of the impact: some
    /// arc down onto marked spots round the player, stabbing in and
    /// exploding, and a row hovers high overhead, then flies at the player
    /// one by one.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The spot is picked <see cref="aheadDistance"/> ahead of the
    ///      player, give or take <see cref="sideScatter"/> to either side.
    ///      Ahead is the way a running player runs, or else from them toward
    ///      the boss.
    ///   2. The mace is borrowed from the boss's <see cref="BossBlades"/> --
    ///      the one spawned from <see cref="bladePrefab"/> -- and starts to
    ///      glisten (<see cref="BladeGlisten"/>). Over <see cref="riseTime"/>
    ///      it flies to over the spot, raised <see cref="raiseHeight"/>, head
    ///      cocked back <see cref="cockAngle"/>.x past upright, away from the
    ///      player.
    ///   3. It hangs there for <see cref="pauseTime"/>, drawing back to
    ///      <see cref="cockAngle"/>.y and trembling harder.
    ///   4. Over <see cref="swingTime"/> it swings over and down toward the
    ///      player, speeding up, its head meeting the floor on the spot. The
    ///      impact is the hall intro's (<see cref="SpearHallIntro"/>) slam: a
    ///      burst and a ball of light <see cref="blastRadius"/> across in the
    ///      mace's colour, the impact frames and a hard jolt of the camera.
    ///      <see cref="fallCount"/> swords burst up out of it and arc down
    ///      onto spots round the player, as Mace Smash's chunks do: the first
    ///      <see cref="aimedAtPlayer"/> at the player, the rest at random
    ///      spots round them. Each spot is marked by a warning circle of its
    ///      blast's reach, <see cref="fallBlastRadius"/> -- smaller than the
    ///      row's below -- in that sword's colour, filling as it falls; the
    ///      sword's point follows its arc, rolling slowly, and it stabs in
    ///      and explodes the moment it lands (<see cref="FlyingSword.Lob"/>).
    ///   5. At the same time <see cref="swordCount"/> more swords
    ///      (<see cref="FlyingSword"/>) burst up out of the impact and, over
    ///      <see cref="swordRiseTime"/>, spread out across the way into a row
    ///      <see cref="swordHeight"/> above it, turning to point at the
    ///      player and lighting up from black. They hover,
    ///      tracking the player, for <see cref="swordHoverTime"/>, then fly at
    ///      them one at a time, in a random order, <see cref="swordInterval"/>
    ///      apart, and explode the instant they land, the Swords' wider Blast
    ///      Radius across.
    ///   6. The mace stays in the floor for <see cref="holdTime"/>, then goes
    ///      back into the orbit. The attack ends once the last sword is away.
    ///
    /// Every sword lights up in a bright colour of its own, all of one
    /// slam's spread round the hue wheel as Stone Swords' are.
    ///
    /// Everything -- the mace, the spot, the falling swords and their marks,
    /// and the hovering swords --
    /// rides along with a run (<see cref="ChaseFrame"/>), so during one it all
    /// happens relative to the running player, as in
    /// <see cref="ChromatistPhaseThree"/>'s wall run. Launched swords keep the
    /// run's speed on top of their own.
    ///
    /// The mace is posed the way <see cref="BossBlades"/> poses it: head down
    /// local -Y, as MaceHead.obj's is, with the entry's Rotation Offset for
    /// one built otherwise. It swings about a grip <see cref="headDistance"/>
    /// back from its head along that line.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Mace Disturb", fileName = "MaceDisturb")]
    public sealed class MaceDisturbPattern : AttackPattern
    {
        private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

        private const int SpotTries = 6;

        [SerializeField]
        [Tooltip("The mace's prefab, as listed in the boss's Boss Blades. The blade spawned from it is the one swung.")]
        private GameObject bladePrefab;

        [SerializeField]
        [Tooltip("The mace's glint as it hangs. Its Colour is also the impact's -- match the hall intro's Mace Glisten.")]
        private BladeGlisten.Settings glisten = new BladeGlisten.Settings { colour = new Color(0.5943396f, 0f, 0.023271088f) };

        [Header("Spot")]
        [SerializeField]
        [Tooltip("How far ahead of the player the mace comes down.")]
        [Min(0f)]
        private float aheadDistance = 7f;

        [SerializeField]
        [Tooltip("Up to this far to the player's left or right the spot can be, at random.")]
        [Min(0f)]
        private float sideScatter = 1.5f;

        [SerializeField]
        [Tooltip("Layers the mace comes down on, and the chunks land on.")]
        private LayerMask floorMask = 1;

        [Header("Swing")]
        [SerializeField]
        [Tooltip("Seconds the mace takes to fly from the orbit to over the spot.")]
        [Min(0.01f)]
        private float riseTime = 0.45f;

        [SerializeField]
        [Tooltip("Seconds it hangs cocked back over the spot before it swings.")]
        [Min(0f)]
        private float pauseTime = 0.8f;

        [SerializeField]
        [Tooltip("Seconds from cocked back to in the floor.")]
        [Min(0.01f)]
        private float swingTime = 0.14f;

        [SerializeField]
        [Tooltip("Seconds it stays in the floor before going back into the orbit.")]
        [Min(0f)]
        private float holdTime = 0.5f;

        [SerializeField]
        [Tooltip("How much higher the grip hangs while cocked back than when the head is in the floor.")]
        [Min(0f)]
        private float raiseHeight = 2.5f;

        [SerializeField]
        [Tooltip("Distance from the mace's grip, the point it swings about, to the middle of its head, at its spawned scale.")]
        [Min(0.1f)]
        private float headDistance = 1.5f;

        [SerializeField]
        [Tooltip("Distance from the prefab's pivot to the middle of the head, toward the head. 0 when the pivot is the head's middle, as MaceHead.obj's is.")]
        private float pivotToHead;

        [SerializeField]
        [Tooltip("Height of the middle of the head above the floor once it is in it: about the head's radius.")]
        private float headLift = 0.4f;

        [SerializeField]
        [Tooltip("Degrees past upright, away from the player, the head is cocked back as the pause starts, and as it ends.")]
        private Vector2 cockAngle = new Vector2(25f, 60f);

        [SerializeField]
        [Tooltip("Degrees from straight up, toward the player, the head ends at. Past 90 points it into the floor.")]
        private float slamAngle = 150f;

        [SerializeField]
        [Tooltip("Distance the mace trembles by at the very end of the pause.")]
        [Min(0f)]
        private float tremble = 0.1f;

        [Header("Impact")]
        [SerializeField]
        [Tooltip("The burst where the mace hits, in the mace's colour, as the hall intro's slam. Its debris is only looks; the swords below are what hurt.")]
        private HammerImpact.Settings impact = new HammerImpact.Settings
        {
            ringRadius = 14f,
            ringWidth = 2f,
            ringTime = 0.6f,
            ringIntensity = 4f,
            debrisCount = 40,
        };

        [SerializeField]
        [Tooltip("How bright the burst is in the mace's colour -- the Glisten's -- as a multiple of it. Above 1 blooms.")]
        [Min(0f)]
        private float lightIntensity = 3f;

        [SerializeField]
        [Tooltip("Radius of the ball of light bursting out where the mace hits. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 7f;

        [SerializeField]
        [Tooltip("The ball of light. Its colour is replaced by the mace's.")]
        private BlastSphere.Settings blast = new BlastSphere.Settings { growTime = 0.15f, fadeTime = 0.5f };

        [SerializeField]
        [Tooltip("Flashes the screen with the impact frames as the mace hits, if the scene has them.")]
        private bool impactFrames = true;

        [SerializeField]
        [Tooltip("How far the impact jolts the camera. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 1.5f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.6f;

        [Header("Falling swords")]
        [SerializeField]
        [Tooltip("Swords thrown up out of the impact to come down on spots round the player. Their look and blast are the Swords' below.")]
        [Min(0)]
        private int fallCount = 10;

        [SerializeField]
        [Tooltip("How many of them come down on the player, give or take Aim Scatter.")]
        [Min(0)]
        private int aimedAtPlayer = 2;

        [SerializeField]
        [Tooltip("How far off the player the aimed ones can come down.")]
        [Min(0f)]
        private float aimScatter = 0.6f;

        [SerializeField]
        [Tooltip("Nearest and furthest ahead of the player the rest come down; negative is behind them.")]
        private Vector2 fallAlong = new Vector2(-4f, 6f);

        [SerializeField]
        [Tooltip("Furthest to the player's left or right the rest come down.")]
        [Min(0f)]
        private float fallAcross = 5f;

        [SerializeField]
        [Tooltip("Shortest and longest seconds each is in the air, picked at random for each.")]
        private Vector2 fallTime = new Vector2(0.9f, 1.4f);

        [SerializeField]
        [Tooltip("How far each arc rises above the straight line from the impact to its spot, at its peak.")]
        [Min(0f)]
        private float fallArc = 6f;

        [SerializeField]
        [Tooltip("Reach of each falling sword's explosion, and its warning circle: smaller than the row's, whose is the Swords' Blast Radius.")]
        [Min(0.1f)]
        private float fallBlastRadius = 1.5f;

        [SerializeField]
        [Tooltip("Damage of each falling sword's explosion to the player within its reach. The row's is the Swords' Blast Damage.")]
        [Min(0f)]
        private float fallBlastDamage = 10f;

        [Header("Swords")]
        [SerializeField] private FlyingSword.Settings swords = new FlyingSword.Settings();

        [SerializeField]
        [Tooltip("Saturation of the swords' colours, as for Stone Swords.")]
        [Range(0f, 1f)]
        private float saturation = 1f;

        [SerializeField]
        [Tooltip("Brightness of the swords' colours. Above 1 blooms.")]
        [Min(0f)]
        private float colourIntensity = 2.5f;

        [SerializeField]
        [Tooltip("Swords spat up out of the impact.")]
        [Min(0)]
        private int swordCount = 5;

        [SerializeField]
        [Tooltip("Height above the floor the swords hover at.")]
        private float swordHeight = 6f;

        [SerializeField]
        [Tooltip("Up to this much higher or lower each sword hovers, so the row is not ruler straight.")]
        [Min(0f)]
        private float swordHeightJitter = 0.6f;

        [SerializeField]
        [Tooltip("Width of the row the swords spread out into, across the way.")]
        [Min(0f)]
        private float swordSpread = 8f;

        [SerializeField]
        [Tooltip("How far the swords burst up past their spot before settling into it.")]
        [Min(0f)]
        private float swordOvershoot = 1.5f;

        [SerializeField]
        [Tooltip("Seconds from the impact for the swords to rise into their row.")]
        [Min(0.01f)]
        private float swordRiseTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds they hover, pointing at the player, before the first flies.")]
        [Min(0f)]
        private float swordHoverTime = 0.7f;

        [SerializeField]
        [Tooltip("Seconds between one sword flying and the next.")]
        [Min(0f)]
        private float swordInterval = 0.25f;

        [SerializeField]
        [Tooltip("Distance the hovering swords bob up and down by.")]
        [Min(0f)]
        private float swordBob = 0.15f;

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

            // Made now, so the first sword does not build its art mid-fight.
            ArtTemplate.Of(swords.art);

            Transform target = enemy.HasTarget ? enemy.Target : null;
            Vector3 playerAt = target != null ? target.position : enemy.transform.position + Facing(enemy) * aheadDistance;
            Vector3 forward = Ahead(enemy, playerAt);
            Vector3 across = Vector3.Cross(Vector3.up, forward);

            // The spot, as it is now; everything is placed from it plus how
            // far a run has carried things on since.
            Vector3 spot = FloorUnder(playerAt + forward * aheadDistance + across * Random.Range(-sideScatter, sideScatter), playerAt.y);
            Vector3 drift = Vector3.zero;

            // The head swings over and down toward the player.
            Vector3 swingDirection = -forward;

            Transform mace = blades.Borrow(index, this);
            Quaternion modelOffset = blades.RotationOffset(index);
            BladeGlisten shine = BladeGlisten.Begin(mace, glisten, riseTime + pauseTime);

            Vector3 fromPosition = mace.position;
            Quaternion fromRotation = mace.rotation;
            float clock = 0f;

            // 1. Out of the orbit to over the spot, cocked back.
            while (clock < riseTime)
            {
                drift += ChaseFrame.Step(Time.deltaTime);
                fromPosition += ChaseFrame.Step(Time.deltaTime);

                Pose(spot + drift, swingDirection, -cockAngle.x, 0f, Vector3.zero, modelOffset, out Vector3 position, out Quaternion rotation);

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

            while (elapsed < pauseTime)
            {
                drift += ChaseFrame.Step(Time.deltaTime);

                float charge = pauseTime > 0f ? elapsed / pauseTime : 1f;
                Vector3 shake = Random.insideUnitSphere * (tremble * charge * charge);

                Pose(spot + drift, swingDirection, -Mathf.Lerp(cockAngle.x, cockAngle.y, charge), 0f, shake, modelOffset,
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

            // 3. Over and down into the floor, speeding up all the way.
            shine.Tick(riseTime + pauseTime);
            elapsed = 0f;

            while (elapsed < swingTime)
            {
                drift += ChaseFrame.Step(Time.deltaTime);

                float swing = elapsed / swingTime;
                swing *= swing;

                Pose(spot + drift, swingDirection, Mathf.Lerp(-cockAngle.y, slamAngle, swing), swing, Vector3.zero, modelOffset,
                     out Vector3 position, out Quaternion rotation);
                mace.SetPositionAndRotation(position, rotation);

                yield return null;
                elapsed += Time.deltaTime;
            }

            drift += ChaseFrame.Step(Time.deltaTime);
            Vector3 struck = spot + drift;
            Pose(struck, swingDirection, slamAngle, 1f, Vector3.zero, modelOffset, out Vector3 slamPosition, out Quaternion slamRotation);
            mace.SetPositionAndRotation(slamPosition, slamRotation);

            Smash(struck);

            // 4. The swords burst up, while the mace is left in the floor:
            // the falling ones and the row, their colours spread round the
            // hue wheel together, so no two in one slam look alike.
            float hue = Random.value;
            int total = fallCount + swordCount;
            Vector3 playerNow = target != null ? target.position : struck - forward * aheadDistance;

            ThrowSwords(enemy, struck, playerNow, forward, across, hue, total);
            List<Sword> row = SpitSwords(struck, across, hue, total);
            float launchAt = swordRiseTime + swordHoverTime;
            int launched = 0;
            bool maceBack = false;
            elapsed = 0f;

            while (!maceBack || launched < row.Count)
            {
                yield return null;
                elapsed += Time.deltaTime;
                drift += ChaseFrame.Step(Time.deltaTime);

                if (enemy.Health.IsDead)
                {
                    foreach (Sword sword in row)
                    {
                        if (sword.blade != null)
                        {
                            sword.blade.Dismiss();
                        }
                    }

                    break;
                }

                if (!maceBack)
                {
                    if (elapsed < holdTime)
                    {
                        Pose(spot + drift, swingDirection, slamAngle, 1f, Vector3.zero, modelOffset, out Vector3 position, out Quaternion rotation);
                        mace.SetPositionAndRotation(position, rotation);
                    }
                    else
                    {
                        End(blades, index, shine);
                        maceBack = true;
                    }
                }

                Vector3 aim = enemy.HasTarget ? enemy.TargetPoint : spot + drift - forward * aheadDistance;

                while (launched < row.Count && elapsed >= launchAt + launched * swordInterval)
                {
                    Sword next = row[launched++];

                    if (next.blade != null)
                    {
                        next.blade.Launch(aim - next.blade.transform.position, ChaseFrame.Velocity);
                    }
                }

                PoseSwords(row, launched, spot + drift, aim, elapsed);
            }

            if (!maceBack)
            {
                End(blades, index, shine);
            }
        }

        /// <summary>A sword spat out by the slam: where it rose from, and where in the row it hovers, both from the spot.</summary>
        private struct Sword
        {
            public FlyingSword blade;
            public Vector3 from;
            public Vector3 hover;
        }

        private void Smash(Vector3 struck)
        {
            // The hall intro's slam: a burst and a ball of light in the
            // mace's colour, the impact frames, and a hard jolt.
            Color burst = glisten.colour * lightIntensity;
            burst.a = 1f;

            if (impact.ringRadius > 0f || impact.debrisCount > 0)
            {
                HammerImpact.Create(struck, impact, null, burst, 0f, Color.white);
            }

            if (blastRadius > 0f)
            {
                BlastSphere.Create(struck + Vector3.up * 0.5f, blastRadius, new BlastSphere.Settings
                {
                    growTime = blast.growTime,
                    fadeTime = blast.fadeTime,
                    colour = new Color(burst.r * 1.5f, burst.g * 1.5f, burst.b * 1.5f, 1f),
                    outlineWidth = blast.outlineWidth,
                    outlineColour = blast.outlineColour,
                    material = blast.material,
                });
            }

            if (impactFrames)
            {
                ImpactFrames frames = FindAnyObjectByType<ImpactFrames>();

                if (frames != null)
                {
                    frames.TriggerImpact(struck);
                }
            }

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }
        }

        /// <summary>
        /// The falling swords, out of the impact onto spots round the player:
        /// the first few on them, the rest scattered.
        /// </summary>
        private void ThrowSwords(Enemy enemy, Vector3 struck, Vector3 playerAt, Vector3 forward, Vector3 across,
                                 float hue, int total)
        {
            // From just above the head, so they do not start in the floor.
            Vector3 start = struck + Vector3.up * (headLift + 0.5f);

            for (int i = 0; i < fallCount; i++)
            {
                Vector3 spot;

                if (i < aimedAtPlayer)
                {
                    Vector2 scatter = Random.insideUnitCircle * aimScatter;
                    spot = playerAt + across * scatter.x + forward * scatter.y;
                }
                else
                {
                    spot = FallSpot(enemy, playerAt, forward, across);
                }

                float seconds = Random.Range(Mathf.Min(fallTime.x, fallTime.y), Mathf.Max(fallTime.x, fallTime.y));

                FlyingSword sword = FlyingSword.Create(start, Quaternion.LookRotation(Vector3.up, across), swords,
                                                       SwordColour(hue, i, total));
                sword.Lob(FloorUnder(spot, playerAt.y), seconds, fallArc, fallBlastRadius, fallBlastDamage);
            }
        }

        /// <summary>A spot round the player, inside the arena if there is one.</summary>
        private Vector3 FallSpot(Enemy enemy, Vector3 playerAt, Vector3 forward, Vector3 across)
        {
            float near = Mathf.Min(fallAlong.x, fallAlong.y);
            float far = Mathf.Max(fallAlong.x, fallAlong.y);
            Vector3 spot = playerAt;

            for (int i = 0; i < SpotTries; i++)
            {
                Vector3 tried = playerAt + forward * Random.Range(near, far) + across * Random.Range(-fallAcross, fallAcross);

                if (enemy.Arena == null || enemy.Arena.Contains(tried))
                {
                    spot = tried;
                    break;
                }
            }

            return spot;
        }

        /// <summary>The row's swords, made at the impact pointing up, each given its place in the row.</summary>
        private List<Sword> SpitSwords(Vector3 struck, Vector3 across, float hue, int total)
        {
            List<Sword> row = new List<Sword>();
            Vector3 from = Vector3.up * (headLift + 0.5f);

            for (int i = 0; i < swordCount; i++)
            {
                float t = swordCount > 1 ? (float)i / (swordCount - 1) : 0.5f;
                Vector3 hover = across * Mathf.Lerp(-swordSpread * 0.5f, swordSpread * 0.5f, t)
                              + Vector3.up * (swordHeight + Random.Range(-swordHeightJitter, swordHeightJitter));

                FlyingSword blade = FlyingSword.Create(struck + from, Quaternion.LookRotation(Vector3.up, across), swords,
                                                       SwordColour(hue, fallCount + i, total));
                row.Add(new Sword { blade = blade, from = from, hover = hover });
            }

            // Flown in a random order.
            for (int i = row.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (row[i], row[j]) = (row[j], row[i]);
            }

            return row;
        }

        /// <summary>
        /// Every sword not yet flown: bursting up past its place and settling
        /// into it, turning from pointing up to pointing at the player, then
        /// hovering there.
        /// </summary>
        private void PoseSwords(List<Sword> row, int launched, Vector3 spot, Vector3 aim, float clock)
        {
            float t = Mathf.Clamp01(clock / swordRiseTime);

            // Out past its place and back, like a thing thrown up that stops.
            float overshoot = swordOvershoot * Mathf.Sin(t * Mathf.PI) * (1f - t);
            float eased = 1f - (1f - t) * (1f - t);

            for (int i = launched; i < row.Count; i++)
            {
                Sword sword = row[i];

                if (sword.blade == null)
                {
                    continue;
                }

                float bob = swordBob * Mathf.Sin(clock * 3f + i * 1.7f) * t;
                Vector3 position = spot + Vector3.Lerp(sword.from, sword.hover, eased) + Vector3.up * (overshoot + bob);

                Vector3 toPlayer = aim - position;
                Quaternion pointing = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer, Vector3.up) : Quaternion.identity;
                Quaternion rotation = Quaternion.Slerp(Quaternion.LookRotation(Vector3.up, toPlayer), pointing, Smooth(t));

                sword.blade.Hold(position, rotation);
            }
        }

        /// <summary>
        /// The colour of sword <paramref name="index"/> of <paramref name="total"/>:
        /// spread evenly round the hue wheel from <paramref name="hue"/>, give
        /// or take a little, as Stone Swords' are.
        /// </summary>
        private Color SwordColour(float hue, int index, int total)
        {
            float jitter = Random.Range(-0.3f, 0.3f) / total;
            return Color.HSVToRGB(Mathf.Repeat(hue + (float)index / total + jitter, 1f), saturation, 1f) * colourIntensity;
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
        /// The mace over <paramref name="spot"/>: swung <paramref name="angle"/>
        /// degrees from head straight up toward <paramref name="swingDirection"/>,
        /// about a grip that is <see cref="raiseHeight"/> up at
        /// <paramref name="swing"/> 0 and down at 1, placed so that at
        /// <see cref="slamAngle"/> and 1 the head is in the floor on the spot.
        /// </summary>
        private void Pose(Vector3 spot, Vector3 swingDirection, float angle, float swing, Vector3 shake, Quaternion modelOffset,
                          out Vector3 position, out Quaternion rotation)
        {
            Vector3 slamGrip = spot + Vector3.up * headLift - Head(swingDirection, slamAngle) * headDistance;
            Vector3 grip = slamGrip + Vector3.up * (raiseHeight * (1f - swing)) + shake;

            Vector3 head = Head(swingDirection, angle);
            position = grip + head * headDistance - head * pivotToHead;

            // As built the head is down -Y; it swings in the upright plane toward the player.
            rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, swingDirection), -head) * modelOffset;
        }

        private static Vector3 Head(Vector3 swingDirection, float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return Vector3.up * Mathf.Cos(radians) + swingDirection * Mathf.Sin(radians);
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

        /// <summary>Ahead of the player: the way a running player runs, or else from them toward the boss.</summary>
        private static Vector3 Ahead(Enemy enemy, Vector3 playerAt)
        {
            if (ChaseFrame.Active)
            {
                return ChaseFrame.Forward;
            }

            Vector3 toBoss = Flat(enemy.transform.position - playerAt);
            return toBoss.sqrMagnitude > 0.0001f ? toBoss.normalized : -Facing(enemy);
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
            swords.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
