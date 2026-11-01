using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Chroma earned in the levels: for reaching a boss fight and for beating
    /// the boss. Each reward is given once, ever, so dying and coming back
    /// to the same fight earns nothing more.
    /// </summary>
    /// <remarks>
    /// Rewards are named by scene and kind ("L1/reach", "L1/beat"), and the
    /// names given are saved in PlayerPrefs. Cleared with the rest of the
    /// Afterworld progress.
    /// </remarks>
    public static class ChromaRewards
    {
        private const string SaveKey = "ChromaRewards";

        private static HashSet<string> given;

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            given = null;
        }

        /// <summary>
        /// Gives <paramref name="amount"/> Chroma for <paramref name="reward"/>
        /// unless it was given before. The Chroma actually given: 0 if not.
        /// </summary>
        public static int Give(string reward, int amount)
        {
            Load();

            if (amount <= 0 || !given.Add(reward))
            {
                return 0;
            }

            PlayerPrefs.SetString(SaveKey, string.Join(";", given));
            PlayerPrefs.Save();
            ChromaPoints.Add(amount);
            Debug.Log($"[ChromaRewards] {reward}: +{amount} Chroma ({ChromaPoints.Count} now).");
            return amount;
        }

        /// <summary>Forgets every reward given, so they can be earned again.</summary>
        public static void Clear()
        {
            given = new HashSet<string>();
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        private static void Load()
        {
            if (given != null)
            {
                return;
            }

            given = new HashSet<string>();
            string saved = PlayerPrefs.GetString(SaveKey, string.Empty);

            foreach (string reward in saved.Split(';'))
            {
                if (reward.Length > 0)
                {
                    given.Add(reward);
                }
            }
        }
    }
}
