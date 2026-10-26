using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Lobs a potion in a high arc to land near the player, where it shatters
    /// into a spreading red pool that hurts whoever stands in it.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The attack's own animation (PotionLob) plays, the enemy turning
    ///      toward the player as usual.
    ///   2. <see cref="releaseTime"/> seconds in, the potion in the hand
    ///      vanishes and a <see cref="potionPrefab"/> is thrown from where it
    ///      was, as a <see cref="LobbedPotion"/>.
    ///   3. <see cref="recovery"/> seconds later the attack is over.
    ///
    /// The landing spot is picked at the throw: a random point within
    /// <see cref="landingSpread"/> of where the player stands then, on the
    /// floor. The potion does not track them after that.
    ///
    /// The hand's potion is the object called <see cref="handPotionName"/>
    /// under the enemy. It grows back in over <see cref="handReturnTime"/>,
    /// timed to be back in the hand just as the attack's Delay After runs
    /// out. Without one, the potion is thrown from the enemy's muzzle.
    ///
    /// The potion's own hit, the pool and the shatter all have their settings
    /// here, under Potion, Pool and Shatter.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Potion Lob", fileName = "PotionLob")]
    public sealed class PotionLobPattern : AttackPattern
    {
        [Header("Throw")]
        [SerializeField]
        [Tooltip("Art for the thrown potion. Needs no scripts or colliders; it is given its own at the throw.")]
        private GameObject potionPrefab;

        [SerializeField]
        [Tooltip("Seconds from the start of the attack animation to letting go of the potion.")]
        [Min(0f)]
        private float releaseTime = 1.5f;

        [SerializeField]
        [Tooltip("Seconds after the throw before the attack counts as over. Lets the animation finish.")]
        [Min(0f)]
        private float recovery = 0.5f;

        [SerializeField]
        [Tooltip("The potion lands at a random spot no further than this from where the player stood at the throw.")]
        [Min(0f)]
        private float landingSpread = 3f;

        [Header("Hand")]
        [SerializeField]
        [Tooltip("Name of the potion object in the enemy's hand, hidden when it throws. Empty, or not found, throws from the muzzle and hides nothing.")]
        private string handPotionName = "Potion";

        [SerializeField]
        [Tooltip("Seconds the hand's potion takes to grow back in. It is back in full just as Delay After runs out.")]
        [Min(0f)]
        private float handReturnTime = 0.5f;

        [Header("Potion")]
        [SerializeField] private LobbedPotion.Settings potion = new LobbedPotion.Settings();

        [Header("Pool")]
        [SerializeField] private PotionPool.Settings pool = new PotionPool.Settings();

        [Header("Shatter")]
        [SerializeField] private PotionShatter.Settings shatter = new PotionShatter.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            if (releaseTime > 0f)
            {
                yield return new WaitForSeconds(releaseTime);
            }

            if (enemy.HasTarget)
            {
                Throw(enemy);
            }

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private void Throw(Enemy enemy)
        {
            if (potionPrefab == null)
            {
                Debug.LogWarning($"[{name}] No potion prefab assigned.", this);
                return;
            }

            HandPotion hand = FindHandPotion(enemy);
            Vector3 from = hand != null ? hand.Centre : enemy.Muzzle.position;

            if (hand != null)
            {
                // Back in full just as the wait after the attack runs out.
                float hiddenFor = recovery + delayAfter - handReturnTime;
                hand.Hide(hiddenFor, handReturnTime);
            }

            Vector2 offset = Random.insideUnitCircle * landingSpread;
            Vector3 aim = enemy.Target.position + new Vector3(offset.x, 0f, offset.y);

            GameObject art = Instantiate(potionPrefab, from, Random.rotationUniform);
            LobbedPotion.Throw(art, FindFloor(aim), enemy.transform, potion, pool, shatter);
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
