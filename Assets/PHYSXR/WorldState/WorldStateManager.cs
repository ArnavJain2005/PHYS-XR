using PHYSXR.Core.Data;
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
    public class WorldStateManager
    {
        private readonly PhysicalStateManager physicalStateManager;

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
                physical = ClonePhysicalState(physical),
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
                timestamp = source.timestamp
            };
        }

        private static PlayerState ClonePlayerState(PlayerState source)
        {
            return new PlayerState
            {
                distanceToGhost = source.distanceToGhost,
                movement = source.movement,
                inThreatZone = source.inThreatZone
            };
        }

        private static VirtualState CloneVirtualState(VirtualState source)
        {
            return new VirtualState
            {
                threatLevel = source.threatLevel,
                eventActive = source.eventActive,
                mandatoryWarningBeat = source.mandatoryWarningBeat,
                gameplayTerminal = source.gameplayTerminal
            };
        }

        private static GhostState CloneGhostState(GhostState source)
        {
            return new GhostState
            {
                currentState = source.currentState,
                phase = source.phase
            };
        }
    }
}
