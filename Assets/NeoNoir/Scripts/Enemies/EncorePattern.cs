using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer's encore: presents rain down all over the arena, each
    /// going off the moment it lands.
    /// </summary>
    /// <remarks>
    /// For <see cref="duration"/> seconds, <see cref="presentsPerSecond"/>
    /// <see cref="GiftBox"/> presents drop from <see cref="dropHeight"/>,
    /// each at a random size as gift boxes are. Most land anywhere in the
    /// boss's <see cref="Enemy.Arena"/>; a share of
    /// <see cref="aimedShare"/> land within <see cref="aimRadius"/> of the
    /// player, so standing still is never safe. A warning circle the size of
    /// each present's blast fills on the floor below it as it falls, and it
    /// breaks the instant it lands, its fuse lit, then blows up as any gift
    /// box does. Each is sent down at <see cref="dropSpeed"/>, gravity
    /// speeding it on, so the strings bring them down hard. Unless
    /// <see cref="chainReactions"/> is on, presents neither set off nor are
    /// set off by other boxes. Shot in the air, a present breaks there
    /// instead.
    ///
    /// Presents are placed as summons are: on floor, clear of walls, bodies
    /// and one another. The attack ends when the last has been dropped;
    /// those still falling land and go off on their own. Needs the arena the
    /// <see cref="PuppeteerIntro"/> hands the boss.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Encore", fileName = "Encore")]
    public sealed class EncorePattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("The gift box prefab, with a Gift Box on it.")]
        private GiftBox presentPrefab;

        [SerializeField]
        [Tooltip("Seconds presents keep raining.")]
        [Min(0f)]
        private float duration = 6f;

        [SerializeField]
        [Tooltip("Presents dropped a second.")]
        [Min(0.1f)]
        private float presentsPerSecond = 4f;

        [SerializeField]
        [Tooltip("How far above its landing spot each present starts falling.")]
        [Min(1f)]
        private float dropHeight = 15f;

        [SerializeField]
        [Tooltip("Speed each present is sent down at, in units per second. Gravity speeds it on from there. 0 just lets it fall.")]
        [Min(0f)]
        private float dropSpeed = 25f;

        [SerializeField]
        [Tooltip("Let presents set each other off. Off, each goes off on its own, harming the player but not other presents.")]
        private bool chainReactions;

        [SerializeField]
        [Tooltip("Blast radius of a full-size present. Smaller presents reach proportionally less far. Replaces the gift box prefab's own.")]
        [Min(0.1f)]
        private float blastRadius = 2.5f;

        [SerializeField]
        [Tooltip("Seconds between a present landing and it blowing up. Replaces the gift box prefab's own.")]
        [Min(0f)]
        private float fuseTime = 0.6f;

        [SerializeField]
        [Tooltip("Share of the presents, 0 to 1, aimed near the player rather than anywhere in the arena.")]
        [Range(0f, 1f)]
        private float aimedShare = 0.35f;

        [SerializeField]
        [Tooltip("How far from the player an aimed present can land.")]
        [Min(0f)]
        private float aimRadius = 3f;

        public override IEnumerator Perform(Enemy enemy)
        {
            ArenaBounds arena = enemy.Arena;

            if (presentPrefab == null || arena == null)
            {
                Debug.LogWarning($"[Encore] {name} needs a present prefab, and {enemy.name} an arena.", this);
                yield break;
            }

            float clock = 0f;
            float due = 0f;

            while (clock < duration && !enemy.Health.IsDead)
            {
                due += presentsPerSecond * Time.deltaTime;

                while (due >= 1f)
                {
                    due -= 1f;
                    Drop(enemy, arena);
                }

                yield return null;
                clock += Time.deltaTime;
            }
        }

        private void Drop(Enemy enemy, ArenaBounds arena)
        {
            // Made first, to measure its body at the size it picked; moved to
            // its spot before physics ever steps, or destroyed unseen.
            GiftBox present = Instantiate(presentPrefab, arena.Centre, presentPrefab.transform.rotation);
            present.name = presentPrefab.name;

            EnemyHealth health = present.GetComponent<EnemyHealth>();
            bool aimed = enemy.HasTarget && Random.value < aimedShare;

            bool placed = aimed
                ? arena.TryPlace(health, enemy.Target.position, 0f, aimRadius, 0f) || arena.TryPlace(health)
                : arena.TryPlace(health);

            if (!placed)
            {
                Destroy(present.gameObject);
                return;
            }

            present.RainDown(dropHeight, dropSpeed, chainReactions, blastRadius, fuseTime);
        }

        private void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
        }
    }
}
