using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss dashes to wherever it can reach the most swords in stones,
    /// wrenches them all out, each lit in a bright colour of its own, and
    /// throws them one after another at the player, to stick in and explode.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The boss looks over every <see cref="StoneSword"/> ready in its
    ///      stone, and every spot within <see cref="dashRange"/> of it, and
    ///      picks the spot with the most swords within
    ///      <see cref="pullRadius"/>; of spots that tie, the nearest. Spots
    ///      closer than <see cref="rockClearance"/> to a sword, or outside
    ///      the boss's <see cref="Enemy.Arena"/>, are passed over.
    ///   2. It dashes there, through <see cref="Enemy.VelocityOverride"/>, so
    ///      walls stop it: speeding up at <see cref="dashAcceleration"/> to
    ///      no more than <see cref="dashSpeed"/>, and slowing again at the
    ///      same rate to stop right on the spot, leaving a
    ///      <see cref="DustTrail"/> behind it. Already at the best spot, it
    ///      stays put.
    ///   3. Every sword in reach is pulled at once, each starting up to
    ///      <see cref="pullStagger"/> seconds late so they tug out of step.
    ///      The colours are spread evenly round the hue wheel from a random
    ///      start, so no two in one pull look alike.
    ///   4. Once all hang free, <see cref="hoverTime"/> later, they are thrown
    ///      in a random order, <see cref="launchInterval"/> apart.
    ///   5. <see cref="recovery"/> seconds later the attack is over. Swords
    ///      still in the air carry on by themselves.
    ///
    /// What each sword does from the pull on -- dust, flight, sticking in,
    /// the fuse and the explosion -- is set in <see cref="swords"/>. With no
    /// sword ready within reach the attack is skipped.
    ///
    /// Leave Range Limit above empty. The Attack Animation plays from the
    /// start of the dash.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Stone Swords", fileName = "StoneSwords")]
    public sealed class StoneSwordPattern : AttackPattern
    {
        [Header("Finding a spot")]
        [SerializeField]
        [Tooltip("Furthest the boss dashes to reach more swords.")]
        [Min(0f)]
        private float dashRange = 10f;

        [SerializeField]
        [Tooltip("Swords within this distance of where the boss ends up are pulled.")]
        [Min(0.1f)]
        private float pullRadius = 8f;

        [SerializeField]
        [Tooltip("The boss never picks a spot closer than this to a sword, so it does not end up in a rock.")]
        [Min(0f)]
        private float rockClearance = 1.5f;

        [SerializeField]
        [Tooltip("Distance between the spots looked at. Smaller is more exact and slower to pick.")]
        [Min(0.25f)]
        private float searchSpacing = 1f;

        [Header("Dash")]
        [SerializeField]
        [Tooltip("Top speed of the dash, in units per second. A short dash may never reach it.")]
        [Min(0.1f)]
        private float dashSpeed = 25f;

        [SerializeField]
        [Tooltip("Speed gained, and lost again, every second at the start and end of the dash. Lower is a softer start and stop.")]
        [Min(1f)]
        private float dashAcceleration = 60f;

        [SerializeField]
        [Tooltip("Dust kicked up behind the boss as it dashes. Only while it moves faster than the dust's Min Speed.")]
        private DustTrail.Settings dashDust = new DustTrail.Settings();

        [SerializeField]
        [Tooltip("Seconds the boss stands after the dash before the swords start to move.")]
        [Min(0f)]
        private float settleTime = 0.15f;

        [Header("Pull")]
        [SerializeField]
        [Tooltip("Most seconds a sword's pull starts late, so they tug out of step.")]
        [Min(0f)]
        private float pullStagger = 0.15f;

        [SerializeField]
        [Tooltip("Saturation of the swords' colours.")]
        [Range(0f, 1f)]
        private float saturation = 1f;

        [SerializeField]
        [Tooltip("Brightness of the swords' colours. Above 1 blooms.")]
        [Min(0f)]
        private float colourIntensity = 2.5f;

        [Header("Throw")]
        [SerializeField]
        [Tooltip("Seconds every sword hangs free, turning on the player, before the first is thrown.")]
        [Min(0f)]
        private float hoverTime = 0.4f;

        [SerializeField]
        [Tooltip("Seconds between one sword being thrown and the next.")]
        [Min(0f)]
        private float launchInterval = 0.25f;

        [SerializeField]
        [Tooltip("Seconds after the last throw before the attack counts as over.")]
        [Min(0f)]
        private float recovery = 0.5f;

        [SerializeField] private StoneSword.Settings swords = new StoneSword.Settings();

        public override bool CanPerform(Enemy enemy)
        {
            float reach = dashRange + pullRadius;
            Vector3 origin = enemy.transform.position;

            foreach (StoneSword sword in StoneSword.All)
            {
                if (sword.IsReady && Flat(sword.Position - origin).sqrMagnitude <= reach * reach)
                {
                    return true;
                }
            }

            return false;
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            List<StoneSword> picked = new List<StoneSword>();
            Vector3 spot = PickSpot(enemy, picked);

            if (picked.Count == 0)
            {
                yield break;
            }

            foreach (object frame in Dash(enemy, spot))
            {
                yield return frame;
            }

            enemy.VelocityOverride = Vector3.zero;

            if (settleTime > 0f)
            {
                yield return new WaitForSeconds(settleTime);
            }

            if (enemy.Health.IsDead)
            {
                yield break;
            }

            float hue = Random.value;
            float longest = 0f;

            for (int i = picked.Count - 1; i >= 0; i--)
            {
                // Something else may have taken it during the dash.
                if (!picked[i].IsReady)
                {
                    picked.RemoveAt(i);
                }
            }

            for (int i = 0; i < picked.Count; i++)
            {
                float jitter = Random.Range(-0.3f, 0.3f) / picked.Count;
                Color colour = Color.HSVToRGB(Mathf.Repeat(hue + (float)i / picked.Count + jitter, 1f), saturation, 1f)
                               * colourIntensity;
                float delay = Random.Range(0f, pullStagger);
                longest = Mathf.Max(longest, delay);
                picked[i].BeginPull(enemy, swords, colour, delay);
            }

            // Until every sword hangs free. A safety limit in case one is cut short.
            float limit = longest + swords.tintTime + swords.jerkCount * (swords.jerkTime + swords.jerkPause)
                          + swords.yankTime + 1f;

            for (float elapsed = 0f; elapsed < limit && picked.Exists(sword => sword.IsPulling); elapsed += Time.deltaTime)
            {
                yield return null;
            }

            if (hoverTime > 0f)
            {
                yield return new WaitForSeconds(hoverTime);
            }

            // Thrown in a random order; should the boss die first, the rest drop by themselves.
            for (int i = picked.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (picked[i], picked[swap]) = (picked[swap], picked[i]);
            }

            for (int i = 0; i < picked.Count; i++)
            {
                if (enemy.Health.IsDead)
                {
                    yield break;
                }

                picked[i].Launch(enemy.HasTarget ? enemy.Target : null);

                if (launchInterval > 0f && i < picked.Count - 1)
                {
                    yield return new WaitForSeconds(launchInterval);
                }
            }

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }

            enemy.VelocityOverride = null;
        }

        /// <summary>
        /// The spot within the dash range reaching the most ready swords, the
        /// nearest of any that tie; <paramref name="picked"/> is filled with
        /// the swords it reaches.
        /// </summary>
        private Vector3 PickSpot(Enemy enemy, List<StoneSword> picked)
        {
            Vector3 origin = enemy.transform.position;
            float reach = dashRange + pullRadius;
            List<StoneSword> nearby = new List<StoneSword>();

            foreach (StoneSword sword in StoneSword.All)
            {
                if (sword.IsReady && Flat(sword.Position - origin).sqrMagnitude <= reach * reach)
                {
                    nearby.Add(sword);
                }
            }

            if (nearby.Count == 0)
            {
                return origin;
            }

            Vector3 best = origin;
            int bestCount = Count(nearby, origin);
            float bestDistance = 0f;
            int steps = Mathf.CeilToInt(dashRange / searchSpacing);

            for (int x = -steps; x <= steps; x++)
            {
                for (int z = -steps; z <= steps; z++)
                {
                    Vector3 offset = new Vector3(x, 0f, z) * searchSpacing;
                    float distance = offset.magnitude;

                    if (distance > dashRange || distance <= 0f)
                    {
                        continue;
                    }

                    Vector3 spot = origin + offset;

                    if ((enemy.Arena != null && !enemy.Arena.Contains(spot)) || InRock(nearby, spot))
                    {
                        continue;
                    }

                    int count = Count(nearby, spot);

                    if (count > bestCount || (count == bestCount && distance < bestDistance))
                    {
                        best = spot;
                        bestCount = count;
                        bestDistance = distance;
                    }
                }
            }

            foreach (StoneSword sword in nearby)
            {
                if (Flat(sword.Position - best).sqrMagnitude <= pullRadius * pullRadius)
                {
                    picked.Add(sword);
                }
            }

            return best;
        }

        private int Count(List<StoneSword> nearby, Vector3 spot)
        {
            int count = 0;

            foreach (StoneSword sword in nearby)
            {
                if (Flat(sword.Position - spot).sqrMagnitude <= pullRadius * pullRadius)
                {
                    count++;
                }
            }

            return count;
        }

        private bool InRock(List<StoneSword> nearby, Vector3 spot)
        {
            foreach (StoneSword sword in nearby)
            {
                if (Flat(sword.Position - spot).sqrMagnitude < rockClearance * rockClearance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Dashes to <paramref name="destination"/>, speeding up from rest and
        /// slowing again in time to stop right on it.
        /// </summary>
        private IEnumerable Dash(Enemy enemy, Vector3 destination)
        {
            float distance = Flat(destination - enemy.transform.position).magnitude;

            // As long as the trip should take, with time to spare; a wall in
            // the way ends it then.
            float cruise = Mathf.Max(0f, distance - dashSpeed * dashSpeed / dashAcceleration) / dashSpeed;
            float limit = cruise + 2f * Mathf.Min(dashSpeed / dashAcceleration, Mathf.Sqrt(distance / dashAcceleration)) + 0.5f;
            float speed = 0f;
            float elapsed = 0f;
            DustTrail dust = DustTrail.On(enemy.gameObject, dashDust);

            while (!enemy.Health.IsDead && elapsed < limit)
            {
                Vector3 remaining = Flat(destination - enemy.transform.position);
                float left = remaining.magnitude;

                if (left <= 0.1f)
                {
                    break;
                }

                float stopping = Mathf.Sqrt(2f * dashAcceleration * left);
                speed = Mathf.Min(speed + dashAcceleration * Time.deltaTime, dashSpeed, stopping);
                enemy.VelocityOverride = remaining / left * speed;
                dust.KeepEmitting();

                yield return null;
                elapsed += Time.deltaTime;
            }

            enemy.VelocityOverride = Vector3.zero;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 3f;
            swords.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
