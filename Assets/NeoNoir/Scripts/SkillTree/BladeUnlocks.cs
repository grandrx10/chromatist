using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Which blades the player owns, and at what level.
    /// </summary>
    /// <remarks>
    /// Level 0 is not owned; activating a blade makes it level 1, and each
    /// upgrade adds one.
    ///
    /// Saved in PlayerPrefs the moment anything changes, by blade asset
    /// name, so it outlives scene loads and quitting.
    ///
    /// Keyed by <see cref="BladeDefinition"/>, not by the Afterworld object,
    /// so that anything (the loadout screen, a HUD) can ask about a blade
    /// without the Afterworld being loaded.
    /// </remarks>
    public static class BladeUnlocks
    {
        private const string SaveKey = "BladeLevels";

        private static readonly Dictionary<string, int> levels = new Dictionary<string, int>();
        private static bool loaded;

        /// <summary>Raised when a blade is activated (reaches level 1).</summary>
        public static event Action<BladeDefinition> BladeAcquired;

        /// <summary>Raised with the new level whenever a blade's level goes up, activation included.</summary>
        public static event Action<BladeDefinition, int> LevelChanged;

        public static int LevelOf(BladeDefinition blade)
        {
            Load();
            return blade != null && levels.TryGetValue(blade.name, out int level) ? level : 0;
        }

        public static bool IsAcquired(BladeDefinition blade)
        {
            return LevelOf(blade) > 0;
        }

        /// <summary>Activates a blade at level 1. Returns false if it was already owned.</summary>
        public static bool Acquire(BladeDefinition blade)
        {
            if (blade == null || IsAcquired(blade))
            {
                return false;
            }

            SetLevel(blade, 1);
            BladeAcquired?.Invoke(blade);
            LevelChanged?.Invoke(blade, 1);
            return true;
        }

        /// <summary>Raises an owned blade one level. Returns the new level, or 0 if it is not owned.</summary>
        public static int Upgrade(BladeDefinition blade)
        {
            int level = LevelOf(blade);

            if (level <= 0)
            {
                return 0;
            }

            level++;
            SetLevel(blade, level);
            LevelChanged?.Invoke(blade, level);
            return level;
        }

        /// <summary>Forgets every blade, saved ones included. For testing, and a future "new game".</summary>
        public static void Clear()
        {
            Load();
            levels.Clear();
            Save();
        }

        private static void SetLevel(BladeDefinition blade, int level)
        {
            Load();
            levels[blade.name] = level;
            Save();
        }

        // Saved as "Luna:1;Rose:2". Asset names never hold ':' or ';'.
        private static void Save()
        {
            StringBuilder builder = new StringBuilder();

            foreach (KeyValuePair<string, int> entry in levels)
            {
                if (builder.Length > 0)
                {
                    builder.Append(';');
                }

                builder.Append(entry.Key).Append(':').Append(entry.Value);
            }

            PlayerPrefs.SetString(SaveKey, builder.ToString());
            PlayerPrefs.Save();
        }

        private static void Load()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            levels.Clear();

            foreach (string entry in PlayerPrefs.GetString(SaveKey, string.Empty).Split(';'))
            {
                int colon = entry.LastIndexOf(':');

                if (colon > 0 && int.TryParse(entry.Substring(colon + 1), out int level) && level > 0)
                {
                    levels[entry.Substring(0, colon)] = level;
                }
            }
        }

        // With domain reload turned off in the editor, statics survive from
        // one play session to the next. Read the save afresh each time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            loaded = false;
            BladeAcquired = null;
            LevelChanged = null;
        }
    }
}
