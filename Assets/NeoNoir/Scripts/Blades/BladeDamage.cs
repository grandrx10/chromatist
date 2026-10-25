using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Damage for attacks that are not a blade touching something: slashes in
    /// flight, dash trails. Sweeps a capsule and hurts every enemy inside.
    /// </summary>
    /// <remarks>
    /// A swept capsule rather than a trigger collider, because these move far
    /// further per frame than their own width -- a rapier slash crosses
    /// several enemies' worth of distance in one frame, and a trigger would
    /// simply teleport past them. Sweeping from last frame's position to this
    /// frame's covers every point in between.
    /// </remarks>
    public static class BladeDamage
    {
        /// <summary>
        /// Damages every enemy overlapping the capsule from <paramref name="start"/>
        /// to <paramref name="end"/> that is not already in <paramref name="alreadyHit"/>,
        /// and adds them to it. Pass the same set for the whole attack so each
        /// enemy is hit once. <paramref name="onHit"/>, if given, is told about
        /// each enemy hit after the damage, along with <paramref name="start"/>:
        /// where the attack arrived from. <paramref name="slashTint"/> colours
        /// the slash effect; black leaves it the plain inversion. Pass false
        /// for <paramref name="slashEffect"/> for an attack that draws its own
        /// hit effect instead.
        /// Returns the total health actually taken off, which is less than
        /// damage times hits when a blow kills -- what lifesteal heals from.
        /// </summary>
        public static float Sweep(Vector3 start, Vector3 end, float radius, float damage,
                                  Vector3 direction, HashSet<EnemyHealth> alreadyHit,
                                  Action<EnemyHealth, Vector3> onHit = null,
                                  Color slashTint = default, bool slashEffect = true)
        {
            Collider[] overlaps = Physics.OverlapCapsule(start, end, radius, ~0, QueryTriggerInteraction.Ignore);
            float dealt = 0f;

            foreach (Collider overlap in overlaps)
            {
                EnemyHealth enemy = overlap.GetComponentInParent<EnemyHealth>();

                if (enemy == null || enemy.IsDead || !alreadyHit.Add(enemy))
                {
                    continue;
                }

                // Read before damaging: a killing blow destroys the enemy.
                Vector3 point = overlap.ClosestPointOnBounds(end);

                float before = enemy.CurrentHealth;
                enemy.TakeDamage(damage);
                dealt += before - enemy.CurrentHealth;

                if (slashEffect && SlashEffect.Current != null)
                {
                    SlashEffect.Current.TriggerSlash(point, direction, 1f, slashTint);
                }

                onHit?.Invoke(enemy, start);
            }

            return dealt;
        }
    }
}
