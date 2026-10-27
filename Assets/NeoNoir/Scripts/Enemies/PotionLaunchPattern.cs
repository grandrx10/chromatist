using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Launches rings of potions around the enemy, each ring tighter than the
    /// last, filling its perimeter with pools.
    /// </summary>
    /// <remarks>
    /// First, with <see cref="blinkToCentre"/> on, the enemy dissolves into
    /// runes that stream to the middle of the arena, and re-forms there
    /// standing on the floor, as a <see cref="RuneBlink"/>. The rings are
    /// centred on wherever it ends up.
    ///
    /// Then, in order, once per ring:
    ///   1. The attack's own animation (WitchPotion) plays from its start.
    ///   2. <see cref="releaseTime"/> seconds in, the potion in the hand
    ///      vanishes and a ring of <see cref="potionPrefab"/>s is thrown from
    ///      where it was, as <see cref="LobbedPotion"/>s, landing spaced
    ///      round a circle centred on the enemy.
    ///   3. The next ring waits until this ring's pools have faded: it is
    ///      timed to land just as they finish shrinking away, plus
    ///      <see cref="ringGap"/>.
    /// The first ring has radius <see cref="startRadius"/>; each after it is
    /// <see cref="radiusStep"/> smaller, down to <see cref="endRadius"/>,
    /// which is the last. <see cref="recovery"/> seconds after the last throw
    /// the attack is over.
    ///
    /// Each ring has just enough potions for their pools, at full size, to
    /// touch edge to edge round it, so a smaller ring has fewer. Each ring is
    /// turned a random amount, so the potions never land in the same places
    /// twice running.
    ///
    /// The hand's potion is the object called <see cref="handPotionName"/>
    /// under the enemy. It grows back in over <see cref="handReturnTime"/>,
    /// timed to be back in the hand just as the next ring is thrown, or after
    /// the last, just as the attack's Delay After runs out. Without one, the
    /// potions are thrown from the enemy's muzzle.
    ///
    /// The potions' own hit, the pools and the shatters all have their
    /// settings here, under Potion, Pool and Shatter.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Potion Launch", fileName = "PotionLaunch")]
    public sealed class PotionLaunchPattern : AttackPattern
    {
        [Header("Blink")]
        [SerializeField]
        [Tooltip("Before the first ring, dissolve into runes and re-form standing in the middle of the arena (the enemy's Arena Centre, set by Boss Intro). Without one, it re-forms where it stood.")]
        private bool blinkToCentre = true;

        [SerializeField] private RuneBlink.Settings blink = new RuneBlink.Settings();

        [Header("Rings")]
        [SerializeField]
        [Tooltip("Radius of the first ring, round the enemy.")]
        [Min(0f)]
        private float startRadius = 30f;

        [SerializeField]
        [Tooltip("How much smaller each ring is than the one before.")]
        [Min(0.1f)]
        private float radiusStep = 5f;

        [SerializeField]
        [Tooltip("Radius of the last ring. The rings stop once they reach it.")]
        [Min(0f)]
        private float endRadius = 10f;

        [SerializeField]
        [Tooltip("Extra seconds after a ring's pools have faded before the next ring lands.")]
        [Min(0f)]
        private float ringGap = 0f;

        [Header("Throw")]
        [SerializeField]
        [Tooltip("Art for each thrown potion. Needs no scripts or colliders; it is given its own at the throw.")]
        private GameObject potionPrefab;

        [SerializeField]
        [Tooltip("Seconds from the start of the attack animation to letting go of the ring.")]
        [Min(0f)]
        private float releaseTime = 1f;

        [SerializeField]
        [Tooltip("Seconds after the last throw before the attack counts as over. Lets the animation finish.")]
        [Min(0f)]
        private float recovery = 0.5f;

        [Header("Hand")]
        [SerializeField]
        [Tooltip("Name of the potion object in the enemy's hand, hidden when it throws. Empty, or not found, throws from the muzzle and hides nothing.")]
        private string handPotionName = "Potion";

        [SerializeField]
        [Tooltip("Seconds the hand's potion takes to grow back in. It is back in full just as the next ring is thrown.")]
        [Min(0f)]
        private float handReturnTime = 0.5f;

        [Header("Potion")]
        [SerializeField] private LobbedPotion.Settings potion = new LobbedPotion.Settings();

        [Header("Pool")]
        [SerializeField] private PotionPool.Settings pool = new PotionPool.Settings { damagePerSecond = 10f };

        [Header("Shatter")]
        [SerializeField] private PotionShatter.Settings shatter = new PotionShatter.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            HandPotion hand = FindHandPotion(enemy);

            // Every potion the biggest ring needs, made now rather than all in the frame it is thrown.
            if (potionPrefab != null)
            {
                ArtPool.Prewarm(potionPrefab, PotionsFor(Mathf.Max(startRadius, endRadius)));
            }

            if (blinkToCentre)
            {
                enemy.PlayIdleAnimation();
                yield return RuneBlink.Teleport(enemy, enemy.ArenaCentre ?? enemy.transform.position, blink);
                enemy.RestartAnimation(AttackAnimation);
            }

            // Lands the next ring just as this one's pools finish shrinking away.
            float waitBetween = Mathf.Max(0f, pool.duration + pool.shrinkTime + ringGap - releaseTime);

            bool first = true;

            for (float radius = startRadius; ; radius -= radiusStep)
            {
                bool last = radius - radiusStep < endRadius - 0.001f;

                if (!first)
                {
                    enemy.RestartAnimation(AttackAnimation);
                }

                first = false;

                if (releaseTime > 0f)
                {
                    yield return new WaitForSeconds(releaseTime);
                }

                // Back in full as the next ring goes, or as Delay After runs out after the last.
                float untilNeeded = last ? recovery + delayAfter : waitBetween + releaseTime;
                ThrowRing(enemy, hand, Mathf.Max(radius, endRadius), untilNeeded);

                if (last)
                {
                    break;
                }

                if (waitBetween > 0f)
                {
                    yield return new WaitForSeconds(waitBetween);
                }
            }

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private void ThrowRing(Enemy enemy, HandPotion hand, float radius, float untilNeeded)
        {
            if (potionPrefab == null)
            {
                Debug.LogWarning($"[{name}] No potion prefab assigned.", this);
                return;
            }

            Vector3 from = hand != null ? hand.Centre : enemy.Muzzle.position;

            if (hand != null)
            {
                hand.Hide(untilNeeded - handReturnTime, handReturnTime);
            }

            Vector3 centre = enemy.transform.position;
            int count = PotionsFor(radius);
            float turn = Random.value * Mathf.PI * 2f;

            for (int i = 0; i < count; i++)
            {
                float angle = turn + i * Mathf.PI * 2f / count;
                Vector3 aim = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                GameObject art = ArtPool.Take(potionPrefab, from, Random.rotationUniform);
                LobbedPotion.Throw(art, FindFloor(aim), enemy.transform, potion, pool, shatter);
            }
        }

        /// <summary>Fewest potions whose pools, at full size, touch edge to edge round a ring of <paramref name="radius"/>.</summary>
        private int PotionsFor(float radius)
        {
            if (radius <= pool.maxRadius)
            {
                return 3;
            }

            // Neighbours' centres one pool diameter apart, as a chord.
            float halfAngle = Mathf.Asin(pool.maxRadius / radius);
            return Mathf.Max(3, Mathf.CeilToInt(Mathf.PI / halfAngle));
        }

        private HandPotion FindHandPotion(Enemy enemy)
        {
            if (string.IsNullOrEmpty(handPotionName))
            {
                return null;
            }

            foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == handPotionName)
                {
                    return HandPotion.On(child.gameObject);
                }
            }

            Debug.LogWarning($"[{name}] No object called \"{handPotionName}\" under {enemy.name}; throwing from the muzzle.", this);
            return null;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 30f,
                                potion.groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private void Reset()
        {
            potion.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
