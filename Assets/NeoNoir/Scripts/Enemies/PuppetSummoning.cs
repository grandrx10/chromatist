using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Brings a puppet into an arena: placed at a valid spot near the player,
    /// clear of other puppets, and dropped in on its strings. Shared by
    /// <see cref="PuppeteerIntro"/> and <see cref="SummonPuppetsPattern"/>.
    /// </summary>
    /// <remarks>
    /// A puppet with a <see cref="PuppetDescent"/> is hidden from the moment
    /// it is made, colliders and all, so the arena cannot see one placed the
    /// same frame for itself; <paramref name="keepClearOf"/> covers that.
    /// </remarks>
    public static class PuppetSummoning
    {
        /// <summary>Tries at a spot clear of the other puppets, first near the player, then as many anywhere.</summary>
        private const int PlacementAttempts = 10;

        /// <summary>Gap kept between two puppets' bodies, so their strings and swings do not tangle.</summary>
        private const float PuppetGap = 0.5f;

        /// <summary>
        /// Makes <paramref name="prefab"/> and places it in <paramref name="arena"/>
        /// between <paramref name="minRadius"/> and <paramref name="maxRadius"/>
        /// of <paramref name="around"/>, or failing that anywhere in it, clear
        /// of every puppet in <paramref name="keepClearOf"/>. Then it drops in.
        /// With <paramref name="hold"/>, it stays limp after landing until its
        /// <see cref="PuppetDescent.HoldAfterLanding"/> is cleared; one without
        /// a descent stays dormant instead. Returns null, with a warning
        /// naming <paramref name="context"/>, if nowhere would do.
        /// </summary>
        public static Enemy Summon(Enemy prefab, ArenaBounds arena, Vector3 around, float minRadius, float maxRadius,
                                   IReadOnlyList<Enemy> keepClearOf, bool hold, Object context)
        {
            // Made first, to measure its body; moved to its spot before
            // physics ever steps, or destroyed before it is ever drawn.
            Enemy unit = Object.Instantiate(prefab, arena.Centre, prefab.transform.rotation);
            unit.name = prefab.name;

            PuppetDescent descent = unit.GetComponent<PuppetDescent>();
            Bounds body = BodyOf(unit);
            float reach = Reach(body);

            if (!TryPlace(unit, body, reach, arena, around, minRadius, maxRadius, keepClearOf))
            {
                Debug.LogWarning($"[PuppetSummoning] Found nowhere in {arena.name} for {prefab.name}: {arena.LastFailure}.", context);
                Object.Destroy(unit.gameObject);
                return null;
            }

            unit.TurnModelTowardTarget(360f);

            GiftBox box = unit.GetComponent<GiftBox>();

            if (box != null)
            {
                // Never fights, so there is nothing to hold.
                box.DropNow();
            }
            else if (descent != null)
            {
                descent.HoldAfterLanding = hold;
                descent.DescendNow();
            }
            else if (hold)
            {
                unit.StayDormant = true;
            }
            else
            {
                unit.Wake();
            }

            return unit;
        }

        /// <summary>Lets go of a puppet summoned with hold: it wakes as soon as it has landed.</summary>
        public static void Release(Enemy unit)
        {
            if (unit == null || unit.Health.IsDead)
            {
                return;
            }

            PuppetDescent descent = unit.GetComponent<PuppetDescent>();

            if (descent != null)
            {
                descent.HoldAfterLanding = false;
            }
            else
            {
                unit.StayDormant = false;
                unit.Wake();
            }
        }

        private static Bounds BodyOf(Enemy unit)
        {
            PuppetDescent descent = unit.GetComponent<PuppetDescent>();
            return descent != null ? descent.BodyBounds() : unit.Health.BodyBounds();
        }

        private static float Reach(Bounds body)
        {
            return Mathf.Max(body.extents.x, body.extents.z);
        }

        private static bool TryPlace(Enemy unit, Bounds body, float reach, ArenaBounds arena, Vector3 around,
                                     float minRadius, float maxRadius, IReadOnlyList<Enemy> keepClearOf)
        {
            for (int attempt = 0; attempt < PlacementAttempts * 2; attempt++)
            {
                bool placed = attempt < PlacementAttempts
                    ? arena.TryPlace(unit.Health, body, around, minRadius, maxRadius, 0f)
                    : arena.TryPlace(unit.Health, body, null, 0f, 0f, 0f);

                if (placed && ClearOfOthers(unit, reach, keepClearOf))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ClearOfOthers(Enemy unit, float reach, IReadOnlyList<Enemy> others)
        {
            if (others == null)
            {
                return true;
            }

            Vector3 position = unit.transform.position;

            foreach (Enemy other in others)
            {
                if (other == null || other == unit || other.Health.IsDead)
                {
                    continue;
                }

                Vector3 apart = other.transform.position - position;
                apart.y = 0f;
                float needed = reach + Reach(BodyOf(other)) + PuppetGap;

                if (apart.sqrMagnitude < needed * needed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
