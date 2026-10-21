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
    //  - ReportDeath moves a Playing run to Dead, also terminal for this
    //    instance. A checkpoint restart reloads the scene and builds a new
    //    GameFlow at the checkpoint's stage (GameFlow(ObjectiveStage)).
    //  - CommitEncounter marks the final encounter as started: only once,
    //    only after power is restored, only while Playing. A checkpoint
    //    restores it uncommitted, so the player crosses the trigger again.
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
        public const string MsgDied = "The Hollow took you.";

        private readonly List<ObjectiveStage> stageHistory = new List<ObjectiveStage> { ObjectiveStage.FindFuse };

        public RunState RunState { get; private set; } = RunState.Playing;

        public ObjectiveStage Stage { get; private set; } = ObjectiveStage.FindFuse;

        public GameFlow()
        {
        }

        // Checkpoint restore: a fresh flow that starts at resumeStage, with the
        // history of an ordinary run that reached it. Only a stable stage
        // between FindFuse and ReachExit can be resumed.
        public GameFlow(ObjectiveStage resumeStage)
        {
            if (resumeStage == ObjectiveStage.RestoringPower || resumeStage == ObjectiveStage.Completed
                || !Enum.IsDefined(typeof(ObjectiveStage), resumeStage))
                throw new ArgumentOutOfRangeException(nameof(resumeStage), resumeStage, "Cannot resume a run at this stage");

            for (ObjectiveStage s = ObjectiveStage.InstallFuse; s <= resumeStage; s++)
                stageHistory.Add(s);
            Stage = resumeStage;
        }

        public bool IsDead => RunState == RunState.Dead;

        // Final encounter started (power restored and the trigger crossed).
        public bool EncounterCommitted { get; private set; }

        public event Action EncounterCommittedChanged;

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
                    case ObjectiveStage.ReachExit:
                        return EncounterCommitted
                            ? "It is between you and the door. Get out."
                            : "Power is back. Leave through the front door.";
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

        // The ghost's strike connected. Only a Playing run can end this way.
        public InteractionResult ReportDeath()
        {
            if (RunState != RunState.Playing)
                return Inactive();

            SetRunState(RunState.Dead);
            return InteractionResult.Accept(MsgDied);
        }

        // Returns true only if the state changed.
        public bool CommitEncounter()
        {
            if (EncounterCommitted || RunState != RunState.Playing || Stage != ObjectiveStage.ReachExit)
                return false;

            EncounterCommitted = true;
            EncounterCommittedChanged?.Invoke();
            return true;
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
