using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// Raises the hammer to the sky and summons a ring of enemies around the
    /// boss, each dissolving into being before it joins the fight.
    /// </summary>
    /// <remarks>
    /// Raised the same way as <see cref="HammerRainPattern"/>: the attack's
    /// own animation is the raised pose, blended into from idle over
    /// <see cref="raiseBlend"/> seconds, held for the whole attack, then
    /// blended back to idle over <see cref="lowerBlend"/>.
    ///
    /// Timing counts from the start of the attack, raise included:
    ///   <see cref="summonDelay"/>            the units appear, invisible, and
    ///                                       start fading in.
    ///   + <see cref="fadeTime"/>             they are solid, wake, and fight.
    ///   <see cref="duration"/>               the attack is over.
    /// While fading in they are harmless and invulnerable; see
    /// <see cref="SummonFadeIn"/>. Once awake each still waits its own Start
    /// Delay before its first attack.
    ///
    /// Each unit is picked at random from <see cref="units"/>, weighted, on
    /// its own, so the same kind can come up more than once. They stand
    /// evenly spaced on a circle of <see cref="radius"/> round the boss,
    /// starting at a random angle. A spot is only used if it has floor at
    /// about the boss's own level, the boss can see it past any wall, and
    /// the unit's body fits there without touching anything; otherwise the
    /// unit is tried closer in, and left out if nowhere on its line will do.
    /// Never more than <see cref="maxAlive"/> of this boss's
    /// summons are alive at once: near the cap fewer are summoned, and at it
    /// none. Summons outlive the boss and fight on after it dies.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Hammer Summon", fileName = "HammerSummon")]
    public sealed class HammerSummonPattern : AttackPattern
    {
        [Serializable]
        public sealed class SummonOption
        {
            [Tooltip("Enemy prefab to summon.")]
            public Enemy prefab;

            [Tooltip("How likely this enemy is next to the others. Twice the weight comes up twice as often; 0 never.")]
            [Min(0f)] public float weight = 1f;
        }

        /// <summary>Gap left under a new unit's body, so it never starts sunk into the floor.</summary>
        private const float FloorLift = 0.05f;

        /// <summary>Spots tried along each unit's line, from the full radius inward.</summary>
        private const int SpotTries = 8;

        /// <summary>Floor this far above or below the boss's feet still counts as the same level.</summary>
        private const float StepHeight = 1.5f;

        private static readonly Collider[] overlaps = new Collider[16];

        [Header("Raise")]
        [SerializeField]
        [Tooltip("Seconds to blend from idle into the raised pose (the Attack Animation above).")]
        [Min(0f)]
        private float raiseBlend = 0.6f;

        [SerializeField]
        [Tooltip("Seconds to blend from the raised pose back to idle once the attack ends.")]
        [Min(0f)]
        private float lowerBlend = 0.5f;

        [Header("Summon")]
        [SerializeField]
        [Tooltip("Seconds from the start of the attack before the units start fading in.")]
        [Min(0f)]
        private float summonDelay = 1f;

        [SerializeField]
        [Tooltip("Seconds a unit takes to fade in. It is harmless and invulnerable until then.")]
        [Min(0.01f)]
        private float fadeTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the whole attack lasts, raise included. Never ends before the units appear.")]
        [Min(0f)]
        private float duration = 2f;

        [SerializeField]
        [Tooltip("Units summoned each time, fewer if that would pass the cap.")]
        [Min(1)]
        private int count = 3;

        [SerializeField]
        [Tooltip("Most of this boss's summons alive at once.")]
        [Min(1)]
        private int maxAlive = 6;

        [SerializeField]
        [Tooltip("How far from the boss the units appear.")]
        [Min(0f)]
        private float radius = 5f;

        [SerializeField]
        [Tooltip("Enemies to pick from, with how likely each is.")]
        private List<SummonOption> units = new List<SummonOption>();

        [Header("Placement")]
        [SerializeField]
        [Tooltip("What counts as a wall between the boss and a unit's spot. Leave the Player and Enemy layers out.")]
        private LayerMask wallMask = 1;

        [SerializeField]
        [Tooltip("Room kept all round a unit's body, clear of walls and anything else.")]
        [Min(0f)]
        private float wallClearance = 0.75f;

        [SerializeField]
        [Tooltip("What counts as floor when finding the height a unit stands at. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        public override float AttackAnimationBlend => raiseBlend;

        public override float IdleReturnBlend => lowerBlend;

        public override IEnumerator Perform(Enemy enemy)
        {
            if (summonDelay > 0f)
            {
                yield return new WaitForSeconds(summonDelay);
            }

            Summon(enemy);

            float rest = duration - summonDelay;

            if (rest > 0f)
            {
                yield return new WaitForSeconds(rest);
            }
        }

        private void Summon(Enemy enemy)
        {
            SummonedUnits roster = SummonedUnits.On(enemy);
            int summoning = Mathf.Min(count, maxAlive - roster.Alive);

            if (summoning <= 0)
            {
                return;
            }

            float start = Random.Range(0f, 360f);

            for (int i = 0; i < summoning; i++)
            {
                Enemy prefab = Pick();

                if (prefab == null)
                {
                    return;
                }

                float angle = start + i * 360f / summoning;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                // Made first, to measure its body; moved to its spot before
                // physics ever steps, or destroyed before it is ever drawn.
                // Physics only hears where a new object is on its next step,
                // so until it is told, its colliders still read as wherever
                // the prefab was saved.
                Enemy unit = Instantiate(prefab, enemy.transform.position, prefab.transform.rotation);
                unit.name = prefab.name;
                Physics.SyncTransforms();

                if (!TryFindSpot(enemy, unit, direction, out Vector3 position))
                {
                    Destroy(unit.gameObject);
                    continue;
                }

                unit.transform.position = position;

                Rigidbody unitBody = unit.GetComponent<Rigidbody>();

                if (unitBody != null)
                {
                    unitBody.position = position;
                }

                Physics.SyncTransforms();
                unit.TurnModelTowardTarget(360f);
                SummonFadeIn.Begin(unit, fadeTime);
                roster.Add(unit);
            }
        }

        /// <summary>A prefab from the list, each as likely as its weight. Null if none can be picked.</summary>
        private Enemy Pick()
        {
            float total = 0f;

            foreach (SummonOption option in units)
            {
                if (option != null && option.prefab != null)
                {
                    total += option.weight;
                }
            }

            if (total <= 0f)
            {
                return null;
            }

            float roll = Random.value * total;
            Enemy last = null;

            foreach (SummonOption option in units)
            {
                if (option == null || option.prefab == null || option.weight <= 0f)
                {
                    continue;
                }

                last = option.prefab;
                roll -= option.weight;

                if (roll <= 0f)
                {
                    return option.prefab;
                }
            }

            return last;
        }

        /// <summary>
        /// Where to put <paramref name="unit"/>'s pivot along
        /// <paramref name="direction"/> from the boss: as far out as
        /// <see cref="radius"/> allows, stepping inward past any spot that
        /// will not do. False if none along the line will.
        /// </summary>
        private bool TryFindSpot(Enemy enemy, Enemy unit, Vector3 direction, out Vector3 position)
        {
            Bounds bossBody = enemy.Health.BodyBounds();
            Bounds unitBody = unit.Health.BodyBounds();

            Vector3 pivot = enemy.transform.position;
            Vector3 centreOffset = unitBody.center - unit.transform.position;
            float bottomOffset = unitBody.min.y - unit.transform.position.y;
            Vector3 halfSize = unitBody.extents + Vector3.one * wallClearance;

            // Never padded below the floor: the body is lifted clear of it,
            // and the padding must not reach down and find it.
            halfSize.y = unitBody.extents.y + Mathf.Min(wallClearance, FloorLift * 0.5f);

            for (int step = 0; step < SpotTries; step++)
            {
                float distance = radius * (1f - (float)step / SpotTries);
                Vector3 flat = pivot + direction * distance;

                if (!TryFindFloor(enemy, unit, flat, bossBody.min.y, out float floor))
                {
                    continue;
                }

                position = new Vector3(flat.x, floor + FloorLift - bottomOffset, flat.z);
                Vector3 centre = position + centreOffset;

                // Past a wall, out of the arena, even if there is floor there.
                if (Physics.Linecast(bossBody.center, centre, wallMask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                // Inside a wall, the boss, or anything else. Solid shells such
                // as ProBuilder meshes push a body out whichever way is
                // nearest, which can be down through the floor.
                if (Blocked(unit, centre, halfSize))
                {
                    continue;
                }

                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Height of the floor at <paramref name="spot"/>, if there is floor
        /// there within a step of <paramref name="level"/>. Looks down from
        /// just a step above, so a wall top or an arch overhead never counts.
        /// </summary>
        private bool TryFindFloor(Enemy enemy, Enemy unit, Vector3 spot, float level, out float floor)
        {
            Vector3 from = new Vector3(spot.x, level + StepHeight, spot.z);
            float nearest = float.MaxValue;
            floor = level;

            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, StepHeight * 2f,
                                                          groundMask, QueryTriggerInteraction.Ignore))
            {
                Transform part = hit.collider.transform;

                if (part.IsChildOf(enemy.transform) || part.IsChildOf(unit.transform) || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                floor = hit.point.y;
            }

            return nearest < float.MaxValue;
        }

        /// <summary>Whether anything solid but the unit itself is inside the box.</summary>
        private static bool Blocked(Enemy unit, Vector3 centre, Vector3 halfSize)
        {
            int count = Physics.OverlapBoxNonAlloc(centre, halfSize, overlaps, Quaternion.identity,
                                                   Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!overlaps[i].transform.IsChildOf(unit.transform))
                {
                    return true;
                }
            }

            return false;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
        }
    }
}
