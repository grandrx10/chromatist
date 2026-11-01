using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The player's Chroma: the points spent in the Afterworld to activate
    /// and upgrade blades.
    /// </summary>
    /// <remarks>
    /// Saved in PlayerPrefs the moment it changes, so it outlives scene loads
    /// and quitting. Shown only in the Afterworld, as the orbs circling the
    /// player (<see cref="ChromaOrbs"/>).
    /// </remarks>
    public static class ChromaPoints
    {
        private const string SaveKey = "ChromaPoints";

        private static int count;
        private static bool loaded;

        /// <summary>Raised with the new count whenever it changes.</summary>
        public static event Action<int> Changed;

        public static int Count
        {
            get
            {
                Load();
                return count;
            }
        }

        public static void Add(int amount)
        {
            Set(Count + amount);
        }

        /// <summary>Takes <paramref name="amount"/> if there is that much. Returns false, and takes nothing, if not.</summary>
        public static bool TrySpend(int amount)
        {
            if (amount < 0 || Count < amount)
            {
                return false;
            }

            Set(count - amount);
            return true;
        }

        public static void Set(int value)
        {
            Load();
            value = Mathf.Max(0, value);

            if (value == count)
            {
                return;
            }

            count = value;
            PlayerPrefs.SetInt(SaveKey, count);
            PlayerPrefs.Save();
            Changed?.Invoke(count);
        }

        private static void Load()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            count = Mathf.Max(0, PlayerPrefs.GetInt(SaveKey, 0));
        }

        // With domain reload off, statics carry over between play sessions.
        // Read the save afresh each time instead.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            loaded = false;
            Changed = null;
        }
    }
}
