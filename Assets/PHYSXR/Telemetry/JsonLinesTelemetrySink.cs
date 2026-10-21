using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PHYSXR.Telemetry
{
    // Writes telemetry as JSON Lines, one record per line, for offline
    // analysis (Technical Spec section G; Tools/analyze_telemetry.py reads it).
    //
    // Telemetry cannot change behaviour:
    //  - Record() only copies the record reference plus the current attempt
    //    and frame onto a bounded queue and returns. It never blocks, never
    //    touches the file and never throws.
    //  - A full queue drops the record and counts it; the writer reports the
    //    count as a TELEMETRY_DROPPED line.
    //  - A background thread serialises and writes, flushing at least once a
    //    second, so a killed process loses at most about a second of records.
    //  - Every exception on the writer side is caught; the sink then marks
    //    itself Faulted and keeps discarding records quietly.
    // Callers must not mutate a TelemetryRecord after handing it over.
    public sealed class JsonLinesTelemetrySink : ITelemetrySink, IDisposable
    {
        public const int DefaultCapacity = 4096;

        private struct Entry
        {
            public TelemetryRecord record;
            public int attempt;
            public long frame;
        }

        private readonly ConcurrentQueue<Entry> queue = new ConcurrentQueue<Entry>();
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private readonly int capacity;
        private readonly string path;
        private readonly Thread writerThread;
        private readonly StringBuilder line = new StringBuilder(256);
        private volatile bool stopping;
        private long dropped;
        private long reportedDropped;
        private long written;
        private int attempt = 1;
        private long frame;

        public JsonLinesTelemetrySink(string path, string trial, string condition, string platform, int capacity = DefaultCapacity)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.capacity = Math.Max(16, capacity);
            Trial = trial ?? string.Empty;
            Condition = condition ?? string.Empty;
            Platform = platform ?? string.Empty;

            writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "PHYSXR Telemetry" };
            writerThread.Start();
        }

        public string Path => path;
        public string Trial { get; }
        public string Condition { get; }
        public string Platform { get; }

        public bool Faulted { get; private set; }
        public string FaultMessage { get; private set; }

        public long Dropped => Interlocked.Read(ref dropped);
        public long Written => Interlocked.Read(ref written);

        // Set by the game on the main thread; copied into each record at Record().
        public int Attempt
        {
            get => Volatile.Read(ref attempt);
            set => Volatile.Write(ref attempt, value);
        }

        public long Frame
        {
            get => Interlocked.Read(ref frame);
            set => Interlocked.Exchange(ref frame, value);
        }

        public void Record(TelemetryRecord record)
        {
            if (record == null || stopping || Faulted)
                return;

            if (queue.Count >= capacity)
            {
                Interlocked.Increment(ref dropped);
                return;
            }

            queue.Enqueue(new Entry { record = record, attempt = Attempt, frame = Frame });
        }

        // Asks the writer to flush now and waits briefly for the queue to drain.
        public void Flush(int timeoutMs = 2000)
        {
            signal.Set();
            int waited = 0;
            while (!queue.IsEmpty && !Faulted && waited < timeoutMs)
            {
                Thread.Sleep(10);
                waited += 10;
            }
        }

        public void Dispose()
        {
            if (stopping)
                return;

            stopping = true;
            signal.Set();
            writerThread.Join(3000);
            signal.Dispose();
        }

        // Writes the trial header next to the log (spec: trial.json).
        public static void WriteTrialHeader(string headerPath, string trial, string condition, string platform,
            string buildVersion, string settingsHash, DateTime utcStart)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('{');
                AppendString(sb, "trial", trial, false);
                AppendString(sb, "cond", condition, true);
                AppendString(sb, "plat", platform, true);
                AppendString(sb, "build", buildVersion, true);
                AppendString(sb, "settings", settingsHash, true);
                AppendString(sb, "utc_start", utcStart.ToString("o", CultureInfo.InvariantCulture), true);
                sb.Append('}');
                File.WriteAllText(headerPath, sb.ToString());
            }
            catch (Exception)
            {
                // A missing header must never stop the game.
            }
        }

        private void WriterLoop()
        {
            StreamWriter writer = null;
            try
            {
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                writer = new StreamWriter(path, true, new UTF8Encoding(false));

                while (true)
                {
                    signal.WaitOne(1000);
                    DrainTo(writer);
                    writer.Flush();
                    if (stopping)
                        break;
                }

                DrainTo(writer);
                writer.Flush();
            }
            catch (Exception ex)
            {
                Faulted = true;
                FaultMessage = ex.Message;
            }
            finally
            {
                try
                {
                    writer?.Dispose();
                }
                catch (Exception)
                {
                    // Nothing more can be done.
                }
            }
        }

        private void DrainTo(StreamWriter writer)
        {
            while (queue.TryDequeue(out Entry entry))
            {
                writer.WriteLine(Serialize(entry));
                Interlocked.Increment(ref written);
            }

            long droppedNow = Interlocked.Read(ref dropped);
            if (droppedNow != reportedDropped)
            {
                var report = new TelemetryRecord
                {
                    type = "TELEMETRY_DROPPED",
                    detail = "dropped=" + (droppedNow - reportedDropped).ToString(CultureInfo.InvariantCulture)
                };
                writer.WriteLine(Serialize(new Entry { record = report, attempt = Attempt, frame = Frame }));
                reportedDropped = droppedNow;
            }
        }

        // Fixed field order: ts, type, trial, cond, plat, attempt, frame, then
        // whichever optional fields the record carries.
        private string Serialize(Entry entry)
        {
            TelemetryRecord r = entry.record;
            line.Clear();
            line.Append('{');
            AppendNumber(line, "ts", r.timestamp, false);
            AppendString(line, "type", r.type ?? r.eventType.ToString(), true);
            AppendString(line, "trial", Trial, true);
            AppendString(line, "cond", Condition, true);
            AppendString(line, "plat", Platform, true);
            AppendNumber(line, "attempt", entry.attempt, true);
            AppendNumber(line, "frame", entry.frame, true);
            if (r.type == null)
                AppendString(line, "stage", r.eventType.ToString(), true);
            if (r.ghostAction.HasValue)
                AppendString(line, "action", r.ghostAction.Value.ToString(), true);
            if (r.ghostExecutionState.HasValue)
                AppendString(line, "exec", r.ghostExecutionState.Value.ToString(), true);
            if (r.ruleId != null)
                AppendString(line, "rule", r.ruleId, true);
            if (r.reason != null)
                AppendString(line, "reason", r.reason, true);
            if (r.decisionId.HasValue)
                AppendNumber(line, "dec", r.decisionId.Value, true);
            if (r.causeEventId.HasValue)
                AppendNumber(line, "cause_evt", r.causeEventId.Value, true);
            if (r.relatedId.HasValue)
                AppendNumber(line, "rel", r.relatedId.Value, true);
            if (r.detail != null)
                AppendString(line, "detail", r.detail, true);
            line.Append('}');
            return line.ToString();
        }

        private static void AppendNumber(StringBuilder sb, string key, long value, bool comma)
        {
            if (comma)
                sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendString(StringBuilder sb, string key, string value, bool comma)
        {
            if (comma)
                sb.Append(',');
            sb.Append('"').Append(key).Append("\":\"");
            AppendEscaped(sb, value ?? string.Empty);
            sb.Append('"');
        }

        internal static void AppendEscaped(StringBuilder sb, string value)
        {
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
        }
    }
}
