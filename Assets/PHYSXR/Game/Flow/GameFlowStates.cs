namespace PHYSXR.Game.Flow
{
    // Run-level state. Only Playing accepts ordinary gameplay input
    // (movement, look, interaction). Death/checkpoint states are not part
    // of M2; they are added with the encounter milestone (M4).
    public enum RunState
    {
        Playing = 0,
        Paused,
        Succeeded
    }

    // Objective progression, strictly ordered. Each stage can only be left
    // forwards, by one specific call on GameFlow:
    //
    //   FindFuse       --TryCollectFuse-->       InstallFuse
    //   InstallFuse    --TryInstallFuse-->       RestoringPower
    //   RestoringPower --ConfirmPowerRestored--> ReachExit
    //   ReachExit      --TryUseExit-->           Completed
    //
    // RestoringPower exists so the power system (not the fuse box) confirms
    // restoration; the exit unlocks only on that confirmation.
    public enum ObjectiveStage
    {
        FindFuse = 0,
        InstallFuse,
        RestoringPower,
        ReachExit,
        Completed
    }

    // Why a gameplay request was refused. None means it was accepted.
    public enum InteractionRefusal
    {
        None = 0,
        GameplayInactive,
        FuseAlreadyCollected,
        FuseRequired,
        FuseAlreadyInstalled,
        NotAwaitingPower,
        ExitLocked,
        AlreadyEscaped
    }
}
