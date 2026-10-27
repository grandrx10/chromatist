using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Calls arcane orbs into being out round the arena, one after another,
    /// that drift in on the witch and burst into homing shots if the player
    /// does not pop them first.
    /// </summary>
    /// <remarks>
    /// The enemy stands and plays the attack's own animation (ArcaneBomb) for
    /// <see cref="duration"/> seconds. For the first
    /// <see cref="spawnWindow"/> of them, an <see cref="ArcaneOrb"/> appears
    /// every <see cref="spawnInterval"/> seconds, the first straight away.
    /// The attack ends on time whatever the orbs are doing; any still out
    /// carry on by themselves.
    ///
    /// Each orb appears at a random bearing, between
    /// <see cref="spawnDistance"/>.x and .y from the enemy, flat, at
    /// <see cref="spawnHeight"/> above her feet. A bearing whose way out is
    /// blocked by <see cref="obstacleMask"/> before the near distance is
    /// tried again, so orbs are not left in walls; one blocked further out
    /// is brought in short of the wall instead.
    ///
    /// An orb that reaches the enemy fires <see cref="shotCount"/>
    /// <see cref="HomingMissile"/>s out of itself, scattered round the
    /// compass like the sceptre's, with a much tighter turn. If the enemy
    /// dies, every orb pops and the attack stops.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Arcane Bomb", fileName = "ArcaneBomb")]
    public sealed class ArcaneBombPattern : AttackPattern
    {
        private const int SpawnTries = 8;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Seconds the attack lasts, standing and casting.")]
        [Min(0f)]
        private float duration = 10f;

        [SerializeField]
        [Tooltip("Orbs appear only during this many seconds from the start.")]
        [Min(0f)]
        private float spawnWindow = 8f;

        [SerializeField]
        [Tooltip("Seconds between one orb and the next.")]
        [Min(0.05f)]
        private float spawnInterval = 1f;

        [Header("Placement")]
        [SerializeField]
        [Tooltip("Nearest and furthest from the enemy an orb appears, measured flat.")]
        private Vector2 spawnDistance = new Vector2(20f, 30f);

        [SerializeField]
        [Tooltip("Height above the enemy's feet the orbs appear and float at. About chest height.")]
        private float spawnHeight = 1.5f;

        [SerializeField]
        [Tooltip("Walls that orbs must not appear behind. Leave the Enemy and Player layers out.")]
        private LayerMask obstacleMask = 1;

        [SerializeField]
        [Tooltip("How far short of a wall an orb is brought in.")]
        [Min(0f)]
        private float wallMargin = 1.5f;

        [Header("Orbs")]
        [SerializeField]
        [Tooltip("Art for each orb. Its colliders are replaced. Empty draws a glowing ball.")]
        private GameObject orbPrefab;

        [SerializeField] private ArcaneOrb.Settings orb = new ArcaneOrb.Settings();

        [Header("Shots")]
        [SerializeField]
        [Tooltip("Art for each shot. Needs no scripts or colliders. Empty draws a glowing ball.")]
        private GameObject shotPrefab;

        [SerializeField]
        [Tooltip("Shots an orb bursts into when it detonates.")]
        [Range(1, 16)]
        private int shotCount = 5;

        [SerializeField]
        [Tooltip("Degrees each scatter direction may stray from an even spread.")]
        [Range(0f, 90f)]
        private float scatterJitter = 30f;

        [SerializeField]
        [Tooltip("Most degrees a shot may be tilted upward as it scatters.")]
        [Range(0f, 80f)]
        private float scatterLift = 25f;

        [SerializeField] private HomingMissile.Settings shot = new HomingMissile.Settings { trackTurnRate = 400f };

        public override IEnumerator Perform(Enemy enemy)
        {
            float elapsed = 0f;
            float nextSpawn = 0f;

            while (elapsed < duration)
            {
                if (enemy.Health.IsDead)
                {
                    yield break;
                }

                while (elapsed >= nextSpawn && nextSpawn < spawnWindow)
                {
                    Spawn(enemy);
                    nextSpawn += spawnInterval;
                }

                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        private void Spawn(Enemy enemy)
        {
            Vector3 centre = enemy.transform.position + Vector3.up * spawnHeight;
            float near = Mathf.Min(spawnDistance.x, spawnDistance.y);
            float far = Mathf.Max(spawnDistance.x, spawnDistance.y);

            Vector3 best = centre;
            float bestReach = -1f;

            for (int i = 0; i < SpawnTries; i++)
            {
                Vector3 bearing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                float distance = Random.Range(near, far);

                // Brought in short of any wall on the way out.
                if (Physics.Raycast(centre, bearing, out RaycastHit hit, distance, obstacleMask,
                                    QueryTriggerInteraction.Ignore))
                {
                    distance = hit.distance - wallMargin;
                }

                if (distance >= near)
                {
                    best = centre + bearing * distance;
                    bestReach = distance;
                    break;
                }

                // Every way blocked: the bearing with the most room will do.
                if (distance > bestReach)
                {
                    best = centre + bearing * Mathf.Max(0f, distance);
                    bestReach = distance;
                }
            }

            ArcaneOrb.Spawn(orbPrefab, best, enemy, orb, point => Detonate(enemy, point));
        }

        private void Detonate(Enemy enemy, Vector3 origin)
        {
            if (enemy == null || enemy.Health.IsDead)
            {
                return;
            }

            Transform target = enemy.HasTarget ? enemy.Target : null;
            float start = Random.Range(0f, 360f);

            for (int i = 0; i < shotCount; i++)
            {
                float heading = start + 360f * i / shotCount + Random.Range(-scatterJitter, scatterJitter);
                float lift = Random.Range(0f, scatterLift);
                Vector3 direction = Quaternion.Euler(-lift, heading, 0f) * Vector3.forward;

                GameObject art = shotPrefab != null ? Instantiate(shotPrefab) : null;
                HomingMissile.Launch(art, origin, direction, target, enemy.transform, shot);
            }
        }

        private void Reset()
        {
            delayAfter = 2f;
            obstacleMask = LayerMask.GetMask("Default");
            shot.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
