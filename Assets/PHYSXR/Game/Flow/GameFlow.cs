using System;
using System.Collections.Generic;

namespace PHYSXR.Game.Flow
{
    // Plain C#, Unity-independent owner of one run's objective and run state.
    // GameFlowController (Unity) wraps it; interactables and input adapters
    // never change state directly, they call these methods and display the
    // returned InteractionResult.
    //
    // Transition policy:
    //  - Objective stages only move forwards, one step at a time, through the
    //    single method listed in ObjectiveStage. Any other call is refused
    //    with a reason and changes nothing, so repeated or out-of-order
    //    interactions are idempotent.
    //  - Player-driven calls (collect, install, exit) are refused unless
    //    RunState is Playing. ConfirmPowerRestored comes from the power
    //    system, not the player, so it is accepted while Paused as well.
    //  - Completing the exit moves RunState to Succeeded, which is terminal
    //    for this GameFlow instance. A restart builds a fresh GameFlow (the
    //    game scene is reloaded), so there is no in-place reset path that
    //    could leave stale state behind.
    //
    // Death, checkpoints and the ghost are out of scope here (M3/M4).
    public class GameFlow
    {
        public const string MsgFuseCollected = "Picked up a fuse.";
        public const string MsgFuseAlreadyCollected = "You already have the fuse.";
        public const string MsgFuseRequired = "The fuse box has an empty slot. You need a fuse.";
        public const string MsgFuseInstalled = "Fuse installed.";
        public const string MsgFuseAlreadyInstalled = "The fuse is already installed.";
        public const string MsgPowerRestored = "Power restored.";
        public const string MsgNotAwaitingPower = "No fuse has been installed.";
        public const string MsgExitLocked = "The front door is locked. Its lock has no power.";
        public const string MsgEscaped = "You escaped Blackthorn House.";
        public const string MsgAlreadyEscaped = "You have already escaped.";
        public const string MsgGameplayInactive = "";

        private readonly List<ObjectiveStage> stageHistory = new List<ObjectiveStage> { ObjectiveStage.FindFuse };

        public RunState RunState { get; private set; } = RunState.Playing;

        public ObjectiveStage Stage { get; private set; } = ObjectiveStage.FindFuse;

        // Every stage this run has entered, in order, starting with FindFuse.
        public IReadOnlyList<ObjectiveStage> StageHistory => stageHistory;

        public bool AcceptsGameplayInput => RunState == RunState.Playing;

        public bool HasFuse => Stage == ObjectiveStage.InstallFuse;

        public bool FuseInstalled => Stage >= ObjectiveStage.RestoringPower;

        public bool PowerRestored => Stage >= ObjectiveStage.ReachExit;

        public bool ExitUnlocked => Stage == ObjectiveStage.ReachExit;

        public bool IsSucceeded => RunState == RunState.Succeeded;

        public event Action<ObjectiveStage> StageChanged;

        public event Action<RunState> RunStateChanged;

        public string ObjectiveText
        {
            get
            {
                switch (Stage)
                {
                    case ObjectiveStage.FindFuse: return "Find a fuse to restore the power.";
                    case ObjectiveStage.InstallFuse: return "Install the fuse in the fuse box.";
                    case ObjectiveStage.RestoringPower: return "Restoring power...";
                    case ObjectiveStage.ReachExit: return "Power is back. Leave through the front door.";
                    default: return "You escaped.";
                }
            }
        }

        public InteractionResult TryCollectFuse()
        {
            if (!AcceptsGameplayInput)
                return Inactive();

            if (Stage != ObjectiveStage.FindFuse)
                return InteractionResult.Refuse(InteractionRefusal.FuseAlreadyCollected, MsgFuseAlreadyCollected);

            Advance(ObjectiveStage.InstallFuse);
            return InteractionResult.Accept(MsgFuseCollected);
        }

        public InteractionResult TryInstallFuse()
        {
            if (!AcceptsGameplayInput)
                return Inactive();

            if (Stage == ObjectiveStage.FindFuse)
                return InteractionResult.Refuse(InteractionRefusal.FuseRequired, MsgFuseRequired);

            if (Stage != ObjectiveStage.InstallFuse)
                return InteractionResult.Refuse(InteractionRefusal.FuseAlreadyInstalled, MsgFuseAlreadyInstalled);

            Advance(ObjectiveStage.RestoringPower);
            return InteractionResult.Accept(MsgFuseInstalled);
        }

        public InteractionResult ConfirmPowerRestored()
        {
            if (Stage != ObjectiveStage.RestoringPower)
                return InteractionResult.Refuse(InteractionRefusal.NotAwaitingPower, MsgNotAwaitingPower);

            Advance(ObjectiveStage.ReachExit);
            return InteractionResult.Accept(MsgPowerRestored);
        }

        public InteractionResult TryUseExit()
        {
            if (RunState == RunState.Succeeded)
                return InteractionResult.Refuse(InteractionRefusal.AlreadyEscaped, MsgAlreadyEscaped);

            if (!AcceptsGameplayInput)
                return Inactive();

            if (Stage != ObjectiveStage.ReachExit)
                return InteractionResult.Refuse(InteractionRefusal.ExitLocked, MsgExitLocked);

            Advance(ObjectiveStage.Completed);
            SetRunState(RunState.Succeeded);
            return InteractionResult.Accept(MsgEscaped);
        }

        // Returns true only if the state changed.
        public bool Pause()
        {
            if (RunState != RunState.Playing)
                return false;

            SetRunState(RunState.Paused);
            return true;
        }

        // Returns true only if the state changed.
        public bool Resume()
        {
            if (RunState != RunState.Paused)
                return false;

            SetRunState(RunState.Playing);
            return true;
        }

        public bool TogglePause()
        {
            return RunState == RunState.Paused ? Resume() : Pause();
        }

        private static InteractionResult Inactive()
        {
            return InteractionResult.Refuse(InteractionRefusal.GameplayInactive, MsgGameplayInactive);
        }

        private void Advance(ObjectiveStage next)
        {
            if (next != Stage + 1)
                throw new InvalidOperationException("Objective can only advance one stage: " + Stage + " -> " + next);

            Stage = next;
            stageHistory.Add(next);
            StageChanged?.Invoke(next);
        }

        private void SetRunState(RunState next)
        {
            RunState = next;
            RunStateChanged?.Invoke(next);
        }
    }
}
