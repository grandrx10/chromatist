using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's eye circle: it glides to the middle, lowers a ring of
    /// eyes round itself looking outward, and while their beams sweep the
    /// arena like wheel spokes it sprays a turning cross of daggers and sends
    /// string circles after the player. Safety is close to the boss, inside
    /// the ring.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. Glide: it slides to the middle of the arena, as the tornado does.
    ///   2. Eyes: <see cref="eyeCount"/> eyes come down from the sky onto a
    ///      circle of <see cref="ringRadius"/> round it, their middles
    ///      <see cref="hangHeight"/> above the floor, each looking straight
    ///      out. They cannot be hurt. See <see cref="EyeRing"/>.
    ///   3. Charge: every eye gathers power for <see cref="chargeTime"/>.
    ///   4. Beams: for <see cref="duration"/> every eye fires a steady beam
    ///      straight out, dealing the beam's damage a second, and the ring
    ///      turns at <see cref="spinSpeed"/>, either way at random. All the
    ///      while it throws <see cref="daggersPerThrow"/> daggers, evenly
    ///      spread all round, every <see cref="shotInterval"/> seconds, the
    ///      spread turned a further <see cref="shotTurn"/> degrees each
    ///      throw, and every
    ///      <see cref="circleInterval"/> seconds a <see cref="TargetedStrings"/>
    ///      circle appears under the boss and moves out to chase the player.
    ///   5. Finale: the daggers stop, the ring stops turning and the beams
    ///      die, and every eye turns on the player and fires the
    ///      <see cref="finale"/> laser, as the eye laser attack's eyes do:
    ///      tracking them, tipping down to the floor and sweeping up, each
    ///      hitting once.
    ///   6. Once every laser is spent, the eyes are hauled away. Circles
    ///      still out play out on their own.
    ///
    /// Movement above is ignored; the attack moves it itself.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Eye Circle", fileName = "EyeCircle")]
    public sealed class EyeCirclePattern : AttackPattern
    {
        /// <summary>Close enough to the middle to stop gliding.</summary>
        private const float ArriveDistance = 0.15f;

        [Header("Glide")]
        [SerializeField]
        [Tooltip("Fastest it glides to the middle.")]
        [Min(0.1f)]
        private float glideSpeed = 8f;

        [SerializeField]
        [Tooltip("Seconds it glides for at most before starting wherever it is.")]
        [Min(0f)]
        private float maxGlideTime = 4f;

        [Header("Eyes")]
        [SerializeField]
        [Tooltip("The eye prefab, with a Puppeteer Eye on it.")]
        private PuppeteerEye eyePrefab;

        [SerializeField]
        [Tooltip("Eyes in the ring.")]
        [Min(1)]
        private int eyeCount = 8;

        [SerializeField]
        [Tooltip("Radius of the ring round the boss.")]
        [Min(0.5f)]
        private float ringRadius = 10f;

        [SerializeField]
        [Tooltip("Height above the floor of the eyes' middles, and so of the beams. About the player's chest.")]
        [Min(0f)]
        private float hangHeight = 1.2f;

        [SerializeField]
        [Tooltip("How far above their places the eyes start coming down from.")]
        [Min(0f)]
        private float dropHeight = 12f;

        [SerializeField]
        [Tooltip("Seconds the eyes take to come down.")]
        [Min(0.01f)]
        private float lowerTime = 1f;

        [Header("Beams")]
        [SerializeField]
        [Tooltip("Seconds the eyes gather power before firing.")]
        [Min(0f)]
        private float chargeTime = 1.2f;

        [SerializeField]
        [Tooltip("Distance from an eye's middle, along its gaze, where its beam starts. About the eye's radius.")]
        [Min(0f)]
        private float muzzleDistance = 0.5f;

        [SerializeField] private SceptorCharge.Settings charge = new SceptorCharge.Settings();

        [SerializeField]
        private SweepBeam.Settings beam = new SweepBeam.Settings
        {
            radius = 0.35f,
            damagePerSecond = 10f,
            breaksGiftBoxes = true,
        };

        [SerializeField]
        [Tooltip("Seconds the beams fire for, the ring turning and the daggers flying all the while.")]
        [Min(0f)]
        private float duration = 10f;

        [SerializeField]
        [Tooltip("Degrees a second the ring turns. Which way is picked at random.")]
        private float spinSpeed = 30f;

        [Header("Finale")]
        [SerializeField]
        [Tooltip("The lasers every eye fires at the player once the ring stops: tracking, tipping down to the floor and sweeping up.")]
        private EyeballLaserPattern.Settings finale = new EyeballLaserPattern.Settings
        {
            beam = new SweepBeam.Settings
            {
                radius = 0.35f,
                singleHit = true,
                hitDamage = 15f,
                breaksGiftBoxes = true,
            },
        };

        [Header("Daggers")]
        [SerializeField]
        private EnemyProjectile daggerPrefab;

        [SerializeField]
        [Tooltip("Daggers in each throw, evenly spaced all round.")]
        [Min(1)]
        private int daggersPerThrow = 3;

        [SerializeField]
        [Tooltip("Seconds between one throw of daggers and the next.")]
        [Min(0.02f)]
        private float shotInterval = 0.1f;

        [SerializeField]
        [Tooltip("Degrees the spread turns after each throw.")]
        private float shotTurn = 3f;

        [SerializeField]
        [Tooltip("Height above its feet the daggers fly at.")]
        [Min(0f)]
        private float daggerHeight = 0.5f;

        [SerializeField]
        [Tooltip("How far out from its middle each dagger appears.")]
        [Min(0f)]
        private float daggerSpawnDistance = 0.8f;

        [Header("String circles")]
        [SerializeField]
        [Tooltip("Seconds between one chasing string circle and the next, while the beams fire. The first comes after one such wait.")]
        [Min(0.1f)]
        private float circleInterval = 3f;

        [SerializeField] private TargetedStrings.Settings circles = new TargetedStrings.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            if (eyePrefab == null)
            {
                Debug.LogWarning($"[EyeCircle] {name} has no eye prefab.", this);
                yield break;
            }

            ArenaBounds arena = enemy.Arena;
            Vector3 middle = arena != null ? arena.Centre : enemy.ArenaCentre ?? enemy.transform.position;
            middle.y = enemy.transform.position.y;

            enemy.PlayIdleAnimation();
            yield return Glide(enemy, middle);

            EyeRing ring = EyeRing.Create(enemy, eyePrefab, eyeCount, Floor(enemy, arena), ringRadius, hangHeight);
            ring.Lower(dropHeight, lowerTime);

            while (ring != null && !ring.IsLowered)
            {
                yield return null;
            }

            if (ring == null || enemy.Health.IsDead)
            {
                yield break;
            }

            ring.Fire(beam, charge, chargeTime, muzzleDistance);

            if (chargeTime > 0f)
            {
                yield return new WaitForSeconds(chargeTime);
            }

            ring.Spin(Random.value < 0.5f ? spinSpeed : -spinSpeed);

            float clock = 0f;
            float nextShot = 0f;
            float nextCircle = circleInterval;
            float crossTurn = Random.Range(0f, 90f);

            while (clock < duration && !enemy.Health.IsDead)
            {
                enemy.VelocityOverride = Vector3.zero;

                while (clock >= nextShot)
                {
                    nextShot += shotInterval;
                    ThrowCross(enemy, crossTurn);
                    crossTurn += shotTurn;
                }

                if (clock >= nextCircle)
                {
                    nextCircle += circleInterval;

                    if (enemy.HasTarget)
                    {
                        // Out from under the boss, chasing the player from there.
                        TargetedStrings.Create(enemy, enemy.Target, enemy.transform.position, circles);
                    }
                }

                yield return null;
                clock += Time.deltaTime;
            }

            // Finale: the daggers stop, and every eye turns on the player.
            if (ring != null && !enemy.Health.IsDead)
            {
                ring.FireAtPlayer(finale);

                // Beyond the lasers' own time, in case one never reports back.
                float limit = Mathf.Max(finale.chargeTime, finale.trackTime) + finale.sweepTime + 1f;
                float waited = 0f;

                while (ring != null && ring.IsLasering && waited < limit && !enemy.Health.IsDead)
                {
                    enemy.VelocityOverride = Vector3.zero;
                    yield return null;
                    waited += Time.deltaTime;
                }
            }

            if (ring != null)
            {
                ring.Lift();
            }

            enemy.VelocityOverride = null;
        }

        /// <summary>Slides to <paramref name="centre"/>, easing in as it arrives.</summary>
        private IEnumerator Glide(Enemy enemy, Vector3 centre)
        {
            float clock = 0f;

            while (clock < maxGlideTime && !enemy.Health.IsDead)
            {
                Vector3 toCentre = centre - enemy.transform.position;
                toCentre.y = 0f;
                float distance = toCentre.magnitude;

                if (distance <= ArriveDistance)
                {
                    break;
                }

                // Slows over the last stretch, so it settles rather than stops dead.
                float speed = Mathf.Min(glideSpeed, distance * 3f);
                enemy.VelocityOverride = toCentre / distance * speed;

                yield return null;
                clock += Time.deltaTime;
            }

            enemy.VelocityOverride = Vector3.zero;
        }

        /// <summary>The floor under the boss: the arena's, or its own feet.</summary>
        private static Vector3 Floor(Enemy enemy, ArenaBounds arena)
        {
            Vector3 spot = enemy.transform.position;

            if (arena != null && arena.FloorAt(spot, enemy.transform, out float floor))
            {
                spot.y = floor;
            }
            else
            {
                spot.y = enemy.Health.BodyBounds().min.y;
            }

            return spot;
        }

        private void ThrowCross(Enemy enemy, float turn)
        {
            if (daggerPrefab == null)
            {
                return;
            }

            for (int arm = 0; arm < daggersPerThrow; arm++)
            {
                Vector3 direction = Quaternion.Euler(0f, turn + arm * 360f / daggersPerThrow, 0f) * Vector3.forward;
                Vector3 from = enemy.transform.position + Vector3.up * daggerHeight + direction * daggerSpawnDistance;

                EnemyProjectile dagger = EnemyProjectile.Spawn(daggerPrefab, from, Quaternion.LookRotation(direction, Vector3.up));
                dagger.Launch(direction);
            }
        }

        private void Reset()
        {
            delayAfter = 1.5f;
            Movement.type = MovementType.StandStill;
        }
    }
}
