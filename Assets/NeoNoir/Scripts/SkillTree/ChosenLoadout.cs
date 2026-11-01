using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The setup the player picked at the <see cref="LoadoutStation"/>: the
    /// primary blade first, then the secondaries in the order they rotate.
    /// </summary>
    /// <remarks>
    /// Saved in PlayerPrefs by blade asset name the moment it changes. Names
    /// are turned back into blades by <see cref="Resolve"/>, given the blades
    /// the player owns, so a blade no longer owned drops out and a newly
    /// owned one joins at the end.
    /// </remarks>
    public static class ChosenLoadout
    {
        private const string SaveKey = "ChosenLoadout";

        private static readonly List<BladeDefinition> order = new List<BladeDefinition>();

        /// <summary>Primary first, then the secondaries in rotation order. Empty until resolved.</summary>
        public static IReadOnlyList<BladeDefinition> Order => order;

        public static BladeDefinition Primary => order.Count > 0 ? order[0] : null;

        public static List<BladeDefinition> Secondaries => order.Count > 1 ? order.GetRange(1, order.Count - 1) : new List<BladeDefinition>();

        /// <summary>
        /// Rebuilds the setup from the save, keeping only blades in
        /// <paramref name="owned"/>, in their saved order, with any owned
        /// blade the save does not mention added at the end.
        /// </summary>
        public static void Resolve(IEnumerable<BladeDefinition> owned)
        {
            Dictionary<string, BladeDefinition> byName = new Dictionary<string, BladeDefinition>();
            List<BladeDefinition> ownedList = new List<BladeDefinition>();

            foreach (BladeDefinition blade in owned)
            {
                if (blade != null && !byName.ContainsKey(blade.name))
                {
                    byName[blade.name] = blade;
                    ownedList.Add(blade);
                }
            }

            order.Clear();

            foreach (string name in PlayerPrefs.GetString(SaveKey, string.Empty).Split(';'))
            {
                if (byName.TryGetValue(name, out BladeDefinition blade) && !order.Contains(blade))
                {
                    order.Add(blade);
                }
            }

            foreach (BladeDefinition blade in ownedList)
            {
                if (!order.Contains(blade))
                {
                    order.Add(blade);
                }
            }

            Save();
        }

        /// <summary>Swaps the blades at two places in the order and saves.</summary>
        public static void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= order.Count || b >= order.Count || a == b)
            {
                return;
            }

            (order[a], order[b]) = (order[b], order[a]);
            Save();
        }

        private static void Save()
        {
            List<string> names = new List<string>(order.Count);

            foreach (BladeDefinition blade in order)
            {
                names.Add(blade.name);
            }

            PlayerPrefs.SetString(SaveKey, string.Join(";", names));
            PlayerPrefs.Save();
        }

        /// <summary>Every blade owned among the <see cref="PurchasableBlade"/>s in the open scenes.</summary>
        public static List<BladeDefinition> OwnedInScene()
        {
            List<BladeDefinition> owned = new List<BladeDefinition>();

            foreach (PurchasableBlade blade in Object.FindObjectsByType<PurchasableBlade>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                if (blade.IsAcquired && !owned.Contains(blade.Blade))
                {
                    owned.Add(blade.Blade);
                }
            }

            return owned;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            order.Clear();
        }
    }
}
