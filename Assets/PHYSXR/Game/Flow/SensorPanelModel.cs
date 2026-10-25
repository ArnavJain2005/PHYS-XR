using System;
using System.Collections.Generic;
using System.Globalization;
using PHYSXR.Communication;

namespace PHYSXR.Game.Flow
{
    // Display-only view of the sensing link for the main menu's sensor panel.
    // It is built from the real diagnostics model (SerialLinkStatus and
    // StationDiagnostics) and never invents a reading: an invalid sensor reads
    // "No valid reading" (never 0), a sensor the station has not reported reads
    // "Not reported", and readings older than the diagnostics window are marked
    // STALE. Every state carries a word as well as a mark, so the panel never
    // relies on colour alone.
    //
    // Plain C# so the standalone harness tests it (SensorPanelModelTests).
    // Nothing here feeds PhysicalState or a ghost decision.
    public static class SensorPanelModel
    {
        public enum Mode
        {
            VirtualOnly,
            Simulated,
            UsbSerial,
            Udp
        }

        // How a value is drawn: Ok (valid, live), Notice (needs attention),
        // Fault (error or invalid), Off (no data, not applicable), Busy (in progress).
        public enum Mark
        {
            Ok,
            Notice,
            Fault,
            Off,
            Busy
        }

        // After this long connected without a PHYS-XR line, the panel says so.
        public const long NoDataNoticeMs = 3000;

        // A station #ERR older than this is no longer shown as the message.
        public const long StationErrorShownMs = 30000;

        public sealed class Field
        {
            public string Label;
            public string Value;
        }

        public sealed class Readout
        {
            public string Name;    // "ToF"
            public string Part;    // "VL53L0X"
            public string Value;   // "41.9 cm", "Nothing in range", "No valid reading"
            public string State;   // "VALID", "INVALID", "STALE", "NO DATA", "ARMED", ...
            public string Detail;  // "class NEAR  ·  age 0.2 s"
            public Mark Mark;
        }

        public sealed class Pin
        {
            public string Name;
            public string Value;
        }

        public sealed class View
        {
            public Mode Mode;
            public string Heading = string.Empty;
            public string Source = string.Empty;
            public string LinkWord = string.Empty;
            public Mark LinkMark = Mark.Off;
            public readonly List<Field> Station = new List<Field>();
            public readonly List<Readout> Readings = new List<Readout>();
            public readonly List<Pin> Pins = new List<Pin>();
            public string Message = string.Empty;
            public Mark MessageMark = Mark.Off;
            public string Explanation = string.Empty;

            public bool ShowsLiveReadings => Mode == Mode.UsbSerial;
        }

        public static string ModeName(Mode mode)
        {
            switch (mode)
            {
                case Mode.Simulated: return "Simulated sensors";
                case Mode.UsbSerial: return "Physical - USB serial";
                case Mode.Udp: return "Physical - UDP";
                default: return "Virtual only";
            }
        }

        // Non-serial modes: explain the active input source. No readings are
        // listed, because none come from hardware before a trial starts.
        public static View ForMode(Mode mode, int udpPort)
        {
            var v = new View { Mode = mode };
            switch (mode)
            {
                case Mode.Simulated:
                    v.Heading = "SENSOR INPUT";
                    v.LinkWord = "SIMULATED";
                    v.LinkMark = Mark.Notice;
                    v.Source = "Scripted station - no hardware is read";
                    v.Explanation = "After Begin, a simulated station sends lines through the same validator and "
                        + "pipeline as the real one. In game, keys 1-9 set presence, confidence, disturbance and "
                        + "faults; the F1 overlay shows what it sends.\n\nNo live readings are shown here: the "
                        + "simulator only runs during a trial, and simulated data is never presented as physical.";
                    break;
                case Mode.Udp:
                    v.Heading = "PHYSICAL SENSORS";
                    v.LinkWord = "AFTER BEGIN";
                    v.LinkMark = Mark.Off;
                    v.Source = "UDP port " + udpPort.ToString(CultureInfo.InvariantCulture);
                    v.Explanation = "The game listens for contract lines over UDP once the trial starts "
                        + "(Wi-Fi firmware, or Tools/esp32_sim.py). Windows Firewall may ask once.\n\n"
                        + "Live readings and link status appear in game (HUD chip and F1 overlay).";
                    break;
                default:
                    v.Heading = "SENSOR INPUT";
                    v.LinkWord = "VIRTUAL ONLY";
                    v.LinkMark = Mark.Off;
                    v.Source = "Physical input ignored (baseline condition)";
                    v.Explanation = "The ghost decides from the virtual world only. Any physical station's "
                        + "lines are still received and logged for comparison, but masked from decisions.\n\n"
                        + "Choose \"Physical - USB serial\" to check the ESP32 station before a trial.";
                    break;
            }
            return v;
        }

        // USB-serial mode. linkOpen: whether a serial link object exists.
        // configuredPort/baud: the menu's settings. rejectReason: the last
        // rejected line during a trial, or empty.
        public static View ForSerial(bool linkOpen, SerialLinkStatus link, string configuredPort, int baud,
            StationDiagnostics station, long nowMs, string rejectReason = "")
        {
            var v = new View { Mode = Mode.UsbSerial, Heading = "PHYSICAL SENSORS" };
            string baudText = baud.ToString(CultureInfo.InvariantCulture);
            bool auto = string.Equals(configuredPort, "AUTO", StringComparison.OrdinalIgnoreCase);
            string resolved = linkOpen && !string.IsNullOrEmpty(link.port) && link.port != "AUTO" ? link.port : null;
            v.Source = "USB serial  " + (auto ? "AUTO" + (resolved != null ? " > " + resolved : string.Empty) : configuredPort) + "  @ " + baudText;

            if (!linkOpen)
            {
                v.LinkWord = "STARTING";
                v.LinkMark = Mark.Busy;
            }
            else
            {
                switch (link.state)
                {
                    case SerialLinkState.CONNECTED:
                        if (link.identified)
                        {
                            bool fresh = station != null && station.DiagnosticsFresh(nowMs);
                            bool everDiag = station != null && station.HasDiag;
                            v.LinkWord = !everDiag || fresh ? "CONNECTED" : "CONNECTED - DATA STALE";
                            v.LinkMark = !everDiag || fresh ? Mark.Ok : Mark.Notice;
                        }
                        else
                        {
                            v.LinkWord = link.legacyFirmware ? "CONNECTED - OLD SKETCH" : "CONNECTED - NO PHYS-XR DATA";
                            v.LinkMark = Mark.Notice;
                        }
                        break;
                    case SerialLinkState.CONNECTING:
                        v.LinkWord = "CONNECTING";
                        v.LinkMark = Mark.Busy;
                        break;
                    case SerialLinkState.FAULTED:
                        v.LinkWord = "FAULT";
                        v.LinkMark = Mark.Fault;
                        break;
                    default:
                        v.LinkWord = "DISCONNECTED";
                        v.LinkMark = Mark.Fault;
                        break;
                }
            }

            bool hello = station != null && station.HasHello;
            v.Station.Add(new Field { Label = "Station", Value = hello ? station.DeviceId : "not identified" });
            v.Station.Add(new Field
            {
                Label = "Firmware",
                Value = hello
                    ? station.FirmwareVersion + "   protocol " + station.Protocol + (station.ProtocolSupported ? string.Empty : "  (UNSUPPORTED)")
                    : "-"
            });
            v.Station.Add(new Field
            {
                Label = "Last line",
                Value = linkOpen && link.lastLineMs >= 0 ? Seconds(nowMs - link.lastLineMs) + " ago" : "none yet"
            });
            v.Station.Add(new Field
            {
                Label = "Lines",
                Value = linkOpen
                    ? "state " + link.contractLines.ToString(CultureInfo.InvariantCulture)
                      + "   diag " + link.diagnosticLines.ToString(CultureInfo.InvariantCulture)
                      + "   other " + (link.noiseLines + link.legacyLines).ToString(CultureInfo.InvariantCulture)
                    : "-"
            });

            if (hello)
                ParsePins(station.PinSummary, v.Pins);

            AddReadings(v, station, nowMs);
            SetMessage(v, linkOpen, link, station, nowMs, rejectReason);
            return v;
        }

        private static void AddReadings(View v, StationDiagnostics station, long nowMs)
        {
            if (station == null || !station.HasDiag)
            {
                v.Readings.Add(NoData("ToF", "VL53L0X"));
                v.Readings.Add(NoData("Sonar", "HC-SR04"));
                v.Readings.Add(NoData("IMU", "MPU6050"));
                return;
            }

            bool fresh = station.DiagnosticsFresh(nowMs);
            string staleDetail = "last update " + Seconds(nowMs - station.DiagArrivalMs) + " ago";
            v.Readings.Add(Distance("ToF", "VL53L0X", station.Tof, station.TofFar, station, nowMs, fresh, staleDetail));
            v.Readings.Add(Distance("Sonar", "HC-SR04", station.Ultrasonic, false, station, nowMs, fresh, staleDetail));
            v.Readings.Add(Imu(station, fresh, staleDetail));
        }

        private static Readout NoData(string name, string part)
        {
            return new Readout { Name = name, Part = part, Value = "No data yet", State = "NO DATA", Detail = "waiting for #DIAG", Mark = Mark.Off };
        }

        private static Readout Distance(string name, string part, StationDiagnostics.Sensor s, bool far, StationDiagnostics station,
            long nowMs, bool fresh, string staleDetail)
        {
            var r = new Readout { Name = name, Part = part };
            if (!s.known)
            {
                r.Value = "Not reported";
                r.State = "NO DATA";
                r.Detail = "the station's #DIAG does not include it";
                r.Mark = Mark.Off;
                return r;
            }

            long age = station.AgeMs(s, nowMs);
            if (s.valid)
            {
                r.Value = far ? "Nothing in range" : s.value.ToString("F1", CultureInfo.InvariantCulture) + " cm";
                r.Detail = "class " + Class(s.cls) + (age >= 0 ? "   age " + Seconds(age) : string.Empty);
                r.State = "VALID";
                r.Mark = Mark.Ok;
            }
            else
            {
                r.Value = "No valid reading";
                r.Detail = s.deviceAgeMs < 0 ? "never valid since boot" : "last valid " + Seconds(age) + " ago";
                r.State = "INVALID";
                r.Mark = Mark.Fault;
            }

            if (!fresh)
            {
                r.State = "STALE";
                r.Mark = Mark.Notice;
                r.Detail = staleDetail;
            }
            return r;
        }

        private static Readout Imu(StationDiagnostics station, bool fresh, string staleDetail)
        {
            StationDiagnostics.Sensor s = station.Imu;
            var r = new Readout { Name = "IMU", Part = "MPU6050" };
            if (!s.known)
            {
                r.Value = "Not reported";
                r.State = "NO DATA";
                r.Detail = "the station's #DIAG does not include it";
                r.Mark = Mark.Off;
                return r;
            }

            if (s.valid)
            {
                r.Value = "|A| " + s.value.ToString("F3", CultureInfo.InvariantCulture) + " g   D " + station.ImuDisturbance.ToString("F3", CultureInfo.InvariantCulture) + " g";
                switch (station.ImuStage)
                {
                    case 0: r.State = "CALIBRATING"; r.Mark = Mark.Busy; break;
                    case 1: r.State = "ARMED"; r.Mark = Mark.Ok; break;
                    case 2: r.State = "LATCHED"; r.Mark = Mark.Ok; break;
                    case 3: r.State = "FAULT"; r.Mark = Mark.Fault; break;
                    default: r.State = "VALID"; r.Mark = Mark.Ok; break;
                }
                r.Detail = "events " + station.LastEventId.ToString(CultureInfo.InvariantCulture)
                    + "   trigger " + station.ImuOnThreshold.ToString("F3", CultureInfo.InvariantCulture) + " g"
                    + (station.ImuStage == 0 ? "   keep the station still" : string.Empty);
            }
            else
            {
                r.Value = "No valid reading";
                r.State = "INVALID";
                r.Detail = "accelerometer not answering";
                r.Mark = Mark.Fault;
            }

            if (!fresh)
            {
                r.State = "STALE";
                r.Mark = Mark.Notice;
                r.Detail = staleDetail;
            }
            return r;
        }

        // Most important first: link errors, then the wrong firmware, then
        // missing data, protocol, station errors and rejected lines.
        private static void SetMessage(View v, bool linkOpen, SerialLinkStatus link, StationDiagnostics station, long nowMs, string rejectReason)
        {
            if (linkOpen && (link.state == SerialLinkState.DISCONNECTED || link.state == SerialLinkState.FAULTED)
                && !string.IsNullOrEmpty(link.lastError))
            {
                v.Message = link.lastError;
                v.MessageMark = Mark.Fault;
                return;
            }
            if (linkOpen && link.state == SerialLinkState.CONNECTED && !link.identified && link.legacyFirmware)
            {
                v.Message = "This board runs an older bench sketch; its lines are ignored. Flash Firmware/physxr_station.";
                v.MessageMark = Mark.Notice;
                return;
            }
            if (linkOpen && link.state == SerialLinkState.CONNECTED && !link.identified && nowMs - link.sinceMs >= NoDataNoticeMs)
            {
                v.Message = "Port open, but no PHYS-XR lines yet: wrong baud rate, a different sketch, or the station is still booting.";
                v.MessageMark = Mark.Notice;
                return;
            }
            if (station != null && station.HasHello && !station.ProtocolSupported)
            {
                v.Message = "Station protocol " + station.Protocol + " is not supported (expected "
                    + StationDiagnostics.SupportedProtocol.ToString(CultureInfo.InvariantCulture) + "); its state lines are rejected.";
                v.MessageMark = Mark.Fault;
                return;
            }
            if (station != null && station.LastError.Length > 0 && station.LastErrorArrivalMs >= 0
                && nowMs - station.LastErrorArrivalMs <= StationErrorShownMs)
            {
                v.Message = "Station reported: " + station.LastError;
                v.MessageMark = Mark.Notice;
                return;
            }
            if (!string.IsNullOrEmpty(rejectReason))
            {
                v.Message = "Last rejected line: " + rejectReason;
                v.MessageMark = Mark.Notice;
            }
        }

        // "SDA 21  SCL 23  TRIG 25 ..." -> name/value pairs.
        public static void ParsePins(string summary, List<Pin> into)
        {
            if (string.IsNullOrEmpty(summary))
                return;
            string[] parts = summary.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2)
                into.Add(new Pin { Name = parts[i], Value = parts[i + 1] });
        }

        private static string Class(string cls) => string.IsNullOrEmpty(cls) || cls == "?" ? "-" : cls;

        public static string Seconds(long ms)
        {
            if (ms < 0)
                ms = 0;
            return (ms / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " s";
        }
    }
}
