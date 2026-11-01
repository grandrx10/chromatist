using UnityEditor;
using UnityEngine;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Editor menu for testing the Afterworld: giving and taking Chroma, and
    /// wiping saved progress. Works in and out of play mode; in play mode the
    /// orbs round the player update at once.
    /// </summary>
    public static class AfterworldProgressMenu
    {
        private const string Root = "Tools/NeoNoir/Afterworld/";

        [MenuItem(Root + "Add 1 Chroma", priority = 40)]
        private static void AddOne()
        {
            ChromaPoints.Add(1);
            Report();
        }

        [MenuItem(Root + "Add 5 Chroma", priority = 41)]
        private static void AddFive()
        {
            ChromaPoints.Add(5);
            Report();
        }

        [MenuItem(Root + "Remove 1 Chroma", priority = 42)]
        private static void RemoveOne()
        {
            ChromaPoints.Set(ChromaPoints.Count - 1);
            Report();
        }

        [MenuItem(Root + "Reset Progress (Chroma and Blades)", priority = 60)]
        private static void ResetProgress()
        {
            if (!EditorUtility.DisplayDialog("Reset Afterworld Progress",
                                             "Set Chroma to 0, forget every activated and upgraded blade, and let the levels' Chroma be earned again?", "Reset", "Cancel"))
            {
                return;
            }

            ChromaPoints.Set(0);
            BladeUnlocks.Clear();
            ChromaRewards.Clear();
            Debug.Log("[Afterworld] Progress reset. Reload the Afterworld to see the stones and lines unlit.");
        }

        private static void Report()
        {
            Debug.Log($"[Afterworld] Chroma: {ChromaPoints.Count}");
        }
    }
}
