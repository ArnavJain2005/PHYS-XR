using System.Collections.Generic;
using System.Text;
using PHYSXR.Core.Enums;

namespace PHYSXR.Communication
{
    // Packet-level stand-in for the ESP32 sensor station (spec section E,
    // "Simulating physical events"). It produces the SAME bytes the firmware
    // contract v1 describes (ProvisionalTextPacketFormat.EncodeV1), which the
    // game feeds through the real UDPReceiver queue, validator and pipeline.
    // It is an alternative source, not a second architecture: nothing
    // downstream can tell a simulated datagram from a real one.
    //
    // Behaviour matches the proposed firmware defaults:
    //  - one state line every SendIntervalMs (10 Hz), plus one immediately
    //    when the semantic state changes;
    //  - a disturbance event gets a new event ID and is repeated on the next
    //    EventRepeatLines lines (the receiver's dedup drops the repeats);
    //  - device time is milliseconds since this simulated boot.
    //
    // Fault injection for tests and the dev hotkeys: resend the last event ID,
    // emit one stale line, reboot (new boot ID, sequence and event IDs restart
    // at 1), stop and resume sending.
    //
    // Plain C#, deterministic: all time comes from the caller's nowMs and boot
    // IDs come from a seeded generator.
    public class SimulatedSensorStation
    {
        public const string DefaultDeviceId = "phys-xr-01";
        public const long SendIntervalMs = 100L;
        public const int EventRepeatLines = 3;

        private readonly List<byte[]> outbox = new List<byte[]>();
        private readonly string deviceId;
        private uint bootSeed;

        private long bootStartedAt;
        private bool started;
        private long sequence;
        private long nextEventId = 1;
        private long lastSendAt = long.MinValue;
        private bool stateDirty;

        private long activeEventId;
        private long activeEventTime;
        private int activeEventLinesLeft;

        private long lastEventId;
        private long lastEventTime;

        public SimulatedSensorStation(string deviceId = DefaultDeviceId, uint seed = 2864130047u)
        {
            this.deviceId = deviceId;
            bootSeed = seed;
            Boot = NextBootId();
        }

        public long Boot { get; private set; }
        public PresenceLevel Presence { get; private set; } = PresenceLevel.NONE;
        public ConfidenceLevel Confidence { get; private set; } = ConfidenceLevel.HIGH;
        public bool Sending { get; private set; } = true;
        public long LastSequence => sequence;
        public long LastEventId => lastEventId;

        public void SetPresence(PresenceLevel presence)
        {
            if (presence != Presence)
                stateDirty = true;
            Presence = presence;
        }

        public void SetConfidence(ConfidenceLevel confidence)
        {
            if (confidence != Confidence)
                stateDirty = true;
            Confidence = confidence;
        }

        public void ToggleConfidence()
        {
            SetConfidence(Confidence == ConfidenceLevel.HIGH ? ConfidenceLevel.LOW : ConfidenceLevel.HIGH);
        }

        // A new qualified disturbance: new event ID, repeated on the next lines.
        public long TriggerDisturbance(long nowMs)
        {
            EnsureStarted(nowMs);
            activeEventId = nextEventId++;
            activeEventTime = DeviceTime(nowMs);
            activeEventLinesLeft = EventRepeatLines;
            lastEventId = activeEventId;
            lastEventTime = activeEventTime;
            stateDirty = true;
            return activeEventId;
        }

        // Fault: send the previous event ID again (must be DUPLICATE).
        public void ResendLastEvent(long nowMs)
        {
            if (lastEventId == 0)
                return;
            EnsureStarted(nowMs);
            outbox.Add(Line(nowMs, true, lastEventId, lastEventTime));
        }

        // Fault: one line whose device timestamp lags far behind its send time
        // (must be STALE_PACKET once the offset envelope is established).
        public void SendStaleLine(long nowMs, long lagMs = 1000L)
        {
            EnsureStarted(nowMs);
            // Never before boot: the device clock starts at 1.
            outbox.Add(Line(System.Math.Max(nowMs - lagMs, bootStartedAt), false, 0, 0));
        }

        // Fault: ESP32 reset. New boot ID; sequence and event IDs restart.
        public void Reboot(long nowMs)
        {
            Boot = NextBootId();
            bootStartedAt = nowMs;
            started = true;
            sequence = 0;
            nextEventId = 1;
            activeEventLinesLeft = 0;
            lastEventId = 0;
            lastSendAt = long.MinValue;
        }

        public void SetSending(bool sending)
        {
            Sending = sending;
            if (sending)
                lastSendAt = long.MinValue;
        }

        // Produces whatever the station would have sent by nowMs and returns
        // the datagrams (the list is reused; copy it if you keep it).
        public IReadOnlyList<byte[]> Poll(long nowMs)
        {
            EnsureStarted(nowMs);

            if (Sending && (stateDirty || lastSendAt == long.MinValue || nowMs - lastSendAt >= SendIntervalMs))
            {
                bool carryEvent = activeEventLinesLeft > 0;
                outbox.Add(Line(nowMs, carryEvent, carryEvent ? activeEventId : 0, carryEvent ? activeEventTime : 0));
                if (carryEvent)
                    activeEventLinesLeft--;
                lastSendAt = nowMs;
                stateDirty = false;
            }

            return outbox;
        }

        // Call after handing Poll()'s datagrams on.
        public void ClearOutbox()
        {
            outbox.Clear();
        }

        private void EnsureStarted(long nowMs)
        {
            if (started)
                return;
            started = true;
            bootStartedAt = nowMs;
        }

        // Device clock: ms since boot, never 0 (the validator rejects ts <= 0).
        private long DeviceTime(long nowMs)
        {
            return nowMs - bootStartedAt + 1;
        }

        private byte[] Line(long nowMs, bool withEvent, long eventId, long eventTime)
        {
            sequence++;
            string text = ProvisionalTextPacketFormat.EncodeV1(
                deviceId, Boot, sequence, DeviceTime(nowMs),
                Presence.ToString(), Confidence.ToString(),
                withEvent,
                withEvent ? "DISTURBANCE" : string.Empty,
                withEvent ? eventId : 0,
                withEvent ? eventTime : 0);
            return Encoding.UTF8.GetBytes(text);
        }

        // xorshift32: deterministic, non-zero boot identities.
        private long NextBootId()
        {
            uint x = bootSeed;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            bootSeed = x == 0 ? 1u : x;
            return bootSeed;
        }
    }
}
