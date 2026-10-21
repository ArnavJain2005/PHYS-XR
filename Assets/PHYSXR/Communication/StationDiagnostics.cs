using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PHYSXR.Communication
{
    // Latest bench diagnostics from the station's '#' lines (Firmware/README.md):
    //
    //   #HELLO proto=1;fw=1.0.0;dev=phys-xr-01;boot=..;baud=115200;sda=21;scl=23;trig=25;echo=26;mpu=0x68;tof=0x29
    //   #DIAG  ts=..;tof_ok=1;tof_far=0;tof_cm=41.9;tof_age=12;tof_cls=NEAR;us_ok=1;us_cm=43.0;us_age=40;
    //          us_cls=NEAR;imu_ok=1;imu_g=1.002;imu_d=0.0040;imu_on=0.0800;imu_cal=1;evt=3;seq=812;loop_us=950
    //   #CAL   state=done;base=..;noise=..;on=..;off=..   /   #ERR sensor=tof;msg=read_failed
    //
    // DISPLAY AND TELEMETRY ONLY. Nothing here is read by the validator,
    // PhysicalState, WorldState or the decision layer: the raw readings stay
    // below the semantic boundary. Main-thread only; fed by SensorService.
    public sealed class StationDiagnostics
    {
        public const int SupportedProtocol = PacketValidator.SupportedVersion;
        public const long DiagnosticsStaleMs = 2000;

        public struct Sensor
        {
            public bool known;      // at least one #DIAG mentioned it
            public bool valid;      // fresh and valid on the device at the last #DIAG
            public float value;     // last valid reading (cm for distance, g for IMU)
            public long deviceAgeMs;  // device-side age of that reading at the #DIAG; -1 never valid
            public string cls;      // per-sensor proximity class before fusion
        }

        private readonly Dictionary<string, string> hello = new Dictionary<string, string>();

        public bool HasHello { get; private set; }
        public long HelloArrivalMs { get; private set; } = -1;
        public bool HasDiag { get; private set; }
        public long DiagArrivalMs { get; private set; } = -1;

        public Sensor Tof;
        public Sensor Ultrasonic;
        public Sensor Imu;
        public bool TofFar { get; private set; }
        public float ImuDisturbance { get; private set; }
        public float ImuOnThreshold { get; private set; }
        public int ImuStage { get; private set; } = -1;  // 0 calibrating, 1 armed, 2 latched, 3 fault
        public long LastEventId { get; private set; }
        public long LoopMaxUs { get; private set; }

        public string LastCalibration { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public long LastErrorArrivalMs { get; private set; } = -1;
        public long DiagLines { get; private set; }
        public long MalformedLines { get; private set; }

        public string FirmwareVersion => Get(hello, "fw");
        public string DeviceId => Get(hello, "dev");
        public string Protocol => Get(hello, "proto");

        public bool ProtocolSupported => !HasHello || Protocol == SupportedProtocol.ToString(CultureInfo.InvariantCulture);

        public string PinSummary => HasHello
            ? "SDA " + Get(hello, "sda") + "  SCL " + Get(hello, "scl") + "  TRIG " + Get(hello, "trig") + "  ECHO " + Get(hello, "echo")
              + "  MPU " + Get(hello, "mpu") + "  ToF " + Get(hello, "tof")
            : string.Empty;

        public void Reset()
        {
            hello.Clear();
            HasHello = false;
            HelloArrivalMs = -1;
            HasDiag = false;
            DiagArrivalMs = -1;
            Tof = Ultrasonic = Imu = default;
            ImuStage = -1;
            LastCalibration = LastError = string.Empty;
            LastErrorArrivalMs = -1;
        }

        // Returns the tag ("HELLO", "DIAG", "CAL", "ERR", or another word) or
        // null if the line is not a well-formed diagnostic line.
        public string Apply(SerialDiagnosticLine line)
        {
            string text = line.text ?? string.Empty;
            if (text.Length < 2 || text[0] != '#')
            {
                MalformedLines++;
                return null;
            }

            int space = text.IndexOf(' ');
            string tag = space < 0 ? text.Substring(1) : text.Substring(1, space - 1);
            string body = space < 0 ? string.Empty : text.Substring(space + 1);
            Dictionary<string, string> fields = body.Length == 0 ? new Dictionary<string, string>() : ProvisionalTextPacketFormat.ParseFields(body);
            if (fields == null)
            {
                MalformedLines++;
                return null;
            }

            DiagLines++;
            switch (tag)
            {
                case "HELLO":
                    hello.Clear();
                    foreach (var pair in fields)
                        hello[pair.Key] = pair.Value;
                    HasHello = true;
                    HelloArrivalMs = line.arrivalMs;
                    break;

                case "DIAG":
                    HasDiag = true;
                    DiagArrivalMs = line.arrivalMs;
                    Tof = ReadSensor(fields, "tof", "tof_cm");
                    Ultrasonic = ReadSensor(fields, "us", "us_cm");
                    TofFar = Flag(fields, "tof_far");
                    Imu = new Sensor
                    {
                        known = fields.ContainsKey("imu_ok"),
                        valid = Flag(fields, "imu_ok"),
                        value = Float(fields, "imu_g"),
                        deviceAgeMs = 0,
                        cls = string.Empty
                    };
                    ImuDisturbance = Float(fields, "imu_d");
                    ImuOnThreshold = Float(fields, "imu_on");
                    ImuStage = (int)Long(fields, "imu_cal", -1);
                    LastEventId = Long(fields, "evt", 0);
                    LoopMaxUs = Long(fields, "loop_us", 0);
                    break;

                case "CAL":
                    LastCalibration = body;
                    break;

                case "ERR":
                    LastError = body;
                    LastErrorArrivalMs = line.arrivalMs;
                    break;
            }
            return tag;
        }

        // Age of a sensor's last valid reading as of nowMs (device age at the
        // #DIAG plus the time since it arrived); -1 if it never had one.
        public long AgeMs(Sensor sensor, long nowMs)
        {
            if (!sensor.known || sensor.deviceAgeMs < 0 || DiagArrivalMs < 0)
                return -1;
            return sensor.deviceAgeMs + Math.Max(0, nowMs - DiagArrivalMs);
        }

        public bool DiagnosticsFresh(long nowMs) => HasDiag && nowMs - DiagArrivalMs <= DiagnosticsStaleMs;

        public static string ImuStageName(int stage)
        {
            switch (stage)
            {
                case 0: return "CALIBRATING (keep still)";
                case 1: return "ARMED";
                case 2: return "LATCHED";
                case 3: return "FAULT (implausible baseline)";
                default: return "?";
            }
        }

        // Multi-line text for the menu and the developer overlay.
        public string Summary(long nowMs)
        {
            var b = new StringBuilder();
            if (HasHello)
            {
                b.Append("station  ").Append(DeviceId).Append("  fw ").Append(FirmwareVersion).Append("  proto ").Append(Protocol);
                if (!ProtocolSupported)
                    b.Append("  UNSUPPORTED (Unity expects ").Append(SupportedProtocol).Append(')');
                b.Append('\n').Append("pins     ").Append(PinSummary).Append('\n');
            }
            else
            {
                b.Append("station  no #HELLO yet\n");
            }

            if (!HasDiag)
            {
                b.Append("readings no #DIAG yet\n");
            }
            else
            {
                string stale = DiagnosticsFresh(nowMs) ? string.Empty : "  (diagnostics " + Seconds(nowMs - DiagArrivalMs) + " old)";
                b.Append("ToF      ").Append(Distance(Tof, nowMs, TofFar)).Append(stale).Append('\n');
                b.Append("sonar    ").Append(Distance(Ultrasonic, nowMs, false)).Append('\n');
                b.Append("IMU      ").Append(Imu.valid ? "|A| " + Imu.value.ToString("F3", CultureInfo.InvariantCulture) + " g" : "INVALID")
                    .Append("  D ").Append(ImuDisturbance.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" / ON ").Append(ImuOnThreshold.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" g  ").Append(ImuStageName(ImuStage)).Append("  events ").Append(LastEventId).Append('\n');
            }
            if (LastError.Length > 0)
                b.Append("last ERR ").Append(LastError).Append("  (").Append(Seconds(nowMs - LastErrorArrivalMs)).Append(" ago)\n");
            return b.ToString();
        }

        private string Distance(Sensor s, long nowMs, bool far)
        {
            if (!s.known)
                return "not reported";
            long age = AgeMs(s, nowMs);
            string reading = s.deviceAgeMs < 0 ? "never valid" : s.value.ToString("F1", CultureInfo.InvariantCulture) + " cm";
            if (far && s.valid)
                reading = "nothing in range";
            return (s.valid ? "VALID   " : "INVALID ") + reading + "  class " + s.cls + (age >= 0 ? "  age " + Seconds(age) : string.Empty);
        }

        private static string Seconds(long ms) => (ms / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " s";

        private static Sensor ReadSensor(Dictionary<string, string> f, string prefix, string valueKey)
        {
            return new Sensor
            {
                known = f.ContainsKey(prefix + "_ok"),
                valid = Flag(f, prefix + "_ok"),
                value = Float(f, valueKey),
                deviceAgeMs = Long(f, prefix + "_age", -1),
                cls = Get(f, prefix + "_cls")
            };
        }

        private static string Get(Dictionary<string, string> f, string key) => f.TryGetValue(key, out string v) ? v : "?";

        private static bool Flag(Dictionary<string, string> f, string key) => f.TryGetValue(key, out string v) && v == "1";

        private static float Float(Dictionary<string, string> f, string key)
        {
            return f.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                   && !float.IsNaN(x) && !float.IsInfinity(x) ? x : 0f;
        }

        private static long Long(Dictionary<string, string> f, string key, long fallback)
        {
            return f.TryGetValue(key, out string v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) ? x : fallback;
        }
    }
}
