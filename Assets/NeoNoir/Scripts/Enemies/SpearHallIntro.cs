using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The spear boss's entrance down a grand hall: the player is stopped,
    /// the boss steps out from behind a pillar and talks, spears rain down
    /// behind the player, and both dash down an endless hall -- the player
    /// forward, the boss backward -- into a finale room whose floor the
    /// boss's katana cuts to pieces along a web of light. It falls away under
    /// them both, the screen closes round the player, and the next scene, the
    /// real fight, opens.
    /// </summary>
    /// <remarks>
    /// Goes on a trigger box across the hall where the player is stopped,
    /// which needs a trigger collider. The hall runs from this object toward
    /// <see cref="chainStart"/>; "behind", "ahead" and "across" are measured
    /// along that line. Plays once.
    ///
    /// In order:
    ///   1. The player walks in: their controls lock and the camera swings,
    ///      over <see cref="cameraTurnTime"/>, to look down the hall toward
    ///      <see cref="bossStandPoint"/>, where the boss will stop.
    ///   2. The boss appears at <see cref="bossStartPoint"/> (behind a
    ///      pillar) -- spawned from the prefab, or a scene boss switched on --
    ///      and walks to the stand point, then turns to face the player.
    ///   3. The dialogue plays through the <see cref="DialogueBox"/>.
    ///   4. Spears rain into the floor behind the player for <see cref="stormTime"/> seconds.
    ///   5. The run: the player dashes forward and is carried down the hall at
    ///      <see cref="runSpeedMultiplier"/> times walking speed, steering only
    ///      left and right. The dash button dashes left or right; attacks
    ///      work as usual, relative to the running player, through
    ///      <see cref="ChaseFrame"/>. Copies of <see cref="hallSegment"/> are laid on
    ///      from <see cref="chainStart"/>, <see cref="segmentLength"/> apart,
    ///      always reaching
    ///      <see cref="buildAhead"/> past the player; ones further than
    ///      <see cref="keepBehind"/> behind are removed. The boss backs away
    ///      ahead of the player, facing them, keeping the distance it had when
    ///      the run began. Spears keep raining in a band behind the player.
    ///      The boss's <see cref="KatanaGuard"/>, if it has one, guards it
    ///      for the whole run.
    ///      After <see cref="firstRunAttackDelay"/> the boss starts on
    ///      <see cref="runAttacks"/>, one after another in order, round and
    ///      round, waiting each one's Delay After between them.
    ///   6. After <see cref="runTime"/> seconds no more segments are laid:
    ///      <see cref="finaleRoom"/> is placed on the end of the hall instead,
    ///      and no new attack is started. A <see cref="FloorWeb"/> is planned
    ///      round the break point, its floor cut, unseen, into the web's cells.
    ///   7. The run goes on into the room, the boss backing no further than
    ///      <see cref="bossBackMargin"/> short of its far end.
    ///      <see cref="linesLead"/> seconds before the player reaches the
    ///      break point, the web's cuts light up in <see cref="lightColour"/>,
    ///      spreading out from the centre.
    ///   8. At the break point the spears stop, the player's controls lock
    ///      and the run ends, so they slide to a stop. Fuzzy walls of light
    ///      rise along the cuts near the centre, at varying heights, while
    ///      the boss's katana (<see cref="katanaPrefab"/>) glistens and
    ///      whirls round the boss, its trail hanging in the air.
    ///   9. The mace (<see cref="macePrefab"/>) rises over the boss, glistens,
    ///      and slams the web's centre, between the player (stopped
    ///      <see cref="playerStopBefore"/> short of it) and the boss (stopped
    ///      <see cref="bossStopAfter"/> past it). The cuts, walls and katana
    ///      lines flare the mace's colour, and at once the floor caves in
    ///      along the web, driven down hardest at the centre, its light
    ///      dissolving. The player falls with it; the boss rises
    ///      <see cref="levitateHeight"/> off it and hovers, bobbing, dust
    ///      blowing out from under it.
    ///  10. <see cref="fadeDelay"/> later, a <see cref="ScreenIris"/> closes
    ///      round the falling player over <see cref="irisCloseTime"/>, loads
    ///      <see cref="nextScene"/>, and opens round the player there over
    ///      <see cref="irisOpenTime"/>.
    ///
    /// The first hall piece is placed in the scene by hand, as
    /// <see cref="chainStart"/>; every copy takes its rotation, height and
    /// sideways position, one segment length further down the hall along its
    /// Z axis, so the prefab can be built however it likes. The room is built
    /// facing +Z, with its pivot level with and in line with the hall piece's
    /// (nudge it with <see cref="roomOffset"/>), and laid so the near edge of
    /// its renderers meets the end of the hall. Its floor, and only its floor,
    /// goes under a child named <see cref="floorName"/>; the walls stay put.
    /// It can hold an empty child named <see cref="breakPointName"/>, where
    /// the floor breaks; without one it breaks halfway through the room.
    ///
    /// The boss plays <see cref="animationState"/> on loop throughout, and
    /// kicks up a <see cref="DustTrail"/> whenever it moves.
    /// </remarks>
    public sealed class SpearHallIntro : MonoBehaviour
    {
        [Header("Boss")]
        [SerializeField]
        [Tooltip("The boss prefab, spawned at the Boss Start Point once the camera has turned. A boss already in the scene works too; it is kept switched off until then.")]
        private Enemy boss;

        [SerializeField]
        [Tooltip("Where the boss appears, hidden behind a pillar. Empty uses where a scene boss is placed, or the Boss Stand Point for a prefab.")]
        private Transform bossStartPoint;

        [SerializeField]
        [Tooltip("Where the boss stops in the hall to talk.")]
        private Transform bossStandPoint;

        [SerializeField]
        [Tooltip("The boss's walking speed out from behind the pillar. Dust only kicks up above the dust trail's Min Speed, 3 by default.")]
        [Min(0.1f)]
        private float bossWalkSpeed = 4f;

        [SerializeField]
        [Tooltip("Degrees per second the boss turns while walking out and to face the player.")]
        [Min(1f)]
        private float bossTurnSpeed = 360f;

        [SerializeField]
        [Tooltip("Animator state the boss plays, looping, the whole way through: walking out, talking, throwing and backing down the hall. Empty uses the boss's own Idle Animation.")]
        private string animationState = "ChromatistIdle";

        [Header("Hall")]
        [SerializeField]
        [Tooltip("The first piece of repeating hall, placed in the scene by hand where the built hall ends. Copies are laid on from it along its Z axis, away from this trigger.")]
        private Transform chainStart;

        [SerializeField]
        [Tooltip("The repeating hall prefab the copies are made from. Empty copies Chain Start itself.")]
        private GameObject hallSegment;

        [SerializeField]
        [Tooltip("Distance from one hall piece to the next. 0 measures the first piece, which counts anything sticking out past its ends, such as pillars or banners, and so leaves gaps.")]
        [Min(0f)]
        private float segmentLength;

        [SerializeField]
        [Tooltip("How far ahead of the player the hall is always laid. Keep it past the edge of the screen.")]
        [Min(1f)]
        private float buildAhead = 80f;

        [SerializeField]
        [Tooltip("How far behind the player laid segments are kept before being removed. Keep it past the spears and the edge of the screen.")]
        [Min(0f)]
        private float keepBehind = 40f;

        [SerializeField]
        [Tooltip("Width of the hall floor spears can land on, centred on the hall's centre line.")]
        [Min(0f)]
        private float hallWidth = 12f;

        [SerializeField]
        [Tooltip("Layers spears can land on. Spots with no floor under them, or floor far above or below the player, such as a pillar top, are skipped.")]
        private LayerMask floorMask = ~0;

        [Header("Finale room")]
        [SerializeField]
        [Tooltip("The finale room, built facing +Z with its entrance at its near (-Z) edge. Placed on the end of the hall once the run time is up.")]
        private GameObject finaleRoom;

        [SerializeField]
        [Tooltip("Nudges the finale room from where it is placed: X across the hall, Y up, Z down the hall.")]
        private Vector3 roomOffset;

        [SerializeField]
        [Tooltip("Name of the child in the finale room holding its floor, which breaks apart. Keep the walls out of it.")]
        private string floorName = "Floor";

        [SerializeField]
        [Tooltip("Name of the empty child in the finale room the player reaches for the floor to break. Without one, it breaks halfway through the room.")]
        private string breakPointName = "BreakPoint";

        [SerializeField]
        [Tooltip("Closest to the finale room's far end the boss backs.")]
        [Min(0f)]
        private float bossBackMargin = 4f;

        [SerializeField]
        [Tooltip("How far short of the break point the player's run ends. They slide on a little from there.")]
        [Min(0f)]
        private float playerStopBefore = 5f;

        [SerializeField]
        [Tooltip("How far past the break point the boss stops backing.")]
        [Min(0f)]
        private float bossStopAfter = 5f;

        [Header("Web")]
        [SerializeField] private FloorWeb.Settings web = new FloorWeb.Settings();

        [SerializeField]
        [Tooltip("Seconds before the player reaches the break point that the web's cuts light up.")]
        [Min(0f)]
        private float linesLead = 0.6f;

        [SerializeField]
        [Tooltip("Colour of the cuts on the floor and the katana's lines until the mace slams. HDR: above 1 blooms. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        [ColorUsage(false, true)]
        private Color lightColour = new Color(0.1f, 2.2f, 3f, 1f);

        [SerializeField]
        [Tooltip("Colour of the walls of light until the mace slams. HDR: above 1 blooms. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        [ColorUsage(false, true)]
        private Color wallColour = new Color(0.1f, 2.2f, 3f, 1f);

        [SerializeField]
        [Tooltip("Colour of the walls of light after the slam. Black uses the mace's colour, as everything else turns.")]
        [ColorUsage(false, true)]
        private Color slamWallColour = Color.black;

        [SerializeField]
        [Tooltip("Seconds after the walls of light have all risen before the mace rises.")]
        [Min(0f)]
        private float lightHold = 0.3f;

        [SerializeField]
        [Tooltip("How many times as bright the light flares at the slam, as the floor caves in.")]
        [Min(1f)]
        private float brightPeak = 2.5f;

        [Header("Katana")]
        [SerializeField]
        [Tooltip("The katana's prefab, as listed in the boss's Boss Blades. Empty leaves it be.")]
        private GameObject katanaPrefab;

        [SerializeField]
        [Tooltip("The katana's glint as it circles. Its Colour is the glow and the gleams.")]
        private BladeGlisten.Settings katanaGlisten = new BladeGlisten.Settings { colour = new Color(0.1f, 0.85f, 1f) };

        [SerializeField]
        [Tooltip("Degrees a second the katana circles the boss. Negative goes the other way.")]
        private float katanaSpin = 600f;

        [SerializeField]
        [Tooltip("Distance from the boss the katana circles at.")]
        [Min(0.1f)]
        private float katanaRadius = 2.5f;

        [SerializeField]
        [Tooltip("How far the katana weaves in and out, as a part of that distance.")]
        [Range(0f, 1f)]
        private float katanaWeave = 0.35f;

        [SerializeField]
        [Tooltip("Height above the boss's pivot the katana circles at.")]
        private float katanaHeight = 1.5f;

        [SerializeField]
        [Tooltip("How far the katana bobs up and down as it circles.")]
        [Min(0f)]
        private float katanaBob = 0.8f;

        [SerializeField]
        [Tooltip("Seconds the katana's lines hang in the air. 0 for none.")]
        [Min(0f)]
        private float katanaTrailTime = 1.5f;

        [SerializeField]
        [Tooltip("Width of the katana's lines.")]
        [Min(0.01f)]
        private float katanaTrailWidth = 0.12f;

        [SerializeField]
        [Tooltip("Seconds the katana's lines take to dissolve as the floor falls apart.")]
        [Min(0.01f)]
        private float katanaTrailFade = 0.3f;

        [Header("Mace")]
        [SerializeField]
        [Tooltip("The mace's prefab, as listed in the boss's Boss Blades. Empty skips the slam.")]
        private GameObject macePrefab;

        [SerializeField]
        [Tooltip("The mace's glint as it is raised. Its Colour is also what the light turns after the slam -- match the Mace Smash attack's.")]
        private BladeGlisten.Settings maceGlisten = new BladeGlisten.Settings { colour = new Color(0.5943396f, 0f, 0.023271088f) };

        [SerializeField]
        [Tooltip("How bright the light is in the mace's colour, as a multiple of it. Above 1 blooms.")]
        [Min(0f)]
        private float maceLightIntensity = 3f;

        [SerializeField]
        [Tooltip("Height above the boss's pivot the mace's head is held at before the slam.")]
        private float maceRaiseHeight = 2.5f;

        [SerializeField]
        [Tooltip("How far the slam's arc bulges up, above the straight line from where the mace is held to the web's centre.")]
        [Min(0f)]
        private float maceArcHeight = 1.5f;

        [SerializeField]
        [Tooltip("Seconds the mace takes to rise from its orbit to overhead.")]
        [Min(0.01f)]
        private float maceRaiseTime = 0.4f;

        [SerializeField]
        [Tooltip("Seconds the mace hangs overhead, glistening, drawing back.")]
        [Min(0f)]
        private float maceWindupTime = 0.8f;

        [SerializeField]
        [Tooltip("Degrees the mace draws back past upright before the slam.")]
        [Range(0f, 60f)]
        private float maceCockAngle = 20f;

        [SerializeField]
        [Tooltip("How hard the mace trembles by the end of the draw back.")]
        [Min(0f)]
        private float maceTremble = 0.1f;

        [SerializeField]
        [Tooltip("Seconds the slam takes, overhead to the floor.")]
        [Min(0.01f)]
        private float maceSlamTime = 0.12f;

        [SerializeField]
        [Tooltip("The burst where the mace hits, in the mace's colour.")]
        private HammerImpact.Settings maceImpact = new HammerImpact.Settings
        {
            ringRadius = 14f,
            ringWidth = 2f,
            ringTime = 0.6f,
            ringIntensity = 4f,
            debrisCount = 40,
        };

        [SerializeField]
        [Tooltip("Radius of the ball of light bursting out where the mace hits. 0 for none.")]
        [Min(0f)]
        private float maceBlastRadius = 7f;

        [SerializeField]
        [Tooltip("The ball of light. Its colour is replaced by the mace's.")]
        private BlastSphere.Settings maceBlast = new BlastSphere.Settings { growTime = 0.15f, fadeTime = 0.5f };

        [SerializeField]
        [Tooltip("How far the slam jolts the camera. 0 for none.")]
        [Min(0f)]
        private float maceShake = 1.5f;

        [SerializeField]
        [Tooltip("Seconds the slam's camera jolt lasts.")]
        [Min(0.01f)]
        private float maceShakeDuration = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the mace stays in the floor after the slam before going back to its orbit.")]
        [Min(0f)]
        private float maceHoldTime = 0.4f;

        [Header("Levitate")]
        [SerializeField]
        [Tooltip("How far the boss rises off the floor as it breaks away under it.")]
        [Min(0f)]
        private float levitateHeight = 1.2f;

        [SerializeField]
        [Tooltip("Seconds the boss takes to rise to that height.")]
        [Min(0.01f)]
        private float levitateRiseTime = 0.6f;

        [SerializeField]
        [Tooltip("How far the boss bobs up and down while hovering.")]
        [Min(0f)]
        private float levitateBob = 0.15f;

        [SerializeField]
        [Tooltip("Bobs up and down this many times a second.")]
        [Min(0f)]
        private float levitateBobRate = 0.8f;

        [SerializeField]
        [Tooltip("Dust puffs a second blown out from under the hovering boss.")]
        [Min(0f)]
        private float levitateDustRate = 30f;

        [SerializeField]
        [Tooltip("Speed the dust is blown outward from under the boss.")]
        [Min(0f)]
        private float levitateDustSpeed = 2.5f;

        [SerializeField]
        [Tooltip("Look of the dust under the hovering boss. Spacing, Puffs Per Step, Min Speed, Rise, Drag and Spread are unused here.")]
        private DustTrail.Settings levitateDust = new DustTrail.Settings { size = 0.3f, lifetime = 0.6f };

        [Header("Next scene")]
        [SerializeField]
        [Tooltip("Seconds after the floor falls apart before the screen starts to close.")]
        [Min(0f)]
        private float fadeDelay = 1f;

        [SerializeField]
        [Tooltip("The scene loaded once the screen has closed. It must be in the build's scene list.")]
        private string nextScene = "L4.2";

        [SerializeField]
        [Tooltip("Seconds the screen takes to close round the falling player.")]
        [Min(0.01f)]
        private float irisCloseTime = 2f;

        [SerializeField]
        [Tooltip("Seconds the screen takes to open round the player in the next scene.")]
        [Min(0.01f)]
        private float irisOpenTime = 2f;

        [Header("Camera")]
        [SerializeField]
        [Tooltip("Seconds the camera takes to swing round to look down the hall.")]
        [Min(0f)]
        private float cameraTurnTime = 1f;

        [Header("Dialogue")]
        [SerializeField] private List<DialogueLine> dialogue = new List<DialogueLine>();

        [Header("Spear storm")]
        [SerializeField] private HallSpear.Settings spear = new HallSpear.Settings();

        [SerializeField]
        [Tooltip("Seconds spears rain behind the standing player before the run starts.")]
        [Min(0f)]
        private float stormTime = 1.5f;

        [SerializeField]
        [Tooltip("Spears a second while the player stands.")]
        [Min(0f)]
        private float stormSpearsPerSecond = 25f;

        [SerializeField]
        [Tooltip("Nearest and furthest behind the standing player spears land.")]
        private Vector2 stormBehind = new Vector2(2f, 14f);

        [SerializeField]
        [Tooltip("Spears a second during the run.")]
        [Min(0f)]
        private float runSpearsPerSecond = 40f;

        [SerializeField]
        [Tooltip("Nearest and furthest behind the running player spears land, measured as they land. Closer than the spear's Damage Radius can hit a player who stalls.")]
        private Vector2 runBehind = new Vector2(1f, 6f);

        [Header("Run")]
        [SerializeField]
        [Tooltip("Seconds of running before the finale room is placed on the end of the hall. The player then still has to run the rest of the laid hall to reach it.")]
        [Min(0f)]
        private float runTime = 8f;

        [SerializeField]
        [Tooltip("The player's speed down the hall, as a multiple of their walking speed.")]
        [Min(0f)]
        private float runSpeedMultiplier = 3f;

        [SerializeField]
        [Tooltip("The player's left-right steering speed during the run, as a multiple of their walking speed.")]
        [Min(0f)]
        private float sideSpeedMultiplier = 1f;

        [SerializeField]
        [Tooltip("How far the dash that starts the run carries the player. 0 skips it and just runs.")]
        [Min(0f)]
        private float dashDistance = 4f;

        [SerializeField]
        [Tooltip("Seconds that dash takes.")]
        [Min(0.01f)]
        private float dashTime = 0.15f;

        [SerializeField]
        [Tooltip("Closest the boss ever lets the player get, down the hall, while backing away. It is pushed on ahead if the player gains on it.")]
        [Min(0f)]
        private float bossMinimumLead = 8f;

        [SerializeField]
        [Tooltip("Speed the boss drifts left and right across the hall as it backs away. 0 keeps it on the centre line.")]
        [Min(0f)]
        private float bossWeaveSpeed = 4f;

        [SerializeField]
        [Tooltip("Furthest the boss drifts to either side of the hall's centre line.")]
        [Min(0f)]
        private float bossWeaveRange = 3f;

        [Header("Run attacks")]
        [SerializeField]
        [Tooltip("Attacks the boss cycles through, in order, as it backs down the hall. Each waits its own Delay After before the next. Their movement and animation settings are ignored here.")]
        private List<AttackPattern> runAttacks = new List<AttackPattern>();

        [SerializeField]
        [Tooltip("Seconds into the run before the first attack.")]
        [Min(0f)]
        private float firstRunAttackDelay = 1f;

        [Header("Falling behind")]
        [SerializeField]
        [Tooltip("How far behind where they would be at full run speed the player can fall -- snagged on a pillar or a wall -- before the spears catch them.")]
        [Min(0.5f)]
        private float stuckDistance = 4f;

        [SerializeField]
        [Tooltip("Damage the spears deal a player who falls behind.")]
        [Min(0f)]
        private float stuckDamage = 30f;

        [SerializeField]
        [Tooltip("Spears thrown down round a player who falls behind, for show; they deal no damage of their own.")]
        [Min(0)]
        private int stuckSpears = 6;

        [SerializeField]
        [Tooltip("How far round the player those spears land.")]
        [Min(0f)]
        private float stuckSpearSpread = 1.5f;

        [SerializeField]
        [Tooltip("Speed the player is knocked toward the middle of the hall at, to get them clear of what caught them.")]
        [Min(0f)]
        private float recoverPush = 30f;

        [SerializeField]
        [Tooltip("Seconds after being caught before the player can be caught again.")]
        [Min(0f)]
        private float recoverGrace = 1f;

        /// <summary>A laid piece of hall, and how far down the hall its far edge is.</summary>
        private struct Laid
        {
            public GameObject piece;
            public float end;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private bool played;
        private PlayerMotor motor;
        private PlayerInputReader input;
        private PlayerHealth health;

        // The boss actually taking part: the scene one, or the prefab's spawn.
        private Enemy fighter;
        private DustTrail dust;

        // Down the hall, and across it to the right; both flat.
        private Vector3 along;
        private Vector3 across;

        private readonly Queue<Laid> laid = new Queue<Laid>();

        // How far down the hall, from this trigger, the laid hall reaches;
        // the gap between pieces; and how many copies have been laid.
        private float chainEnd;
        private float segmentStep;
        private int laidCount;

        // Where the player would be down the hall had they kept full run
        // speed, and whether they are being caught for falling short of it.
        private float paceProgress;
        private bool caught;

        // Set once the room is placed: how far down the hall its entrance,
        // far end and break point are, and what breaks and carves its floor.
        private GameObject room;
        private float roomEntrance;
        private float roomFar;
        private float breakAt;
        private FloorWeb floorWeb;
        private bool linesShown;

        // The katana's lines in the air, while it circles.
        private TrailRenderer katanaTrail;

        /// <summary>The boss taking part, once it has appeared; null before.</summary>
        public Enemy Boss => fighter;

        /// <summary>Raised the moment the player walks in and is stopped.</summary>
        public event Action<SpearHallIntro> Triggered;

        /// <summary>Raised as the player dashes off down the hall.</summary>
        public event Action<SpearHallIntro> RunStarted;

        /// <summary>Raised once the finale room has been placed on the end of the hall.</summary>
        public event Action<SpearHallIntro, GameObject> RoomPlaced;

        /// <summary>Raised as the finale room's floor breaks and the player starts to fall.</summary>
        public event Action<SpearHallIntro> FloorBroke;

        private bool ChainStartIsHallPiece => chainStart != null && chainStart.GetComponentInChildren<Renderer>() != null;

        private bool BossInScene => boss != null && boss.gameObject.scene.IsValid();

        private void Awake()
        {
            List<string> missing = new List<string>();

            if (boss == null) missing.Add("Boss is empty");
            if (bossStandPoint == null) missing.Add("Boss Stand Point is empty");
            if (chainStart == null) missing.Add("Chain Start is empty");
            if (finaleRoom == null) missing.Add("Finale Room is empty");

            if (missing.Count > 0)
            {
                Debug.LogWarning($"[SpearHallIntro] {name} won't play: {string.Join("; ", missing)}.", this);
                played = true;
                return;
            }

            if (!ChainStartIsHallPiece)
            {
                Debug.LogWarning($"[SpearHallIntro] {name}'s Chain Start, {chainStart.name}, has nothing visible on it. "
                               + "It should be a hall piece placed in the scene: copies are lined up on its pivot, so with an empty they can land far off to the side of the hall.", this);
            }

            // A prefab asset is left alone: changes to it would stick after
            // play mode.
            if (BossInScene)
            {
                boss.StayDormant = true;
                boss.gameObject.SetActive(false);
            }

            Prewarm();
        }

        /// <summary>
        /// Builds every spear and landing burst the run can have out at once
        /// now, while the level loads, so none are built mid-cutscene.
        /// </summary>
        private void Prewarm()
        {
            float rate = Mathf.Max(stormSpearsPerSecond, runSpearsPerSecond);
            float spearLife = spear.flightTime + spear.lingerTime + spear.sinkTime;

            // A little over the steady count, for the clumping a frame hitch causes.
            HallSpear.Prewarm(spear.spearPrefab, Mathf.CeilToInt(rate * spearLife * 1.2f) + 4);

            HammerImpact.Settings burst = spear.landingBurst;

            if (burst.ringRadius > 0f || burst.debrisCount > 0)
            {
                float burstLife = Mathf.Max(burst.ringTime, burst.debrisCount > 0 ? burst.debrisTime : 0f);
                HammerImpact.Prewarm(Mathf.CeilToInt(rate * burstLife * 1.2f) + 4, burst.debrisCount);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (played)
            {
                return;
            }

            PlayerMotor player = other.GetComponentInParent<PlayerMotor>();

            if (player == null)
            {
                return;
            }

            played = true;
            motor = player;
            input = player.GetComponent<PlayerInputReader>();
            health = player.GetComponent<PlayerHealth>();

            // Down the hall is the first piece's Z axis, whichever way along
            // it leads away from here. An empty's axes mean nothing, so then
            // it is simply from here toward it.
            along = Flat(chainStart.position - transform.position).normalized;

            if (ChainStartIsHallPiece)
            {
                Vector3 axis = Flat(chainStart.forward).normalized;

                if (axis.sqrMagnitude > 0.0001f)
                {
                    along = Vector3.Dot(axis, along) < 0f ? -axis : axis;
                }
            }

            across = Vector3.Cross(Vector3.up, along);

            Vector2 first = Extent(chainStart.gameObject, along);
            segmentStep = segmentLength > 0f ? segmentLength : MeasureSegment(first);
            chainEnd = Progress(chainStart.position) + first.x + segmentStep;

            Triggered?.Invoke(this);
            StartCoroutine(Play());
        }

        private void OnDisable()
        {
            ReleasePlayer();
        }

        private IEnumerator Play()
        {
            SetLocked(true);
            TopDownCamera.TurnToward(bossStandPoint.position, cameraTurnTime);

            // The view settles on the empty hall before the boss steps out.
            if (cameraTurnTime > 0f)
            {
                yield return new WaitForSeconds(cameraTurnTime);
            }

            yield return WalkOut();

            bool talking = true;
            Say(0, () => talking = false);

            while (talking)
            {
                yield return null;
            }

            DialogueBox.Instance.Close();

            yield return Storm();

            if (motor == null)
            {
                yield break;
            }

            yield return Run();

            if (motor != null)
            {
                yield return Finale();
            }
        }

        /// <summary>
        /// The player comes to a stop over the web's centre. Walls of light
        /// rise along its cuts while the katana circles the boss, drawing
        /// lines in the air; then the mace rises overhead and slams down in
        /// front of the boss, turning the light its colour. The light
        /// brightens, the floor collapses along the web, everyone falls, and
        /// a moment later the screen closes on the way to the next scene.
        /// </summary>
        private IEnumerator Finale()
        {
            // Their run speed bleeds off; the floor still holds them, for now.
            ChaseFrame.End();
            motor.StopAutoRun();
            motor.CancelDash();
            SetLocked(true);
            SetGuarding(false);

            // The mace strikes the web's centre, between the boss and the player.
            Vector3 target = floorWeb != null ? floorWeb.Centre : OnCentreLine(breakAt, fighter.transform.position.y);

            float wait = lightHold;

            if (floorWeb != null)
            {
                ShowLines();
                floorWeb.RaiseLight();
                wait += floorWeb.LightTime;
            }

            bool finished = false;
            float strike = wait + maceRaiseTime + maceWindupTime;
            StartCoroutine(KatanaDance(strike, () => finished));

            yield return new WaitForSeconds(wait);

            // The mace, overhead and into the floor; the light takes its colour.
            Transform mace = null;
            BossBlades blades = fighter.GetComponent<BossBlades>();
            int maceIndex = blades != null && macePrefab != null ? blades.IndexOf(macePrefab) : -1;

            if (maceIndex >= 0)
            {
                mace = blades.Borrow(maceIndex, this);
                yield return MaceSlam(mace, blades.RotationOffset(maceIndex), target);
            }
            else if (macePrefab != null)
            {
                Debug.LogWarning($"[SpearHallIntro] {name}: the boss has no Boss Blades blade made from the Mace Prefab, so there is no slam.", this);
            }

            // The strike: the light flares the mace's colour and the floor
            // caves in from the centre at once.
            Color maceLight = maceGlisten.colour * maceLightIntensity;
            maceLight.a = 1f;
            Recolour(maceLight);
            finished = true;

            if (floorWeb != null)
            {
                floorWeb.SetColour(maceLight, SlamWalls(maceLight), brightPeak);
                floorWeb.Break();
            }

            if (mace != null)
            {
                StartCoroutine(ReturnLater(blades, maceIndex, maceHoldTime));
            }

            StartCoroutine(BossLevitate(floorWeb != null ? floorWeb.DelayAt(fighter.transform.position) : 0f));
            FloorBroke?.Invoke(this);

            if (fadeDelay > 0f)
            {
                yield return new WaitForSeconds(fadeDelay);
            }

            ScreenIris.CloseThenLoad(nextScene, motor != null ? motor.transform : null, irisCloseTime, irisOpenTime);
        }

        /// <summary>Lights the web's cuts, in the first colour, if not already.</summary>
        private void ShowLines()
        {
            if (floorWeb == null || linesShown)
            {
                return;
            }

            linesShown = true;

            floorWeb.SetColour(Opaque(lightColour), Opaque(wallColour));
            floorWeb.ShowLines();
        }

        /// <summary>The walls' colour after the slam: their own, or <paramref name="maceLight"/> without one.</summary>
        private Color SlamWalls(Color maceLight)
        {
            return slamWallColour.maxColorComponent > 0f ? Opaque(slamWallColour) : maceLight;
        }

        private static Color Opaque(Color colour)
        {
            colour.a = 1f;
            return colour;
        }

        /// <summary>Turns the katana's lines in the air <paramref name="colour"/>, and the web too.</summary>
        private void Recolour(Color colour)
        {
            if (floorWeb != null)
            {
                floorWeb.SetColour(colour, SlamWalls(colour));
            }

            if (katanaTrail != null)
            {
                FlatMaterial.Tint(katanaTrail, colour, new MaterialPropertyBlock());
            }
        }

        /// <summary>
        /// The katana leaves its orbit, glistening, and whirls round the boss,
        /// weaving in and out and up and down, its trail hanging in the air,
        /// until <paramref name="finished"/>. Then the trail dissolves and the
        /// katana goes back.
        /// </summary>
        private IEnumerator KatanaDance(float strike, Func<bool> finished)
        {
            BossBlades blades = fighter.GetComponent<BossBlades>();
            int index = blades != null && katanaPrefab != null ? blades.IndexOf(katanaPrefab) : -1;

            if (index < 0)
            {
                if (katanaPrefab != null)
                {
                    Debug.LogWarning($"[SpearHallIntro] {name}: the boss has no Boss Blades blade made from the Katana Prefab, so it doesn't dance.", this);
                }

                yield break;
            }

            Transform blade = blades.Borrow(index, this);
            Quaternion modelOffset = blades.RotationOffset(index);
            BladeGlisten shine = BladeGlisten.Begin(blade, katanaGlisten, strike);

            Vector3 fromPosition = blade.position;
            Quaternion fromRotation = blade.rotation;
            float angle = UnityEngine.Random.Range(0f, 360f);
            bool trailing = false;
            float clock = 0f;

            while (!finished() && fighter != null)
            {
                angle += katanaSpin * Time.deltaTime;

                // Weaving: in and out at one beat, up and down at another.
                float radius = katanaRadius * (1f + katanaWeave * Mathf.Sin(clock * 3.1f));
                float height = katanaHeight + katanaBob * Mathf.Sin(clock * 4.7f + 1f);

                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 ahead = Vector3.Cross(Vector3.up, outward) * Mathf.Sign(katanaSpin);
                Vector3 position = fighter.transform.position + outward * radius + Vector3.up * height;
                Quaternion rotation = Pointing(ahead + Vector3.up * (0.6f * Mathf.Cos(clock * 4.7f + 1f)), outward) * modelOffset;

                // Out of the orbit over a moment, then round and round.
                float enter = Smooth(clock / 0.25f);
                blade.SetPositionAndRotation(Vector3.Lerp(fromPosition, position, enter),
                                             Quaternion.Slerp(fromRotation, rotation, enter));
                shine.Tick(clock);

                // Drawn only once it is out among the circling.
                if (!trailing && enter >= 1f)
                {
                    trailing = true;
                    katanaTrail = MakeTrail(blade, BladeGleam.TipOf(blade));
                }

                yield return null;
                clock += Time.deltaTime;
            }

            shine.End();

            if (blades != null)
            {
                blades.Return(index);
            }

            if (katanaTrail != null)
            {
                TrailRenderer trail = katanaTrail;
                katanaTrail = null;
                trail.emitting = false;
                yield return Dissolve(trail, katanaTrailFade);
            }
        }

        /// <summary>
        /// The mace rises from its orbit to straight over the boss, head up,
        /// glistening and trembling, then swings over and down to slam the
        /// floor at <paramref name="ground"/>, where it stays.
        /// </summary>
        private IEnumerator MaceSlam(Transform mace, Quaternion modelOffset, Vector3 ground)
        {
            BladeGlisten shine = BladeGlisten.Begin(mace, maceGlisten, maceRaiseTime + maceWindupTime);
            Vector3 headLocal = BladeGleam.TipOf(mace);

            Vector3 facing = Flat(ground - fighter.transform.position);
            facing = facing.sqrMagnitude > 0.0001f ? facing.normalized : -along;
            Vector3 side = Vector3.Cross(Vector3.up, facing);

            // The head is held over the boss, then thrown up and over in an
            // arc to the target, the mace turning from upright to head first
            // into the floor.
            Vector3 raised = fighter.transform.position + Vector3.up * maceRaiseHeight;
            Vector3 over = Vector3.Lerp(raised, ground, 0.5f) + Vector3.up * maceArcHeight;
            Vector3 headFirst = (facing - Vector3.up * 1.5f).normalized;

            // Swing of the way from held to the target, drawn back cock degrees.
            void Pose(float swing, float cock, Vector3 shake, out Vector3 position, out Quaternion rotation)
            {
                float u = 1f - swing;
                Vector3 head = u * u * raised + 2f * u * swing * over + swing * swing * ground;
                Vector3 pointing = Vector3.Slerp(Quaternion.AngleAxis(-cock, side) * Vector3.up, headFirst, swing);

                rotation = Pointing(pointing, side) * modelOffset;
                position = head - rotation * Vector3.Scale(mace.lossyScale, headLocal) + shake;
            }

            Vector3 fromPosition = mace.position;
            Quaternion fromRotation = mace.rotation;
            float clock = 0f;

            for (float elapsed = 0f; elapsed < maceRaiseTime; elapsed += Time.deltaTime, clock += Time.deltaTime)
            {
                Pose(0f, 0f, Vector3.zero, out Vector3 position, out Quaternion rotation);
                float eased = Smooth(elapsed / maceRaiseTime);
                mace.SetPositionAndRotation(Vector3.Lerp(fromPosition, position, eased), Quaternion.Slerp(fromRotation, rotation, eased));
                shine.Tick(clock);
                yield return null;
            }

            // Drawing back, trembling harder.
            for (float elapsed = 0f; elapsed < maceWindupTime; elapsed += Time.deltaTime, clock += Time.deltaTime)
            {
                float charge = elapsed / maceWindupTime;
                Pose(0f, maceCockAngle * charge, UnityEngine.Random.insideUnitSphere * (maceTremble * charge * charge),
                     out Vector3 position, out Quaternion rotation);
                mace.SetPositionAndRotation(position, rotation);
                shine.Tick(clock);
                yield return null;
            }

            shine.Tick(maceRaiseTime + maceWindupTime);

            // Over and down, speeding up all the way.
            for (float elapsed = 0f; elapsed < maceSlamTime; elapsed += Time.deltaTime)
            {
                float swing = elapsed / maceSlamTime;
                Pose(swing * swing, maceCockAngle * (1f - swing), Vector3.zero, out Vector3 position, out Quaternion rotation);
                mace.SetPositionAndRotation(position, rotation);
                yield return null;
            }

            Pose(1f, 0f, Vector3.zero, out Vector3 landed, out Quaternion landedRotation);
            mace.SetPositionAndRotation(landed, landedRotation);
            shine.End();

            Color burst = maceGlisten.colour * maceLightIntensity;
            burst.a = 1f;
            HammerImpact.Create(ground, maceImpact, null, burst, 0f, Color.white);

            if (maceBlastRadius > 0f)
            {
                maceBlast.colour = new Color(burst.r * 1.5f, burst.g * 1.5f, burst.b * 1.5f, 1f);
                BlastSphere.Create(ground + Vector3.up * 0.5f, maceBlastRadius, maceBlast);
            }

            ImpactFrames frames = FindAnyObjectByType<ImpactFrames>();

            if (frames != null)
            {
                frames.TriggerImpact(ground);
            }

            if (maceShake > 0f)
            {
                TopDownCamera.Shake(maceShake, maceShakeDuration);
            }
        }

        /// <summary>Hands blade <paramref name="index"/> back to its orbit after <paramref name="delay"/>.</summary>
        private static IEnumerator ReturnLater(BossBlades blades, int index, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (blades != null)
            {
                blades.Return(index);
            }
        }

        /// <summary>A trail of the first light colour, on the blade at its tip.</summary>
        private TrailRenderer MakeTrail(Transform blade, Vector3 tipLocal)
        {
            if (katanaTrailTime <= 0f)
            {
                return null;
            }

            GameObject host = new GameObject("KatanaTrail");
            host.transform.SetParent(blade, false);
            host.transform.localPosition = tipLocal;

            TrailRenderer trail = host.AddComponent<TrailRenderer>();
            trail.time = katanaTrailTime;
            trail.widthMultiplier = katanaTrailWidth;
            trail.minVertexDistance = 0.1f;
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;

            Color colour = lightColour;
            colour.a = 1f;
            FlatMaterial.Tint(trail, colour, new MaterialPropertyBlock());
            return trail;
        }

        /// <summary>Dissolves <paramref name="trail"/> over <paramref name="seconds"/>, then removes it.</summary>
        private static IEnumerator Dissolve(TrailRenderer trail, float seconds)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            for (float clock = 0f; clock < seconds && trail != null; clock += Time.deltaTime)
            {
                trail.GetPropertyBlock(block);
                block.SetFloat(FadeId, 1f - clock / seconds);
                trail.SetPropertyBlock(block);
                yield return null;
            }

            if (trail != null)
            {
                Destroy(trail.gameObject);
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
        /// <summary>
        /// A rotation pointing a blade built tip down -Y along
        /// <paramref name="tip"/>, its flat facing across
        /// <paramref name="flat"/>, before the model's own offset.
        /// </summary>
        private static Quaternion Pointing(Vector3 tip, Vector3 flat)
        {
            tip = tip.sqrMagnitude > 0.0001f ? tip.normalized : Vector3.down;

            Vector3 forward = Vector3.ProjectOnPlane(flat, tip);

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(Vector3.forward, tip);
            }

            return Quaternion.LookRotation(forward.normalized, -tip);
        }

        /// <summary>
        /// Once the break reaches the boss, it rises off the falling floor and
        /// hovers, bobbing, dust blowing out from under it, until the scene
        /// changes.
        /// </summary>
        private IEnumerator BossLevitate(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (fighter == null)
            {
                yield break;
            }

            Transform bossTransform = fighter.transform;
            Collider bossBody = fighter.GetComponent<Collider>();
            Vector3 ground = bossTransform.position;
            DustTrail.Cloud cloud = DustTrail.Cloud.Create("Levitate Dust (" + fighter.name + ")");
            float owed = 0f;

            for (float clock = 0f; fighter != null; clock += Time.deltaTime)
            {
                // Rises smoothly, then the bob eases in so there is no jolt.
                float rise = Smooth(clock / levitateRiseTime);
                float bob = levitateBob * Mathf.Sin(clock * levitateBobRate * Mathf.PI * 2f) * rise;
                bossTransform.position = ground + Vector3.up * (levitateHeight * rise + bob);

                Bounds bounds = bossBody != null ? bossBody.bounds : new Bounds(bossTransform.position, Vector3.zero);
                Vector3 feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                float footWidth = Mathf.Max(0.2f, Mathf.Min(bounds.extents.x, bounds.extents.z));

                // Blown out and a little down from under the feet, as if
                // pushed off whatever holds it up.
                owed += levitateDustRate * Time.deltaTime;

                while (owed >= 1f)
                {
                    owed -= 1f;

                    Vector3 outward = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                    cloud.Emit(feet + outward * (footWidth * UnityEngine.Random.Range(0.2f, 0.8f)),
                               outward * (levitateDustSpeed * UnityEngine.Random.Range(0.6f, 1.2f))
                               + Vector3.down * (levitateDustSpeed * UnityEngine.Random.Range(0.1f, 0.4f)),
                               levitateDust);
                }

                yield return null;
            }

            cloud.Release();
        }

        /// <summary>The boss appears behind its pillar, walks to its spot, and turns to the player.</summary>
        private IEnumerator WalkOut()
        {
            Vector3 stand = bossStandPoint.position;
            Vector3 start = bossStartPoint != null ? bossStartPoint.position
                          : BossInScene ? boss.transform.position
                          : stand;

            if (BossInScene)
            {
                fighter = boss;
                fighter.transform.position = start;
            }
            else
            {
                // Its brain only first checks for waking a frame later, so
                // keeping it dormant here is in time.
                fighter = Instantiate(boss, start, boss.transform.rotation);
                fighter.name = boss.name;
            }

            fighter.StayDormant = true;

            // Attacks still work during the chase; the boss shrugs them off.
            // The real fight is in the next scene.
            if (fighter.Health != null)
            {
                fighter.Health.Invulnerable = true;
            }

            Rigidbody bossBody = fighter.GetComponent<Rigidbody>();

            // Moved by hand throughout, so nothing physical pushes back.
            // Interpolation would drag the transform back toward the body's
            // last physics position every frame, losing ground each time.
            if (bossBody != null)
            {
                bossBody.isKinematic = true;
                bossBody.interpolation = RigidbodyInterpolation.None;
            }

            fighter.gameObject.SetActive(true);
            fighter.TurnModelToward(stand - start, 360f);
            fighter.PlayAnimation(string.IsNullOrEmpty(animationState) ? fighter.IdleAnimation : animationState, 0f);
            dust = DustTrail.On(fighter.gameObject);

            while ((fighter.transform.position - stand).sqrMagnitude > 0.0001f)
            {
                yield return null;
                fighter.TurnModelToward(stand - fighter.transform.position, bossTurnSpeed * Time.deltaTime);
                fighter.transform.position = Vector3.MoveTowards(fighter.transform.position, stand, bossWalkSpeed * Time.deltaTime);
                dust.KeepEmitting();
            }

            while (motor != null && fighter.TurnModelToward(motor.transform.position - stand, bossTurnSpeed * Time.deltaTime) > 1f)
            {
                yield return null;
            }
        }

        /// <summary>Spears rain behind the player while they stand.</summary>
        private IEnumerator Storm()
        {
            float owed = 0f;
            float clock = 0f;

            while (clock < stormTime && motor != null)
            {
                yield return null;
                clock += Time.deltaTime;
                ThrowSpears(ref owed, stormSpearsPerSecond, stormBehind, 0f);
            }
        }

        /// <summary>
        /// Both dash down the hall, which is laid ahead of the player as they
        /// go, spears following them, until the run time is up and the
        /// player has reached the break point of the room placed on the end,
        /// its web carved in on the way.
        /// </summary>
        private IEnumerator Run()
        {
            float runSpeed = motor.MoveSpeed * runSpeedMultiplier;

            // Attacks and the dash stay on: the dash goes sideways, and
            // attacks happen relative to the running player.
            SetLocked(false);
            ChaseFrame.Begin(along * runSpeed);

            if (dashDistance > 0f)
            {
                motor.DashFixed(along, dashDistance, dashTime);
            }

            motor.StartAutoRun(along * runSpeed, motor.MoveSpeed * sideSpeedMultiplier);
            TopDownCamera.TurnToward(transform.position + along * 1000f, cameraTurnTime * 0.5f);
            RunStarted?.Invoke(this);
            StartCoroutine(RunAttacks());
            SetGuarding(true);

            // The boss keeps the lead it has now, as far as the room allows.
            // It catches up quickly if it fell behind the dash.
            float bossLead = Progress(fighter.transform.position) - Progress(motor.transform.position);
            float bossHeight = fighter.transform.position.y;
            float bossSpeed = runSpeed * 1.5f;

            // Where it is across the hall, and where it is drifting to.
            float weave = Vector3.Dot(fighter.transform.position - transform.position, across);
            float weaveTo = weave;
            float owed = 0f;
            float clock = 0f;
            paceProgress = Progress(motor.transform.position);

            while (motor != null && (room == null || Progress(motor.transform.position) < breakAt - playerStopBefore))
            {
                yield return null;
                clock += Time.deltaTime;

                float player = Progress(motor.transform.position);

                LayHall(player, clock >= runTime);
                // Aimed where the player will be as the spears land.
                ThrowSpears(ref owed, runSpearsPerSecond, runBehind, runSpeed * spear.flightTime);

                // Ahead of the pace banks nothing; only falling short counts.
                paceProgress = Mathf.Max(paceProgress + runSpeed * Time.deltaTime, player);

                if (!caught && paceProgress - player > stuckDistance)
                {
                    StartCoroutine(CatchStraggler());
                }

                // The web's cuts light up just before the player gets there.
                if (room != null && (breakAt - playerStopBefore - player) / Mathf.Max(0.01f, runSpeed) <= linesLead)
                {
                    ShowLines();
                }

                // Backs no further than the room's far end allows.
                float furthest = room != null ? Mathf.Min(roomFar - bossBackMargin, breakAt + bossStopAfter) : float.MaxValue;
                float lead = Mathf.Min(player + bossLead, furthest);
                // Drifting from side to side, picking a new spot each time it
                // gets there -- until the room is down, when it drifts back to
                // the middle, faster if need be to be there as the run ends.
                float weaveSpeed = bossWeaveSpeed;

                if (room != null)
                {
                    weaveTo = 0f;
                    float timeLeft = (breakAt - playerStopBefore - player) / Mathf.Max(0.01f, runSpeed) - 0.25f;
                    weaveSpeed = Mathf.Max(weaveSpeed, Mathf.Abs(weave) / Mathf.Max(0.05f, timeLeft));
                }
                else if (Mathf.Abs(weave - weaveTo) < 0.01f)
                {
                    weaveTo = UnityEngine.Random.Range(-bossWeaveRange, bossWeaveRange);
                }

                weave = Mathf.MoveTowards(weave, weaveTo, weaveSpeed * Time.deltaTime);

                Vector3 goal = OnCentreLine(lead, bossHeight) + across * weave;
                Vector3 before = fighter.transform.position;

                fighter.transform.position = Vector3.MoveTowards(before, goal, bossSpeed * Time.deltaTime);

                // Never lets the player catch up, short of the room's far end.
                float behindBy = Mathf.Min(player + bossMinimumLead, furthest) - Progress(fighter.transform.position);

                if (behindBy > 0f)
                {
                    fighter.transform.position += along * behindBy;
                }

                fighter.TurnModelToward(motor.transform.position - fighter.transform.position, bossTurnSpeed * Time.deltaTime);

                if ((fighter.transform.position - before).sqrMagnitude > 0.000001f)
                {
                    dust.KeepEmitting();
                }
            }
        }

        /// <summary>
        /// The spears catch a player who has fallen behind: a cluster comes
        /// down round them, and as it lands they take
        /// <see cref="stuckDamage"/> and are knocked toward the middle of the
        /// hall, clear of whatever held them. Their pace starts over from
        /// where they are, and they cannot be caught again for
        /// <see cref="recoverGrace"/>.
        /// </summary>
        private IEnumerator CatchStraggler()
        {
            caught = true;

            Vector3 at = motor.transform.position;

            for (int i = 0; i < stuckSpears; i++)
            {
                Vector2 scatter = UnityEngine.Random.insideUnitCircle * stuckSpearSpread;

                if (FindFloor(at + new Vector3(scatter.x, 0f, scatter.y), at.y, out Vector3 floor))
                {
                    HallSpear.Throw(floor, along, spear, null);
                }
            }

            yield return new WaitForSeconds(spear.flightTime);

            if (motor == null)
            {
                yield break;
            }

            at = motor.transform.position;

            if (health != null)
            {
                health.TakeDamage(stuckDamage, at);
            }

            // Toward the hall's centre line; from on it, either way.
            float offset = Vector3.Dot(at - transform.position, across);
            float side = Mathf.Abs(offset) > 0.5f ? -Mathf.Sign(offset) : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            motor.AddImpulse(across * (side * recoverPush));

            paceProgress = Progress(at);

            if (recoverGrace > 0f)
            {
                yield return new WaitForSeconds(recoverGrace);
            }

            if (motor != null)
            {
                paceProgress = Mathf.Max(paceProgress, Progress(motor.transform.position));
            }

            caught = false;
        }

        /// <summary>
        /// The boss's attacks down the hall, in order and round again, until
        /// the room is placed.
        /// </summary>
        private IEnumerator RunAttacks()
        {
            if (runAttacks.Count == 0)
            {
                yield break;
            }

            yield return new WaitForSeconds(firstRunAttackDelay);

            for (int next = 0; room == null && motor != null && fighter != null; next = (next + 1) % runAttacks.Count)
            {
                AttackPattern attack = runAttacks[next];

                if (attack == null || !attack.CanPerform(fighter))
                {
                    // Every one skipped would otherwise spin here forever.
                    yield return null;
                    continue;
                }

                // Run on the boss, so it ends with it.
                yield return fighter.StartCoroutine(attack.Perform(fighter));
                yield return new WaitForSeconds(attack.DelayAfter);
            }
        }

        /// <summary>
        /// Lays segments until the hall reaches <see cref="buildAhead"/> past
        /// <paramref name="player"/>, or, once <paramref name="finish"/>, the
        /// boss room on the end instead; and removes segments left far behind.
        /// </summary>
        private void LayHall(float player, bool finish)
        {
            // With no length known, the room simply goes on the end.
            while (room == null && (chainEnd < player + buildAhead || segmentStep <= 0f))
            {
                if (finish)
                {
                    PlaceRoom();
                    break;
                }

                if (segmentStep <= 0f)
                {
                    break;
                }

                laidCount++;

                GameObject source = hallSegment != null ? hallSegment : chainStart.gameObject;
                GameObject piece = Instantiate(source, chainStart.position + along * (segmentStep * laidCount), chainStart.rotation);
                piece.name = source.name;

                chainEnd += segmentStep;
                laid.Enqueue(new Laid { piece = piece, end = chainEnd });
            }

            // The room is the player's way on; once it is down, nothing
            // behind is in view long enough to matter.
            while (room == null && laid.Count > 0 && laid.Peek().end < player - keepBehind)
            {
                Destroy(laid.Dequeue().piece);
            }
        }

        /// <summary>
        /// The length of a hall piece, for when no Segment Length is set:
        /// the first piece's, or, if Chain Start has nothing to measure, a
        /// copy of the hall segment's. 0, and a warning, if neither will do.
        /// </summary>
        private float MeasureSegment(Vector2 first)
        {
            float length = first.y - first.x;

            if (length < 1f && hallSegment != null)
            {
                GameObject probe = Instantiate(hallSegment, chainStart.position, chainStart.rotation);
                Vector2 extent = Extent(probe, along);
                DestroyImmediate(probe);
                length = extent.y - extent.x;
            }

            if (length < 1f)
            {
                Debug.LogWarning($"[SpearHallIntro] {name} can't tell how long a hall piece is, so it lays none. Set Segment Length, or put a hall piece in Chain Start.", this);
                return 0f;
            }

            Debug.Log($"[SpearHallIntro] Measured the hall piece as {length:0.##} long. Set Segment Length if the pieces leave gaps or overlap.", this);
            return length;
        }

        private void PlaceRoom()
        {
            Quaternion facing = Quaternion.LookRotation(along, Vector3.up);
            room = Instantiate(finaleRoom, chainStart.position, facing * finaleRoom.transform.rotation);
            room.name = finaleRoom.name;

            // In line with the hall piece, its near edge on the hall's end.
            Vector2 extent = Extent(room, along);
            room.transform.position = chainStart.position
                                    + along * (chainEnd - extent.x - Progress(chainStart.position))
                                    + facing * roomOffset;

            roomEntrance = chainEnd + roomOffset.z;
            roomFar = roomEntrance + (extent.y - extent.x);

            Transform point = FindChild(room.transform, breakPointName);
            breakAt = point != null ? Progress(point.position) : (roomEntrance + roomFar) * 0.5f;

            Transform floor = FindChild(room.transform, floorName);

            if (floor != null)
            {
                Vector3 centre = point != null ? point.position : OnCentreLine(breakAt, 0f);
                floorWeb = FloorWeb.Build(floor, centre, web);
            }
            else
            {
                Debug.LogWarning($"[SpearHallIntro] {finaleRoom.name} has no child named \"{floorName}\" holding its floor, so the floor won't break.", this);
            }

            RoomPlaced?.Invoke(this, room);
        }

        /// <summary>
        /// How far behind (x) and ahead (y) of its pivot, down the hall, the
        /// renderers of <paramref name="piece"/> reach.
        /// </summary>
        private static Vector2 Extent(GameObject piece, Vector3 direction)
        {
            Vector3 pivot = piece.transform.position;
            float near = float.MaxValue;
            float far = float.MinValue;

            foreach (Renderer part in piece.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = part.bounds;

                // Every corner, so a turned box is measured right.
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));

                    float distance = Vector3.Dot(corner - pivot, direction);
                    near = Mathf.Min(near, distance);
                    far = Mathf.Max(far, distance);
                }
            }

            return near <= far ? new Vector2(near, far) : Vector2.zero;
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (string.IsNullOrEmpty(childName))
            {
                return null;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Throws however many spears are due this frame at <paramref name="rate"/>
        /// a second, carrying the fraction of one over in <paramref name="owed"/>.
        /// Each lands between <paramref name="behind"/>.x and .y behind where
        /// the player will be, <paramref name="ahead"/> on from where they are
        /// now, anywhere across the hall.
        /// </summary>
        private void ThrowSpears(ref float owed, float rate, Vector2 behind, float ahead)
        {
            if (motor == null)
            {
                return;
            }

            owed += rate * Time.deltaTime;

            Vector3 player = motor.transform.position;
            float progress = Progress(player) + ahead;

            for (; owed >= 1f; owed--)
            {
                float back = UnityEngine.Random.Range(Mathf.Min(behind.x, behind.y), Mathf.Max(behind.x, behind.y));
                float side = UnityEngine.Random.Range(-0.5f, 0.5f) * hallWidth;
                Vector3 spot = OnCentreLine(progress - back, player.y) + across * side;

                if (FindFloor(spot, player.y, out Vector3 floor))
                {
                    HallSpear.Throw(floor, along, spear, health);
                }
            }
        }

        /// <summary>The floor under <paramref name="spot"/>, if there is some near the player's height.</summary>
        private bool FindFloor(Vector3 spot, float playerHeight, out Vector3 floor)
        {
            const float Reach = 3f;

            Vector3 from = new Vector3(spot.x, playerHeight + Reach, spot.z);

            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, Reach * 2f, floorMask, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.7f
                && hit.collider.GetComponentInParent<PlayerMotor>() == null)
            {
                floor = hit.point;
                return true;
            }

            floor = default;
            return false;
        }

        /// <summary>How far down the hall <paramref name="position"/> is from this trigger.</summary>
        private float Progress(Vector3 position)
        {
            return Vector3.Dot(position - transform.position, along);
        }

        /// <summary>The point on the hall's centre line <paramref name="progress"/> down it, at <paramref name="height"/>.</summary>
        private Vector3 OnCentreLine(float progress, float height)
        {
            Vector3 point = transform.position + along * progress;
            point.y = height;
            return point;
        }

        private void SetLocked(bool locked)
        {
            if (input != null)
            {
                input.Locked = locked;
            }
        }

        /// <summary>Gives the player their normal controls back, whatever state the intro left them in.</summary>
        private void ReleasePlayer()
        {
            if (played)
            {
                ChaseFrame.End();
            }

            if (motor != null)
            {
                motor.StopAutoRun();
            }

            if (input != null)
            {
                input.MoveOnly = false;
                input.Locked = false;
            }
        }

        /// <summary>Turns the boss's <see cref="KatanaGuard"/>, if it has one, on or off.</summary>
        private void SetGuarding(bool on)
        {
            KatanaGuard guard = fighter != null ? fighter.GetComponent<KatanaGuard>() : null;

            if (guard != null)
            {
                guard.Guarding = on;
            }
        }

        /// <summary>Says the lines from <paramref name="index"/> on, then calls <paramref name="done"/>.</summary>
        private void Say(int index, Action done)
        {
            if (index >= dialogue.Count)
            {
                done();
                return;
            }

            DialogueBox.Instance.Say(dialogue[index], () => Say(index + 1, done));
        }

        private static Vector3 Flat(Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z);
        }

        private void OnDrawGizmos()
        {
            if (chainStart == null)
            {
                return;
            }

            Vector3 down = Flat(chainStart.position - transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, down) * (hallWidth * 0.5f);

            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.8f);
            Gizmos.DrawLine(transform.position, chainStart.position);
            Gizmos.DrawLine(transform.position - side, transform.position + side);
            Gizmos.DrawLine(chainStart.position - side, chainStart.position + side);
            Gizmos.DrawRay(chainStart.position, down * 5f);

            foreach (Transform point in new[] { bossStartPoint, bossStandPoint })
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 0.75f);
                }
            }
        }
    }
}
