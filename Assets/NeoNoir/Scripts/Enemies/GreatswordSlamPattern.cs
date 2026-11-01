using System;
using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss's greatsword: it leaves the boss's orbit glowing, with a
    /// gleam, rises overhead and turns to follow the player, stops following
    /// them a moment before it strikes, then slams down, splitting a thick
    /// line through the floor that way.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The sword is borrowed from the boss's <see cref="BossBlades"/>
    ///      -- the one spawned from <see cref="bladePrefab"/> -- and starts
    ///      to glow <see cref="colour"/>, with a <see cref="BladeGleam"/>
    ///      flashing at its tip. It keeps glistening -- gleams twinkling at
    ///      the tip, ever faster and bigger, its glow brightening -- until a
    ///      last, biggest gleam peaks just as it starts down.
    ///   2. Over <see cref="riseTime"/> it rises, tip up, to a grip
    ///      <see cref="gripHeight"/> above the boss.
    ///   3. It hangs there for <see cref="aimTime"/>, leaning further back and
    ///      trembling harder, its edge turning to follow the player, at up to
    ///      <see cref="trackTurnSpeed"/>, until <see cref="lockTime"/> before
    ///      the end; then it holds that aim.
    ///   4. Over <see cref="swingTime"/> it swings down along the held aim,
    ///      speeding up as it goes, and an <see cref="AmberSlam"/> line splits
    ///      from the boss's feet that way, hurting the player once if its
    ///      front passes them.
    ///   5. It stays in the floor for <see cref="holdTime"/>, then stops
    ///      glowing and goes back into the orbit.
    /// The sword follows the boss throughout, so it works with the boss on
    /// the move, as in the hall run of <see cref="SpearHallIntro"/>.
    ///
    /// The sword is posed the way <see cref="BossBlades"/> poses it: built
    /// flat side facing local +Z, tip down local -Y, with the entry's
    /// Rotation Offset for one built otherwise. Its flat faces across the
    /// swing, so it comes down edge first.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Greatsword Slam", fileName = "GreatswordSlam")]
    public sealed class GreatswordSlamPattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("The greatsword's prefab, as listed in the boss's Boss Blades. The blade spawned from it is the one swung.")]
        private GameObject bladePrefab;

        [SerializeField]
        [Tooltip("Turn the blade half round its length for this attack, so its edge rather than its back leads the strike.")]
        private bool flipEdge = true;

        [SerializeField]
        [Tooltip("Colour of the glow, the gleam and, with no Slash Material, the line.")]
        private Color colour = new Color(0.6f, 0.2f, 1f);

        [Header("Glow")]
        [SerializeField]
        [Tooltip("Brightness of the sword's glow. Above 1 blooms.")]
        [Min(0f)]
        private float glowIntensity = 2.5f;

        [SerializeField]
        [Tooltip("Seconds the glow takes to come on, and to go once the sword heads back.")]
        [Min(0f)]
        private float glowFadeTime = 0.2f;

        [SerializeField] private BladeGleam.Settings gleam = new BladeGleam.Settings();

        [SerializeField]
        [Tooltip("Nudges the gleam from the blade's tip, in the sword's own space. The tip is found from the model: the far end of its longest side.")]
        private Vector3 gleamOffset;

        [SerializeField]
        [Tooltip("What the glow's brightness is multiplied by just before the strike, building up from 1 as the attack starts.")]
        [Min(0f)]
        private float glowPeak = 2f;

        [Header("Glisten")]
        [SerializeField]
        [Tooltip("Seconds between gleams as the attack starts, and just before the strike.")]
        private Vector2 glistenInterval = new Vector2(0.3f, 0.07f);

        [SerializeField]
        [Tooltip("Size of each gleam, as a multiple of the gleam's own, as the attack starts and just before the strike.")]
        private Vector2 glistenSize = new Vector2(0.5f, 1.1f);

        [SerializeField]
        [Tooltip("Size of the last gleam, timed to be fully open just as the sword starts down.")]
        [Min(0f)]
        private float finalGleamSize = 2.5f;

        [Header("Raise")]
        [SerializeField]
        [Tooltip("Seconds the sword takes to rise from the orbit to overhead.")]
        [Min(0.01f)]
        private float riseTime = 0.35f;

        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot: the point the sword swings about.")]
        private float gripHeight = 4f;

        [SerializeField]
        [Tooltip("Distance from the grip to the sword's pivot, toward the tip.")]
        private float gripToPivot = 1f;

        [SerializeField]
        [Tooltip("Degrees the tip leans back past upright as the aim starts, and as it ends.")]
        private Vector2 windup = new Vector2(10f, 35f);

        [SerializeField]
        [Tooltip("Distance the sword trembles by at the very end of the aim.")]
        [Min(0f)]
        private float tremble = 0.08f;

        [Header("Aim")]
        [SerializeField]
        [Tooltip("Seconds the sword hangs overhead before it comes down, lock included.")]
        [Min(0f)]
        private float aimTime = 1f;

        [SerializeField]
        [Tooltip("Seconds before it comes down that the sword stops following the player.")]
        [Min(0f)]
        private float lockTime = 0.3f;

        [SerializeField]
        [Tooltip("Degrees per second the aim turns after the player while following them.")]
        [Min(0f)]
        private float trackTurnSpeed = 540f;

        [Header("Swing")]
        [SerializeField]
        [Tooltip("Seconds from overhead to buried in the floor.")]
        [Min(0.01f)]
        private float swingTime = 0.1f;

        [SerializeField]
        [Tooltip("Degrees from straight up the tip ends at, toward the player. Past 90 points it into the floor.")]
        private float slamAngle = 115f;

        [SerializeField]
        [Tooltip("Height of the grip above the boss's pivot once the sword has come down.")]
        private float slamGripHeight = 1.5f;

        [SerializeField]
        [Tooltip("How far toward the player the grip comes down.")]
        private float slamReach = 1f;

        [SerializeField]
        [Tooltip("Seconds the sword stays in the floor before going back into the orbit.")]
        [Min(0f)]
        private float holdTime = 0.35f;

        [Header("Slash")]
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
        [Tooltip("Material for the line, e.g. NeoNoir/Flat in purple. Empty uses Colour.")]
        private Material slashMaterial;

        [SerializeField]
        [Tooltip("Layers the line is laid on. Without floor under the boss, it is laid at the boss's pivot.")]
        private LayerMask floorMask = 1;

        [SerializeField]
        [Tooltip("How far the slam jolts the camera. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.5f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.3f;

        // Made from Colour when no Slash Material is set; shared by every boss.
        [NonSerialized] private Material runtimeSlashMaterial;

        public override bool CanPerform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            return blades != null && blades.IndexOf(bladePrefab) >= 0;
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

            Transform sword = blades.Borrow(index);
            Quaternion modelOffset = blades.RotationOffset(index) * (flipEdge ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);

            Color glowColour = colour * glowIntensity;
            glowColour.a = colour.a;
            HammerGlow glow = HammerGlow.On(sword.gameObject, null, glowColour, glowFadeTime);
            Vector3 tip = BladeGleam.TipOf(sword) + gleamOffset;
            BladeGleam.Flash(sword, tip, colour, gleam);
            Glisten glisten = new Glisten(this, sword, glow, tip);

            Vector3 aim = Facing(enemy);
            Vector3 fromPosition = sword.position;
            Quaternion fromRotation = sword.rotation;
            float elapsed = 0f;

            // 1. Up out of the orbit, already turning to the player.
            while (elapsed < riseTime)
            {
                aim = Track(enemy, aim);
                Pose(aim, -windup.x, Grip(enemy, aim, 0f), modelOffset, out Vector3 position, out Quaternion rotation);

                float eased = Smooth(elapsed / riseTime);
                sword.SetPositionAndRotation(Vector3.Lerp(fromPosition, position, eased),
                                             Quaternion.Slerp(fromRotation, rotation, eased));
                glisten.Tick(elapsed);

                yield return null;
                elapsed += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    End(blades, index, glow);
                    yield break;
                }
            }

            // 2. Overhead, following the player until the lock.
            elapsed = 0f;

            while (elapsed < aimTime)
            {
                if (elapsed < aimTime - lockTime)
                {
                    aim = Track(enemy, aim);
                }

                float charge = aimTime > 0f ? elapsed / aimTime : 1f;
                Vector3 shake = UnityEngine.Random.insideUnitSphere * (tremble * charge * charge);

                Pose(aim, -Mathf.Lerp(windup.x, windup.y, charge), Grip(enemy, aim, 0f) + shake, modelOffset,
                     out Vector3 position, out Quaternion rotation);
                sword.SetPositionAndRotation(position, rotation);
                glisten.Tick(riseTime + elapsed);

                yield return null;
                elapsed += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    End(blades, index, glow);
                    yield break;
                }
            }

            // 3. Down, speeding up all the way.
            glisten.Tick(riseTime + aimTime);
            elapsed = 0f;

            while (elapsed < swingTime)
            {
                float swing = elapsed / swingTime;
                swing *= swing;

                Pose(aim, Mathf.Lerp(-windup.y, slamAngle, swing), Grip(enemy, aim, swing), modelOffset,
                     out Vector3 position, out Quaternion rotation);
                sword.SetPositionAndRotation(position, rotation);

                yield return null;
                elapsed += Time.deltaTime;
            }

            Slam(enemy, aim);

            // 4. Left in the floor a moment, still riding along with the boss.
            elapsed = 0f;

            do
            {
                Pose(aim, slamAngle, Grip(enemy, aim, 1f), modelOffset, out Vector3 position, out Quaternion rotation);
                sword.SetPositionAndRotation(position, rotation);

                yield return null;
                elapsed += Time.deltaTime;
            }
            while (elapsed < holdTime && !enemy.Health.IsDead);

            End(blades, index, glow);
        }

        /// <summary>
        /// The sword's glistening from the start of the attack to the strike:
        /// gleams twinkling along it, quicker and bigger as the strike nears,
        /// the glow brightening, and one last big gleam at the strike.
        /// </summary>
        private sealed class Glisten
        {
            private readonly GreatswordSlamPattern pattern;
            private readonly Transform sword;
            private readonly HammerGlow glow;
            private readonly Vector3 tip;
            private readonly float strike;
            private float nextGleam;
            private bool finalDone;

            public Glisten(GreatswordSlamPattern pattern, Transform sword, HammerGlow glow, Vector3 tip)
            {
                this.pattern = pattern;
                this.sword = sword;
                this.glow = glow;
                this.tip = tip;
                strike = pattern.riseTime + pattern.aimTime;

                // The opening flash has just gone off.
                nextGleam = Mathf.Max(0.01f, pattern.glistenInterval.x);
            }

            /// <summary>Glistens for <paramref name="clock"/> seconds since the attack began.</summary>
            public void Tick(float clock)
            {
                float charge = strike > 0f ? Mathf.Clamp01(clock / strike) : 1f;

                if (glow != null)
                {
                    glow.GlowIntensity = Mathf.Lerp(1f, pattern.glowPeak, charge * charge);
                }

                if (finalDone)
                {
                    return;
                }

                // Fully open the moment the sword starts down.
                float finalAt = strike - pattern.gleam.time * pattern.gleam.openFraction;

                if (clock >= finalAt)
                {
                    finalDone = true;
                    BladeGleam.Flash(sword, tip, pattern.colour, pattern.gleam, pattern.finalGleamSize);
                    return;
                }

                while (clock >= nextGleam)
                {
                    float at = Mathf.Clamp01(nextGleam / strike);
                    BladeGleam.Flash(sword, tip, pattern.colour, pattern.gleam, Mathf.Lerp(pattern.glistenSize.x, pattern.glistenSize.y, at));
                    nextGleam += Mathf.Max(0.01f, Mathf.Lerp(pattern.glistenInterval.x, pattern.glistenInterval.y, at));
                }
            }
        }

        private void Slam(Enemy enemy, Vector3 aim)
        {
            Vector3 feet = enemy.transform.position;
            Vector3 floor = Physics.Raycast(feet + Vector3.up, Vector3.down, out RaycastHit hit, 4f, floorMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : feet;

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            AmberSlam.Create(floor, aim, feet.y + 1f, feet.y + gripHeight - floor.y, -windup.y, slamAngle,
                             slash, SlashMaterial(), player);

            TopDownCamera.Shake(shakeStrength, shakeDuration);
        }

        private Material SlashMaterial()
        {
            if (slashMaterial != null)
            {
                return slashMaterial;
            }

            if (runtimeSlashMaterial == null && FlatMaterial.DoubleSided != null)
            {
                runtimeSlashMaterial = new Material(FlatMaterial.DoubleSided) { name = "Runtime Greatsword Slash" };
            }

            if (runtimeSlashMaterial != null)
            {
                runtimeSlashMaterial.SetColor(FlatMaterial.BaseColorId, colour);
            }

            return runtimeSlashMaterial;
        }

        private void End(BossBlades blades, int index, HammerGlow glow)
        {
            if (glow != null)
            {
                glow.FadeOut(glowFadeTime);
            }

            if (blades != null)
            {
                blades.Return(index);
            }
        }

        /// <summary>
        /// The grip: overhead at <paramref name="swing"/> 0, down and out
        /// toward the player at 1.
        /// </summary>
        private Vector3 Grip(Enemy enemy, Vector3 aim, float swing)
        {
            Vector3 overhead = enemy.transform.position + Vector3.up * gripHeight;
            Vector3 down = enemy.transform.position + Vector3.up * slamGripHeight + aim * slamReach;
            return Vector3.Lerp(overhead, down, swing);
        }

        /// <summary>
        /// The sword about <paramref name="grip"/>, its tip <paramref name="angle"/>
        /// degrees from straight up toward <paramref name="aim"/>, its flat
        /// facing across the swing.
        /// </summary>
        private void Pose(Vector3 aim, float angle, Vector3 grip, Quaternion modelOffset,
                          out Vector3 position, out Quaternion rotation)
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector3 tip = Vector3.up * Mathf.Cos(radians) + aim * Mathf.Sin(radians);
            Vector3 across = Vector3.Cross(Vector3.up, aim);

            position = grip + tip * gripToPivot;
            // As built the tip is down -Y, so up +Y points back at the grip.
            rotation = Quaternion.LookRotation(across, -tip) * modelOffset;
        }

        private Vector3 Track(Enemy enemy, Vector3 aim)
        {
            if (!enemy.HasTarget)
            {
                return aim;
            }

            Vector3 toPlayer = Flat(enemy.TargetPoint - enemy.transform.position);

            if (toPlayer.sqrMagnitude < 0.0001f)
            {
                return aim;
            }

            Vector3 turned = Vector3.RotateTowards(aim, toPlayer.normalized, trackTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
            return Flat(turned).normalized;
        }

        private static Vector3 Facing(Enemy enemy)
        {
            Vector3 toPlayer = enemy.HasTarget ? Flat(enemy.TargetPoint - enemy.transform.position) : Vector3.zero;

            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                return toPlayer.normalized;
            }

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
