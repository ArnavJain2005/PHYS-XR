using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.PhysicalWorld;

namespace PHYSXR.WorldStateSystem
{
    // Combines the existing state domains (PhysicalState, PlayerState,
    // VirtualState, GhostState) into a single coherent WorldState snapshot
    // for the future IDecisionProvider. Contains no decision logic and
    // never selects a GhostAction - it only ever assembles state that
    // already exists elsewhere.
    //
    // There is no PlayerStateManager / VirtualStateManager / GhostStateManager
    // in this project. Rather than inventing three manager classes purely to
    // fill out the architecture, this class owns the most recently supplied
    // PlayerState/VirtualState/GhostState directly - the same minimal
    // ownership pattern PhysicalStateManager already uses for PhysicalState.
    //
    // Namespace note: this is deliberately PHYSXR.WorldStateSystem, not
    // PHYSXR.WorldState (which the containing folder name would suggest by
    // the PHYSXR.PhysicalWorld/PHYSXR.Communication convention). A namespace
    // named PHYSXR.WorldState would share its simple name with the
    // PHYSXR.Core.Data.WorldState class, which breaks unqualified "WorldState"
    // usage project-wide (e.g. in IDecisionProvider.cs) with a "namespace
    // used like a type" compiler error - not just within this file. This
    // was discovered as a genuine compilation issue while wiring
    // RuleBasedDecisionProvider and fixed by renaming this namespace.
    //
    // PHYSICAL GATING (fix P2, spec sections D/E): the original manager kept
    // the last accepted PhysicalState in every snapshot forever, so a sensor
    // that had gone silent minutes ago could still trigger PROTECT. When the
    // manager is built with a clock (the gated constructor), the physical part
    // of each snapshot is:
    //   - neutral (NONE / LOW / no disturbance) unless SourceStatus is FRESH,
    //     and always neutral while PhysicalInputMasked (virtual-only trials);
    //   - otherwise the live presence/confidence, with disturbance true only
    //     during the DisturbancePulseMs pulse started by a newly accepted
    //     physical event (NotifyAcceptedEvent). The raw per-line disturbance
    //     flag is not used, so a repeated packet cannot restart the pulse.
    // The legacy constructor keeps the original pass-through behaviour, which
    // the WORLD suites document.
    public class WorldStateManager
    {
        public const long DefaultDisturbancePulseMs = 3000L;

        private readonly PhysicalStateManager physicalStateManager;
        private readonly Func<long> clock;
        private long pulseUntil = long.MinValue;
        private long pulseEventId;

        // Explicit default objects (not null) so a coherent snapshot can
        // always be produced, even before any Player/Virtual/Ghost state
        // has been supplied. These are the classes' own default field
        // values (0 / false / first enum value) - not gameplay values
        // invented by this manager - and are expected to be replaced via
        // UpdatePlayerState/UpdateVirtualState/UpdateGhostState before real
        // decision-making happens.
        private PlayerState currentPlayerState = new PlayerState();
        private VirtualState currentVirtualState = new VirtualState();
        private GhostState currentGhostState = new GhostState();

        public WorldStateManager(PhysicalStateManager physicalStateManager)
        {
            this.physicalStateManager = physicalStateManager;
        }

        // Gated constructor (P2). clock supplies "now" in milliseconds for the
        // disturbance pulse; it must be monotonic. The source starts
        // UNAVAILABLE until UpdateSourceStatus says otherwise.
        public WorldStateManager(PhysicalStateManager physicalStateManager, Func<long> clock,
            long disturbancePulseMs = DefaultDisturbancePulseMs)
        {
            this.physicalStateManager = physicalStateManager;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            DisturbancePulseMs = disturbancePulseMs;
        }

        public bool IsGated => clock != null;

        public long DisturbancePulseMs { get; } = DefaultDisturbancePulseMs;

        // Freshness of the physical source, pushed each frame by the sensing
        // service. Only FRESH lets physical state through (gated mode).
        public PhysicalSourceStatus SourceStatus { get; private set; } = PhysicalSourceStatus.UNAVAILABLE;

        // True for the VIRTUAL_ONLY condition: packets are still received and
        // logged upstream, but the decision rules always see neutral physical state.
        public bool PhysicalInputMasked { get; set; }

        public bool DisturbancePulseActive => clock != null && clock() < pulseUntil;

        public void UpdateSourceStatus(PhysicalSourceStatus status)
        {
            SourceStatus = status;
        }

        // A newly accepted (already deduplicated) physical event starts the
        // disturbance pulse. Duplicates never reach here: PhysicalEventManager
        // drops them first. Ignored in legacy mode.
        public void NotifyAcceptedEvent(PhysicalEvent physicalEvent)
        {
            if (clock == null || physicalEvent == null)
                return;

            pulseUntil = clock() + DisturbancePulseMs;
            pulseEventId = physicalEvent.eventId;
        }

        // Checkpoint restart: an old pulse must not carry into the new attempt.
        public void ClearDisturbancePulse()
        {
            pulseUntil = long.MinValue;
            pulseEventId = 0L;
        }

        public void UpdatePlayerState(PlayerState newState)
        {
            if (newState == null)
                return;

            currentPlayerState = newState;
        }

        public void UpdateVirtualState(VirtualState newState)
        {
            if (newState == null)
                return;

            currentVirtualState = newState;
        }

        public void UpdateGhostState(GhostState newState)
        {
            if (newState == null)
                return;

            currentGhostState = newState;
        }

        // Returns a coherent, independent snapshot of all four state
        // domains as they stand right now.
        //
        // Snapshot/copy semantics: every sub-state is copied into a new
        // instance before being placed on the returned WorldState. Because
        // PhysicalState/PlayerState/VirtualState/GhostState hold only
        // value-type fields (enums, bool, float, long), a shallow
        // field-by-field copy is already a full copy - no nested mutable
        // references exist to worry about. This means:
        //   - mutating the returned WorldState (or any of its sub-objects)
        //     can never corrupt this manager's internal state, and
        //   - this manager's internal state changing later can never be
        //     observed through a snapshot a caller is already holding.
        // No cloning library/serialization was introduced; this is a
        // handful of explicit field copies.
        //
        // If PhysicalStateManager has not yet received any state (e.g.
        // before the communication boundary or simulation source has run),
        // an explicitly-initialized default PhysicalState is substituted
        // instead of null, so callers never have to null-check
        // worldState.physical. This does not invent a semantic value -
        // PresenceLevel/ConfidenceLevel default to their first enum member
        // and disturbance/timestamp default to false/0, the same "nothing
        // known yet" convention used for Player/Virtual/Ghost above.
        public WorldState GetCurrentWorldState()
        {
            PhysicalState physical = physicalStateManager.GetCurrentState() ?? new PhysicalState();

            return new WorldState
            {
                physical = clock != null ? GatePhysicalState(physical) : ClonePhysicalState(physical),
                player = ClonePlayerState(currentPlayerState),
                virtualWorld = CloneVirtualState(currentVirtualState),
                ghost = CloneGhostState(currentGhostState)
            };
        }

        private static PhysicalState ClonePhysicalState(PhysicalState source)
        {
            return new PhysicalState
            {
                presence = source.presence,
                confidence = source.confidence,
                disturbance = source.disturbance,
                timestamp = source.timestamp,
                causeEventId = source.causeEventId
            };
        }

        private PhysicalState GatePhysicalState(PhysicalState source)
        {
            bool live = SourceStatus == PhysicalSourceStatus.FRESH && !PhysicalInputMasked;
            bool pulse = live && clock() < pulseUntil;

            return new PhysicalState
            {
                presence = live ? source.presence : PresenceLevel.NONE,
                confidence = live ? source.confidence : ConfidenceLevel.LOW,
                disturbance = pulse,
                timestamp = source.timestamp,
                causeEventId = pulse ? pulseEventId : 0L
            };
        }

        private static PlayerState ClonePlayerState(PlayerState source)
        {
            return new PlayerState
            {
                distanceToGhost = source.distanceToGhost,
                movement = source.movement,
                inThreatZone = source.inThreatZone,
                inWarnBand = source.inWarnBand
            };
        }

        private static VirtualState CloneVirtualState(VirtualState source)
        {
            return new VirtualState
            {
                threatLevel = source.threatLevel,
                eventActive = source.eventActive,
                mandatoryWarningBeat = source.mandatoryWarningBeat,
                gameplayTerminal = source.gameplayTerminal,
                encounterCommitted = source.encounterCommitted,
                attackGraceActive = source.attackGraceActive
            };
        }

        private static GhostState CloneGhostState(GhostState source)
        {
            return new GhostState
            {
                currentState = source.currentState,
                phase = source.phase,
                phaseElapsedMs = source.phaseElapsedMs,
                attackCooldownMs = source.attackCooldownMs
            };
        }
    }
}
