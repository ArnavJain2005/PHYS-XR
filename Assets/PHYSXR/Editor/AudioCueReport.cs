using System;
using System.Globalization;
using System.IO;
using System.Text;
using PHYSXR.Game.Audio;
using UnityEditor;
using UnityEngine;

namespace PHYSXR.EditorTools
{
    // Renders every procedural sound cue, writes each as a 16-bit WAV for
    // listening, and logs its length, peak and loudness (RMS, raw and after
    // GameAudio's mix level) - the numbers behind the mix table in
    // Docs/HORROR_PRESENTATION.md.
    //
    // Batch use (the folder defaults to Temp/AudioCues):
    //   Unity -batchmode -quit -projectPath <proj> -executeMethod PHYSXR.EditorTools.AudioCueReport.Run -audioOut <folder>
    public static class AudioCueReport
    {
        [MenuItem("PHYS-XR/Audio/Export Cue Report")]
        public static void Run()
        {
            string folder = ArgumentAfter("-audioOut") ?? Path.Combine("Temp", "AudioCues");
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            report.AppendLine("cue variant length_s peak rms_db mix eff_rms_db");
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                for (int v = 0; v < ProceduralAudio.VariantCount(cue); v++)
                {
                    AudioClip clip = ProceduralAudio.Get(cue, v);
                    var data = new float[clip.samples];
                    clip.GetData(data, 0);
                    double sum = 0;
                    float peak = 0f;
                    foreach (float s in data)
                    {
                        sum += s * s;
                        peak = Mathf.Max(peak, Mathf.Abs(s));
                    }
                    float rms = (float)Math.Sqrt(sum / Math.Max(1, data.Length));
                    float mix = GameAudio.Level(cue);
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2:F2} {3:F2} {4:F1} {5:F2} {6:F1}",
                        cue, v, clip.length, peak, Db(rms), mix, Db(rms * mix)));
                    WriteWav(Path.Combine(folder, cue + "_" + v + ".wav"), data, clip.frequency);
                }
            }
            File.WriteAllText(Path.Combine(folder, "cues.txt"), report.ToString());
            Debug.Log("AUDIO_CUE_REPORT\n" + report);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static float Db(float rms) => 20f * Mathf.Log10(Mathf.Max(1e-6f, rms));

        private static void WriteWav(string path, float[] data, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + data.Length * 2);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data.Length * 2);
                foreach (float s in data)
                    w.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));
            }
        }

        private static string ArgumentAfter(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
