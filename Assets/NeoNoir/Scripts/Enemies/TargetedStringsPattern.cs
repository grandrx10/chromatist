using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Puppeteer marks the player again and again: every
    /// <see cref="interval"/> seconds a <see cref="TargetedStrings"/> circle
    /// appears near them and gives chase, until strings shoot down into it.
    /// </summary>
    /// <remarks>
    /// The first circle comes at once, and another every interval while the
    /// attack's <see cref="duration"/> lasts, so the defaults of a circle a
    /// second for 4 seconds make four. Each appears in a random direction
    /// from the player, at a distance from the volley's Spawn Distance, and
    /// inside the boss's <see cref="Enemy.Arena"/> if it has one: a spot
    /// outside is tried again elsewhere, and failing that the circle starts
    /// on the player.
    ///
    /// The attack ends when the last circle has appeared; volleys still
    /// chasing or holding play out on their own. See
    /// <see cref="TargetedStrings"/> for what each does.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Targeted Strings", fileName = "TargetedStrings")]
    public sealed class TargetedStringsPattern : AttackPattern
    {
        /// <summary>Directions tried for a circle's start before settling on the player.</summary>
        private const int SpawnTries = 8;

        [Header("Volleys")]
        [SerializeField]
        [Tooltip("Seconds over which circles keep appearing.")]
        [Min(0f)]
        private float duration = 4f;

        [SerializeField]
        [Tooltip("Seconds between one circle and the next.")]
        [Min(0.05f)]
        private float interval = 1f;

        [SerializeField] private TargetedStrings.Settings volley = new TargetedStrings.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            float clock = 0f;
            float next = 0f;

            while (clock < duration)
            {
                if (clock >= next)
                {
                    next += interval;

                    if (enemy.HasTarget)
                    {
                        TargetedStrings.Create(enemy, enemy.Target, StartPoint(enemy), volley);
                    }
                }

                yield return null;
                clock += Time.deltaTime;
            }
        }

        private Vector3 StartPoint(Enemy enemy)
        {
            Vector3 player = enemy.Target.position;
            ArenaBounds arena = enemy.Arena;
            float near = Mathf.Max(0f, Mathf.Min(volley.spawnDistance.x, volley.spawnDistance.y));
            float far = Mathf.Max(near, volley.spawnDistance.y);

            for (int i = 0; i < SpawnTries; i++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                Vector3 spot = player + new Vector3(direction.x, 0f, direction.y) * Random.Range(near, far);

                if (arena == null || arena.Contains(spot))
                {
                    return spot;
                }
            }

            return player;
        }

        private void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
        }
    }
}
