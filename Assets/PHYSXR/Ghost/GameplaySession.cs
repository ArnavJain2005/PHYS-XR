using System;
using PHYSXR.Core.Data;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Ghost
{
    public enum TerminalReason
    {
        None = 0,
        PlayerDied,
        EndingPlaying,
        RunCompleted
    }

    // Lives in the Ghost folder/namespace on purpose: GhostRuntime exposes it
    // and it drives GhostRuntimePipeline, so a separate Session assembly would
    // be an assembly cycle once asmdefs are added.
    //
    // Plain C#, Unity-independent owner of the run's terminal state. This is
    // the single call-site game flow uses to end and restart a run:
    //
    //   EnterTerminal(reason)   player died / ending started / run complete
    //   RestartFromCheckpoint() fresh run from the last checkpoint
    //
    // It composes only what already exists - WorldStateManager (which owns
    // Player/Virtual/Ghost state) and GhostRuntimePipeline (which owns the
    // execution gate). It contains no decision logic and no GhostAction.
    //
    // EnterTerminal: records the reason, writes gameplayTerminal=true (and
    // clears mandatoryWarningBeat) into the VirtualState so the decision layer
    // and telemetry see the same truth, and engages the pipeline's terminal
    // gate, which stops all ghost decision/execution/behaviour. Idempotent;
    // the first reason wins.
    //
    // RestartFromCheckpoint: replaces Player/Virtual/Ghost state with fresh
    // defaults (no stale terminal flag, warning beat, INTERRUPTED phase or
    // threat zone), clears the gate, resets the behaviour controller and the
    // decision provider's hysteresis. Physical state is deliberately kept: it
    // describes the live room, not the run.
    public class GameplaySession
    {
        private readonly WorldStateManager worldStateManager;
        private readonly GhostRuntimePipeline pipeline;

        public TerminalReason TerminalReason { get; private set; }

        public bool IsTerminal => TerminalReason != TerminalReason.None;

        public int CheckpointRestarts { get; private set; }

        public GameplaySession(WorldStateManager worldStateManager, GhostRuntimePipeline pipeline)
        {
            this.worldStateManager = worldStateManager ?? throw new ArgumentNullException(nameof(worldStateManager));
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public void EnterTerminal(TerminalReason reason)
        {
            if (reason == TerminalReason.None || !Enum.IsDefined(typeof(TerminalReason), reason))
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "A terminal state needs a defined, non-None reason");

            if (IsTerminal)
                return;

            TerminalReason = reason;

            VirtualState virtualState = worldStateManager.GetCurrentWorldState().virtualWorld;
            virtualState.gameplayTerminal = true;
            virtualState.mandatoryWarningBeat = false;
            worldStateManager.UpdateVirtualState(virtualState);

            pipeline.EngageTerminalGate();
        }

        public void RestartFromCheckpoint()
        {
            worldStateManager.UpdatePlayerState(new PlayerState());
            worldStateManager.UpdateVirtualState(new VirtualState());
            worldStateManager.UpdateGhostState(new GhostState());
            worldStateManager.ClearDisturbancePulse();

            pipeline.RestartFromCheckpoint();

            TerminalReason = TerminalReason.None;
            CheckpointRestarts++;
        }
    }
}
