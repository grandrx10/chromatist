using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Luna's finale, played once she is down to her last hit point: she
    /// rises over the middle of the arena, charges a ball of light at her
    /// tip and fires it straight down into the floor, kicking up more and
    /// more dust until a <see cref="WhiteHole"/> tears open under her, while
    /// swords of every colour drop in round it and are dragged in. Once the
    /// hole is full grown, white swallows the screen and the fight is over.
    /// </summary>
    /// <remarks>
    /// Set as the Luna enemy's Last Stand, not in a phase: the blow that
    /// would kill her leaves her on 1 health, invulnerable, and this plays
    /// once whatever attack she is in has finished. In order:
    ///   1. Gathering: over <see cref="gatherTime"/> she floats to the
    ///      enemy's <see cref="Enemy.ArenaCentre"/> (without one, she stays
    ///      where she is), standing tip down, turned toward the player, and
    ///      rises until her tip is <see cref="tipHeight"/> off the floor.
    ///   2. Charging: a ball of light swells at her tip to
    ///      <see cref="ballRadius"/> over <see cref="chargeTime"/>, while
    ///      <see cref="ChargeLines"/> streak in to it from all round, more and
    ///      more of them, from <see cref="streakRate"/>'s first value a second
    ///      to its second. They keep coming through the laser, building with
    ///      the dust up to <see cref="blastStreakRate"/>, until the white.
    ///   3. The laser: a <see cref="SweepBeam"/> fires from the ball straight
    ///      down into the floor and holds there. Dust bursts out from where
    ///      it strikes, building over <see cref="dustBuildTime"/> from
    ///      <see cref="dustRate"/>'s first value to its second, the puffs
    ///      growing through <see cref="dustSize"/> and flung out faster
    ///      through <see cref="dustSpeed"/>, the camera rumbling harder up to
    ///      <see cref="rumble"/>.
    ///   4. The white hole: once the dust is at full strength, the hole opens
    ///      on the floor under her and grows by its own settings, pulling the
    ///      player in and burning them in its white middle.
    ///   5. Swords: from the moment the laser fires, every
    ///      <see cref="bladeInterval"/> seconds a <see cref="FinaleBlade"/>
    ///      drops in somewhere between <see cref="bladeDistance"/>'s two
    ///      values from the middle, each a colour a golden ratio step round
    ///      the rainbow from the last, and is dragged round and in, all of
    ///      them turning the same way as the hole. Before the hole opens they
    ///      are swallowed where the laser strikes.
    ///   6. The end: <see cref="holdAtFull"/> seconds after the hole is full
    ///      grown, nothing hurts any more, and a <see cref="WhiteOut"/>
    ///      swells from its white middle to cover the screen over
    ///      <see cref="whiteOutTime"/>. Under the white, everything of the
    ///      attack is cleared away. After <see cref="whiteHoldTime"/> more,
    ///      <see cref="nextScene"/> is loaded and the white fades away there
    ///      over <see cref="fadeTime"/>. With no scene, or one missing from the
    ///      build, Luna dies instead, and the white fades away here.
    ///
    /// Should the player die along the way, the attack is cleared away at
    /// once, and Luna is left as she is.
    ///
    /// Her art is posed tip down local -Y, flat facing local +Z, as the
    /// player's blades are. She is posed by hand throughout; her Rigidbody,
    /// if she has one, is kinematic.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Luna/Luna Finale", fileName = "LunaFinale")]
    public sealed class LunaFinalePattern : AttackPattern
    {
        private const float GoldenRatio = 0.618034f;

        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        [Header("Gathering")]
        [SerializeField]
        [Tooltip("Seconds she takes to float to the middle and rise.")]
        [Min(0.01f)]
        private float gatherTime = 1.5f;

        [SerializeField]
        [Tooltip("How far off the floor her tip hangs through the finale.")]
        [Min(0f)]
        private float tipHeight = 3f;

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Charging")]
        [SerializeField]
        [Tooltip("Seconds the ball of light takes to swell at her tip.")]
        [Min(0f)]
        private float chargeTime = 2.5f;

        [SerializeField]
        [Tooltip("Radius the ball swells to.")]
        [Min(0.01f)]
        private float ballRadius = 0.9f;

        [SerializeField]
        [ColorUsage(false, true)]
        [Tooltip("Colour of the ball. Above 1 blooms.")]
        private Color ballColour = new Color(4f, 4f, 4f, 1f);

        [SerializeField]
        [Tooltip("Streaks rushing in to the ball a second, as the charge starts and as it ends.")]
        private Vector2 streakRate = new Vector2(15f, 60f);

        [SerializeField]
        [Tooltip("Streaks a second once the laser is at full strength. They build up to it from the charge's last rate along with the dust.")]
        [Min(0f)]
        private float blastStreakRate = 200f;

        [SerializeField] private ChargeLines.Settings streaks = new ChargeLines.Settings();

        [Header("Laser")]
        [SerializeField]
        private SweepBeam.Settings beam = new SweepBeam.Settings
        {
            length = 30f,
            radius = 0.7f,
            growTime = 0.2f,
            shrinkTime = 0.3f,
            damagePerSecond = 0f,
            colour = new Color(4f, 4f, 4f, 1f),
        };

        [Header("Dust")]
        [SerializeField]
        [Tooltip("Seconds from the laser firing for the dust to reach full strength. The white hole opens then.")]
        [Min(0f)]
        private float dustBuildTime = 5f;

        [SerializeField]
        [Tooltip("Puffs a second as the laser fires, and at full strength.")]
        private Vector2 dustRate = new Vector2(8f, 60f);

        [SerializeField]
        [Tooltip("Size of the puffs as the laser fires, and at full strength.")]
        private Vector2 dustSize = new Vector2(0.4f, 1f);

        [SerializeField]
        [Tooltip("Speed the puffs are flung out at as the laser fires, and at full strength. It bleeds off quickly.")]
        private Vector2 dustSpeed = new Vector2(6f, 30f);

        [SerializeField]
        [Tooltip("Lifetime, rise and look of each puff. Its size, spacing, speed and spread are ignored.")]
        private DustTrail.Settings dust = new DustTrail.Settings
        {
            lifetime = 0.8f,
            rise = 2f,
        };

        [SerializeField]
        [Tooltip("How hard the camera rumbles at full strength. 0 for none.")]
        [Min(0f)]
        private float rumble = 0.35f;

        [Header("White hole")]
        [SerializeField]
        private WhiteHole.Settings hole = new WhiteHole.Settings();

        [SerializeField]
        [Tooltip("Seconds the hole stays at full size before the white swallows the screen.")]
        [Min(0f)]
        private float holdAtFull = 1.5f;

        [Header("Swords")]
        [SerializeField]
        [Tooltip("Sword model, standing upright, tip down, with NeoNoir/Flat materials so it takes its colour. Empty draws a plain slab.")]
        private GameObject bladeModel;

        [SerializeField]
        [Tooltip("Seconds between one sword dropping in and the next.")]
        [Min(0.02f)]
        private float bladeInterval = 0.35f;

        [SerializeField]
        [Tooltip("Nearest and furthest from the middle a sword drops in, measured flat.")]
        private Vector2 bladeDistance = new Vector2(30f, 50f);

        [SerializeField]
        [Tooltip("Saturation of the swords' colours.")]
        [Range(0f, 1f)]
        private float bladeSaturation = 1f;

        [SerializeField] private FinaleBlade.Settings blade = new FinaleBlade.Settings();

        [Header("Ending")]
        [SerializeField]
        [Tooltip("Seconds the white takes to swell from the hole's middle to cover the screen.")]
        [Min(0.01f)]
        private float whiteOutTime = 0.8f;

        [SerializeField]
        [Tooltip("Seconds the screen stays white before moving on.")]
        [Min(0f)]
        private float whiteHoldTime = 1f;

        [SerializeField]
        [Tooltip("Seconds the white takes to fade away after.")]
        [Min(0.01f)]
        private float fadeTime = 1.5f;

        [SerializeField]
        [Tooltip("Scene loaded under the white. Empty, or missing from the build, kills Luna instead.")]
        private string nextScene = "";

        public override IEnumerator Perform(Enemy enemy)
        {
            Transform root = enemy.transform;

            // Posed by hand from here to the end.
            enemy.VelocityOverride = Vector3.zero;
            bool wasPhysicsDriven = enemy.PhysicsDriven;
            enemy.PhysicsDriven = true;

            Rigidbody body = enemy.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            // ---- gathering ----------------------------------------------------

            Vector3 floor = FindGround(root, enemy.ArenaCentre ?? root.position);

            Vector3 heading = enemy.HasTarget ? Flat(enemy.Target.position - floor) : Flat(root.forward);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;
            Quaternion upright = Quaternion.LookRotation(heading, Vector3.up);

            float tipBelow = MeasureTip(root, upright);
            Vector3 hover = floor + Vector3.up * (tipBelow + tipHeight);
            Vector3 start = root.position;
            Quaternion startRotation = root.rotation;

            for (float clock = 0f; clock < gatherTime;)
            {
                yield return null;
                clock += Time.deltaTime;

                float t = Smooth(clock / gatherTime);
                root.SetPositionAndRotation(Vector3.Lerp(start, hover, t), Quaternion.Slerp(startRotation, upright, t));
            }

            root.SetPositionAndRotation(hover, upright);
            Vector3 tip = hover + Vector3.down * tipBelow;

            // ---- charging -----------------------------------------------------

            Transform ball = MakeBall(tip);
            ChargeLines lines = ChargeLines.Create(tip, streakRate.x, streaks);

            for (float clock = 0f; clock < chargeTime;)
            {
                yield return null;
                clock += Time.deltaTime;

                float t = Mathf.Clamp01(clock / chargeTime);
                ball.localScale = Vector3.one * (ballRadius * 2f * (1f - (1f - t) * (1f - t)));
                lines.Rate = Mathf.Lerp(streakRate.x, streakRate.y, t);
            }

            ball.localScale = Vector3.one * (ballRadius * 2f);

            // ---- the laser, the dust, the hole and the swords ---------------

            SweepBeam laser = SweepBeam.Fire(tip, Vector3.down, root, beam);
            DustTrail.Cloud cloud = DustTrail.Cloud.Create("Finale Dust");
            DustTrail.Settings puff = CopyDust();

            float way = Random.value < 0.5f ? 1f : -1f;
            float hue = Random.value;
            float dustOwed = 0f;
            float nextBlade = 0f;
            float fullFor = 0f;

            // There from the start, unopened, for the swords to be dragged to.
            WhiteHole whiteHole = WhiteHole.Create(floor, enemy, enemy.Target, way, hole);

            for (float clock = 0f; ;)
            {
                if (player != null && player.IsDead)
                {
                    Clear(laser, ball, lines, whiteHole, cloud);
                    Restore(enemy, body, wasKinematic, wasPhysicsDriven);
                    yield break;
                }

                laser.Aim(tip, Vector3.down);

                float strength = dustBuildTime > 0f ? Mathf.Clamp01(clock / dustBuildTime) : 1f;
                Kick(cloud, puff, floor, strength, ref dustOwed);
                lines.Rate = Mathf.Lerp(streakRate.y, blastStreakRate, strength);

                if (rumble > 0f)
                {
                    TopDownCamera.Shake(rumble * strength, 0.25f);
                }

                if (strength >= 1f)
                {
                    whiteHole.Open();
                }

                while (clock >= nextBlade)
                {
                    Color colour = Color.HSVToRGB(hue, bladeSaturation, 1f);
                    hue = Mathf.Repeat(hue + GoldenRatio, 1f);

                    float near = Mathf.Min(bladeDistance.x, bladeDistance.y);
                    float far = Mathf.Max(bladeDistance.x, bladeDistance.y);

                    FinaleBlade.Drop(bladeModel, whiteHole, Random.Range(0f, 360f),
                                     Random.Range(near, far), colour, way, enemy.Target, blade);
                    nextBlade += bladeInterval;
                }

                if (whiteHole.Full)
                {
                    fullFor += Time.deltaTime;

                    if (fullFor >= holdAtFull)
                    {
                        break;
                    }
                }

                yield return null;
                clock += Time.deltaTime;
            }

            // ---- the end ------------------------------------------------------

            whiteHole.Hurts = false;
            WhiteOut white = WhiteOut.Cover(whiteHole.Centre, whiteHole.CoreRadius, whiteOutTime);

            while (!white.Covered)
            {
                laser.Aim(tip, Vector3.down);
                Kick(cloud, puff, floor, 1f, ref dustOwed);
                yield return null;
            }

            Clear(laser, ball, lines, whiteHole, cloud);

            if (whiteHoldTime > 0f)
            {
                yield return new WaitForSecondsRealtime(whiteHoldTime);
            }

            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
            {
                white.LoadThenClear(nextScene, fadeTime);
                yield break;
            }

            if (!string.IsNullOrEmpty(nextScene))
            {
                Debug.LogWarning($"[LunaFinale] Can't load scene \"{nextScene}\": it isn't in File > Build Profiles' scene list. Killing Luna instead.");
            }

            white.Clear(fadeTime);
            Restore(enemy, body, wasKinematic, wasPhysicsDriven);

            // Last: her death stops every coroutine on her, this one included.
            EnemyHealth health = enemy.Health;
            health.Invulnerable = false;
            health.CannotDie = false;
            health.MinimumHealth = 0f;
            health.TakeLinkedDamage(health.CurrentHealth);
        }

        /// <summary>
        /// Throws this frame's share of dust out from <paramref name="floor"/>,
        /// at <paramref name="strength"/> of the way from the dust's first
        /// values to its full ones. <paramref name="owed"/> carries the
        /// fraction of a puff left over from frame to frame.
        /// </summary>
        private void Kick(DustTrail.Cloud cloud, DustTrail.Settings puff, Vector3 floor, float strength, ref float owed)
        {
            owed += Mathf.Lerp(dustRate.x, dustRate.y, strength) * Time.deltaTime;
            float speed = Mathf.Lerp(dustSpeed.x, dustSpeed.y, strength);
            puff.size = Mathf.Lerp(dustSize.x, dustSize.y, strength);

            for (; owed >= 1f; owed -= 1f)
            {
                Vector2 way = Random.insideUnitCircle.normalized;
                Vector3 outward = new Vector3(way.x, 0f, way.y);

                cloud.Emit(floor + outward * Random.Range(0f, 1f) + Vector3.up * (puff.size * 0.3f),
                           outward * (speed * Random.Range(0.5f, 1.1f)) + Vector3.up * (puff.rise * Random.Range(0.6f, 1.2f)),
                           puff);
            }
        }

        private DustTrail.Settings CopyDust()
        {
            return new DustTrail.Settings
            {
                size = dustSize.x,
                lifetime = dust.lifetime,
                rise = dust.rise,
                colour = dust.colour,
                material = dust.material,
            };
        }

        private Transform MakeBall(Vector3 tip)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(piece.GetComponent<Collider>());
            piece.name = "FinaleCharge";
            piece.transform.position = tip;
            piece.transform.localScale = Vector3.zero;

            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;

            if (renderer.sharedMaterial != null)
            {
                FlatMaterial.Tint(renderer, ballColour, new MaterialPropertyBlock());
            }

            return piece.transform;
        }

        /// <summary>Everything of the attack's gone. The swords go with the hole.</summary>
        private static void Clear(SweepBeam laser, Transform ball, ChargeLines lines, WhiteHole whiteHole,
                                  DustTrail.Cloud cloud)
        {
            if (lines != null)
            {
                Destroy(lines.gameObject);
            }

            if (laser != null)
            {
                Destroy(laser.gameObject);
            }

            if (ball != null)
            {
                Destroy(ball.gameObject);
            }

            if (whiteHole != null)
            {
                Destroy(whiteHole.gameObject);
            }

            if (cloud != null)
            {
                cloud.Release();
            }
        }

        private static void Restore(Enemy enemy, Rigidbody body, bool wasKinematic, bool wasPhysicsDriven)
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

        /// <summary>
        /// How far her art reaches below her pivot in <paramref name="pose"/>:
        /// measured from her renderers by trying the pose on and putting it
        /// back, all within the frame.
        /// </summary>
        private static float MeasureTip(Transform root, Quaternion pose)
        {
            Quaternion rotation = root.rotation;
            root.rotation = pose;

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
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
            groundMask = LayerMask.GetMask("Default");
            beam.wallMask = LayerMask.GetMask("Default");
        }
    }
}
