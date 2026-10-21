using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    // managers" live in Bootstrap so the socket and the event-dedup memory
    // survive game-scene reloads).
    //
    // Per trial condition:
    //   VIRTUAL_ONLY           UDP socket open; packets validated and logged;
    //                          events logged MASKED. The game also masks the
    //                          physical part of WorldState.
    //   PHYSICAL_PLUS_VIRTUAL  UDP socket open on Port; live packets used.
    //   SIMULATED              no socket; SimulatedSensorStation produces the
    //                          same bytes into the same receiver queue.
    //
    // Each frame (early, see DefaultExecutionOrder) it drains the receiver on
    // the main thread through SensorFeed, writes one telemetry record per
    // datagram, and publishes this frame's accepted events and the source
    // status for the game to push into its WorldStateManager. It never selects
    // a ghost action and never touches gameplay.
    //
    // Dev hotkeys (SIMULATED only; the spec's letter keys clash with WASD, so
    // the number row is used): 1/2/3 presence NONE/NEAR/VERY_NEAR, 4 toggle
    // confidence, 5 new disturbance event, 6 resend last event ID, 7 send a
    // stale line, 8 simulate a reboot, 9 stop/resume sending.
    [DefaultExecutionOrder(-200)]
    public class SensorService : MonoBehaviour, IPhysicalInputSource
    {
        public const int DefaultPort = 47800;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<PhysicalEvent> frameEvents = new List<PhysicalEvent>();

        private UDPReceiver receiver;
        private PhysicalCommunicationPipeline pipeline;
        private SensorFeed feed;
        private SimulatedSensorStation simulator;
        private TelemetryChannel telemetry = TelemetryChannel.Null;
        private bool socketWanted;

        public int Port { get; private set; } = DefaultPort;

        public string DeviceId { get; private set; } = SimulatedSensorStation.DefaultDeviceId;

        public RunCondition Condition { get; private set; } = RunCondition.VIRTUAL_ONLY;

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

        public long NowMs => clock.ElapsedMilliseconds;

        public string LastRejectReason { get; private set; } = string.Empty;

        public void SetTelemetry(TelemetryChannel channel)
        {
            telemetry = channel ?? TelemetryChannel.Null;
        }

        // Builds a fresh receiver, validator, pipeline and dedup memory for a
        // new trial. Safe to call again for the next trial.
        public void Configure(RunCondition condition, int port = DefaultPort, string deviceId = SimulatedSensorStation.DefaultDeviceId)
        {
            Shutdown();

            Condition = condition;
            Port = port;
            DeviceId = deviceId;
            StateManager = new PhysicalStateManager();

            receiver = new UDPReceiver(port, () => clock.ElapsedMilliseconds);
            pipeline = new PhysicalCommunicationPipeline(new PacketValidator(deviceId), new PhysicalStateParser(),
                StateManager, new PhysicalEventManager())
            {
                EventsMasked = condition == RunCondition.VIRTUAL_ONLY
            };
            feed = new SensorFeed(receiver, pipeline);

            simulator = condition == RunCondition.SIMULATED ? new SimulatedSensorStation(deviceId) : null;
            socketWanted = condition != RunCondition.SIMULATED;
            if (socketWanted)
                receiver.Start();

            telemetry.Emit("SOURCE_STATUS", "from=NONE to=UNAVAILABLE condition=" + condition
                + (socketWanted ? " port=" + port : " simulator=on"));
            UnityEngine.Debug.Log("[Sensors] condition " + condition + (socketWanted ? ", listening on UDP " + port : ", simulated station"));
        }

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

        private void Update()
        {
            frameEvents.Clear();
            if (feed == null)
                return;

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
                telemetry.Emit("SOURCE_STATUS", "from=" + feed.PreviousStatus + " to=" + feed.Status);
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
        private void OnApplicationPause(bool paused)
        {
            if (receiver == null || !socketWanted)
                return;

            if (paused)
                receiver.Stop();
            else
                receiver.Start();
        }

        private void Shutdown()
        {
            receiver?.Stop();
            receiver = null;
            feed = null;
            pipeline = null;
            simulator = null;
            frameEvents.Clear();
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
