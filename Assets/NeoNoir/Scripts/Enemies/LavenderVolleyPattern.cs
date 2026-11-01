using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Lavender the Longbow's fight, all one attack: she takes her place
    /// between Luna and the middle of the arena, glistens, and a storm of
    /// arrow rain sweeps down the arena's sides, leaving a channel down the
    /// middle. From then on, until she is beaten, she cycles through volleys
    /// in that channel: a sine wave, row shots and a creep.
    /// </summary>
    /// <remarks>
    /// The arena is laid out along the line from Luna (the back) through the
    /// arena centre (the front). Every flying arrow runs down that line, back
    /// to front, launched from <see cref="spawnBehind"/> behind Lavender, at
    /// <see cref="arrowHeight"/> over the floor, in lanes
    /// <see cref="laneSpacing"/> apart across it.
    ///
    /// In order:
    ///   1. She appears <see cref="dropHeight"/> straight above the spot
    ///      <see cref="standFraction"/> of the way from Luna to the arena
    ///      centre and drops onto the floor there over <see cref="dropTime"/>
    ///      (see <see cref="BladeDrop"/>), while a <see cref="WarningCircle"/>
    ///      of <see cref="landingRadius"/> fills under her. She lands with a
    ///      heavy impact -- a <see cref="HammerImpact"/>, a
    ///      <see cref="BlastSphere"/> and a jolt of the camera -- and
    ///      <see cref="landingDamage"/> to the player inside the circle, and
    ///      stays there. Her colliders are off in the air.
    ///   2. She glistens for <see cref="glistenTime"/>.
    ///   3. The storm, at once and until she dies: everything outside the
    ///      middle <see cref="middleWidth"/> is split into columns running
    ///      back to front, each a circle wide. Sweeps of circles run down all
    ///      of them together as one wave, a rank of circles across every
    ///      column every <see cref="stormStepTime"/>, from the arena's back
    ///      edge to its front; <see cref="stormRestTime"/> after one sweep's
    ///      last rank, the next starts at the back. Each circle rains arrows
    ///      as it fills, and a player inside takes <see cref="stormDamage"/>.
    ///   4. Then, over and over:
    ///      - Sine wave, <see cref="sineTime"/>: the middle lanes fire too,
    ///        all but those inside a <see cref="channelWidth"/> gap that
    ///        swings side to side, one swing every <see cref="wavePeriod"/>.
    ///        As the arrows fly on, the gap traces a wave down the arena.
    ///      - <see cref="gapTime"/> of rest.
    ///      - Row shot, <see cref="rowTime"/>: the middle is split into rows
    ///        running back to front, each a circle wide. Every
    ///        <see cref="rowInterval"/> a random row, never the last one,
    ///        lays a line of <see cref="WarningCircle"/>s from the arena's
    ///        back edge to its front, in quick succession. Arrows rain into
    ///        each circle at an angle as it fills, and a player inside takes
    ///        <see cref="rowDamage"/>, once per circle.
    ///      - Creep: a rank of circles across the whole middle, starting at
    ///        the arena's back edge and stepping a circle further toward the
    ///        front every <see cref="creepStepTime"/>, until
    ///        <see cref="creepReach"/> of the way down, driving the player to
    ///        the front. One wave: each spot strikes once, for
    ///        <see cref="rowDamage"/>.
    ///      - <see cref="gapTime"/> of rest.
    ///
    /// Luna is the enemy's <see cref="Enemy.Summoner"/>, which
    /// <see cref="LunaCaptives"/> sets; without one, the back is where
    /// Lavender stood when the attack began. The front is the enemy's
    /// <see cref="Enemy.ArenaCentre"/>, or the player without one. How wide
    /// and long the arena is comes from the enemy's <see cref="Enemy.Arena"/>,
    /// or the fallback sizes without one.
    ///
    /// The flying arrow prefab: an <see cref="EnemyProjectile"/> (its own
    /// Damage, Speed and Lifetime are used) with the arrow art as a child,
    /// pointing along +Z. Set its Hit Mask to the Player layer alone, so
    /// walls and Lavender herself never stop it, and give it a Lifetime long
    /// enough to cross the arena from behind her. The player's parry catches
    /// them as it does any enemy bullet.
    ///
    /// The raining arrows are <see cref="HallSpear"/>s and only for show: set
    /// the Spear Prefab in Storm and in Rain to the arrow art posed upright,
    /// tip down. Their own Damage, Landing Burst and shake are ignored; each
    /// circle hurts and bursts once as it fills, and the middle's circles
    /// jolt the camera; the storm's, striking all the time, do not. Storm's
    /// arrows are the storm's; Rain's are the row shot's and the creep's.
    ///
    /// The attack never ends by itself, so give the enemy only this pattern,
    /// and leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Blade Attacks/Lavender Volley", fileName = "LavenderVolley")]
    public sealed class LavenderVolleyPattern : AttackPattern
    {
        private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

        // Made ready a few a frame before the fight, so no frame builds many.
        private const int RainWarmedPerFrame = 12;
        private const int BurstsWarmedPerFrame = 3;

        [Header("Taking her place")]
        [SerializeField]
        [Tooltip("Where she stands, as a fraction of the way from Luna to the arena centre.")]
        [Range(0f, 1f)]
        private float standFraction = 0.5f;

        [SerializeField]
        [Tooltip("How far above her spot she appears, to drop straight down.")]
        [Min(0f)]
        private float dropHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. She gathers speed all the way down.")]
        [FormerlySerializedAs("moveTime")]
        [Min(0.01f)]
        private float dropTime = 0.9f;

        [SerializeField]
        [Tooltip("Seconds she glistens before the storm starts.")]
        [Min(0.01f)]
        private float glistenTime = 1.5f;

        [SerializeField] private BladeGlisten.Settings glisten = new BladeGlisten.Settings();

        [Header("Landing")]
        [SerializeField]
        [Tooltip("Radius of the warning circle where she lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 4f;

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as she lands.")]
        [Min(0f)]
        private float landingDamage = 25f;

        [SerializeField]
        private Color landingWarningColour = new Color(1f, 0.1f, 0.15f);

        [SerializeField]
        [Tooltip("Height above the floor the landing's warning circle is drawn at, to clear a rug or carpet.")]
        [Range(0f, 1f)]
        private float landingWarningHeight = 0.12f;

        [SerializeField]
        [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want. Empty draws no burst.")]
        private Material landingMaterial;

        [SerializeField]
        private HammerImpact.Settings landing = new HammerImpact.Settings
        {
            ringRadius = 6f,
            ringWidth = 1f,
            ringTime = 0.45f,
            debrisCount = 14,
            debrisSize = 0.5f,
            debrisUpSpeed = new Vector2(6f, 10f),
            debrisOutSpeed = new Vector2(3f, 7f),
            debrisTime = 0.8f,
        };

        [SerializeField]
        [Tooltip("Radius of the explosion sphere the landing bursts into. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 3.5f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.12f,
            fadeTime = 0.35f,
            colour = new Color(1.6f, 0.6f, 3f, 1f),
            outlineWidth = 0.15f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 0.8f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.4f;

        [Header("Arena")]
        [SerializeField]
        [Tooltip("Half the arena's width across the line from Luna, used when the enemy has no Arena.")]
        [Min(0f)]
        private float fallbackHalfWidth = 15f;

        [SerializeField]
        [Tooltip("Half the arena's length along the line from Luna, used when the enemy has no Arena.")]
        [Min(0f)]
        private float fallbackHalfLength = 15f;

        [SerializeField]
        [Tooltip("Layers the floor is looked for on. Arrows fly, and circles are laid, at its height under her; without it, at her pivot.")]
        private LayerMask floorMask = 1;

        [Header("Flying arrows")]
        [SerializeField]
        [Tooltip("EnemyProjectile with the arrow art pointing along +Z. Hit Mask: Player only. Its Damage, Speed and Lifetime are used.")]
        private EnemyProjectile arrowPrefab;

        [SerializeField]
        [Tooltip("How far behind her the arrows are launched from. Far enough to be off screen.")]
        [Min(0f)]
        private float spawnBehind = 12f;

        [SerializeField]
        [Tooltip("Height over the floor the arrows fly at.")]
        private float arrowHeight = 1f;

        [SerializeField]
        [Tooltip("Distance between neighbouring lanes across the arena. Keep it under the player's width, or they can stand between two.")]
        [Min(0.1f)]
        private float laneSpacing = 0.8f;

        [SerializeField]
        [Tooltip("Seconds between arrows down one lane, give or take Fire Jitter.")]
        [Min(0.02f)]
        private float fireInterval = 0.15f;

        [SerializeField]
        [Tooltip("Each gap between arrows down a lane is up to this fraction longer or shorter, so the lanes never fire in step.")]
        [Range(0f, 0.9f)]
        private float fireJitter = 0.3f;

        [Header("Storm")]
        [SerializeField]
        [Tooltip("Width of the middle, clear of the storm, where the sine wave, row shots and creep play out.")]
        [Min(0.1f)]
        private float middleWidth = 8f;

        [SerializeField]
        [Tooltip("Seconds between one rank of the storm's circles and the next as a sweep runs down the arena.")]
        [Min(0.02f)]
        private float stormStepTime = 0.15f;

        [SerializeField]
        [Tooltip("Seconds after one sweep's last rank before the next sweep starts at the back.")]
        [Min(0f)]
        private float stormRestTime = 0.5f;

        [SerializeField]
        [Tooltip("Arrows that land in each storm circle. The row shot's and the creep's take Arrows Per Circle under Rain.")]
        [Min(1)]
        private int stormArrowsPerCircle = 1;

        [SerializeField]
        [Tooltip("Damage to a player in a storm circle as its arrows land. Once per circle.")]
        [Min(0f)]
        private float stormDamage = 20f;

        [SerializeField]
        [Tooltip("The storm's arrows. Set the Spear Prefab to the arrow art, upright, tip down. Their Damage, Landing Burst and shake are ignored; the impact colours and material are used for the circle's burst.")]
        private HallSpear.Settings storm = new HallSpear.Settings
        {
            flightTime = 0.15f,
            leanAngle = 35f,
            lingerTime = 0.6f,
            impactColour = new Color(0.7f, 0.45f, 1f, 1f),
        };

        [Header("Sine wave")]
        [SerializeField]
        [Tooltip("Seconds the sine wave lasts.")]
        [Min(0f)]
        private float sineTime = 10f;

        [SerializeField]
        [Tooltip("Width of the gap left in the middle: the path the player walks. About two player widths.")]
        [Min(0.1f)]
        private float channelWidth = 2f;

        [SerializeField]
        [Tooltip("Seconds the gap takes to swing from one side and back. With the arrows' speed, this sets the wave's length down the arena.")]
        [Min(0.1f)]
        private float wavePeriod = 3f;

        [Header("Row shot")]
        [SerializeField]
        [Tooltip("Seconds the row shot lasts.")]
        [Min(0f)]
        private float rowTime = 5f;

        [SerializeField]
        [Tooltip("Seconds between one row and the next.")]
        [Min(0.05f)]
        private float rowInterval = 1.5f;

        [SerializeField]
        [Tooltip("Radius of each circle: the warning, and the area the arrows hurt. The middle is split into rows this wide twice over.")]
        [Min(0.1f)]
        private float circleRadius = 1.5f;

        [SerializeField]
        [Tooltip("Distance between circles down a row.")]
        [Min(0.1f)]
        private float circleSpacing = 3f;

        [SerializeField]
        [Tooltip("Seconds between one circle of a row appearing and the next, back to front.")]
        [Min(0f)]
        private float circleStagger = 0.05f;

        [SerializeField]
        [Tooltip("Seconds each warning fills before its arrows land.")]
        [Min(0f)]
        private float warnTime = 0.6f;

        [SerializeField]
        [Tooltip("Damage to a player in a circle as its arrows land. Once per circle.")]
        [Min(0f)]
        private float rowDamage = 10f;

        [SerializeField]
        private Color warningColour = new Color(0.7f, 0.45f, 1f, 1f);

        [SerializeField]
        [Tooltip("Thickness of each warning circle's outline.")]
        [Min(0f)]
        private float warningOutline = 0.1f;

        [Header("Creep")]
        [SerializeField]
        [Tooltip("Seconds between one rank of the creep and the next, a circle further toward the front.")]
        [Min(0.02f)]
        private float creepStepTime = 0.5f;

        [SerializeField]
        [Tooltip("How far down the arena the creep goes, as a fraction of its length from the back.")]
        [Range(0f, 1f)]
        private float creepReach = 0.75f;

        [Header("Rain")]
        [SerializeField]
        [Tooltip("Arrows that land in each circle of the row shot and the creep.")]
        [Min(1)]
        private int arrowsPerCircle = 3;

        [SerializeField]
        [Tooltip("How far into the circle the arrows other than the middle one land, as a fraction of its radius.")]
        [Range(0f, 1f)]
        private float arrowSpread = 0.75f;

        [SerializeField]
        [Tooltip("The raining arrows. Set the Spear Prefab to the arrow art, upright, tip down. Their Damage, Landing Burst and shake are ignored; the impact colours and material are used for the circle's burst.")]
        private HallSpear.Settings rain = new HallSpear.Settings
        {
            flightTime = 0.15f,
            leanAngle = 35f,
            lingerTime = 0.6f,
            impactColour = new Color(0.7f, 0.45f, 1f, 1f),
        };

        [SerializeField]
        [Tooltip("The one ring and debris burst where each circle's arrows land. Ring Radius and Debris Count both 0 for none.")]
        private HammerImpact.Settings circleBurst = new HammerImpact.Settings
        {
            ringRadius = 1.8f,
            ringWidth = 0.35f,
            ringTime = 0.25f,
            debrisCount = 4,
            debrisSize = 0.25f,
            debrisUpSpeed = new Vector2(4f, 7f),
            debrisOutSpeed = new Vector2(2f, 4f),
            debrisTime = 0.5f,
        };

        [SerializeField]
        [Tooltip("How far the camera jolts each frame the middle's circles strike. The storm's never jolt it. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.15f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.12f;

        [Header("Rhythm")]
        [SerializeField]
        [Tooltip("Seconds of rest after the sine wave, and after the creep.")]
        [Min(0f)]
        private float gapTime = 2f;

        /// <summary>One lane of arrows down the arena.</summary>
        private sealed class Lane
        {
            public float across;
            public float nextShot;
        }

        /// <summary>One circle waiting on its arrows.</summary>
        private sealed class Strike
        {
            public Vector3 floor;
            public float layAt;
            public float damage;
            public HallSpear.Settings arrows;
            public bool laid;
            public WarningCircle warning;
            public float age;
            public bool thrown;
        }

        private enum Stage
        {
            Sine,
            RestAfterSine,
            Rows,
            Creep,
            RestAfterCreep,
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            enemy.VelocityOverride = Vector3.zero;

            Transform root = enemy.transform;
            Rigidbody body = enemy.GetComponent<Rigidbody>();

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            // The line down the arena: from Luna, the back, to its centre, the front.
            Vector3 back = enemy.Summoner != null ? enemy.Summoner.position : root.position;
            Vector3 centre = enemy.ArenaCentre ?? (enemy.HasTarget ? enemy.Target.position : root.position + root.forward);
            Vector3 forward = Flat(centre - back);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Measure(enemy, centre, right, forward, out Vector2 across, out Vector2 along);

            if (storm.spearPrefab == null)
            {
                Debug.LogWarning($"[{name}] has no Spear Prefab set in Storm, so no storm arrows fall. The circles still hurt.", this);
            }

            if (rain.spearPrefab == null)
            {
                Debug.LogWarning($"[{name}] has no Spear Prefab set in Rain, so no row shot or creep arrows fall. The circles still hurt.", this);
            }

            if (Mathf.Max(Mathf.Abs(across.x), Mathf.Abs(across.y)) <= middleWidth * 0.5f)
            {
                Debug.LogWarning($"[{name}] Middle Width ({middleWidth}) covers the whole arena width ({across.y - across.x:0.#}), so there is no room for the storm.", this);
            }

            List<float> columns = StormColumns(across);

            foreach (object frame in WarmRain(along, columns.Count))
            {
                yield return frame;
            }

            // Her pivot on the floor there, looked for from the centre's
            // height, since she is still up in the sky.
            Vector3 stand = Vector3.Lerp(back, centre, standFraction);
            stand.y = FindFloor(new Vector3(stand.x, centre.y, stand.z), out float standFloor) ? standFloor : centre.y;

            foreach (object frame in Drop(enemy, stand))
            {
                yield return frame;
            }

            BladeGlisten shine = BladeGlisten.Begin(enemy.Model, glisten, glistenTime);

            for (float clock = 0f; clock < glistenTime; clock += Time.deltaTime)
            {
                shine.Tick(clock);
                yield return null;
            }

            shine.End();

            float floor = FindFloor(stand, out float found) ? found : stand.y;
            Vector3 origin = new Vector3(centre.x, floor, centre.z);
            float launchAlong = Vector3.Dot(Flat(stand - centre), forward) - spawnBehind;
            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            List<Lane> middle = MiddleLanes();
            List<Strike> strikes = new List<Strike>();
            float middleHalf = middleWidth * 0.5f;
            float channelHalf = Mathf.Min(channelWidth * 0.5f, middleHalf);
            float swing = middleHalf - channelHalf;
            int rowCount = Mathf.Max(1, Mathf.FloorToInt(middleWidth / (2f * circleRadius)));
            int lastRow = -1;

            Stage stage = Stage.Sine;
            float stageClock = 0f;
            float nextRow = 0f;
            int ranks = CirclesPerRow(along);
            int creepRanks = CreepRanks(along);
            float stormClock = 0f;
            float nextStormRank = 0f;
            int stormRank = 0;
            float nextCreepRank = 0f;
            int creepRank = 0;

            // Until she dies, which stops this from outside.
            while (true)
            {
                float deltaTime = Time.deltaTime;
                stormClock += deltaTime;

                // A rank across every column at a time, back to front, as one wave.
                while (stormClock >= nextStormRank)
                {
                    float z = RankAlong(along, stormRank);

                    foreach (float x in columns)
                    {
                        strikes.Add(NewStrike(origin + right * x + forward * z, nextStormRank - stormClock, stormDamage, storm));
                    }

                    stormRank++;

                    if (stormRank >= ranks)
                    {
                        stormRank = 0;
                        nextStormRank += stormStepTime + stormRestTime;
                    }
                    else
                    {
                        nextStormRank += stormStepTime;
                    }
                }

                stageClock += deltaTime;

                switch (stage)
                {
                    case Stage.Sine:
                    {
                        float gapCentre = swing * Mathf.Sin(2f * Mathf.PI * stageClock / wavePeriod);

                        foreach (Lane lane in middle)
                        {
                            if (Mathf.Abs(lane.across - gapCentre) >= channelHalf)
                            {
                                Fire(lane, stageClock, origin, right, forward, launchAlong);
                            }
                            else
                            {
                                // Ready to fire the moment the gap moves off it.
                                lane.nextShot = Mathf.Min(lane.nextShot, stageClock);
                            }
                        }

                        if (stageClock >= sineTime)
                        {
                            stage = Stage.RestAfterSine;
                            stageClock = 0f;
                        }

                        break;
                    }

                    case Stage.RestAfterSine:
                        if (stageClock >= gapTime)
                        {
                            stage = Stage.Rows;
                            stageClock = 0f;
                            nextRow = 0f;
                        }

                        break;

                    case Stage.Rows:
                        while (nextRow < rowTime && stageClock >= nextRow)
                        {
                            lastRow = PickRow(rowCount, lastRow);
                            float rowAcross = -middleHalf + (lastRow + 0.5f) * middleWidth / rowCount;
                            LayRow(origin, right, forward, rowAcross, along, nextRow - stageClock, strikes);
                            nextRow += rowInterval;
                        }

                        if (stageClock >= rowTime)
                        {
                            stage = Stage.Creep;
                            stageClock = 0f;
                            nextCreepRank = 0f;
                            creepRank = 0;
                        }

                        break;

                    case Stage.Creep:
                        while (creepRank < creepRanks && stageClock >= nextCreepRank)
                        {
                            float z = RankAlong(along, creepRank);

                            for (int row = 0; row < rowCount; row++)
                            {
                                float x = -middleHalf + (row + 0.5f) * middleWidth / rowCount;
                                strikes.Add(NewStrike(origin + right * x + forward * z, nextCreepRank - stageClock, rowDamage, rain));
                            }

                            creepRank++;
                            nextCreepRank += creepStepTime;
                        }

                        if (creepRank >= creepRanks)
                        {
                            stage = Stage.RestAfterCreep;
                            stageClock = 0f;
                        }

                        break;

                    case Stage.RestAfterCreep:
                        if (stageClock >= gapTime)
                        {
                            stage = Stage.Sine;
                            stageClock = 0f;

                            foreach (Lane lane in middle)
                            {
                                lane.nextShot = Random.Range(0f, fireInterval);
                            }
                        }

                        break;
                }

                bool struck = false;

                // Circles overlap, so a player in several striking at once is
                // hit once, by the hardest.
                float hit = 0f;
                Vector3 hitFrom = default;

                for (int i = strikes.Count - 1; i >= 0; i--)
                {
                    Strike strike = strikes[i];

                    if (!Advance(enemy, strike, forward, deltaTime))
                    {
                        continue;
                    }

                    // The storm strikes all the time, so only the middle's circles jolt the camera.
                    struck |= strike.arrows == rain;
                    strikes.RemoveAt(i);

                    if (strike.damage > hit && Inside(player, strike.floor))
                    {
                        hit = strike.damage;
                        hitFrom = strike.floor;
                    }
                }

                if (hit > 0f)
                {
                    player.TakeDamage(hit, hitFrom);
                }

                if (struck && shakeStrength > 0f)
                {
                    TopDownCamera.Shake(shakeStrength, shakeDuration);
                }

                yield return null;
            }
        }

        /// <summary>
        /// How far the arena reaches from <paramref name="centre"/>:
        /// <paramref name="across"/> along <paramref name="right"/> and
        /// <paramref name="along"/> along <paramref name="forward"/>, each as
        /// (least, most).
        /// </summary>
        private void Measure(Enemy enemy, Vector3 centre, Vector3 right, Vector3 forward, out Vector2 across, out Vector2 along)
        {
            if (enemy.Arena == null)
            {
                across = new Vector2(-fallbackHalfWidth, fallbackHalfWidth);
                along = new Vector2(-fallbackHalfLength, fallbackHalfLength);
                return;
            }

            enemy.Arena.Extent(out Vector3 boxRight, out Vector3 boxForward, out Vector2 min, out Vector2 max);
            Vector3 boxCentre = enemy.Arena.Centre;

            across = new Vector2(float.MaxValue, float.MinValue);
            along = new Vector2(float.MaxValue, float.MinValue);

            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = boxCentre
                                + boxRight * ((corner & 1) == 0 ? min.x : max.x)
                                + boxForward * ((corner & 2) == 0 ? min.y : max.y);
                Vector3 offset = Flat(point - centre);

                float x = Vector3.Dot(offset, right);
                float z = Vector3.Dot(offset, forward);
                across = new Vector2(Mathf.Min(across.x, x), Mathf.Max(across.y, x));
                along = new Vector2(Mathf.Min(along.x, z), Mathf.Max(along.y, z));
            }
        }

        /// <summary>
        /// Where the storm's columns run, across the line down the arena:
        /// every circle-wide strip outside the middle, out to the arena's
        /// sides, on both.
        /// </summary>
        private List<float> StormColumns(Vector2 across)
        {
            float middleHalf = middleWidth * 0.5f;
            List<float> columns = new List<float>();

            for (float inner = middleHalf; inner < across.y; inner += 2f * circleRadius)
            {
                columns.Add(inner + circleRadius);
            }

            for (float inner = middleHalf; inner < -across.x; inner += 2f * circleRadius)
            {
                columns.Add(-(inner + circleRadius));
            }

            return columns;
        }

        /// <summary>How far along the line down the arena the <paramref name="rank"/>th circle from the back sits.</summary>
        private float RankAlong(Vector2 along, int rank)
        {
            return along.x + circleRadius + rank * circleSpacing;
        }

        /// <summary>Ranks the creep lays: from the back edge to <see cref="creepReach"/> of the way down.</summary>
        private int CreepRanks(Vector2 along)
        {
            float reach = (along.y - along.x) * creepReach - 2f * circleRadius;
            return reach < 0f ? 1 : Mathf.FloorToInt(reach / circleSpacing) + 1;
        }

        private static Strike NewStrike(Vector3 floor, float delay, float damage, HallSpear.Settings arrows)
        {
            return new Strike { floor = floor, layAt = delay, damage = damage, arrows = arrows };
        }

        /// <summary>The lanes across the middle, spread evenly over it.</summary>
        private List<Lane> MiddleLanes()
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(middleWidth / laneSpacing));
            List<Lane> lanes = new List<Lane>(count);

            for (int k = 0; k < count; k++)
            {
                lanes.Add(new Lane
                {
                    across = -middleWidth * 0.5f + (k + 0.5f) * middleWidth / count,
                    nextShot = Random.Range(0f, fireInterval),
                });
            }

            return lanes;
        }

        /// <summary>Looses every arrow <paramref name="lane"/> is due by <paramref name="clock"/>.</summary>
        private void Fire(Lane lane, float clock, Vector3 origin, Vector3 right, Vector3 forward, float launchAlong)
        {
            if (arrowPrefab == null)
            {
                return;
            }

            while (clock >= lane.nextShot)
            {
                Vector3 position = origin + right * lane.across + forward * launchAlong + Vector3.up * arrowHeight;
                EnemyProjectile arrow = EnemyProjectile.Spawn(arrowPrefab, position, Quaternion.LookRotation(forward, Vector3.up));
                arrow.Launch(forward);

                lane.nextShot += fireInterval * (1f + Random.Range(-fireJitter, fireJitter));
            }
        }

        /// <summary>A random row of <paramref name="count"/>, never <paramref name="last"/> unless it is the only one.</summary>
        private static int PickRow(int count, int last)
        {
            if (count <= 1 || last < 0)
            {
                return Random.Range(0, count);
            }

            int row = Random.Range(0, count - 1);
            return row >= last ? row + 1 : row;
        }

        /// <summary>
        /// Queues a line of circles <paramref name="across"/> from the line
        /// down the arena, from its back edge to its front, the first
        /// appearing <paramref name="delay"/> seconds from now.
        /// </summary>
        private void LayRow(Vector3 origin, Vector3 right, Vector3 forward, float across, Vector2 along, float delay,
                            List<Strike> strikes)
        {
            int count = CirclesPerRow(along);

            for (int i = 0; i < count; i++)
            {
                float z = RankAlong(along, i);

                strikes.Add(NewStrike(origin + right * across + forward * z, delay + i * circleStagger, rowDamage, rain));
            }
        }

        private int CirclesPerRow(Vector2 along)
        {
            float length = Mathf.Max(0f, along.y - along.x - 2f * circleRadius);
            return Mathf.FloorToInt(length / circleSpacing) + 1;
        }

        /// <summary>
        /// Moves a circle on a frame: appearing, filling, raining its arrows
        /// in to land as it fills, then striking. True once it has struck;
        /// the caller then hurts a player inside it. The arrows never hurt.
        /// </summary>
        private bool Advance(Enemy enemy, Strike strike, Vector3 forward, float deltaTime)
        {
            if (!strike.laid)
            {
                strike.layAt -= deltaTime;

                if (strike.layAt > 0f)
                {
                    return false;
                }

                strike.warning = WarningCircle.Create(strike.floor + Vector3.up * 0.03f, circleRadius, warningOutline, null, warningColour);
                strike.warning.DestroyWhen(() => enemy == null || enemy.Health.IsDead);
                strike.laid = true;
                return false;
            }

            strike.age += deltaTime;

            if (strike.warning != null)
            {
                strike.warning.Fill = warnTime > 0f ? strike.age / warnTime : 1f;
            }

            if (!strike.thrown && strike.age >= warnTime - strike.arrows.flightTime)
            {
                strike.thrown = true;
                Rain(strike.floor, -forward, strike.arrows, strike.arrows == storm ? stormArrowsPerCircle : arrowsPerCircle);
            }

            if (strike.age < warnTime)
            {
                return false;
            }

            if (strike.warning != null)
            {
                strike.warning.Release();
                strike.warning = null;
            }

            if (circleBurst.ringRadius > 0f || circleBurst.debrisCount > 0)
            {
                HammerImpact.Create(strike.floor, circleBurst, strike.arrows.impactMaterial,
                    strike.arrows.impactColour, strike.arrows.impactEdgeWidth, strike.arrows.impactEdgeColour);
            }

            return true;
        }

        /// <summary>Whether <paramref name="player"/> stands, alive, within a circle at <paramref name="floor"/>.</summary>
        private bool Inside(PlayerHealth player, Vector3 floor)
        {
            if (player == null || player.IsDead)
            {
                return false;
            }

            Vector3 offset = Flat(player.transform.position - floor);
            return offset.sqrMagnitude <= circleRadius * circleRadius;
        }

        /// <summary>Rains a circle's arrows in from Lavender's side: one in the middle, the rest scattered inside.</summary>
        private void Rain(Vector3 floor, Vector3 thrownFrom, HallSpear.Settings arrows, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 spot = floor;

                if (i > 0)
                {
                    Vector2 scatter = Random.insideUnitCircle * (circleRadius * arrowSpread);
                    spot += new Vector3(scatter.x, 0f, scatter.y);
                }

                // For show: no player is passed, so they hurt no one.
                // Hurting, bursting and shaking are the circle's job.
                HallSpear.Throw(spot, thrownFrom, arrows, null, quiet: true);
            }
        }

        /// <summary>
        /// Makes ready, a few a frame, the most raining arrows ever out at
        /// once: every row laid within one arrow's life, or the creep's ranks
        /// if more, and the storm's ranks over one arrow's life.
        /// </summary>
        private IEnumerable WarmRain(Vector2 along, int columns)
        {
            float life = warnTime + rain.lingerTime + rain.sinkTime;
            int rows = Mathf.Min(Mathf.FloorToInt(life / rowInterval) + 1, Mathf.FloorToInt(rowTime / rowInterval) + 1);
            int rowCircles = rows * CirclesPerRow(along);
            int rowCount = Mathf.Max(1, Mathf.FloorToInt(middleWidth / (2f * circleRadius)));
            int creepCircles = Mathf.Min(CreepRanks(along), Mathf.FloorToInt(life / creepStepTime) + 1) * rowCount;
            int rainTotal = rain.spearPrefab != null ? Mathf.Max(rowCircles, creepCircles) * arrowsPerCircle : 0;

            float stormLife = warnTime + storm.lingerTime + storm.sinkTime;
            int stormRanks = Mathf.Min(CirclesPerRow(along), Mathf.FloorToInt(stormLife / stormStepTime) + 1);
            int stormTotal = storm.spearPrefab != null ? stormRanks * columns * stormArrowsPerCircle : 0;

            // One pool between them when they share the art.
            if (storm.spearPrefab == rain.spearPrefab)
            {
                rainTotal += stormTotal;
                stormTotal = 0;
            }

            // The bursts out at once: the storm's ranks over one burst's life, and the middle's.
            bool bursting = circleBurst.ringRadius > 0f || circleBurst.debrisCount > 0;
            float burstLife = Mathf.Max(circleBurst.ringTime, circleBurst.debrisCount > 0 ? circleBurst.debrisTime : 0f);
            int burstTotal = bursting
                ? (Mathf.FloorToInt(burstLife / stormStepTime) + 1) * columns + Mathf.Max(CirclesPerRow(along), rowCount)
                : 0;

            for (int rainReady = 0, stormReady = 0, burstsReady = 0;
                 rainReady < rainTotal || stormReady < stormTotal || burstsReady < burstTotal; )
            {
                rainReady = Mathf.Min(rainTotal, rainReady + RainWarmedPerFrame);
                stormReady = Mathf.Min(stormTotal, stormReady + RainWarmedPerFrame);
                burstsReady = Mathf.Min(burstTotal, burstsReady + BurstsWarmedPerFrame);

                if (burstsReady > 0)
                {
                    HammerImpact.Prewarm(burstsReady, circleBurst.debrisCount);
                }

                if (rainTotal > 0)
                {
                    HallSpear.Prewarm(rain.spearPrefab, rainReady);
                }

                if (stormTotal > 0)
                {
                    HallSpear.Prewarm(storm.spearPrefab, stormReady);
                }

                yield return null;
            }
        }

        /// <summary>
        /// A drop straight down onto <paramref name="stand"/>, a point on the
        /// floor, with a heavy impact, where she stays.
        /// </summary>
        private IEnumerable Drop(Enemy enemy, Vector3 stand)
        {
            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            WarningCircle warning = WarningCircle.Create(stand + Vector3.up * landingWarningHeight, landingRadius,
                                                         landingOutline, null, landingWarningColour);

            foreach (object frame in BladeDrop.Fall(enemy.transform, stand, dropHeight, dropTime, warning))
            {
                yield return frame;
            }

            warning.Release();

            if (landingMaterial != null)
            {
                HammerImpact.Create(stand, landing, landingMaterial);
            }

            if (blastRadius > 0f)
            {
                BlastSphere.Create(stand, blastRadius, blast);
            }

            if (landingShake > 0f)
            {
                TopDownCamera.Shake(landingShake, landingShakeDuration);
            }

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            if (player != null && !player.IsDead && landingDamage > 0f
                && Flat(player.transform.position - stand).sqrMagnitude <= landingRadius * landingRadius)
            {
                player.TakeDamage(landingDamage, stand);
            }

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }
        }

        /// <summary>Height of the floor under <paramref name="spot"/>, if there is some below it or a little above.</summary>
        private bool FindFloor(Vector3 spot, out float floor)
        {
            const float Above = 4f;
            const float Below = 20f;

            Vector3 from = spot + Vector3.up * Above;
            int hits = Physics.RaycastNonAlloc(from, Vector3.down, FloorHits, Above + Below, floorMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            floor = default;

            // The nearest floor below, seen through her own body and the player.
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = FloorHits[i];

                if (hit.distance < best
                    && hit.normal.y > 0.7f
                    && hit.collider.GetComponentInParent<PlayerMotor>() == null
                    && hit.collider.GetComponentInParent<Enemy>() == null)
                {
                    best = hit.distance;
                    floor = hit.point.y;
                }
            }

            return best < float.MaxValue;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z);
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
        }
    }
}
