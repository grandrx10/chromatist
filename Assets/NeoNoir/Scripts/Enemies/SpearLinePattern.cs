using System;
using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss's spear: it lifts beside the boss glowing gold, is thrown
    /// point first into the floor ahead of the player, off to their left or
    /// right, and bursts into golden light once the player draws level with
    /// it. Then it flies back.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The spear is borrowed from the boss's <see cref="BossBlades"/>
    ///      -- the one spawned from <see cref="bladePrefab"/> -- and lifts
    ///      beside the boss, point up, over <see cref="chargeTime"/>,
    ///      glowing <see cref="colour"/>, ever brighter. Gleams twinkle at its
    ///      tip every <see cref="glistenInterval"/> until it bursts.
    ///   2. It is thrown, over <see cref="throwTime"/>, point first into the
    ///      floor ahead of the player: <see cref="aheadDistance"/> on from
    ///      them, plus <see cref="aheadTime"/> seconds of a running player's
    ///      speed, and <see cref="sideDistance"/> to their left or right, at
    ///      random. Ahead is the way a running player runs, or else toward
    ///      the boss.
    ///   3. Stuck there, a <see cref="WarningCircle"/> of the blast's reach
    ///      shows round it, filling as the player closes in. Once the player
    ///      draws level with it -- or after <see cref="maxWait"/> regardless --
    ///      it bursts, the way a <see cref="GiftBox"/> does: a ball of light,
    ///      a shockwave ring and dust. A player within
    ///      <see cref="blastRadius"/> takes <see cref="damage"/> and is shoved
    ///      away.
    ///   4. The spear flies back into the orbit.
    ///
    /// Stuck, the spear stays where it is in the world: in the hall run of
    /// <see cref="SpearHallIntro"/> the player runs up to it.
    ///
    /// The spear is posed the way <see cref="BossBlades"/> poses it: built
    /// flat side facing local +Z, tip down local -Y, with the entry's
    /// Rotation Offset for one built otherwise. Its tip, found from the model,
    /// is what goes into the floor and where the gleams flash.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Spear Line", fileName = "SpearLine")]
    public sealed class SpearLinePattern : AttackPattern
    {
        private static readonly Collider[] BlastHits = new Collider[32];

        [SerializeField]
        [Tooltip("The spear's prefab, as listed in the boss's Boss Blades. The blade spawned from it is the one thrown.")]
        private GameObject bladePrefab;

        [SerializeField]
        [Tooltip("Colour of the glow and the gleams.")]
        private Color colour = new Color(1f, 0.75f, 0.2f);

        [Header("Charge")]
        [SerializeField]
        [Tooltip("Seconds the spear glows, point up beside the boss, before it is thrown.")]
        [Min(0f)]
        private float chargeTime = 0.7f;

        [SerializeField]
        [Tooltip("Height above the boss's pivot the spear's pivot lifts to while charging.")]
        private float hoverHeight = 3f;

        [SerializeField]
        [Tooltip("How far to the boss's side the spear lifts, so the boss does not hide it.")]
        private float hoverSide = 1.5f;

        [SerializeField]
        [Tooltip("Brightness of the glow as the charge starts. Above 1 blooms.")]
        [Min(0f)]
        private float glowIntensity = 2.5f;

        [SerializeField]
        [Tooltip("What the glow's brightness is multiplied by by the end of the charge, building up from 1.")]
        [Min(0f)]
        private float glowPeak = 2.5f;

        [SerializeField]
        [Tooltip("Seconds the glow takes to come on, and to go once the spear comes back.")]
        [Min(0f)]
        private float glowFadeTime = 0.2f;

        [SerializeField] private BladeGleam.Settings gleam = new BladeGleam.Settings();

        [SerializeField]
        [Tooltip("Nudges the gleam from the spear's tip, in the spear's own space. The tip is found from the model.")]
        private Vector3 gleamOffset;

        [SerializeField]
        [Tooltip("Seconds between gleams twinkling at the tip, until it bursts.")]
        [Min(0.02f)]
        private float glistenInterval = 0.25f;

        [SerializeField]
        [Tooltip("Size of each twinkle, as a multiple of the gleam's own.")]
        [Min(0f)]
        private float glistenSize = 0.7f;

        [SerializeField]
        [Tooltip("Size of the gleam as it is thrown, as a multiple of the gleam's own.")]
        [Min(0f)]
        private float launchGleamSize = 2f;

        [Header("Throw")]
        [SerializeField]
        [Tooltip("Seconds the spear takes to fly from beside the boss into the floor.")]
        [Min(0.01f)]
        private float throwTime = 0.35f;

        [SerializeField]
        [Tooltip("How far ahead of the player the spear lands, before any run speed.")]
        [Min(0f)]
        private float aheadDistance = 4f;

        [SerializeField]
        [Tooltip("Seconds of a running player's speed added to how far ahead it lands, so they have time to steer clear.")]
        [Min(0f)]
        private float aheadTime = 0.8f;

        [SerializeField]
        [Tooltip("Nearest and furthest to the player's left or right it lands. Which side is random.")]
        private Vector2 sideDistance = new Vector2(1f, 3f);

        [SerializeField]
        [Tooltip("Degrees the stuck spear leans back toward the boss from upright.")]
        [Range(0f, 80f)]
        private float stuckLean = 20f;

        [SerializeField]
        [Tooltip("How far the tip goes into the floor.")]
        [Min(0f)]
        private float embedDepth = 0.6f;

        [SerializeField]
        [Tooltip("Layers the spear sticks into. Without floor under the spot, it sticks in at the player's height.")]
        private LayerMask floorMask = 1;

        [SerializeField]
        [Tooltip("How far the throw jolts the camera as it sticks in. 0 for none.")]
        [Min(0f)]
        private float stickShake = 0.2f;

        [Header("Burst")]
        [SerializeField]
        [Tooltip("Longest the stuck spear waits for the player to draw level before bursting anyway.")]
        [Min(0f)]
        private float maxWait = 4f;

        [SerializeField]
        [Tooltip("Reach of the blast.")]
        [Min(0.1f)]
        private float blastRadius = 3f;

        [SerializeField]
        [Tooltip("Damage to a player within the blast's reach.")]
        [Min(0f)]
        private float damage = 20f;

        [SerializeField]
        [Tooltip("Speed the blast shoves the player away at, falling off to nothing at its edge.")]
        [Min(0f)]
        private float knockback = 15f;

        [SerializeField]
        [Tooltip("What the blast reaches. Leave projectiles and triggers out.")]
        private LayerMask blastMask = ~0;

        [SerializeField]
        [Tooltip("Colour of the warning circle on the floor round the stuck spear.")]
        private Color warningColour = new Color(1f, 0.75f, 0.2f, 1f);

        [SerializeField]
        [Tooltip("Thickness of the warning circle's outline.")]
        [Min(0f)]
        private float warningOutline = 0.1f;

        [SerializeField]
        [Tooltip("The ball of golden light, bursting out to the blast's reach.")]
        private BlastSphere.Settings sphere = new BlastSphere.Settings
        {
            colour = new Color(3f, 2f, 0.4f, 1f),
        };

        [SerializeField]
        [Tooltip("Material for the shockwave ring, e.g. Saffron. Empty uses NeoNoir/Flat.")]
        private Material ringMaterial;

        [SerializeField]
        private HammerImpact.Settings ring = new HammerImpact.Settings
        {
            ringWidth = 0.8f,
            ringTime = 0.35f,
            debrisCount = 0,
        };

        [SerializeField]
        [Tooltip("Dust puffs thrown out along the floor by the blast.")]
        [Min(0)]
        private int dustCount = 14;

        [SerializeField]
        private DustTrail.Settings dust = new DustTrail.Settings
        {
            size = 0.6f,
            lifetime = 0.7f,
            rise = 1.2f,
        };

        [SerializeField]
        [Tooltip("How far the blast jolts the camera. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.5f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.3f;

        // So a missing setup is said once, not every time the attack comes round.
        [NonSerialized] private bool warned;

        public override IEnumerator Perform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null ? blades.IndexOf(bladePrefab) : -1;

            if (index < 0)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[{name}] {enemy.name} has no Boss Blades blade made from the Blade Prefab.", this);
                }

                yield break;
            }

            Transform blade = blades.Borrow(index);
            Quaternion modelOffset = blades.RotationOffset(index);

            Color glowColour = colour * glowIntensity;
            glowColour.a = colour.a;
            HammerGlow glow = HammerGlow.On(blade.gameObject, null, glowColour, glowFadeTime);
            Vector3 tipLocal = BladeGleam.TipOf(blade);
            Vector3 gleamAt = tipLocal + gleamOffset;
            BladeGleam.Flash(blade, gleamAt, colour, gleam);

            float clock = 0f;
            float nextGleam = glistenInterval;

            void Glisten()
            {
                if (clock >= nextGleam)
                {
                    nextGleam = clock + glistenInterval;
                    BladeGleam.Flash(blade, gleamAt, colour, gleam, glistenSize);
                }
            }

            // 1. Lifted beside the boss, point up, glowing ever brighter. Its
            // start is carried on with a running frame, so the lift is
            // measured from where it would be.
            Vector3 fromPosition = blade.position;
            Quaternion fromRotation = blade.rotation;
            float elapsed = 0f;

            while (elapsed < chargeTime)
            {
                fromPosition += ChaseFrame.Step(Time.deltaTime);

                float t = elapsed / chargeTime;
                float lift = Smooth(t * 2f);
                Quaternion pointUp = Pointing(Vector3.up, Facing(enemy)) * modelOffset;

                blade.SetPositionAndRotation(Vector3.Lerp(fromPosition, Hover(enemy), lift),
                                             Quaternion.Slerp(fromRotation, pointUp, lift));
                glow.GlowIntensity = Mathf.Lerp(1f, glowPeak, t * t);
                Glisten();

                yield return null;
                elapsed += Time.deltaTime;
                clock += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    End(blades, index, glow);
                    yield break;
                }
            }

            // Where it sticks: ahead of the player, off to one side.
            Transform target = enemy.HasTarget ? enemy.Target : null;
            Vector3 playerAt = target != null ? target.position : enemy.transform.position + Facing(enemy) * aheadDistance;
            Vector3 ahead = Ahead(enemy, playerAt);
            Vector3 side = Vector3.Cross(Vector3.up, ahead) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);

            float reach = aheadDistance + ChaseFrame.Velocity.magnitude * aheadTime;
            float off = UnityEngine.Random.Range(Mathf.Min(sideDistance.x, sideDistance.y), Mathf.Max(sideDistance.x, sideDistance.y));
            Vector3 floor = Floor(playerAt + ahead * reach + side * off, playerAt.y);

            // Point first into the floor, its butt leaning back toward the boss.
            Vector3 toBoss = Flat(enemy.transform.position - floor);
            toBoss = toBoss.sqrMagnitude > 0.0001f ? toBoss.normalized : -ahead;
            float lean = stuckLean * Mathf.Deg2Rad;
            Vector3 tipDirection = -(Vector3.up * Mathf.Cos(lean) + toBoss * Mathf.Sin(lean));

            Quaternion stuckRotation = Pointing(tipDirection, side) * modelOffset;
            Vector3 stuckPosition = floor + tipDirection * embedDepth
                                  - stuckRotation * Vector3.Scale(blade.lossyScale, tipLocal);

            BladeGleam.Flash(blade, gleamAt, colour, gleam, launchGleamSize);

            // 2. Thrown, speeding up, point turning to lead the way.
            fromPosition = blade.position;
            fromRotation = blade.rotation;
            elapsed = 0f;

            while (elapsed < throwTime)
            {
                float t = elapsed / throwTime;

                blade.SetPositionAndRotation(Vector3.Lerp(fromPosition, stuckPosition, t * t),
                                             Quaternion.Slerp(fromRotation, stuckRotation, Smooth(t * 2f)));
                Glisten();

                yield return null;
                elapsed += Time.deltaTime;
                clock += Time.deltaTime;
            }

            blade.SetPositionAndRotation(stuckPosition, stuckRotation);

            if (stickShake > 0f)
            {
                TopDownCamera.Shake(stickShake, 0.15f);
            }

            // 3. Stuck, waiting for the player to draw level.
            WarningCircle circle = WarningCircle.Create(floor + Vector3.up * 0.03f, blastRadius, warningOutline, null, warningColour);
            float startGap = target != null ? Mathf.Max(0.01f, Vector3.Dot(floor - target.position, ahead)) : 1f;
            elapsed = 0f;

            while (elapsed < maxWait && target != null && !enemy.Health.IsDead)
            {
                float gap = Vector3.Dot(floor - target.position, ahead);

                if (gap <= 0f)
                {
                    break;
                }

                circle.Fill = 1f - gap / startGap;
                Glisten();

                yield return null;
                elapsed += Time.deltaTime;
                clock += Time.deltaTime;
            }

            Destroy(circle.gameObject);
            Burst(floor);

            // 4. Back into the orbit.
            End(blades, index, glow);
        }

        /// <summary>
        /// The burst of golden light, the way a <see cref="GiftBox"/> goes
        /// off: a player in reach is hurt and shoved away.
        /// </summary>
        private void Burst(Vector3 floor)
        {
            Vector3 centre = floor + Vector3.up * 1f;
            int count = Physics.OverlapSphereNonAlloc(centre, blastRadius, BlastHits, blastMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth player = BlastHits[i].GetComponentInParent<PlayerHealth>();

                if (player == null)
                {
                    continue;
                }

                player.TakeDamage(damage, centre);

                PlayerMotor motor = player.GetComponent<PlayerMotor>();

                if (motor != null)
                {
                    motor.AddExplosionForce(knockback, centre, blastRadius);
                }

                break;
            }

            BlastSphere.Create(centre, blastRadius, sphere);

            HammerImpact.Settings burst = ring;
            burst.ringRadius = blastRadius;
            HammerImpact.Create(floor, burst, ringMaterial != null ? ringMaterial : FlatMaterial.Shared);

            ThrowDust(floor);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }
        }

        private void ThrowDust(Vector3 floor)
        {
            if (dustCount <= 0)
            {
                return;
            }

            DustTrail.Cloud cloud = DustTrail.Cloud.Create("SpearDust");

            for (int i = 0; i < dustCount; i++)
            {
                float angle = (i + UnityEngine.Random.Range(-0.4f, 0.4f)) * 360f / dustCount;
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float speed = blastRadius * UnityEngine.Random.Range(1.5f, 3f);

                cloud.Emit(floor + outward * (blastRadius * 0.15f) + Vector3.up * (dust.size * 0.3f),
                           outward * speed + Vector3.up * (dust.rise * UnityEngine.Random.Range(0.6f, 1.2f)),
                           dust);
            }

            cloud.Release();
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

        /// <summary>Where the spear charges: above and beside the boss.</summary>
        private Vector3 Hover(Enemy enemy)
        {
            Vector3 side = Vector3.Cross(Vector3.up, Facing(enemy));
            return enemy.transform.position + Vector3.up * hoverHeight + side * hoverSide;
        }

        /// <summary>The floor under <paramref name="spot"/>, or the spot at <paramref name="height"/> with none.</summary>
        private Vector3 Floor(Vector3 spot, float height)
        {
            Vector3 from = new Vector3(spot.x, height + 5f, spot.z);

            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 15f, floorMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return new Vector3(spot.x, height, spot.z);
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
        /// A rotation pointing a blade built tip down -Y along
        /// <paramref name="tip"/>, its flat facing across
        /// <paramref name="flat"/>, before the model's own offset.
        /// </summary>
        private static Quaternion Pointing(Vector3 tip, Vector3 flat)
        {
            tip = tip.sqrMagnitude > 0.0001f ? tip.normalized : Vector3.up;

            Vector3 forward = Vector3.ProjectOnPlane(flat, tip);

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(Vector3.forward, tip);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(Vector3.right, tip);
            }

            return Quaternion.LookRotation(forward.normalized, -tip);
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
        }
    }
}
