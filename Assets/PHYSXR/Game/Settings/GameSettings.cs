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
        private const string KeyTransport = "physxr.sensorTransport";
        private const string KeySerialPort = "physxr.serialPort";
        private const string KeySerialBaud = "physxr.serialBaud";

        public const string AutoSerialPort = "AUTO";
        public const int DefaultSerialBaud = 115200;

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

        // Physical sensor link (PHYSXR.Communication.PhysicalTransport):
        // 0 = USB serial (default, the project's link), 1 = UDP. Port "AUTO"
        // finds the station by its PHYS-XR lines; or a name such as "COM5".
        public int Transport { get; set; }
        public string SerialPort { get; set; } = AutoSerialPort;
        public int SerialBaud { get; set; } = DefaultSerialBaud;

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
                s.Transport = Mathf.Clamp(PlayerPrefs.GetInt(KeyTransport, 0), 0, 1);
                s.SerialPort = PlayerPrefs.GetString(KeySerialPort, AutoSerialPort);
                if (string.IsNullOrWhiteSpace(s.SerialPort))
                    s.SerialPort = AutoSerialPort;
                s.SerialBaud = PlayerPrefs.GetInt(KeySerialBaud, DefaultSerialBaud);
                if (s.SerialBaud <= 0)
                    s.SerialBaud = DefaultSerialBaud;
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
                PlayerPrefs.SetInt(KeyTransport, Transport);
                PlayerPrefs.SetString(KeySerialPort, SerialPort ?? AutoSerialPort);
                PlayerPrefs.SetInt(KeySerialBaud, SerialBaud);
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
