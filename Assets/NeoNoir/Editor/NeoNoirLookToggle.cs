using UnityEditor;
using UnityEngine;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Editor toggle that switches the neo-noir look off so scenes can actually
    /// be worked on.
    /// </summary>
    /// <remarks>
    /// Turning off only the fullscreen pass would make authoring WORSE, not
    /// better: the flat materials would still render pure black and the
    /// outlines that separate them would be gone. So one global shader keyword
    /// drives both halves at once -- NeoNoirFlat swaps to a plain shaded grey,
    /// and NeoNoirEdges passes the frame through untouched.
    ///
    /// Nothing is written to disk. No asset is dirtied, no scene is modified,
    /// and the keyword is editor-only, so a build always ships with the look on
    /// regardless of how this was left.
    /// </remarks>
    [InitializeOnLoad]
    public static class NeoNoirLookToggle
    {
        private const string Keyword = "NEONOIR_BYPASS";
        private const string MenuPath = "Tools/NeoNoir/Neo-Noir Look";
        private const string PreferenceKey = "NeoNoir.LookEnabled";

        static NeoNoirLookToggle()
        {
            // Shader keywords are runtime state and do not survive a domain
            // reload, so reapply on every recompile and play mode change.
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += _ => Apply();
        }

        /// <summary>Whether the black-and-white look is currently applied.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PreferenceKey, true);
            set
            {
                EditorPrefs.SetBool(PreferenceKey, value);
                Apply();
            }
        }

        [MenuItem(MenuPath + " %&n", priority = 20)]
        private static void Toggle()
        {
            Enabled = !Enabled;
        }

        [MenuItem(MenuPath + " %&n", validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Apply()
        {
            if (Enabled)
            {
                Shader.DisableKeyword(Keyword);
            }
            else
            {
                Shader.EnableKeyword(Keyword);
            }

            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
