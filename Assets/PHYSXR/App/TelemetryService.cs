using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PHYSXR.Telemetry;
using UnityEngine;

namespace PHYSXR.App
{
    // Owns the trial's telemetry file (Technical Spec section G). Lives on the
    // persistent [Systems] object, so one trial spans every scene reload and
    // checkpoint attempt.
    //
    //   <persistentDataPath>/telemetry/<trial>_<platform>_<utc>.jsonl
    //   <persistentDataPath>/telemetry/<trial>_<platform>_<utc>.trial.json
    //
    // Every record's ts is rewritten to milliseconds since the trial started,
    // from one monotonic Stopwatch, so records from different emitters (the
    // decision pipeline, sensing, gameplay) share one time base. A small ring
    // of recent records feeds the dev overlay; gameplay never reads either.
    public sealed class TelemetryService : ITelemetrySink, IDisposable
    {
        public const int RecentCapacity = 12;

        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly Queue<TelemetryRecord> recent = new Queue<TelemetryRecord>();
        private JsonLinesTelemetrySink file;

        public long NowMs => stopwatch.ElapsedMilliseconds;

        public string CurrentPath => file?.Path;

        public bool Faulted => file != null && file.Faulted;

        public long Dropped => file?.Dropped ?? 0L;

        public IEnumerable<TelemetryRecord> Recent => recent;

        public TelemetryChannel Channel { get; }

        public TelemetryService()
        {
            Channel = new TelemetryChannel(this, () => NowMs);
        }

        public static string PlatformName =>
            Application.platform == RuntimePlatform.Android ? "Quest" : "Windows";

        public void BeginTrial(string trialId, string condition, string settingsSummary)
        {
            EndTrial();
            stopwatch.Restart();

            string utc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            string safeTrial = Sanitize(string.IsNullOrEmpty(trialId) ? "trial" : trialId);
            string directory = Path.Combine(Application.persistentDataPath, "telemetry");
            string stem = Path.Combine(directory, safeTrial + "_" + PlatformName + "_" + utc);

            try
            {
                Directory.CreateDirectory(directory);
                JsonLinesTelemetrySink.WriteTrialHeader(stem + ".trial.json", safeTrial, condition, PlatformName,
                    Application.version + " (Unity " + Application.unityVersion + ")",
                    settingsSummary.GetHashCode().ToString("x8"), DateTime.UtcNow);
                file = new JsonLinesTelemetrySink(stem + ".jsonl", safeTrial, condition, PlatformName);
                UnityEngine.Debug.Log("[Telemetry] trial " + safeTrial + " (" + condition + ") -> " + stem + ".jsonl");
            }
            catch (Exception ex)
            {
                file = null;
                UnityEngine.Debug.LogWarning("[Telemetry] disabled for this trial: " + ex.Message);
            }
        }

        public void EndTrial()
        {
            if (file == null)
                return;

            file.Dispose();
            file = null;
        }

        public void SetFrame(int attempt, long frame)
        {
            if (file == null)
                return;
            file.Attempt = attempt;
            file.Frame = frame;
        }

        public void Flush()
        {
            file?.Flush(500);
        }

        public void Record(TelemetryRecord record)
        {
            if (record == null)
                return;

            record.timestamp = NowMs;
            recent.Enqueue(record);
            while (recent.Count > RecentCapacity)
                recent.Dequeue();

            file?.Record(record);
        }

        public void Dispose()
        {
            EndTrial();
        }

        private static string Sanitize(string value)
        {
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                    chars[i] = '_';
            return new string(chars);
        }
    }
}
