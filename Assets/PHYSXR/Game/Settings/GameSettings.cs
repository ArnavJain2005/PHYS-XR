using UnityEngine;

namespace PHYSXR.Game.Settings
{
    // Player-adjustable settings, persisted per device in PlayerPrefs.
    // Read by the input adapter (sensitivity, invert), the audio mixer
    // (volume) and the post-processing (brightness). Plain values; nothing
    // here affects game rules or the decision pipeline.
    public class GameSettings
    {
        private const string KeySensitivity = "physxr.mouseSensitivity";
        private const string KeyInvertY = "physxr.invertY";
        private const string KeyVolume = "physxr.masterVolume";
        private const string KeyBrightness = "physxr.brightness";
        private const string KeyHeadBob = "physxr.headBob";
        private const string KeyCondition = "physxr.condition";
        private const string KeyDevOverlay = "physxr.devOverlay";

        public const float MinSensitivity = 0.02f;
        public const float MaxSensitivity = 0.3f;

        // Degrees per pixel of mouse movement.
        public float MouseSensitivity { get; set; } = 0.1f;
        public bool InvertY { get; set; }
        public float MasterVolume { get; set; } = 0.85f;

        // Exposure offset in stops, -1..+1.
        public float Brightness { get; set; }
        public bool HeadBob { get; set; } = true;
        public int LastCondition { get; set; }
        public bool DevOverlay { get; set; }

        public static GameSettings Load()
        {
            var s = new GameSettings();
            try
            {
                s.MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, s.MouseSensitivity), MinSensitivity, MaxSensitivity);
                s.InvertY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
                s.MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, s.MasterVolume));
                s.Brightness = Mathf.Clamp(PlayerPrefs.GetFloat(KeyBrightness, 0f), -1f, 1f);
                s.HeadBob = PlayerPrefs.GetInt(KeyHeadBob, 1) == 1;
                s.LastCondition = PlayerPrefs.GetInt(KeyCondition, 0);
                s.DevOverlay = PlayerPrefs.GetInt(KeyDevOverlay, 0) == 1;
            }
            catch (System.Exception)
            {
                // Defaults are fine.
            }
            return s;
        }

        public void Save()
        {
            try
            {
                PlayerPrefs.SetFloat(KeySensitivity, MouseSensitivity);
                PlayerPrefs.SetInt(KeyInvertY, InvertY ? 1 : 0);
                PlayerPrefs.SetFloat(KeyVolume, MasterVolume);
                PlayerPrefs.SetFloat(KeyBrightness, Brightness);
                PlayerPrefs.SetInt(KeyHeadBob, HeadBob ? 1 : 0);
                PlayerPrefs.SetInt(KeyCondition, LastCondition);
                PlayerPrefs.SetInt(KeyDevOverlay, DevOverlay ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
                // Not persisting is not fatal.
            }
        }

        public void ApplyAudio()
        {
            AudioListener.volume = MasterVolume;
        }
    }
}
