using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Which blades the player has acquired.
    /// </summary>
    /// <remarks>
    /// Static, so it outlives scene loads: the player acquires a blade in the
    /// shrine, then carries it into the next run. It lasts for one play
    /// session. When saving arrives, write <see cref="Acquired"/> out by
    /// blade name and feed it back through <see cref="Acquire"/> on load --
    /// nothing else needs to change.
    ///
    /// Keyed by <see cref="BladeDefinition"/>, not by the shrine object, so
    /// that anything (the loadout screen, a HUD) can ask about a blade
    /// without the shrine scene being loaded.
    /// </remarks>
    public static class BladeUnlocks
    {
        private static readonly HashSet<BladeDefinition> acquired = new HashSet<BladeDefinition>();

        /// <summary>Raised when a blade is acquired for the first time.</summary>
        public static event Action<BladeDefinition> BladeAcquired;

        public static IReadOnlyCollection<BladeDefinition> Acquired => acquired;

        public static bool IsAcquired(BladeDefinition blade)
        {
            return blade != null && acquired.Contains(blade);
        }

        /// <summary>Marks a blade acquired. Returns false if it already was.</summary>
        public static bool Acquire(BladeDefinition blade)
        {
            if (blade == null || !acquired.Add(blade))
            {
                return false;
            }

            BladeAcquired?.Invoke(blade);
            return true;
        }

        /// <summary>Forgets every blade. For testing, and a future "new game".</summary>
        public static void Clear()
        {
            acquired.Clear();
        }

        // With domain reload turned off in the editor, statics survive from
        // one play session to the next. Start each one empty regardless.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            acquired.Clear();
            BladeAcquired = null;
        }
    }
}
