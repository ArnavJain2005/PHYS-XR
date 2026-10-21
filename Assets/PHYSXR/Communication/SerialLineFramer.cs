using System;
using System.Collections.Generic;
using System.Text;

namespace PHYSXR.Communication
{
    // Splits a USB-serial byte stream into '\n'-terminated lines. A read may
    // end mid-line or hold several lines; the framer keeps the partial tail
    // for the next call. A trailing '\r' is dropped (CRLF from Serial.println).
    //
    // A line longer than MaxLineBytes is never delivered: its bytes are
    // discarded up to the next '\n' and counted once as oversized, so memory
    // stays bounded whatever the device sends.
    //
    // Plain C#, single-threaded: SerialLineReceiver calls it from its reader thread.
    public sealed class SerialLineFramer
    {
        public const int DefaultMaxLineBytes = PacketValidator.MaxDatagramBytes;

        private readonly byte[] buffer;
        private int length;
        private bool discarding;

        public SerialLineFramer(int maxLineBytes = DefaultMaxLineBytes)
        {
            buffer = new byte[Math.Max(16, maxLineBytes)];
        }

        public int MaxLineBytes => buffer.Length;

        public long OversizedLines { get; private set; }

        public int PendingBytes => length;

        // Appends bytes and adds every completed line (without terminator) to lines.
        public void Push(byte[] data, int offset, int count, List<byte[]> lines)
        {
            for (int i = offset; i < offset + count; i++)
            {
                byte b = data[i];
                if (b == (byte)'\n')
                {
                    if (discarding)
                    {
                        discarding = false;
                    }
                    else
                    {
                        int n = length;
                        if (n > 0 && buffer[n - 1] == (byte)'\r')
                            n--;
                        var line = new byte[n];
                        Buffer.BlockCopy(buffer, 0, line, 0, n);
                        lines.Add(line);
                    }
                    length = 0;
                    continue;
                }

                if (discarding)
                    continue;

                if (length == buffer.Length)
                {
                    OversizedLines++;
                    discarding = true;
                    length = 0;
                    continue;
                }
                buffer[length++] = b;
            }
        }

        // Called on reconnect: a partial line from the previous connection must
        // not be glued onto the first line of the next one.
        public void Reset()
        {
            length = 0;
            discarding = false;
        }
    }

    // Caps how many raw lines the monitor prints per second (fixed one-second
    // windows); the rest are counted in Suppressed, never printed.
    public sealed class SerialRawLineLimiter
    {
        private readonly int maxPerSecond;
        private long windowStart = long.MinValue;
        private int inWindow;

        public SerialRawLineLimiter(int maxPerSecond)
        {
            this.maxPerSecond = Math.Max(1, maxPerSecond);
        }

        public long Suppressed { get; private set; }

        public bool Allow(long nowMs)
        {
            if (windowStart == long.MinValue || nowMs - windowStart >= 1000)
            {
                windowStart = nowMs;
                inWindow = 0;
            }
            if (inWindow < maxPerSecond)
            {
                inWindow++;
                return true;
            }
            Suppressed++;
            return false;
        }
    }

    public enum SerialLineKind
    {
        Empty = 0,
        Contract,    // a semantic state line: goes to PacketValidator
        Diagnostic,  // '#HELLO', '#DIAG', '#CAL', '#ERR': display and telemetry only
        Legacy,      // raw lines from the old bench sketches ("Distance = 41.97 cm")
        Noise        // anything else (ESP32 ROM boot text, line noise)
    }

    public static class SerialLineClassifier
    {
        // Contract candidates are passed to the validator even when malformed,
        // so a broken state line is rejected with a reason and logged; text
        // that is plainly not a state line (boot messages) is only counted.
        public static SerialLineKind Classify(byte[] line)
        {
            if (line == null || line.Length == 0)
                return SerialLineKind.Empty;

            if (line[0] == (byte)'#')
                return SerialLineKind.Diagnostic;

            if (StartsWith(line, "v=") || IndexOf(line, "seq=") >= 0)
                return SerialLineKind.Contract;

            if (StartsWith(line, "Distance") || StartsWith(line, "LASER") || StartsWith(line, "SONAR") || StartsWith(line, "MPU"))
                return SerialLineKind.Legacy;

            bool blank = true;
            for (int i = 0; i < line.Length && blank; i++)
                blank = line[i] == (byte)' ' || line[i] == (byte)'\t' || line[i] == (byte)'\r';
            return blank ? SerialLineKind.Empty : SerialLineKind.Noise;
        }

        public static string Text(byte[] line, int maxChars = 160)
        {
            string s = Encoding.UTF8.GetString(line);
            return s.Length > maxChars ? s.Substring(0, maxChars) + "..." : s;
        }

        // Terminal-safe rendering of an unrecognised line for the monitor's
        // --raw mode. Printable ASCII is kept; every other byte (control
        // characters, ESC, DEL, backslash, non-ASCII) becomes \xNN, so a
        // device cannot move the cursor, clear the screen or send terminal
        // escape sequences. At most maxChars characters are produced; a longer
        // line ends in "...(+N bytes)".
        public static string Printable(byte[] line, int maxChars = 160)
        {
            var b = new StringBuilder(Math.Min(line.Length * 4, maxChars) + 24);
            int i = 0;
            for (; i < line.Length; i++)
            {
                byte c = line[i];
                bool plain = c >= 0x20 && c < 0x7F && c != (byte)'\\';
                if (b.Length + (plain ? 1 : 4) > maxChars)
                    break;
                if (plain)
                    b.Append((char)c);
                else
                    b.Append("\\x").Append(c.ToString("X2"));
            }
            if (i < line.Length)
                b.Append("...(+").Append(line.Length - i).Append(" bytes)");
            return b.ToString();
        }

        private static bool StartsWith(byte[] line, string prefix)
        {
            if (line.Length < prefix.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
                if (line[i] != (byte)prefix[i])
                    return false;
            return true;
        }

        private static int IndexOf(byte[] line, string token)
        {
            for (int i = 0; i + token.Length <= line.Length; i++)
            {
                int j = 0;
                while (j < token.Length && line[i + j] == (byte)token[j])
                    j++;
                if (j == token.Length)
                    return i;
            }
            return -1;
        }
    }
}
