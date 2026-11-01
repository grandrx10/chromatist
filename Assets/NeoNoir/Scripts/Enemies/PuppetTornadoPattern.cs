using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's tornado: glides to the middle of the arena, then
    /// spins up into a whirlwind, circling out from the middle in a widening
    /// spiral and throwing daggers every which way.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. Glide: it slides to the middle of the arena -- the
    ///      <see cref="Enemy.ArenaCentre"/> the intro gave it -- at up to
    ///      <see cref="glideSpeed"/>, easing in as it arrives. It gives up
    ///      after <see cref="maxGlideTime"/> wherever it has got to. No arena
    ///      centre, and it starts where it stands.
    ///   2. Spin up: over <see cref="spinUpTime"/> it winds up to
    ///      <see cref="spinSpeed"/> on the spot and a
    ///      <see cref="TornadoEffect"/> rises round it. Then it stays spinning
    ///      in the middle for <see cref="holdTime"/>, throwing daggers if
    ///      <see cref="throwWhileHolding"/> is on.
    ///   3. Tornado: for <see cref="duration"/> it circles the middle,
    ///      speeding up steadily from <see cref="spiralStartSpeed"/> to
    ///      <see cref="orbitSpeed"/>, the circle widening steadily from
    ///      <see cref="startRadius"/> to <see cref="endRadius"/>, so it
    ///      spirals out. It never leaves the arena: where the circle would
    ///      carry it out, it is drawn in to stay inside. All the while it
    ///      throws <see cref="daggersPerSecond"/> daggers, each in a random
    ///      direction all round, flat at <see cref="daggerHeight"/>.
    ///   4. Laps: at the end radius it goes round the full circle
    ///      <see cref="finalLaps"/> times at <see cref="lapSpeed"/>, still
    ///      throwing daggers.
    ///   5. Wind down: over <see cref="windDownTime"/> the spin, the
    ///      whirlwind and its drift all die away wherever the spiral left
    ///      it. Then the next attack.
    /// It does not hurt to touch; only the daggers do.
    ///
    /// Leave Attack Animation above empty: <see cref="spinAnimation"/> plays
    /// from the spin up, and the idle plays through the glide. Movement
    /// above is ignored; the attack moves it itself.
    ///
    /// The daggers' speed, damage and range belong to the dagger prefab.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Puppet Tornado", fileName = "PuppetTornado")]
    public sealed class PuppetTornadoPattern : AttackPattern
    {
        /// <summary>Close enough to the middle to stop gliding.</summary>
        private const float ArriveDistance = 0.15f;

        /// <summary>How hard it is pulled back onto the spiral if it drifts off, per second.</summary>
        private const float PathCorrection = 10f;

        [Header("Glide")]
        [SerializeField]
        [Tooltip("Fastest it glides to the middle.")]
        [Min(0.1f)]
        private float glideSpeed = 8f;

        [SerializeField]
        [Tooltip("Seconds it glides for at most before starting wherever it is.")]
        [Min(0f)]
        private float maxGlideTime = 4f;

        [Header("Spin")]
        [SerializeField]
        [Tooltip("Degrees a second it spins at full speed.")]
        private float spinSpeed = 1440f;

        [SerializeField]
        [Tooltip("Seconds it takes to spin up on the spot before it starts circling.")]
        [Min(0.01f)]
        private float spinUpTime = 0.6f;

        [SerializeField]
        [Tooltip("Seconds it stays spinning in the middle, once spun up, before it starts spiralling out.")]
        [Min(0f)]
        private float holdTime = 2f;

        [SerializeField]
        [Tooltip("Throw daggers while holding in the middle too. Off, they start as the spiral does.")]
        private bool throwWhileHolding = true;

        [SerializeField]
        [Tooltip("Seconds the spin, whirlwind and drift take to die away at the end.")]
        [Min(0.01f)]
        private float windDownTime = 0.8f;

        [SerializeField]
        [Tooltip("Animator state played from the spin up to the end. Empty keeps the idle.")]
        private string spinAnimation = "";

        [SerializeField] private TornadoEffect.Settings tornado = new TornadoEffect.Settings();

        [Header("Spiral")]
        [SerializeField]
        [Tooltip("Seconds it spends spiralling out, throwing daggers.")]
        [Min(0.1f)]
        private float duration = 6f;

        [SerializeField]
        [Tooltip("Radius of its circle round the middle as the spiral starts.")]
        [Min(0f)]
        private float startRadius = 0.75f;

        [SerializeField]
        [Tooltip("Radius of its circle by the end. The arena's edge holds it in regardless.")]
        [Min(0f)]
        private float endRadius = 7f;

        [SerializeField]
        [Tooltip("Speed it has built up to by the end of the spiral, in units per second round its circle, whatever the circle's size.")]
        [Min(0f)]
        private float orbitSpeed = 6f;

        [SerializeField]
        [Tooltip("Speed it starts the spiral at. It speeds up steadily to Orbit Speed as the spiral widens.")]
        [Min(0f)]
        private float spiralStartSpeed = 1f;

        [Header("Laps")]
        [SerializeField]
        [Tooltip("Full circles it makes at the spiral's end radius before winding down.")]
        [Min(0f)]
        private float finalLaps = 3f;

        [SerializeField]
        [Tooltip("Speed it goes round the laps at, in units per second.")]
        [Min(0f)]
        private float lapSpeed = 16f;

        [Header("Daggers")]
        [SerializeField]
        private EnemyProjectile daggerPrefab;

        [SerializeField]
        [Tooltip("Daggers thrown a second while spiralling, each in a random direction.")]
        [Min(0f)]
        private float daggersPerSecond = 20f;

        [SerializeField]
        [Tooltip("Height above its feet the daggers fly at.")]
        [Min(0f)]
        private float daggerHeight = 0.5f;

        [SerializeField]
        [Tooltip("How far out from its middle each dagger appears.")]
        [Min(0f)]
        private float daggerSpawnDistance = 0.8f;

        public override IEnumerator Perform(Enemy enemy)
        {
            Vector3 centre = enemy.ArenaCentre ?? enemy.transform.position;
            centre.y = enemy.transform.position.y;

            enemy.PlayIdleAnimation();
            yield return Glide(enemy, centre);

            TornadoEffect whirlwind = TornadoEffect.Create(enemy.transform, tornado);

            if (!string.IsNullOrEmpty(spinAnimation))
            {
                enemy.PlayAnimation(spinAnimation);
            }

            // Spiral in progress, kept between frames.
            float angle = Random.Range(0f, 360f);
            float radius = 0f;
            Vector3 onPath = enemy.transform.position;
            float daggerDebt = 0f;
            float clock = 0f;
            float held = spinUpTime + holdTime;
            float total = held + duration;

            while (clock < total)
            {
                yield return null;

                if (enemy.Health.IsDead)
                {
                    whirlwind.Release();
                    yield break;
                }

                float deltaTime = Time.deltaTime;
                clock += deltaTime;

                float wound = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / spinUpTime));
                enemy.SpinOverride = spinSpeed * wound;
                whirlwind.Intensity = wound;

                if (clock < spinUpTime)
                {
                    enemy.VelocityOverride = Vector3.zero;
                    continue;
                }

                if (clock < held)
                {
                    // Spinning on the spot in the middle, held there if nudged.
                    Vector3 toCentre = centre - enemy.transform.position;
                    toCentre.y = 0f;
                    enemy.VelocityOverride = toCentre * PathCorrection;

                    if (throwWhileHolding)
                    {
                        ThrowDue(enemy, ref daggerDebt, deltaTime);
                    }

                    continue;
                }

                float spiralled = Mathf.Clamp01((clock - held) / duration);
                float wanted = Mathf.Lerp(startRadius, endRadius, spiralled);

                // Round the circle at a steady speed, so a wide circle does
                // not whip it round faster than a tight one.
                // Picks up speed steadily as the spiral widens.
                float spiralSpeed = Mathf.Lerp(spiralStartSpeed, orbitSpeed, spiralled);
                angle += spiralSpeed / Mathf.Max(radius, 0.5f) * Mathf.Rad2Deg * deltaTime;

                // Eased onto the starting circle rather than jumped to it.
                radius = Mathf.MoveTowards(radius, wanted, Mathf.Max(orbitSpeed, startRadius / 0.3f) * deltaTime);

                Vector3 next = Spiral(enemy, centre, angle, radius);
                Vector3 path = (next - onPath) / Mathf.Max(deltaTime, 0.0001f);
                onPath = next;

                // Following the path, and drawn back onto it if pushed off.
                Vector3 velocity = path + (next - enemy.transform.position) * PathCorrection;
                velocity.y = 0f;
                enemy.VelocityOverride = velocity;

                ThrowDue(enemy, ref daggerDebt, deltaTime);
            }

            // Laps: round the full circle at full speed.
            float lapped = 0f;
            float lapTotal = finalLaps * 360f;

            while (lapped < lapTotal)
            {
                yield return null;

                if (enemy.Health.IsDead)
                {
                    whirlwind.Release();
                    yield break;
                }

                float deltaTime = Time.deltaTime;
                float step = lapSpeed / Mathf.Max(radius, 0.5f) * Mathf.Rad2Deg * deltaTime;

                angle += step;
                lapped += step;
                radius = Mathf.MoveTowards(radius, endRadius, orbitSpeed * deltaTime);

                Vector3 next = Spiral(enemy, centre, angle, radius);
                Vector3 path = (next - onPath) / Mathf.Max(deltaTime, 0.0001f);
                onPath = next;

                Vector3 velocity = path + (next - enemy.transform.position) * PathCorrection;
                velocity.y = 0f;
                enemy.VelocityOverride = velocity;

                ThrowDue(enemy, ref daggerDebt, deltaTime);
            }

            // Winds down where the spiral left it, drifting to a stop.
            Vector3 drift = enemy.VelocityOverride ?? Vector3.zero;
            float spin = enemy.SpinOverride ?? 0f;
            float winding = 0f;

            while (winding < windDownTime)
            {
                yield return null;

                if (enemy.Health.IsDead)
                {
                    break;
                }

                winding += Time.deltaTime;
                float left = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(winding / windDownTime));

                enemy.SpinOverride = spin * left;
                enemy.VelocityOverride = drift * left;
                whirlwind.Intensity = left;
            }

            whirlwind.Release();
            enemy.SpinOverride = null;
            enemy.VelocityOverride = null;
            enemy.PlayIdleAnimation();
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

        /// <summary>
        /// The point <paramref name="radius"/> out from <paramref name="centre"/>
        /// at <paramref name="angle"/>, drawn in until it is inside the arena.
        /// Only the point is drawn in, never the spiral's own radius, so along
        /// an edge it slides round the edge, and swings back out to the full
        /// circle once past it.
        /// </summary>
        private static Vector3 Spiral(Enemy enemy, Vector3 centre, float angle, float radius)
        {
            Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 point = centre + outward * radius;
            ArenaBounds arena = enemy.Arena;

            if (arena != null)
            {
                // Fine steps, so the edge it slides along is smooth.
                for (int i = 0; i < 80 && radius > 0.05f && !arena.Contains(point); i++)
                {
                    radius *= 0.97f;
                    point = centre + outward * radius;
                }
            }

            return point;
        }

        /// <summary>Throws the daggers due over <paramref name="deltaTime"/>, carrying the fraction over.</summary>
        private void ThrowDue(Enemy enemy, ref float debt, float deltaTime)
        {
            debt += daggersPerSecond * deltaTime;

            while (debt >= 1f)
            {
                debt -= 1f;
                Throw(enemy);
            }
        }

        private void Throw(Enemy enemy)
        {
            if (daggerPrefab == null)
            {
                return;
            }

            Vector3 direction = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 from = enemy.transform.position + Vector3.up * daggerHeight + direction * daggerSpawnDistance;

            EnemyProjectile dagger = EnemyProjectile.Spawn(daggerPrefab, from, Quaternion.LookRotation(direction, Vector3.up));
            dagger.Launch(direction);
        }

        private void Reset()
        {
            delayAfter = 1.5f;
            Movement.type = MovementType.StandStill;
        }
    }
}
