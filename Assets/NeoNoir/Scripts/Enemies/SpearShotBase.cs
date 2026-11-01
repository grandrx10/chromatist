using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Shared by the spear shot attacks: rings of warning circles laid round
    /// the boss, spears raining into each circle as its warning fills. Each
    /// attack only says which rings go down, where and when; this does the
    /// rest.
    /// </summary>
    /// <remarks>
    /// Each circle is a <see cref="WarningCircle"/> of
    /// <see cref="circleRadius"/> that fills over <see cref="warnTime"/>.
    /// <see cref="spearsPerCircle"/> <see cref="HallSpear"/>s, thrown in from
    /// the boss's side, land in it just as it fills -- one in the middle, the
    /// rest anywhere inside -- and a player in the circle then takes
    /// <see cref="damage"/>, once per circle. The circle bursts once,
    /// <see cref="circleBurst"/>, rather than each spear bursting, and the
    /// camera jolts once for every frame circles strike.
    ///
    /// Every circle is laid flat at the height of the floor under the boss as
    /// the attack starts, wherever it falls -- walls and the arena's edge do
    /// not stop it. Without floor on the Floor Mask under the boss, they are
    /// laid at its pivot. The attack ends once the last circle's spears are
    /// down; the spears sink away by themselves. If the boss dies, circles not
    /// yet struck vanish.
    ///
    /// Before the first ring, the most spears and bursts the attack ever has
    /// out at once are made ready, a few a frame, so none are built
    /// mid-attack; after the first time they are already pooled and this
    /// takes no time.
    ///
    /// The spears' own Damage, Landing Burst and shake are ignored: the
    /// circle does the hurting, bursting and shaking.
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    public abstract class SpearShotBase : AttackPattern
    {
        private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

        // Made ready a few a frame before the first ring, so no frame builds many.
        private const int SpearsWarmedPerFrame = 12;
        private const int BurstsWarmedPerFrame = 3;

        [Header("Circles")]
        [SerializeField]
        [Tooltip("Radius of each circle: the warning, and the area the spears hurt.")]
        [Min(0.1f)]
        protected float circleRadius = 2.5f;

        [SerializeField]
        [Tooltip("Seconds each warning fills before its spears land.")]
        [Min(0f)]
        protected float warnTime = 0.5f;

        [SerializeField]
        [Tooltip("Damage to a player in a circle as its spears land. Once per circle.")]
        [Min(0f)]
        private float damage = 10f;

        [SerializeField]
        private Color warningColour = new Color(1f, 0.75f, 0.2f, 1f);

        [SerializeField]
        [Tooltip("Thickness of each warning circle's outline.")]
        [Min(0f)]
        private float warningOutline = 0.1f;

        [SerializeField]
        [Tooltip("Layers the floor under the boss is looked for on. Every circle is laid at its height; without it, at the boss's pivot.")]
        private LayerMask floorMask = 1;

        [Header("Spears")]
        [SerializeField]
        [Tooltip("Spears that land in each circle.")]
        [Min(1)]
        protected int spearsPerCircle = 4;

        [SerializeField]
        [Tooltip("How far into the circle the spears other than the middle one land, as a fraction of its radius.")]
        [Range(0f, 1f)]
        private float spearSpread = 0.75f;

        [SerializeField]
        [Tooltip("The spears, as in the hall run. Set the Spear Prefab. Their Damage, Landing Burst and shake are ignored; the impact colours and material are used for the circle's burst.")]
        private HallSpear.Settings spear = new HallSpear.Settings { lingerTime = 1f };

        [Header("Impact")]
        [SerializeField]
        [Tooltip("The one ring and debris burst where each circle's spears land. Ring Radius and Debris Count both 0 for none.")]
        private HammerImpact.Settings circleBurst = new HammerImpact.Settings
        {
            ringRadius = 3f,
            ringWidth = 0.5f,
            ringTime = 0.3f,
            debrisCount = 6,
            debrisSize = 0.3f,
            debrisUpSpeed = new Vector2(5f, 9f),
            debrisOutSpeed = new Vector2(2f, 5f),
            debrisTime = 0.6f,
        };

        [SerializeField]
        [Tooltip("How far the camera jolts each frame circles strike. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.2f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.15f;

        /// <summary>One ring of circles, laid <see cref="time"/> seconds into the attack.</summary>
        protected struct Ring
        {
            public float time;
            public float distance;
            public float angle;
            public int circles;
        }

        /// <summary>The colours of one circle: its warning and burst, and the spears that land in it.</summary>
        protected struct CircleColours
        {
            public Color warning;
            public Color spear;
        }

        /// <summary>One circle waiting on its spears.</summary>
        private sealed class Strike
        {
            public WarningCircle warning;
            public Vector3 floor;
            public float age;
            public bool thrown;
            public CircleColours? colours;
        }

        /// <summary>The rings this attack lays, in any order.</summary>
        protected abstract List<Ring> PlanRings();

        /// <summary>
        /// Colours for the next circle laid, asked once per circle. Null, as
        /// here, keeps the Warning Colour, the spears as modelled and the
        /// spears' Impact Colour.
        /// </summary>
        protected virtual CircleColours? NextCircleColours()
        {
            return null;
        }

        /// <summary>A flat nudge for the next circle laid, off its even place in its ring. None, as here.</summary>
        protected virtual Vector3 NextCircleOffset()
        {
            return Vector3.zero;
        }

        /// <summary>Called every frame the attack runs, from the first ring on, with seconds since it.</summary>
        protected virtual void Striking(Enemy enemy, float clock)
        {
        }

        /// <summary>
        /// Runs before anything else, for an attack that moves the boss
        /// first. The rings are laid round wherever it ends up.
        /// </summary>
        protected virtual IEnumerator Arrive(Enemy enemy)
        {
            yield break;
        }

        /// <summary>Called once the last circle has struck, unless the boss died first.</summary>
        protected virtual void Finished(Enemy enemy)
        {
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            if (spear.spearPrefab == null)
            {
                Debug.LogWarning($"[{name}] has no Spear Prefab set, so it throws nothing.", this);
                yield break;
            }

            yield return Arrive(enemy);

            if (enemy.Health.IsDead)
            {
                yield break;
            }

            // Every circle is laid at the height of the floor under the boss.
            Vector3 centre = enemy.transform.position;
            centre.y = FindFloor(centre, out float floorHeight) ? floorHeight : centre.y;
            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            List<Ring> rings = PlanRings();
            rings.Sort((a, b) => a.time.CompareTo(b.time));

            foreach (object frame in Warm(rings))
            {
                yield return frame;
            }

            if (enemy.Health.IsDead)
            {
                yield break;
            }

            List<Strike> strikes = new List<Strike>();
            float clock = 0f;
            int ring = 0;

            while (ring < rings.Count || strikes.Count > 0)
            {
                while (ring < rings.Count && clock >= rings[ring].time)
                {
                    LayRing(enemy, centre, rings[ring], strikes);
                    ring++;
                }

                bool struck = false;

                for (int i = strikes.Count - 1; i >= 0; i--)
                {
                    if (Advance(strikes[i], centre, player))
                    {
                        strikes.RemoveAt(i);
                        struck = true;
                    }
                }

                if (struck && shakeStrength > 0f)
                {
                    TopDownCamera.Shake(shakeStrength, shakeDuration);
                }

                Striking(enemy, clock);

                yield return null;
                clock += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    yield break;
                }
            }

            Finished(enemy);
        }

        /// <summary>
        /// Makes ready, a few a frame, the most spears and bursts the attack
        /// has out at once: those of every ring laid within one spear's, or
        /// one burst's, life.
        /// </summary>
        private IEnumerable Warm(List<Ring> rings)
        {
            float spearLife = spear.flightTime + spear.lingerTime + spear.sinkTime;
            int spears = CirclesWithin(spearLife, rings) * spearsPerCircle;

            bool bursting = circleBurst.ringRadius > 0f || circleBurst.debrisCount > 0;
            float burstLife = Mathf.Max(circleBurst.ringTime, circleBurst.debrisCount > 0 ? circleBurst.debrisTime : 0f);
            int bursts = bursting ? CirclesWithin(burstLife, rings) : 0;

            for (int spearsReady = 0, burstsReady = 0; spearsReady < spears || burstsReady < bursts; )
            {
                spearsReady = Mathf.Min(spears, spearsReady + SpearsWarmedPerFrame);
                burstsReady = Mathf.Min(bursts, burstsReady + BurstsWarmedPerFrame);

                HallSpear.Prewarm(spear.spearPrefab, spearsReady);

                if (burstsReady > 0)
                {
                    HammerImpact.Prewarm(burstsReady, circleBurst.debrisCount);
                }

                yield return null;
            }
        }

        /// <summary>The most circles in rings laid within <paramref name="seconds"/> of one another. <paramref name="rings"/> is in time order.</summary>
        private static int CirclesWithin(float seconds, List<Ring> rings)
        {
            int most = 0;

            for (int first = 0; first < rings.Count; first++)
            {
                int circles = 0;

                for (int i = first; i < rings.Count && rings[i].time - rings[first].time <= seconds; i++)
                {
                    circles += rings[i].circles;
                }

                most = Mathf.Max(most, circles);
            }

            return most;
        }

        /// <summary>Starts a ring of circles round <paramref name="centre"/>, at its height.</summary>
        private void LayRing(Enemy enemy, Vector3 centre, Ring ring, List<Strike> strikes)
        {
            for (int i = 0; i < ring.circles; i++)
            {
                Vector3 outward = Quaternion.AngleAxis(ring.angle + i * 360f / ring.circles, Vector3.up) * Vector3.forward;
                Vector3 floor = centre + outward * ring.distance + NextCircleOffset();

                CircleColours? colours = NextCircleColours();
                Color colour = colours.HasValue ? colours.Value.warning : warningColour;

                WarningCircle warning = WarningCircle.Create(floor + Vector3.up * 0.03f, circleRadius, warningOutline, null, colour);
                warning.DestroyWhen(() => enemy == null || enemy.Health.IsDead);
                strikes.Add(new Strike { warning = warning, floor = floor, colours = colours });
            }
        }

        /// <summary>Moves a circle on a frame: filling, throwing its spears in time to land as it fills, then striking. True once it has struck.</summary>
        private bool Advance(Strike strike, Vector3 centre, PlayerHealth player)
        {
            strike.age += Time.deltaTime;

            if (strike.warning != null)
            {
                strike.warning.Fill = warnTime > 0f ? strike.age / warnTime : 1f;
            }

            if (!strike.thrown && strike.age >= warnTime - spear.flightTime)
            {
                strike.thrown = true;
                Throw(strike.floor, centre, strike.colours);
            }

            if (strike.age < warnTime)
            {
                return false;
            }

            if (strike.warning != null)
            {
                Destroy(strike.warning.gameObject);
            }

            if (circleBurst.ringRadius > 0f || circleBurst.debrisCount > 0)
            {
                Color burstColour = strike.colours.HasValue ? strike.colours.Value.warning : spear.impactColour;
                HammerImpact.Create(strike.floor, circleBurst, spear.impactMaterial,
                    burstColour, spear.impactEdgeWidth, spear.impactEdgeColour);
            }

            if (player != null && !player.IsDead)
            {
                Vector3 offset = player.transform.position - strike.floor;
                offset.y = 0f;

                if (offset.sqrMagnitude <= circleRadius * circleRadius)
                {
                    player.TakeDamage(damage, strike.floor);
                }
            }

            return true;
        }

        /// <summary>
        /// The flat direction a circle's spears are thrown in from: the
        /// boss's side, unless an attack says otherwise.
        /// </summary>
        protected virtual Vector3 ThrownFrom(Vector3 floor, Vector3 centre)
        {
            Vector3 towardBoss = centre - floor;
            towardBoss.y = 0f;
            return towardBoss.sqrMagnitude > 0.0001f ? towardBoss.normalized : Vector3.forward;
        }

        /// <summary>Throws the circle's spears in: one in the middle, the rest scattered inside.</summary>
        private void Throw(Vector3 floor, Vector3 centre, CircleColours? colours)
        {
            Vector3 towardBoss = ThrownFrom(floor, centre);
            Color? tint = colours.HasValue ? colours.Value.spear : (Color?)null;

            for (int i = 0; i < spearsPerCircle; i++)
            {
                Vector3 spot = floor;

                if (i > 0)
                {
                    Vector2 scatter = Random.insideUnitCircle * (circleRadius * spearSpread);
                    spot += new Vector3(scatter.x, 0f, scatter.y);
                }

                // Hurting, bursting and shaking are the circle's job.
                HallSpear.Throw(spot, towardBoss, spear, null, quiet: true, tint: tint);
            }
        }

        /// <summary>Height of the floor under <paramref name="spot"/>, if there is some below it or a little above.</summary>
        protected bool FindFloor(Vector3 spot, out float floor)
        {
            const float Above = 4f;
            const float Below = 20f;

            Vector3 from = spot + Vector3.up * Above;
            int hits = Physics.RaycastNonAlloc(from, Vector3.down, FloorHits, Above + Below, floorMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            floor = default;

            // The nearest floor below, seen through the boss's own body and the player.
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

        protected virtual void Reset()
        {
            delayAfter = 1.5f;
            Movement.type = MovementType.StandStill;
        }
    }
}
