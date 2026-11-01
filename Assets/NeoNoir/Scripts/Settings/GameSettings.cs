using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The player's settings: volume, display and text speed. Changed from
    /// the <see cref="SettingsMenu"/>.
    /// </summary>
    /// <remarks>
    /// Volume and text speed are saved in PlayerPrefs the moment they change
    /// and applied at startup. The window mode and resolution are not saved
    /// here: Unity remembers those itself between runs of a build. Neither
    /// does anything in the editor's Game view.
    /// </remarks>
    public static class GameSettings
    {
        private const string VolumeKey = "Settings.Volume";
        private const string TextSpeedKey = "Settings.TextSpeed";

        public const float DefaultTextSpeed = 100f;
        public const float MinTextSpeed = 10f;
        public const float MaxTextSpeed = 300f;

        private static float volume = 1f;
        private static float textSpeed = DefaultTextSpeed;

        /// <summary>Master volume, 0 to 1.</summary>
        public static float Volume
        {
            get => volume;
            set
            {
                volume = Mathf.Clamp01(value);
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat(VolumeKey, volume);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Characters a second dialogue types at.</summary>
        public static float TextSpeed
        {
            get => textSpeed;
            set
            {
                textSpeed = Mathf.Clamp(value, MinTextSpeed, MaxTextSpeed);
                PlayerPrefs.SetFloat(TextSpeedKey, textSpeed);
                PlayerPrefs.Save();
            }
        }

        public static FullScreenMode WindowMode
        {
            get => Screen.fullScreenMode;
            set => Screen.fullScreenMode = value;
        }

        /// <summary>Sets the resolution, keeping the window mode.</summary>
        public static void SetResolution(int width, int height)
        {
            Screen.SetResolution(width, height, Screen.fullScreenMode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            textSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(TextSpeedKey, DefaultTextSpeed), MinTextSpeed, MaxTextSpeed);
            AudioListener.volume = volume;
        }
    }
}
