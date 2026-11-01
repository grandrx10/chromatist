using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer lowers puppets into the arena one at a time, every
    /// second or two, for as long as the attack lasts.
    /// </summary>
    /// <remarks>
    /// Over <see cref="duration"/> seconds a puppet is summoned after each
    /// wait of between <see cref="interval"/>'s two values, picked afresh
    /// every time, until <see cref="maxSummons"/> have come this attack. The
    /// attack lasts its full duration even once the cap is reached.
    ///
    /// Each puppet is picked at random from <see cref="puppets"/>, weighted,
    /// and placed in the boss's <see cref="Enemy.Arena"/> between
    /// <see cref="minRadius"/> and <see cref="maxRadius"/> of the player, or
    /// anywhere in the arena if the ring has no room. One with a
    /// <see cref="PuppetDescent"/> drops in on its strings and fights once it
    /// has landed and hung limp for its Inactive Time. Puppets outlive the
    /// boss and fight on after it dies.
    ///
    /// The boss needs an arena, which <see cref="PuppeteerIntro"/> gives it;
    /// without one the attack summons nothing and just waits out its time.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Summon Puppets", fileName = "SummonPuppets")]
    public sealed class SummonPuppetsPattern : AttackPattern
    {
        [Header("Summon")]
        [SerializeField]
        [Tooltip("Seconds the whole attack lasts.")]
        [Min(0f)]
        private float duration = 10f;

        [SerializeField]
        [Tooltip("Shortest and longest seconds between one puppet and the next, picked afresh each time. The first comes after one such wait.")]
        private Vector2 interval = new Vector2(1f, 2f);

        [SerializeField]
        [Tooltip("Most puppets summoned in one attack.")]
        [Min(1)]
        private int maxSummons = 5;

        [SerializeField]
        [Tooltip("Enemies to pick from, with how likely each is.")]
        private List<HammerSummonPattern.SummonOption> puppets = new List<HammerSummonPattern.SummonOption>();

        [Header("Placement")]
        [SerializeField]
        [Tooltip("Closest to the player a puppet can appear.")]
        [Min(0f)]
        private float minRadius = 4f;

        [SerializeField]
        [Tooltip("Furthest from the player a puppet can appear. If nowhere in this ring will do, it appears anywhere in the arena.")]
        [Min(0f)]
        private float maxRadius = 8f;

        public override IEnumerator Perform(Enemy enemy)
        {
            ArenaBounds arena = enemy.Arena;

            if (arena == null)
            {
                Debug.LogWarning($"[SummonPuppets] {enemy.name} has no arena to summon puppets into.", enemy);
            }

            SummonedUnits roster = SummonedUnits.On(enemy);
            float shortest = Mathf.Max(0.05f, Mathf.Min(interval.x, interval.y));
            float longest = Mathf.Max(shortest, interval.y);

            float clock = 0f;
            float next = Random.Range(shortest, longest);
            int summoned = 0;

            while (clock < duration)
            {
                yield return null;
                clock += Time.deltaTime;

                if (clock < next || summoned >= maxSummons || arena == null)
                {
                    continue;
                }

                next += Random.Range(shortest, longest);

                Enemy prefab = HammerSummonPattern.Pick(puppets);

                if (prefab == null || !enemy.HasTarget)
                {
                    continue;
                }

                Enemy unit = PuppetSummoning.Summon(prefab, arena, enemy.Target.position, minRadius, maxRadius,
                                                    null, false, enemy);

                if (unit != null)
                {
                    roster.Add(unit);
                    summoned++;
                }
            }
        }

        private void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
        }
    }
}
