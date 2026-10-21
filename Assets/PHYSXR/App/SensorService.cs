using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PHYSXR.Communication;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Flow;
using PHYSXR.PhysicalWorld;
using PHYSXR.Telemetry;
using UnityEngine;
using UnityEngine.InputSystem;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.App
{
    // The sensing side of the persistent [Systems] object (spec section C:
    // "UDPReceiver + PhysicalCommunicationPipeline and the state and event
    // managers" live in Bootstrap so the link and the event-dedup memory
    // survive game-scene reloads).
    //
    // Per trial condition:
    //   VIRTUAL_ONLY           the selected physical link is open; packets
    //                          validated and logged; events logged MASKED.
    //                          The game also masks the physical part of WorldState.
    //   PHYSICAL_PLUS_VIRTUAL  live contract lines over the selected link:
    //                          USB serial (default; the project's ESP32 link)
    //                          or UDP on Port.
    //   SIMULATED              no link; SimulatedSensorStation produces the
    //                          same bytes into the same queue. The serial port
    //                          is released, and the HUD says SIMULATED.
    //
    // Both links deliver into the same SensorFeed -> PacketValidator ->
    // PhysicalCommunicationPipeline, so serial and simulated input reach
    // WorldState and R1-R8 identically. When the link is down, Status goes
    // STALE then UNAVAILABLE and the game runs as virtual-only; nothing is
    // ever substituted for missing hardware data.
    //
    // Each frame (early, see DefaultExecutionOrder) it drains the queue on
    // the main thread through SensorFeed, writes one telemetry record per
    // line/datagram, and publishes this frame's accepted events and the source
    // status for the game to push into its WorldStateManager. It never selects
    // a ghost action and never touches gameplay.
    //
    // The serial link outlives trials: the menu opens it as soon as "Physical
    // sensors - USB serial" is selected so the live readings can be checked
    // before Begin, and a trial with the same port settings reuses it.
    //
    // Dev hotkeys (SIMULATED only; the spec's letter keys clash with WASD, so
    // the number row is used): 1/2/3 presence NONE/NEAR/VERY_NEAR, 4 toggle
    // confidence, 5 new disturbance event, 6 resend last event ID, 7 send a
    // stale line, 8 simulate a reboot, 9 stop/resume sending.
    [DefaultExecutionOrder(-200)]
    public class SensorService : MonoBehaviour, IPhysicalInputSource
    {
        public const int DefaultPort = 47800;
        private const float SummaryIntervalSeconds = 5f;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<PhysicalEvent> frameEvents = new List<PhysicalEvent>();
        private readonly StationDiagnostics station = new StationDiagnostics();
        private readonly StringBuilder text = new StringBuilder();

        private ISerialPortFactory serialPorts = new Win32SerialPorts();
        private UDPReceiver receiver;
        private SerialLineReceiver serial;
        private PhysicalCommunicationPipeline pipeline;
        private SensorFeed feed;
        private SimulatedSensorStation simulator;
        private TelemetryChannel telemetry = TelemetryChannel.Null;
        private bool socketWanted;
        private string lastHello;
        private bool legacyReported;
        private bool protocolReported;
        private long idleDiscarded;
        private float nextSummaryAt;
        private string lastCounters;

        public int Port { get; private set; } = DefaultPort;

        public string DeviceId { get; private set; } = SimulatedSensorStation.DefaultDeviceId;

        public RunCondition Condition { get; private set; } = RunCondition.VIRTUAL_ONLY;

        public PhysicalTransport Transport { get; private set; } = PhysicalTransport.USB_SERIAL;

        public bool Configured => feed != null;

        public PhysicalStateManager StateManager { get; private set; } = new PhysicalStateManager();

        public PhysicalSourceStatus Status => feed?.Status ?? PhysicalSourceStatus.UNAVAILABLE;

        public bool PhysicalInputMasked => Condition == RunCondition.VIRTUAL_ONLY;

        // Events accepted this frame (deduplicated, fresh, resynced, not in a
        // blackout, not masked). Cleared at the start of the next frame.
        public IReadOnlyList<PhysicalEvent> FrameAcceptedEvents => frameEvents;

        public PhysicalCommunicationPipeline Pipeline => pipeline;

        public SimulatedSensorStation Simulator => simulator;

        public UDPReceiver Receiver => receiver;

        public SerialLineReceiver Serial => serial;

        public StationDiagnostics Station => station;

        public long NowMs => clock.ElapsedMilliseconds;

        public string LastRejectReason { get; private set; } = string.Empty;

        public void SetTelemetry(TelemetryChannel channel)
        {
            telemetry = channel ?? TelemetryChannel.Null;
        }

        // Tests and tools can swap the port backend (the default is kernel32).
        public void SetSerialPortFactory(ISerialPortFactory factory)
        {
            ReleaseSerialLink();
            serialPorts = factory ?? new Win32SerialPorts();
        }

        public IReadOnlyList<SerialPortInfo> ListSerialPorts()
        {
            try
            {
                return serialPorts.ListPorts();
            }
            catch (System.Exception)
            {
                return new SerialPortInfo[0];
            }
        }

        // Opens (or keeps) the USB-serial link with these settings. Changing
        // the port or baud closes the old link first, so there is never more
        // than one open port.
        public void UseSerialLink(SerialLinkSettings settings)
        {
            if (serial != null && serial.Settings.SameLink(settings))
            {
                serial.Start();
                return;
            }

            ReleaseSerialLink();
            serial = new SerialLineReceiver(serialPorts, settings, () => clock.ElapsedMilliseconds);
            station.Reset();
            lastHello = null;
            legacyReported = false;
            protocolReported = false;
            serial.Start();
            UnityEngine.Debug.Log("[Sensors] USB serial link " + settings + " starting");
        }

        public void ReleaseSerialLink()
        {
            if (serial == null)
                return;
            serial.Stop();
            DrainSerialEvents();
            serial = null;
            UnityEngine.Debug.Log("[Sensors] USB serial link released");
        }

        // Builds a fresh validator, pipeline and dedup memory for a new trial
        // (and a fresh UDP receiver when UDP is used). Safe to call again for
        // the next trial.
        public void Configure(RunCondition condition, PhysicalTransport transport = PhysicalTransport.UDP,
            SerialLinkSettings serialSettings = null, int port = DefaultPort,
            string deviceId = SimulatedSensorStation.DefaultDeviceId)
        {
            ShutdownTrial();

            Condition = condition;
            Transport = transport;
            Port = port;
            DeviceId = deviceId;
            StateManager = new PhysicalStateManager();

            var validator = new PacketValidator(deviceId) { RequireVersion = UsesSerial, RequireDeviceId = UsesSerial };
            pipeline = new PhysicalCommunicationPipeline(validator, new PhysicalStateParser(), StateManager, new PhysicalEventManager())
            {
                EventsMasked = condition == RunCondition.VIRTUAL_ONLY
            };

            simulator = condition == RunCondition.SIMULATED ? new SimulatedSensorStation(deviceId) : null;
            socketWanted = condition != RunCondition.SIMULATED && transport == PhysicalTransport.UDP;

            string link;
            if (UsesSerial)
            {
                UseSerialLink(serialSettings ?? new SerialLinkSettings());
                serial.ClearQueue();
                feed = new SensorFeed(serial, pipeline);
                link = "serial=" + serial.Settings;
            }
            else
            {
                // SIMULATED or UDP: the UDP receiver queue is the source (the
                // simulator enqueues into it without opening a socket).
                ReleaseSerialLink();
                receiver = new UDPReceiver(port, () => clock.ElapsedMilliseconds);
                feed = new SensorFeed(receiver, pipeline);
                if (socketWanted)
                    receiver.Start();
                link = socketWanted ? "udp=" + port : "simulator=on";
            }

            telemetry.Emit("SOURCE_STATUS", "from=NONE to=UNAVAILABLE condition=" + condition + " transport="
                + (condition == RunCondition.SIMULATED ? "SIMULATED" : transport.ToString()) + " " + link);
            UnityEngine.Debug.Log("[Sensors] condition " + condition + ", " + SourceLabel);
        }

        private bool UsesSerial => Condition != RunCondition.SIMULATED && Transport == PhysicalTransport.USB_SERIAL;

        // The game's WorldStateManager reads the shared PhysicalStateManager,
        // gated by freshness and masking (P2).
        public WorldStateManager CreateWorldStateManager(System.Func<long> worldClock)
        {
            return new WorldStateManager(StateManager, worldClock);
        }

        // Called on a checkpoint restart: no event is accepted for durationMs.
        public void BeginEventBlackout(long durationMs)
        {
            pipeline?.BeginEventBlackout(NowMs, durationMs);
        }

        // ------------------------------------------------------------ display

        public string SourceLabel
        {
            get
            {
                if (feed != null && Condition == RunCondition.SIMULATED)
                    return "SIMULATED";
                string link = LinkLabel();
                return feed != null && Condition == RunCondition.VIRTUAL_ONLY ? "OFF (virtual only)  " + link : link;
            }
        }

        private string LinkLabel()
        {
            if (Transport == PhysicalTransport.UDP && receiver != null)
                return "UDP " + Port + (receiver.IsBound ? string.Empty : " (not bound)");
            if (serial == null)
                return "USB serial off";

            SerialLinkStatus s = serial.Status;
            string label = "USB " + s.port + " " + s.state;
            if (s.state == SerialLinkState.CONNECTED && !s.identified)
                label += s.legacyFirmware ? " (old raw-distance sketch, flash physxr_station)" : " (no PHYS-XR data yet)";
            else if (s.state != SerialLinkState.CONNECTED && !string.IsNullOrEmpty(s.lastError))
                label += ": " + s.lastError;
            return label;
        }

        // Link, counters, station readings: for the menu and the F1 overlay.
        public string DiagnosticsText
        {
            get
            {
                long now = NowMs;
                text.Clear();
                text.Append("link     ").Append(SourceLabel).Append('\n');
                if (pipeline != null)
                {
                    text.Append("source   ").Append(Status).Append("  accepted ").Append(pipeline.PacketsAccepted)
                        .Append(" rejected ").Append(pipeline.PacketsRejected).Append(" lost ").Append(pipeline.PacketsLost)
                        .Append(" reboots ").Append(pipeline.Reboots).Append('\n');
                }
                else
                {
                    text.Append("source   no trial running (readings are previewed, not used)\n");
                }

                if (serial != null)
                {
                    SerialLinkStatus s = serial.Status;
                    text.Append("serial   lines ").Append(s.contractLines).Append(" diag ").Append(s.diagnosticLines)
                        .Append(" noise ").Append(s.noiseLines + s.legacyLines).Append(" oversized ").Append(s.oversizedLines)
                        .Append(" dropped ").Append(s.droppedLines).Append(" reconnects ").Append(s.reconnects);
                    if (s.lastLineMs >= 0)
                        text.Append("  last line ").Append(((now - s.lastLineMs) / 1000.0).ToString("F1", CultureInfo.InvariantCulture)).Append(" s ago");
                    text.Append('\n');
                    text.Append(station.Summary(now));
                }
                if (!string.IsNullOrEmpty(LastRejectReason))
                    text.Append("reject   ").Append(LastRejectReason).Append('\n');
                return text.ToString();
            }
        }

        // ------------------------------------------------------------ frame

        private void Update()
        {
            frameEvents.Clear();
            DrainSerialEvents();
            DrainDiagnostics();

            if (feed == null)
            {
                // Menu preview: the link is open but no trial is running.
                // State lines are discarded here, never stored.
                if (serial != null)
                    while (serial.TryDequeue(out _))
                        idleDiscarded++;
                return;
            }

            long now = NowMs;
            if (simulator != null)
            {
                HandleSimulatorHotkeys(now);
                foreach (byte[] datagram in simulator.Poll(now))
                    receiver.Enqueue(datagram, now);
                simulator.ClearOutbox();
            }

            IReadOnlyList<PacketOutcome> outcomes = feed.Pump(now);
            for (int i = 0; i < outcomes.Count; i++)
                Log(outcomes[i]);

            if (feed.StatusChanged)
                telemetry.Emit("SOURCE_STATUS", "from=" + feed.PreviousStatus + " to=" + feed.Status + " link=" + LinkLabel());

            if (Time.unscaledTime >= nextSummaryAt)
            {
                nextSummaryAt = Time.unscaledTime + SummaryIntervalSeconds;
                EmitSummary(now);
            }
        }

        // Link state changes: telemetry and the console, once per change.
        private void DrainSerialEvents()
        {
            if (serial == null)
                return;
            while (serial.TryDequeueEvent(out SerialLinkEvent e))
            {
                telemetry.Emit("SERIAL_LINK", "state=" + e.state + " port=" + e.port + " t2=" + e.atMs + " detail=" + e.detail);
                string message = "[Sensors] USB serial " + e.port + " " + e.state + ": " + e.detail;
                if (e.state == SerialLinkState.FAULTED)
                    UnityEngine.Debug.LogWarning(message);
                else
                    UnityEngine.Debug.Log(message);
            }

            SerialLinkStatus s = serial.Status;
            if (s.legacyFirmware && !legacyReported)
            {
                legacyReported = true;
                telemetry.Emit("SERIAL_LEGACY_FIRMWARE", "port=" + s.port);
                UnityEngine.Debug.LogWarning("[Sensors] " + s.port + " is sending raw bench lines (old sketch). "
                    + "They are ignored; flash Firmware/physxr_station to drive the ghost.");
            }
        }

        // '#' lines: display state, plus telemetry for the rare ones.
        private void DrainDiagnostics()
        {
            if (serial == null)
                return;
            while (serial.TryDequeueDiagnostic(out SerialDiagnosticLine line))
            {
                string tag = station.Apply(line);
                if (tag == "HELLO")
                {
                    // Repeated every 5 s; log only when it changes (new boot).
                    string key = station.DeviceId + "|" + line.text.Length + "|" + station.FirmwareVersion + "|" + station.PinSummary;
                    if (key != lastHello)
                    {
                        lastHello = key;
                        telemetry.Emit("STATION_LOG", "t2=" + line.arrivalMs + " " + line.text);
                        UnityEngine.Debug.Log("[Sensors] station " + line.text);
                    }
                    if (!station.ProtocolSupported && !protocolReported)
                    {
                        protocolReported = true;
                        UnityEngine.Debug.LogWarning("[Sensors] station protocol " + station.Protocol + " is not supported (expected "
                            + StationDiagnostics.SupportedProtocol + "); its state lines will be rejected.");
                    }
                }
                else if (tag == "CAL" || tag == "ERR")
                {
                    telemetry.Emit("STATION_LOG", "t2=" + line.arrivalMs + " " + line.text);
                    if (tag == "ERR")
                        UnityEngine.Debug.LogWarning("[Sensors] station " + line.text);
                    else
                        UnityEngine.Debug.Log("[Sensors] station " + line.text);
                }
            }
        }

        // Every 5 s during a trial: sensor validity/ages and link counters, so
        // a log shows what the hardware was doing without per-line records.
        private void EmitSummary(long now)
        {
            if (serial == null)
                return;
            SerialLinkStatus s = serial.Status;
            string counters = string.Format(CultureInfo.InvariantCulture,
                "state={0} port={1} identified={2} lines={3} diag={4} noise={5} legacy={6} oversized={7} dropped={8} reconnects={9} idle_discarded={10}",
                s.state, s.port, s.identified, s.contractLines, s.diagnosticLines, s.noiseLines, s.legacyLines, s.oversizedLines,
                s.droppedLines, s.reconnects, idleDiscarded);
            if (counters != lastCounters)
            {
                lastCounters = counters;
                telemetry.Emit("SERIAL_COUNTERS", counters);
            }

            if (station.HasDiag)
            {
                telemetry.Emit("SENSOR_DIAG", string.Format(CultureInfo.InvariantCulture,
                    "tof_ok={0} tof_cm={1:F1} tof_age={2} us_ok={3} us_cm={4:F1} us_age={5} imu_ok={6} imu_g={7:F3} imu_d={8:F4} imu_on={9:F4} imu_stage={10} diag_age={11}",
                    station.Tof.valid ? 1 : 0, station.Tof.value, station.AgeMs(station.Tof, now),
                    station.Ultrasonic.valid ? 1 : 0, station.Ultrasonic.value, station.AgeMs(station.Ultrasonic, now),
                    station.Imu.valid ? 1 : 0, station.Imu.value, station.ImuDisturbance, station.ImuOnThreshold, station.ImuStage,
                    now - station.DiagArrivalMs));
            }
        }

        private void Log(PacketOutcome outcome)
        {
            if (outcome.isNewBoot)
                telemetry.Emit("ESP32_REBOOT", "boot=" + outcome.boot);

            if (outcome.accepted)
            {
                // PKT_RX for every datagram would be ~10 lines/s; it is the
                // record the spec asks for, and the writer is off-thread.
                telemetry.Emit("PKT_RX", string.Format(CultureInfo.InvariantCulture,
                    "boot={0} seq={1} bytes={2} t1_dev={3} t2={4} age={5} gap={6} presence={7} confidence={8}",
                    outcome.boot, outcome.sequence, outcome.bytes, outcome.deviceTimestamp, outcome.arrivalMs,
                    outcome.transitAgeMs, outcome.sequenceGap, outcome.state?.presence, outcome.state?.confidence));
            }
            else
            {
                LastRejectReason = outcome.code + ": " + outcome.reason;
                telemetry.Emit("PKT_REJECT", string.Format(CultureInfo.InvariantCulture,
                    "reason={0} boot={1} seq={2} bytes={3} t2={4} detail={5}",
                    outcome.code, outcome.boot, outcome.sequence, outcome.bytes, outcome.arrivalMs, outcome.reason));
            }

            if (outcome.eventResult != EventAcceptance.NoEvent)
            {
                telemetry.Emit("PHYS_EVENT", string.Format(CultureInfo.InvariantCulture,
                    "evt={0} result={1} boot={2} t2={3}", outcome.eventId, outcome.eventResult, outcome.boot, outcome.arrivalMs),
                    relatedId: outcome.eventId);
                if (outcome.acceptedEvent != null)
                    frameEvents.Add(outcome.acceptedEvent);
            }
        }

        private void HandleSimulatorHotkeys(long now)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame) simulator.SetPresence(PresenceLevel.NONE);
            if (keyboard.digit2Key.wasPressedThisFrame) simulator.SetPresence(PresenceLevel.NEAR);
            if (keyboard.digit3Key.wasPressedThisFrame) simulator.SetPresence(PresenceLevel.VERY_NEAR);
            if (keyboard.digit4Key.wasPressedThisFrame) simulator.ToggleConfidence();
            if (keyboard.digit5Key.wasPressedThisFrame) simulator.TriggerDisturbance(now);
            if (keyboard.digit6Key.wasPressedThisFrame) simulator.ResendLastEvent(now);
            if (keyboard.digit7Key.wasPressedThisFrame) simulator.SendStaleLine(now);
            if (keyboard.digit8Key.wasPressedThisFrame) simulator.Reboot(now);
            if (keyboard.digit9Key.wasPressedThisFrame) simulator.SetSending(!simulator.Sending);
        }

        // Quest: the socket closes while the app is paused (headset off) and
        // reopens on resume; status reads UNAVAILABLE until packets return.
        // The USB-serial link is desktop-only and stays open.
        private void OnApplicationPause(bool paused)
        {
            if (receiver == null || !socketWanted)
                return;

            if (paused)
                receiver.Stop();
            else
                receiver.Start();
        }

        private void ShutdownTrial()
        {
            receiver?.Stop();
            receiver = null;
            feed = null;
            pipeline = null;
            simulator = null;
            frameEvents.Clear();
            LastRejectReason = string.Empty;
        }

        // Exiting Play Mode or quitting must release the COM port, or the
        // Arduino IDE (and the next Play) cannot open it.
        private void OnApplicationQuit()
        {
            ShutdownTrial();
            ReleaseSerialLink();
        }

        private void OnDestroy()
        {
            ShutdownTrial();
            ReleaseSerialLink();
        }
    }
}
