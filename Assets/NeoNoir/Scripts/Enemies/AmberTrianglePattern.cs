using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Amber the Axe's fight, all one attack: it leaps in an arc to the middle
    /// of the arena, slams down, splits three blades out round it into a
    /// <see cref="SwordTriangle"/> of lasers, then, from the middle of it,
    /// lifts, glistens and chops down toward the player, again and again
    /// until it is beaten, each strike splitting the floor and throwing
    /// debris out along it.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. To the middle: it appears <see cref="dropHeight"/> straight
    ///      above the enemy's <see cref="Enemy.ArenaCentre"/> and drops onto
    ///      it over <see cref="dropTime"/> (see <see cref="BladeDrop"/>),
    ///      while a <see cref="WarningCircle"/> of <see cref="landingRadius"/>
    ///      fills where it will land. It falls side on, turned
    ///      <see cref="leapSpin"/> edge first, to come down head first, its head sunk
    ///      <see cref="landingSink"/> into the floor, with a heavy impact --
    ///      a <see cref="HammerImpact"/> of dust and debris, a
    ///      <see cref="BlastSphere"/> and a jolt of the camera -- and
    ///      <see cref="landingDamage"/> to the player inside the circle. It
    ///      stays buried for <see cref="buriedTime"/>, then pulls itself out:
    ///      it strains, jerking, for <see cref="pullStrainTime"/>; yanks
    ///      itself straight up clear of the floor, still head down, over
    ///      <see cref="pullYankTime"/>; then turns over, upright, over
    ///      <see cref="pullFlipTime"/>, settling to float
    ///      <see cref="floatHeight"/> off the floor. Its colliders are off
    ///      meanwhile. Without an arena centre it drops where it is.
    ///   2. The triangle: a <see cref="SwordTriangle"/> round the floor under
    ///      it, turned at random, is made, and its lasers charge.
    ///   3. Strikes, from the moment its lasers are up:
    ///      <see cref="strikeInterval"/> apart. Each: its edge swings back and
    ///      up over <see cref="liftTime"/>, as it rises
    ///      <see cref="liftHeight"/>; it glistens for <see cref="warnTime"/>,
    ///      turning after the player until <see cref="lockTime"/> before the
    ///      end; then it chops down toward them over <see cref="swingTime"/>
    ///      and an <see cref="AmberSlam"/> line splits from under it, both
    ///      ahead and behind, so there is no safe spot at its back. It is
    ///      held down for <see cref="strikeHoldTime"/>, then swings on
    ///      through, back upright, over <see cref="recoverTime"/>.
    ///   4. After every <see cref="slashesPerSlam"/> strikes, the slam: it
    ///      flies up <see cref="slamFlyHeight"/> over
    ///      <see cref="slamRiseTime"/>, while a <see cref="WarningCircle"/>
    ///      of <see cref="slamRadius"/> fills under it; hangs there
    ///      <see cref="slamHangTime"/>; then drops over
    ///      <see cref="slamDropTime"/>, speeding up, spinning
    ///      <see cref="slamSpins"/> times edge first in the plane of its
    ///      blade, to land handle up, its head buried
    ///      <see cref="slamSink"/> in the floor, with the heavy impact and
    ///      <see cref="slamDamage"/> to the player inside the circle. It is stuck there <see cref="stuckTime"/>, while
    ///      <see cref="debrisPerSecond"/> <see cref="MaceDebris"/> chunks a
    ///      second drop from the sky onto spots inside the triangle; then it
    ///      pulls itself out as after the leap. Back to 3.
    ///
    /// It keeps its own facing for the whole fight, the enemy's turning
    /// switched off: between moves it turns blade on to the player, edge
    /// toward them, at <see cref="idleTurnSpeed"/>, rather than flat on.
    ///
    /// The attack never ends by itself: strikes and slams go on until it dies,
    /// and the triangle, its owner gone, shrinks away. So give the enemy only
    /// this pattern.
    ///
    /// It swings about a point <see cref="gripAbovePivot"/> up from its
    /// model's pivot. Its art is posed tip (or head) down local -Y, flat
    /// facing local +Z, as the player's blades are; it turns its edge to
    /// lead each strike. Its Rigidbody, if it has one, is kinematic
    /// throughout.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Blade Attacks/Amber Triangle", fileName = "AmberTriangle")]
    public sealed class AmberTrianglePattern : AttackPattern
    {
        [Header("Drop to the middle")]
        [SerializeField]
        [Tooltip("How far above where it lands it appears, to drop straight down.")]
        [FormerlySerializedAs("leapHeight")]
        [Min(0f)]
        private float dropHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. It gathers speed all the way down.")]
        [FormerlySerializedAs("leapTime")]
        [Min(0.01f)]
        private float dropTime = 0.9f;

        [SerializeField]
        [Tooltip("Degrees it is turned edge first from side on as it drops, and turns back over pulling out. 0 keeps it upright.")]
        private float leapSpin = 360f;

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
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 0.8f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.4f;

        [SerializeField]
        [Tooltip("Radius of the warning circle where it lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 4f;

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as it lands.")]
        [Min(0f)]
        private float landingDamage = 25f;

        [SerializeField]
        [Tooltip("How far its head sinks into the floor as it lands.")]
        [Min(0f)]
        private float landingSink = 0.3f;

        [SerializeField]
        [Tooltip("Seconds it stays buried head first before pulling itself out.")]
        [Min(0f)]
        private float buriedTime = 0.35f;

        [Header("Pulling out")]
        [SerializeField]
        [Tooltip("Seconds it strains, jerking, before it comes free.")]
        [Min(0f)]
        private float pullStrainTime = 0.6f;

        [SerializeField]
        [Tooltip("Jerks a second while straining.")]
        [Min(0f)]
        private float pullStrainRate = 5f;

        [SerializeField]
        [Tooltip("How far each jerk lifts it, at the height of the strain.")]
        [Min(0f)]
        private float pullStrainLift = 0.15f;

        [SerializeField]
        [Tooltip("Degrees it rocks either way while straining, at the height of the strain.")]
        [Min(0f)]
        private float pullStrainRock = 6f;

        [SerializeField]
        [Tooltip("Seconds it takes to yank itself straight up clear of the floor, still upside down.")]
        [Min(0.01f)]
        private float pullYankTime = 0.18f;

        [SerializeField]
        [Tooltip("Seconds it then takes to turn over, upright, settling to float.")]
        [Min(0.01f)]
        private float pullFlipTime = 0.45f;

        [SerializeField]
        [Tooltip("Gap between the floor and the lowest point of its art while it floats, after the leap.")]
        [Min(0f)]
        private float floatHeight = 1f;

        [Header("Heavy impact")]
        [SerializeField]
        [Tooltip("Radius of the explosion sphere a heavy landing bursts into. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 3.5f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.12f,
            fadeTime = 0.35f,
            colour = new Color(3f, 1.5f, 0.25f, 1f),
            outlineWidth = 0.15f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Warnings")]
        [SerializeField]
        [Tooltip("Colour of every warning in the attack: the debris circles and the triangle's charge lines. Overrides the colours set further down.")]
        private Color warningColour = new Color(1f, 0.65f, 0.12f);

        [SerializeField]
        [Tooltip("Brightness of the triangle's charge lines. Above 1 blooms.")]
        [Min(0f)]
        private float warningLineIntensity = 2.5f;

        [SerializeField]
        [Tooltip("Least height above the floor every warning, strike line and ring is drawn at, to clear a rug or carpet.")]
        [Range(0f, 1f)]
        private float warningHeight = 0.12f;

        [Header("Triangle")]
        [SerializeField] private SwordTriangle.Settings triangle = new SwordTriangle.Settings();

        [Header("Strikes")]
        [SerializeField]
        [Tooltip("Seconds from one strike landing to the next. Never shorter than a strike takes.")]
        [Min(0f)]
        private float strikeInterval = 2f;

        [SerializeField]
        [Tooltip("How far up from its pivot, toward the handle, the point it swings about is.")]
        private float gripAbovePivot = 2f;

        [SerializeField]
        [Tooltip("On for art built head up, local +Y, as Amber's axe is: it rests head up and strikes with the head. Off for art built point down, as the other blades are.")]
        private bool headUp = true;

        [SerializeField]
        [Tooltip("Seconds its edge takes to swing back and up.")]
        [Min(0.01f)]
        private float liftTime = 0.35f;

        [SerializeField]
        [Tooltip("How far it rises as it lifts.")]
        private float liftHeight = 2f;

        [SerializeField]
        [Tooltip("Degrees its head leans back past straight up as the glisten starts, and as it ends.")]
        private Vector2 windup = new Vector2(15f, 35f);

        [SerializeField]
        [Tooltip("Seconds it glistens, raised, before it strikes.")]
        [Min(0f)]
        private float warnTime = 1f;

        [SerializeField]
        [Tooltip("Seconds before it strikes that it stops turning after the player.")]
        [Min(0f)]
        private float lockTime = 0.3f;

        [SerializeField]
        [Tooltip("Degrees per second its aim turns after the player until the lock.")]
        [Min(0f)]
        private float trackTurnSpeed = 540f;

        [SerializeField]
        [Tooltip("Degrees per second it turns to keep its blade edge on to the player between moves.")]
        [Min(0f)]
        private float idleTurnSpeed = 360f;

        [SerializeField]
        [Tooltip("Seconds from raised to struck.")]
        [Min(0.01f)]
        private float swingTime = 0.12f;

        [SerializeField]
        [Tooltip("Degrees from straight up its head ends at, toward the player. Past 90 points it into the floor.")]
        private float strikeAngle = 110f;

        [SerializeField]
        [Tooltip("How far below where it started the point it swings about comes down as it strikes.")]
        private float strikeDrop = 1f;

        [SerializeField]
        [Tooltip("How far toward the player the point it swings about comes as it strikes.")]
        private float strikeReach = 2f;

        [SerializeField]
        [Tooltip("Seconds it is held down after each strike.")]
        [Min(0f)]
        private float strikeHoldTime = 0.2f;

        [SerializeField]
        [Tooltip("Seconds it takes to swing on through, back upright.")]
        [Min(0.01f)]
        private float recoverTime = 0.3f;

        [SerializeField] private BladeGlisten.Settings glisten = new BladeGlisten.Settings();

        [SerializeField]
        private AmberSlam.Settings slash = new AmberSlam.Settings
        {
            damage = 20f,
            length = 30f,
            width = 5f,
            waveTime = 0.15f,
            holdTime = 0.3f,
            fadeTime = 0.4f,
            frontTaper = 3f,
            farWidth = 1f,
            lineIntensity = 2f,
            wallHeight = 4f,
            wallWidth = 0.4f,
            wallIntensity = 4f,
            arcRadius = 4.5f,
            arcWidth = 1.5f,
            arcTime = 0.2f,
        };

        [SerializeField]
        [Tooltip("Colour of the line, when no Slash Material is set.")]
        private Color slashColour = new Color(1f, 0.7f, 0.2f);

        [SerializeField]
        [Tooltip("Material for the line, e.g. NeoNoir/Flat. Empty uses the Slash Colour.")]
        private Material slashMaterial;

        [SerializeField]
        [Tooltip("How far each strike jolts the camera. 0 for none.")]
        [Min(0f)]
        private float strikeShake = 0.6f;

        [SerializeField]
        [Min(0.01f)]
        private float strikeShakeDuration = 0.3f;

        [Header("Slam")]
        [SerializeField]
        [Tooltip("Strikes between one slam and the next.")]
        [Min(1)]
        private int slashesPerSlam = 3;

        [SerializeField]
        [Tooltip("How far it flies up before the slam.")]
        [Min(0f)]
        private float slamFlyHeight = 6f;

        [SerializeField]
        [Tooltip("Seconds the flight up takes. It slows to the top.")]
        [Min(0.01f)]
        private float slamRiseTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds it hangs at the top before dropping.")]
        [Min(0f)]
        private float slamHangTime = 0.3f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. It speeds up all the way down.")]
        [Min(0.01f)]
        private float slamDropTime = 0.5f;

        [SerializeField]
        [Tooltip("Whole turns it spins on the way down, edge first in the plane of its blade. Head-up art turns half a turn more, to land head down.")]
        [Min(0)]
        private int slamSpins = 2;

        [SerializeField]
        [Tooltip("Radius of the warning circle under it, and of the slam's damage.")]
        [Min(0f)]
        private float slamRadius = 6f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as it lands.")]
        [Min(0f)]
        private float slamDamage = 30f;

        [SerializeField]
        [Tooltip("How far its head sinks into the floor.")]
        [Min(0f)]
        private float slamSink = 0.4f;

        [SerializeField]
        [Tooltip("Seconds it stays stuck in the floor, while debris rains, before pulling itself out.")]
        [Min(0f)]
        private float stuckTime = 5f;

        [SerializeField]
        [Tooltip("Chunks a second that drop inside the triangle while it is stuck.")]
        [Min(0f)]
        private float debrisPerSecond = 5f;

        [SerializeField]
        [Tooltip("How high above the floor each chunk drops from.")]
        [Min(0f)]
        private float debrisDropHeight = 25f;

        [SerializeField]
        [Tooltip("The chunks that rain while it is stuck.")]
        private MaceDebris.Settings debris = new MaceDebris.Settings
        {
            damage = 10f,
            flightTime = new Vector2(0.8f, 1.2f),
            arcHeight = 6f,
        };

        // Made from Slash Colour when no Slash Material is set; shared by every boss.
        [NonSerialized] private Material runtimeSlashMaterial;

        public override IEnumerator Perform(Enemy enemy)
        {
            enemy.VelocityOverride = Vector3.zero;

            // Posed by hand for the whole fight: the enemy's own turning
            // would point its flat at the player, and right it while it is
            // stuck in the floor.
            enemy.PhysicsDriven = true;

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

            foreach (object frame in Leap(enemy, enemy.ArenaCentre ?? root.position))
            {
                yield return frame;
            }

            // Copies, with the warnings recoloured and raised: the asset is
            // shared and must not change.
            SwordTriangle.Settings shownTriangle = Copy(triangle);
            shownTriangle.warningColour = Glow(warningColour, warningLineIntensity);
            shownTriangle.debris = Warned(triangle.debris);
            shownTriangle.stabImpact = Lifted(triangle.stabImpact);

            AmberSlam.Settings shownSlash = Copy(slash);
            shownSlash.floorOffset = Mathf.Max(slash.floorOffset, warningHeight);

            MaceDebris.Settings shownDebris = Warned(debris);

            SwordTriangle swords = SwordTriangle.Create(enemy, FindFloor(root.position),
                                                        UnityEngine.Random.Range(0f, 360f), shownTriangle);

            while (swords != null && !swords.LasersOn)
            {
                FaceBladeOn(enemy);
                yield return null;
            }

            Strikes strikes = new Strikes(this, enemy, shownSlash);

            // Until it dies, which stops this from outside.
            while (true)
            {
                for (int i = 0; i < slashesPerSlam; i++)
                {
                    foreach (object frame in strikes.Strike())
                    {
                        yield return frame;
                    }

                    float rest = strikeInterval - (strikeHoldTime + recoverTime + liftTime + warnTime + swingTime);

                    // At least a frame, turning blade on to the player.
                    float waited = 0f;

                    do
                    {
                        FaceBladeOn(enemy);
                        yield return null;
                        waited += Time.deltaTime;
                    }
                    while (waited < rest);
                }

                strikes.End();

                foreach (object frame in Slam(enemy, swords, shownDebris))
                {
                    yield return frame;
                }
            }
        }

        /// <summary>
        /// Flies up, hangs, then drops spinning edge first in the plane of
        /// its blade, to land handle up, head buried, as a circle warns
        /// where; stays stuck while debris rains inside the triangle; then
        /// pulls itself out.
        /// </summary>
        private IEnumerable Slam(Enemy enemy, SwordTriangle swords, MaceDebris.Settings rain)
        {
            Transform root = enemy.transform;
            Transform model = enemy.Model;


            Vector3 hover = root.position;
            Quaternion upright = model.rotation;
            Vector3 floor = FindFloor(hover);
            Vector3 top = hover + Vector3.up * slamFlyHeight;

            // Spun about the blade's flat, so its edge leads round. Head-up
            // art spins half a turn more, to land head down.
            Vector3 axis = model.forward;
            float spin = slamSpins * 360f + (headUp ? 180f : 0f);
            Quaternion landed = Quaternion.AngleAxis(spin, axis) * upright;
            Vector3 stuck = Buried(root, model, floor, landed, floor.y - slamSink);

            enemy.FacingOverride = Flat(axis).sqrMagnitude > 0.0001f ? Flat(axis).normalized : (Vector3?)null;

            WarningCircle warning = WarningCircle.Create(floor + Vector3.up * warningHeight, slamRadius,
                                                         landingOutline, null, warningColour);
            float warnTotal = slamRiseTime + slamHangTime + slamDropTime;
            float clock = 0f;

            // Up, slowing to the top.
            for (float t = 0f; t < slamRiseTime; t += Time.deltaTime)
            {
                float eased = 1f - Square(1f - t / slamRiseTime);
                root.position = Vector3.Lerp(hover, top, eased);
                warning.Fill = (clock + t) / warnTotal;
                yield return null;
            }

            clock += slamRiseTime;
            root.position = top;

            for (float t = 0f; t < slamHangTime; t += Time.deltaTime)
            {
                warning.Fill = (clock + t) / warnTotal;
                yield return null;
            }

            clock += slamHangTime;

            // Down, speeding up, spinning all the way.
            for (float t = 0f; t < slamDropTime; t += Time.deltaTime)
            {
                float u = t / slamDropTime;
                root.position = Vector3.Lerp(top, stuck, u * u);
                model.rotation = Quaternion.AngleAxis(spin * u, axis) * upright;
                warning.Fill = (clock + t) / warnTotal;
                yield return null;
            }

            root.position = stuck;
            model.rotation = landed;
            Destroy(warning.gameObject);

            HeavyImpact(floor);

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;
            Hurt(player, floor, slamRadius, slamDamage);

            // Stuck, while debris rains inside the triangle.
            float stuckFor = 0f;
            float nextDrop = 0f;

            while (stuckFor < stuckTime)
            {
                while (debrisPerSecond > 0f && nextDrop <= stuckFor)
                {
                    DropDebris(swords, floor, rain, player);
                    nextDrop += 1f / debrisPerSecond;
                }

                yield return null;
                stuckFor += Time.deltaTime;
            }

            foreach (object frame in PullOut(root, model, upright, axis, spin, floor))
            {
                yield return frame;
            }

            enemy.FacingOverride = null;
        }

        /// <summary>
        /// Out of the floor, from turned <paramref name="angle"/> about
        /// <paramref name="axis"/> off <paramref name="upright"/>: it strains,
        /// jerking, then yanks itself straight up clear of the floor, still
        /// as it was, and only then turns on round to upright as it settles
        /// <see cref="floatHeight"/> off the floor.
        /// </summary>
        private IEnumerable PullOut(Transform root, Transform model, Quaternion upright, Vector3 axis, float angle, Vector3 floor)
        {
            Vector3 stuck = root.position;
            Quaternion buried = Quaternion.AngleAxis(angle, axis) * upright;

            // On round the same way to the next whole turn: upright again.
            float rest = Mathf.Ceil(angle / 360f - 0.0001f) * 360f;
            Vector3 clear = Buried(root, model, floor, buried, floor.y + floatHeight);
            Vector3 hover = Buried(root, model, floor, upright, floor.y + floatHeight);

            // Straining: short jerks up and back, rocking, harder as it goes.
            for (float t = 0f; t < pullStrainTime; t += Time.deltaTime)
            {
                float build = t / pullStrainTime;
                float jerk = Mathf.Abs(Mathf.Sin(t * pullStrainRate * Mathf.PI)) * pullStrainLift * build;
                float rock = Mathf.Sin(t * pullStrainRate * 2f * Mathf.PI) * pullStrainRock * build;

                root.position = stuck + Vector3.up * jerk;
                model.rotation = Quaternion.AngleAxis(angle + rock, axis) * upright;
                yield return null;
            }

            model.rotation = buried;

            // Yanked free, fast off the mark.
            for (float t = 0f; t < pullYankTime; t += Time.deltaTime)
            {
                float eased = 1f - Square(1f - t / pullYankTime);
                root.position = Vector3.Lerp(stuck, clear, eased);
                yield return null;
            }

            root.position = clear;

            // Over, upright, settling to float.
            for (float t = 0f; t < pullFlipTime; t += Time.deltaTime)
            {
                float eased = Smooth(t / pullFlipTime);
                root.position = Vector3.Lerp(clear, hover, eased);
                model.rotation = Quaternion.AngleAxis(Mathf.Lerp(angle, rest, eased), axis) * upright;
                yield return null;
            }

            root.position = hover;
            model.rotation = upright;
        }

        /// <summary>Turns it a frame's worth toward standing blade on to the player, edge toward them.</summary>
        private void FaceBladeOn(Enemy enemy)
        {
            if (!enemy.HasTarget)
            {
                return;
            }

            Transform model = enemy.Model;
            Vector3 toPlayer = Flat(enemy.TargetPoint - model.position);

            if (toPlayer.sqrMagnitude < 0.0001f)
            {
                return;
            }

            model.rotation = Quaternion.RotateTowards(model.rotation, BladeOn(toPlayer.normalized),
                                                      idleTurnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Upright, its flat square to <paramref name="toward"/>, so its edge
        /// points along it: the pose a strike rests in and lifts from.
        /// </summary>
        private static Quaternion BladeOn(Vector3 toward)
        {
            return Quaternion.LookRotation(Vector3.Cross(Vector3.up, toward), Vector3.up);
        }

        /// <summary>The heavy landing: dust and debris, an explosion sphere, and a hard jolt.</summary>
        private void HeavyImpact(Vector3 floor)
        {
            if (landingMaterial != null)
            {
                HammerImpact.Create(floor, Lifted(landing), landingMaterial);
            }

            if (blastRadius > 0f)
            {
                BlastSphere.Create(floor, blastRadius, blast);
            }

            if (landingShake > 0f)
            {
                TopDownCamera.Shake(landingShake, landingShakeDuration);
            }
        }

        /// <summary>Hurts <paramref name="player"/> if within <paramref name="radius"/> of <paramref name="centre"/>, measured flat.</summary>
        private static void Hurt(PlayerHealth player, Vector3 centre, float radius, float damage)
        {
            if (player == null || player.IsDead || damage <= 0f)
            {
                return;
            }

            Vector3 offset = Flat(player.transform.position - centre);

            if (offset.sqrMagnitude <= radius * radius)
            {
                player.TakeDamage(damage, centre);
            }
        }

        /// <summary>
        /// One chunk dropped from the sky onto a spot inside the triangle, or,
        /// with the triangle gone, within the slam's radius of
        /// <paramref name="centre"/>.
        /// </summary>
        private void DropDebris(SwordTriangle swords, Vector3 centre, MaceDebris.Settings rain, PlayerHealth player)
        {
            Vector3 spot;

            if (swords != null)
            {
                spot = swords.RandomPoint();
            }
            else
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * slamRadius;
                spot = centre + new Vector3(offset.x, 0f, offset.y);
            }

            Vector3 floor = FindFloor(spot);
            Vector2 flight = rain.flightTime;
            float seconds = UnityEngine.Random.Range(Mathf.Min(flight.x, flight.y), Mathf.Max(flight.x, flight.y));

            MaceDebris.Throw(floor + Vector3.up * debrisDropHeight, floor, seconds, rain, player);
        }

        /// <summary>
        /// A drop straight down onto <paramref name="centre"/>, to land head
        /// first in the floor there with a heavy impact; then it pulls
        /// itself out and floats upright.
        /// </summary>
        private IEnumerable Leap(Enemy enemy, Vector3 centre)
        {
            Transform root = enemy.transform;
            Transform model = enemy.Model;

            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            // Measured with its colliders off, so the ray never finds it.
            Vector3 floor = FindFloor(centre);
            Vector3 start = root.position;

            Vector3 heading = Flat(floor - start);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Flat(model.forward);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;
            Vector3 tumbleAxis = Vector3.Cross(Vector3.up, heading);

            // Side on, its flat square to the way it came, posed head first
            // for the whole drop, and the pull-out turns it over the same
            // way. Head-up art turns half a turn more, to come down head
            // first.
            Quaternion sideOn = Quaternion.LookRotation(tumbleAxis, Vector3.up);
            float spin = leapSpin + (headUp ? 180f : 0f);
            Quaternion headFirst = Quaternion.AngleAxis(spin, tumbleAxis) * sideOn;
            Vector3 buried = Buried(root, model, floor, headFirst, floor.y - landingSink);

            WarningCircle warning = WarningCircle.Create(floor + Vector3.up * warningHeight, landingRadius,
                                                         landingOutline, null, warningColour);
            enemy.FacingOverride = heading;
            model.rotation = headFirst;

            foreach (object frame in BladeDrop.Fall(root, buried, dropHeight, dropTime, warning))
            {
                yield return frame;
            }

            Destroy(warning.gameObject);

            HeavyImpact(floor);
            Hurt(enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null, floor, landingRadius, landingDamage);

            if (buriedTime > 0f)
            {
                yield return new WaitForSeconds(buriedTime);
            }

            foreach (object frame in PullOut(root, model, sideOn, tumbleAxis, spin, floor))
            {
                yield return frame;
            }

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            enemy.FacingOverride = null;
        }

        /// <summary>
        /// Where the root goes for the model, turned <paramref name="pose"/>,
        /// to reach down exactly to <paramref name="bottom"/> above
        /// <paramref name="over"/>. Measured from its renderers by trying the
        /// pose on and putting it back, all within the frame.
        /// </summary>
        internal static Vector3 Buried(Transform root, Transform model, Vector3 over, Quaternion pose, float bottom)
        {
            Vector3 position = root.position;
            Quaternion rotation = model.rotation;

            root.position = over;
            model.rotation = pose;

            float lowest = float.MaxValue;

            foreach (Renderer part in model.GetComponentsInChildren<Renderer>())
            {
                if (part.enabled && !(part is ParticleSystemRenderer) && !(part is LineRenderer))
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            root.position = position;
            model.rotation = rotation;

            return lowest < float.MaxValue ? over + Vector3.up * (bottom - lowest) : over;
        }

        /// <summary>
        /// Its strikes: everything they need to remember, since the asset
        /// itself is shared and holds nothing. It is posed by its model
        /// every frame, after the enemy's own turn.
        /// </summary>
        private sealed class Strikes
        {
            private readonly AmberTrianglePattern pattern;
            private readonly Enemy enemy;
            private readonly Transform model;
            private readonly AmberSlam.Settings slash;
            private BladeGlisten shine;
            private Vector3 aim;
            private Vector3 rest;
            private float clock;

            public Strikes(AmberTrianglePattern pattern, Enemy enemy, AmberSlam.Settings slash)
            {
                this.pattern = pattern;
                this.enemy = enemy;
                this.slash = slash;
                model = enemy.Model;
                // Its flat no longer points at the player, so aim from where they are.
                Vector3 toPlayer = enemy.HasTarget ? Flat(enemy.TargetPoint - model.position) : Vector3.zero;
                aim = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;
            }

            private bool Stopped => enemy == null || enemy.Health.IsDead;

            public IEnumerable Strike()
            {
                rest = model.position;
                Quaternion fromRotation = model.rotation;
                // Head-up art rests head up, so it only leans back to lift;
                // point-down art swings its point round behind it and up.
                float restAngle = pattern.headUp ? 0f : 180f;
                float lean = pattern.headUp ? -pattern.windup.x : 360f - pattern.windup.x;

                // Point back and up, rising, turning its edge to the player.
                float elapsed = 0f;

                while (elapsed < pattern.liftTime)
                {
                    Track();
                    float t = Smooth(elapsed / pattern.liftTime);
                    Pose to = Raised(Mathf.Lerp(restAngle, lean, t), 0f);
                    model.SetPositionAndRotation(Vector3.Lerp(rest, to.position, t),
                                                 Quaternion.Slerp(fromRotation, to.rotation, t));

                    yield return null;
                    elapsed += Time.deltaTime;

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                // Glistening, following the player until the lock.
                Warn(pattern.warnTime);
                elapsed = 0f;

                while (elapsed < pattern.warnTime)
                {
                    if (elapsed < pattern.warnTime - pattern.lockTime)
                    {
                        Track();
                    }

                    float charge = elapsed / pattern.warnTime;
                    Place(Raised(-Mathf.Lerp(pattern.windup.x, pattern.windup.y, charge), 0f));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                enemy.FacingOverride = aim;

                // Down, speeding up all the way.
                elapsed = 0f;

                while (elapsed < pattern.swingTime)
                {
                    float swing = Square(elapsed / pattern.swingTime);
                    Place(Raised(Mathf.Lerp(-pattern.windup.y, pattern.strikeAngle, swing), swing));

                    yield return null;
                    elapsed += Tick();
                }

                Place(Raised(pattern.strikeAngle, 1f));
                pattern.Strike(enemy, aim, Grip(0f).y, slash);

                elapsed = 0f;

                while (elapsed < pattern.strikeHoldTime)
                {
                    Place(Raised(pattern.strikeAngle, 1f));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                // On through, point down again, back upright where it began.
                Quaternion upright = BladeOn(aim);
                elapsed = 0f;

                while (elapsed < pattern.recoverTime)
                {
                    float t = Smooth(elapsed / pattern.recoverTime);
                    Pose to = Raised(Mathf.Lerp(pattern.strikeAngle, restAngle, t), 1f - t);
                    model.SetPositionAndRotation(Vector3.Lerp(to.position, rest, t),
                                                 Quaternion.Slerp(to.rotation, upright, t));

                    yield return null;
                    elapsed += Tick();

                    if (Stopped)
                    {
                        yield break;
                    }
                }

                model.SetPositionAndRotation(rest, upright);
            }

            public void End()
            {
                shine?.End();

                // An ended glisten's glow is gone; the next strike starts a fresh one.
                shine = null;

                if (enemy != null)
                {
                    enemy.FacingOverride = null;
                }
            }

            /// <summary>Starts the glisten building to a strike <paramref name="seconds"/> from now.</summary>
            private void Warn(float seconds)
            {
                if (shine == null)
                {
                    shine = BladeGlisten.Begin(model, pattern.glisten, seconds);
                }
                else
                {
                    shine.Restart(seconds);
                }

                clock = 0f;
            }

            /// <summary>Moves the glisten on a frame, and returns the frame's length.</summary>
            private float Tick()
            {
                float deltaTime = Time.deltaTime;
                clock += deltaTime;
                shine?.Tick(clock);
                return deltaTime;
            }

            private void Place(Pose pose)
            {
                model.SetPositionAndRotation(pose.position, pose.rotation);
            }

            /// <summary>Turns its aim toward the player.</summary>
            private void Track()
            {
                Vector3 toPlayer = enemy.HasTarget ? Flat(enemy.TargetPoint - rest) : Vector3.zero;

                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    Vector3 turned = Vector3.RotateTowards(aim, toPlayer.normalized,
                                                           pattern.trackTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
                    aim = Flat(turned).normalized;
                }

                enemy.FacingOverride = aim;
            }

            /// <summary>Where it swings about: raised at <paramref name="swing"/> 0, down and out toward the aim at 1.</summary>
            private Vector3 Grip(float swing)
            {
                Vector3 start = rest + Vector3.up * pattern.gripAbovePivot;

                return Vector3.Lerp(start + Vector3.up * pattern.liftHeight,
                                    start + Vector3.down * pattern.strikeDrop + aim * pattern.strikeReach,
                                    swing);
            }

            /// <summary>
            /// Its point <paramref name="angle"/> degrees from straight up
            /// toward the aim, its flat facing across the swing so its edge leads.
            /// </summary>
            private Pose Raised(float angle, float swing)
            {
                float radians = angle * Mathf.Deg2Rad;
                Vector3 tip = Vector3.up * Mathf.Cos(radians) + aim * Mathf.Sin(radians);
                Vector3 across = Vector3.Cross(Vector3.up, aim);

                // Built point down -Y, up +Y points back toward the hilt;
                // built head up, +Y is the head, toward the point.
                Quaternion rotation = Quaternion.LookRotation(across, pattern.headUp ? tip : -tip);
                return new Pose(Grip(swing) + tip * pattern.gripAbovePivot, rotation);
            }
        }

        private void Strike(Enemy enemy, Vector3 aim, float shoulder, AmberSlam.Settings slash)
        {
            Vector3 floor = FindFloor(enemy.Model.position);
            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            AmberSlam.Create(floor, aim, floor.y + 1f, shoulder - floor.y, -windup.y, strikeAngle,
                             slash, SlashMaterial(), player);

            // And out behind it, so there is no safe spot at its back; with
            // no arc, which the one ahead already draws.
            AmberSlam.Settings behind = Copy(slash);
            behind.arcRadius = 0f;
            AmberSlam.Create(floor, -aim, floor.y + 1f, shoulder - floor.y, -windup.y, strikeAngle,
                             behind, SlashMaterial(), player);

            if (strikeShake > 0f)
            {
                TopDownCamera.Shake(strikeShake, strikeShakeDuration);
            }
        }

        /// <summary>A copy of <paramref name="source"/> in the warning colour, its circle and landing ring raised.</summary>
        private MaceDebris.Settings Warned(MaceDebris.Settings source)
        {
            MaceDebris.Settings copy = Copy(source);
            copy.warningColour = warningColour;
            copy.warningHeight = Mathf.Max(source.warningHeight, warningHeight);
            copy.landingBurst = Lifted(source.landingBurst);
            return copy;
        }

        /// <summary>A copy of <paramref name="source"/> with its ring at least <see cref="warningHeight"/> up.</summary>
        private HammerImpact.Settings Lifted(HammerImpact.Settings source)
        {
            HammerImpact.Settings copy = Copy(source);
            copy.floorOffset = Mathf.Max(source.floorOffset, warningHeight);
            return copy;
        }

        private static Color Glow(Color colour, float intensity)
        {
            Color glow = colour * intensity;
            glow.a = 1f;
            return glow;
        }

        private static readonly MethodInfo CloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>A shallow copy of a settings block, safe to change without touching the asset's.</summary>
        private static T Copy<T>(T source) where T : class
        {
            return (T)CloneMethod.Invoke(source, null);
        }

        private Material SlashMaterial()
        {
            if (slashMaterial != null)
            {
                return slashMaterial;
            }

            if (runtimeSlashMaterial == null && FlatMaterial.DoubleSided != null)
            {
                runtimeSlashMaterial = new Material(FlatMaterial.DoubleSided) { name = "Runtime Amber Triangle Slash" };
            }

            if (runtimeSlashMaterial != null)
            {
                runtimeSlashMaterial.SetColor(FlatMaterial.BaseColorId, slashColour);
            }

            return runtimeSlashMaterial;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            // From well above, so a raised floor still counts.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Square(float t) => t * t;

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
            groundMask = LayerMask.GetMask("Default");
            triangle.groundMask = LayerMask.GetMask("Default");
            triangle.missile.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
