using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Where the player comes back to after dying: a level scene, and
    /// whether at the start of its minion part or its boss part.
    /// </summary>
    /// <remarks>
    /// Written by the level's <see cref="LevelCheckpoints"/>: the minion
    /// part when the level starts, the boss part the moment its boss fight
    /// is triggered. A scene with no <see cref="LevelCheckpoints"/> (L4.2,
    /// the Afterworld) never writes it, so dying there goes back to the last
    /// level that did.
    ///
    /// Saved in PlayerPrefs the moment it changes, so it outlives scene loads
    /// and quitting.
    ///
    /// <see cref="BeginRespawn"/>, called by the <see cref="AfterworldDoor"/>
    /// just before loading the saved scene, is what tells that scene's
    /// <see cref="LevelCheckpoints"/> to put the player at the saved spot.
    /// Loading a level any other way (the editor's play button, the next
    /// level) starts it from the top as usual.
    /// </remarks>
    public static class RespawnSave
    {
        private const string SceneKey = "RespawnScene";
        private const string BossKey = "RespawnAtBoss";
        private const string CircleKey = "RespawnCircle";

        private static bool pending;

        /// <summary>The scene to respawn in. Empty when nothing has been saved yet.</summary>
        public static string Scene => PlayerPrefs.GetString(SceneKey, string.Empty);

        /// <summary>True to respawn at the start of the boss part; false for the start of the level.</summary>
        public static bool AtBoss => PlayerPrefs.GetInt(BossKey, 0) == 1;

        /// <summary>The <see cref="ReturnCircle.Id"/> of the return circle to come back to, or empty for the part's spawn.</summary>
        public static string Circle => PlayerPrefs.GetString(CircleKey, string.Empty);

        public static bool HasSave => !string.IsNullOrEmpty(Scene);

        /// <summary>Saves the start of a part, forgetting any return circle.</summary>
        public static void Set(string scene, bool atBoss)
        {
            SetCircle(scene, atBoss, string.Empty);
        }

        /// <summary>Saves a return circle, within the given part.</summary>
        public static void SetCircle(string scene, bool atBoss, string circle)
        {
            circle ??= string.Empty;

            if (Scene == scene && AtBoss == atBoss && Circle == circle)
            {
                return;
            }

            PlayerPrefs.SetString(SceneKey, scene);
            PlayerPrefs.SetInt(BossKey, atBoss ? 1 : 0);
            PlayerPrefs.SetString(CircleKey, circle);
            PlayerPrefs.Save();
        }

        /// <summary>Marks the next load of <see cref="Scene"/> as a respawn.</summary>
        public static void BeginRespawn()
        {
            pending = true;
        }

        /// <summary>
        /// True once, if a respawn was begun and <paramref name="scene"/> is
        /// the saved one. Clears the mark either way.
        /// </summary>
        public static bool ConsumeRespawn(string scene)
        {
            if (!pending)
            {
                return false;
            }

            pending = false;
            return scene == Scene;
        }

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            pending = false;
        }
    }
}
