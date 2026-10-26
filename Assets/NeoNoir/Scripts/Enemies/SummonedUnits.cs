using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The enemies one summoner has brought in that are still alive, so a
    /// summoning attack can stay under its cap.
    /// </summary>
    /// <remarks>
    /// Added at runtime by the attack, onto the summoner, since an attack
    /// asset may hold no state of its own. Only remembers the units: they
    /// live on their own, and keep fighting if the summoner dies.
    /// </remarks>
    public sealed class SummonedUnits : MonoBehaviour
    {
        private readonly List<Enemy> units = new List<Enemy>();

        /// <summary>Finds or adds the roster on <paramref name="summoner"/>.</summary>
        public static SummonedUnits On(Enemy summoner)
        {
            SummonedUnits roster = summoner.GetComponent<SummonedUnits>();
            return roster != null ? roster : summoner.gameObject.AddComponent<SummonedUnits>();
        }

        /// <summary>Units summoned so far that are still alive, fading in or fighting.</summary>
        public int Alive
        {
            get
            {
                units.RemoveAll(unit => unit == null || unit.Health.IsDead);
                return units.Count;
            }
        }

        public void Add(Enemy unit)
        {
            units.Add(unit);
        }
    }
}
