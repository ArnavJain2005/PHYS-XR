using System.Collections.Generic;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.PhysicalWorld;

namespace PHYSXR.Communication
{
    // Wires PacketValidator -> PhysicalStateParser into the existing
    // PhysicalStateManager / PhysicalEventManager. This is integration glue
    // only: it contains no Ghost decision logic (never references
    // GhostAction/IDecisionProvider) and no sensor-specific logic.
    //
    // Duplicate event-ID rejection is intentionally NOT reimplemented here -
    // PhysicalEventManager already does it, so parsed events are simply
    // handed to it.
    //
    // Two entry points:
    //
    //  HandleRawPacket(raw, nowTimestamp) + GetStatus(now): the ORIGINAL API.
    //    It judges staleness by comparing the packet's device timestamp with
    //    the caller's clock, which only works if both share an epoch. Kept
    //    unchanged for its existing tests and callers.
    //
    //  Process(raw, arrivalMs) + GetSourceStatus(now): the live path (fix P5,
    //    spec section E). The device timestamp is used only for ordering and
    //    latency; freshness comes from Unity-side ARRIVAL time:
    //      - transit age = (arrival - ts) - offset, where offset is the
    //        minimum (arrival - ts) over the last 50 packets of this boot.
    //        Age > StaleTransitAgeMs -> STALE_PACKET: logged, never committed,
    //        cannot refresh freshness or create an event.
    //      - FRESH while the last accepted state line arrived <= FreshWindowMs
    //        ago, STALE up to UnavailableAfterMs, then UNAVAILABLE.
    //      - events are accepted only once resync is complete (2 consecutive
    //        in-order state lines since the last UNAVAILABLE or reboot), only
    //        if ts - tEvent <= MaxEventAgeMs, never during a restart blackout,
    //        and never while events are masked (VIRTUAL_ONLY condition).
    //    Every datagram yields exactly one PacketOutcome for telemetry.
    public class PhysicalCommunicationPipeline
    {
        public const long DefaultStaleTransitAgeMs = 250L;
        public const long DefaultFreshWindowMs = 500L;
        public const long DefaultUnavailableAfterMs = 3000L;
        public const long DefaultMaxEventAgeMs = 300L;
        public const int OffsetWindowSize = 50;
        public const int ResyncStateLines = 2;

        private readonly PacketValidator validator;
        private readonly PhysicalStateParser parser;
        private readonly PhysicalStateManager stateManager;
        private readonly PhysicalEventManager eventManager;
        private readonly long staleThresholdMillis;

        private long lastAcceptedTimestamp = -1;
        private bool lastPacketWasValid = true;
        private bool lastPacketWasStale;

        // Live-path state.
        private readonly Queue<long> offsetWindow = new Queue<long>();
        private long lastStateArrival = long.MinValue;
        private int inOrderStateLines;
        private long blackoutUntil = long.MinValue;

        public PhysicalCommunicationPipeline(
            PacketValidator validator,
            PhysicalStateParser parser,
            PhysicalStateManager stateManager,
            PhysicalEventManager eventManager,
            long staleThresholdMillis = 2000)
        {
            this.validator = validator;
            this.parser = parser;
            this.stateManager = stateManager;
            this.eventManager = eventManager;
            this.staleThresholdMillis = staleThresholdMillis;
        }

        public long StaleTransitAgeMs { get; set; } = DefaultStaleTransitAgeMs;
        public long FreshWindowMs { get; set; } = DefaultFreshWindowMs;
        public long UnavailableAfterMs { get; set; } = DefaultUnavailableAfterMs;
        public long MaxEventAgeMs { get; set; } = DefaultMaxEventAgeMs;

        // VIRTUAL_ONLY: packets and state are still processed (so both trial
        // conditions report the same reliability figures), but events are
        // logged MASKED and never accepted.
        public bool EventsMasked { get; set; }

        // Totals for the dev overlay and the run summary.
        public long PacketsReceived { get; private set; }
        public long PacketsAccepted { get; private set; }
        public long PacketsRejected { get; private set; }
        public long PacketsLost { get; private set; }
        public long Reboots { get; private set; }

        // nowTimestamp is supplied by the caller (rather than read from a
        // system clock in here) so staleness handling stays deterministic
        // and testable without real time passing.
        public void HandleRawPacket(byte[] rawData, long nowTimestamp)
        {
            ValidationResult result = validator.Validate(rawData);
            lastPacketWasValid = result.isValid;

            if (!result.isValid)
            {
                lastPacketWasStale = false;
                return;
            }

            if (!parser.TryParseState(result.packet, out PhysicalState state))
            {
                lastPacketWasValid = false;
                lastPacketWasStale = false;
                return;
            }

            // Staleness is decided before anything is committed, so a
            // time-stale packet can neither overwrite PhysicalStateManager
            // nor generate a PhysicalEvent - the previously accepted
            // PhysicalState is preserved untouched.
            bool isStale = (nowTimestamp - result.packet.timestamp) > staleThresholdMillis;
            lastPacketWasStale = isStale;

            if (isStale)
                return;

            stateManager.UpdateState(state);
            lastAcceptedTimestamp = result.packet.timestamp;

            if (parser.TryParseEvent(result.packet, out PhysicalEvent physicalEvent))
                eventManager.RegisterEvent(physicalEvent);
        }

        // Explicit stale/invalid signal for the future decision layer to
        // consult, rather than the communication layer selecting a
        // GhostAction on communication failure.
        public CommunicationState GetStatus(long nowTimestamp)
        {
            if (!lastPacketWasValid)
                return CommunicationState.Invalid;

            if (lastPacketWasStale)
                return CommunicationState.Stale;

            if (lastAcceptedTimestamp < 0)
                return CommunicationState.Stale;

            if ((nowTimestamp - lastAcceptedTimestamp) > staleThresholdMillis)
                return CommunicationState.Stale;

            return CommunicationState.Ok;
        }

        // ------------------------------------------------------------ live path

        // Freshness from arrival time only.
        public PhysicalSourceStatus GetSourceStatus(long nowMs)
        {
            if (lastStateArrival == long.MinValue)
                return PhysicalSourceStatus.UNAVAILABLE;

            long since = nowMs - lastStateArrival;
            if (since <= FreshWindowMs)
                return PhysicalSourceStatus.FRESH;
            if (since <= UnavailableAfterMs)
                return PhysicalSourceStatus.STALE;
            return PhysicalSourceStatus.UNAVAILABLE;
        }

        // After a checkpoint restart, no event is accepted for durationMs
        // (spec: 1 s), so an event from before the restart cannot immediately
        // retrigger the encounter.
        public void BeginEventBlackout(long nowMs, long durationMs)
        {
            blackoutUntil = nowMs + durationMs;
        }

        public bool IsBlackoutActive(long nowMs) => nowMs < blackoutUntil;

        public PacketOutcome Process(byte[] rawData, long arrivalMs)
        {
            PacketsReceived++;
            var outcome = new PacketOutcome { arrivalMs = arrivalMs, bytes = rawData?.Length ?? 0 };

            // Silence longer than the UNAVAILABLE limit means events must
            // resynchronise, whatever arrives next.
            if (GetSourceStatus(arrivalMs) == PhysicalSourceStatus.UNAVAILABLE)
                inOrderStateLines = 0;

            ValidationResult result = validator.Validate(rawData);
            if (!result.isValid)
            {
                if (result.code == PacketRejectCode.OUT_OF_ORDER)
                    inOrderStateLines = 0;
                return Rejected(outcome, result.code, result.failureReason);
            }

            SemanticPacket packet = result.packet;
            outcome.boot = packet.boot;
            outcome.sequence = packet.sequenceNumber;
            outcome.deviceTimestamp = packet.timestamp;
            outcome.isNewBoot = result.isNewBoot;
            outcome.sequenceGap = result.sequenceGap;
            PacketsLost += result.sequenceGap;

            if (result.isNewBoot)
            {
                Reboots++;
                offsetWindow.Clear();
                inOrderStateLines = 0;
            }

            if (!parser.TryParseState(packet, out PhysicalState state))
                return Rejected(outcome, PacketRejectCode.INVALID_FIELD, "unparseable state");

            outcome.transitAgeMs = TransitAge(arrivalMs, packet.timestamp);
            if (outcome.transitAgeMs > StaleTransitAgeMs)
            {
                if (packet.hasEvent)
                {
                    outcome.eventId = packet.eventId;
                    outcome.eventResult = EventAcceptance.EVENT_TOO_OLD;
                }
                return Rejected(outcome, PacketRejectCode.STALE_PACKET,
                    "transit age " + outcome.transitAgeMs + " ms exceeds " + StaleTransitAgeMs + " ms");
            }

            int previousInOrder = inOrderStateLines;
            stateManager.UpdateState(state);
            lastStateArrival = arrivalMs;
            inOrderStateLines++;
            PacketsAccepted++;
            outcome.accepted = true;
            outcome.code = PacketRejectCode.None;
            outcome.state = state;

            if (parser.TryParseEvent(packet, out PhysicalEvent physicalEvent))
            {
                outcome.eventId = physicalEvent.eventId;
                outcome.eventResult = ClassifyEvent(physicalEvent, packet, previousInOrder, arrivalMs);
                if (outcome.eventResult == EventAcceptance.ACCEPTED)
                    outcome.acceptedEvent = physicalEvent;
            }

            return outcome;
        }

        private EventAcceptance ClassifyEvent(PhysicalEvent physicalEvent, SemanticPacket packet, int previousInOrder, long arrivalMs)
        {
            PhysicalEventManager.Verdict verdict = eventManager.Check(physicalEvent);
            if (verdict == PhysicalEventManager.Verdict.Duplicate)
                return EventAcceptance.DUPLICATE;
            if (verdict == PhysicalEventManager.Verdict.Old)
                return EventAcceptance.OLD_EVENT;
            if (packet.eventTimestamp > 0 && packet.timestamp - packet.eventTimestamp > MaxEventAgeMs)
                return EventAcceptance.EVENT_TOO_OLD;
            if (previousInOrder < ResyncStateLines)
                return EventAcceptance.RESYNCING;
            if (IsBlackoutActive(arrivalMs))
                return EventAcceptance.BLACKOUT;
            if (EventsMasked)
                return EventAcceptance.MASKED;

            eventManager.Accept(physicalEvent);
            return EventAcceptance.ACCEPTED;
        }

        // Estimated transit age against the minimum-offset envelope of this
        // boot. The two clocks never need a shared epoch.
        private long TransitAge(long arrivalMs, long deviceTimestamp)
        {
            long offset = arrivalMs - deviceTimestamp;
            offsetWindow.Enqueue(offset);
            while (offsetWindow.Count > OffsetWindowSize)
                offsetWindow.Dequeue();

            long minimum = long.MaxValue;
            foreach (long value in offsetWindow)
                if (value < minimum)
                    minimum = value;

            return offset - minimum;
        }

        private PacketOutcome Rejected(PacketOutcome outcome, PacketRejectCode code, string reason)
        {
            PacketsRejected++;
            outcome.accepted = false;
            outcome.code = code;
            outcome.reason = reason;
            return outcome;
        }
    }

    // One datagram's result on the live path. Value type, so producing one
    // per packet does not allocate.
    public struct PacketOutcome
    {
        public bool accepted;
        public PacketRejectCode code;
        public string reason;
        public int bytes;
        public long arrivalMs;
        public long boot;
        public long sequence;
        public long deviceTimestamp;
        public long transitAgeMs;
        public long sequenceGap;
        public bool isNewBoot;
        public PhysicalState state;

        public long eventId;
        public EventAcceptance eventResult;
        public PhysicalEvent acceptedEvent;
    }
}
