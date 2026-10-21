using System;
using System.Collections.Generic;
using PHYSXR.Game.Flow;

namespace PHYSXR.Testing
{
    // M2 objective / run-state tests (FLOW-01..FLOW-16) for the plain C#
    // GameFlow and GameSession. They cover the transition policy only; the
    // Unity side (targeting, lighting, scene reload) is verified by the
    // in-player route self-test (M2RouteSelfTest) and by manual checks.
    public static class GameFlowTests
    {
        public static int RunAll()
        {
            int passed = 0;
            int failed = 0;

            void Run(string name, Action test)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS: " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL: " + name + " - " + ex.Message);
                }
            }

            Run("FLOW-01_InitialState_PowerOffExitLockedFuseAvailable", FLOW01);
            Run("FLOW-02_Fuse_CollectedExactlyOnce", FLOW02);
            Run("FLOW-03_FuseBox_RefusesBeforeFuse_NoStateChange", FLOW03);
            Run("FLOW-04_FuseBox_InstallsCarriedFuse_AwaitsPowerConfirmation", FLOW04);
            Run("FLOW-05_PowerConfirmation_OnlyAcceptedAfterInstall", FLOW05);
            Run("FLOW-06_Exit_RefusedInEveryStageBeforePower", FLOW06);
            Run("FLOW-07_Exit_SucceedsAfterPower_HistoryInOrder", FLOW07);
            Run("FLOW-08_Succeeded_RejectsAllGameplayAndPause", FLOW08);
            Run("FLOW-09_Pause_BlocksInteractions_ResumeRestores", FLOW09);
            Run("FLOW-10_RepeatedInteractions_AfterPower_AreIdempotent", FLOW10);
            Run("FLOW-11_Events_FireOncePerTransition", FLOW11);
            Run("FLOW-12_PowerConfirmation_AcceptedWhilePaused", FLOW12);
            Run("FLOW-13_FreshFlowAfterCompletedRun_IsCleanInitialState", FLOW13);
            Run("FLOW-14_Session_NewRunIncrementsRun_AttemptBackToOne", FLOW14);
            Run("FLOW-15_RandomRequestSequences_PreserveInvariants", FLOW15);
            Run("FLOW-16_RefusalsAlwaysCarryPlayerMessage", FLOW16);

            Console.WriteLine("GameFlowTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static void AssertRefused(InteractionResult result, InteractionRefusal expected, string context)
        {
            AssertTrue(!result.Accepted, context + ": expected refusal but was accepted");
            AssertEq(expected, result.Refusal, context + " refusal");
        }

        private static GameFlow FlowAt(ObjectiveStage stage)
        {
            var flow = new GameFlow();
            if (stage >= ObjectiveStage.InstallFuse) flow.TryCollectFuse();
            if (stage >= ObjectiveStage.RestoringPower) flow.TryInstallFuse();
            if (stage >= ObjectiveStage.ReachExit) flow.ConfirmPowerRestored();
            if (stage >= ObjectiveStage.Completed) flow.TryUseExit();
            AssertEq(stage, flow.Stage, "setup stage");
            return flow;
        }

        private static void FLOW01()
        {
            var flow = new GameFlow();
            AssertEq(RunState.Playing, flow.RunState, "run state");
            AssertEq(ObjectiveStage.FindFuse, flow.Stage, "stage");
            AssertTrue(flow.AcceptsGameplayInput, "gameplay input accepted");
            AssertTrue(!flow.HasFuse, "no fuse carried");
            AssertTrue(!flow.FuseInstalled, "fuse not installed");
            AssertTrue(!flow.PowerRestored, "power off");
            AssertTrue(!flow.ExitUnlocked, "exit locked");
            AssertTrue(!flow.IsSucceeded, "not succeeded");
            AssertEq(1, flow.StageHistory.Count, "history length");
        }

        private static void FLOW02()
        {
            var flow = new GameFlow();
            InteractionResult first = flow.TryCollectFuse();
            AssertTrue(first.Accepted, "first pickup accepted");
            AssertTrue(flow.HasFuse, "fuse carried");

            for (int i = 0; i < 5; i++)
                AssertRefused(flow.TryCollectFuse(), InteractionRefusal.FuseAlreadyCollected, "repeat pickup " + i);

            AssertEq(ObjectiveStage.InstallFuse, flow.Stage, "stage after repeats");
            AssertEq(2, flow.StageHistory.Count, "history length after repeats");
        }

        private static void FLOW03()
        {
            var flow = new GameFlow();
            for (int i = 0; i < 3; i++)
                AssertRefused(flow.TryInstallFuse(), InteractionRefusal.FuseRequired, "install without fuse " + i);

            AssertEq(ObjectiveStage.FindFuse, flow.Stage, "stage unchanged");
            AssertTrue(!flow.PowerRestored, "power still off");
            AssertTrue(flow.TryCollectFuse().Accepted, "fuse still collectable afterwards");
        }

        private static void FLOW04()
        {
            var flow = FlowAt(ObjectiveStage.InstallFuse);
            InteractionResult result = flow.TryInstallFuse();
            AssertTrue(result.Accepted, "install accepted");
            AssertEq(ObjectiveStage.RestoringPower, flow.Stage, "stage");
            AssertTrue(flow.FuseInstalled, "fuse installed");
            AssertTrue(!flow.HasFuse, "fuse no longer carried");
            AssertTrue(!flow.PowerRestored, "power not yet confirmed");
            AssertTrue(!flow.ExitUnlocked, "exit stays locked until power confirms");
            AssertRefused(flow.TryUseExit(), InteractionRefusal.ExitLocked, "exit before confirmation");
        }

        private static void FLOW05()
        {
            foreach (ObjectiveStage stage in new[] { ObjectiveStage.FindFuse, ObjectiveStage.InstallFuse })
            {
                var flow = FlowAt(stage);
                AssertRefused(flow.ConfirmPowerRestored(), InteractionRefusal.NotAwaitingPower, "confirm at " + stage);
                AssertEq(stage, flow.Stage, "stage unchanged at " + stage);
            }

            var ready = FlowAt(ObjectiveStage.RestoringPower);
            AssertTrue(ready.ConfirmPowerRestored().Accepted, "confirm after install");
            AssertTrue(ready.PowerRestored && ready.ExitUnlocked, "power on and exit unlocked");
            AssertRefused(ready.ConfirmPowerRestored(), InteractionRefusal.NotAwaitingPower, "second confirm");
        }

        private static void FLOW06()
        {
            foreach (ObjectiveStage stage in new[] { ObjectiveStage.FindFuse, ObjectiveStage.InstallFuse, ObjectiveStage.RestoringPower })
            {
                var flow = FlowAt(stage);
                InteractionResult result = flow.TryUseExit();
                AssertRefused(result, InteractionRefusal.ExitLocked, "exit at " + stage);
                AssertEq(GameFlow.MsgExitLocked, result.Message, "locked message at " + stage);
                AssertEq(stage, flow.Stage, "stage unchanged at " + stage);
                AssertEq(RunState.Playing, flow.RunState, "still playing at " + stage);
            }
        }

        private static void FLOW07()
        {
            var flow = FlowAt(ObjectiveStage.ReachExit);
            InteractionResult result = flow.TryUseExit();
            AssertTrue(result.Accepted, "exit accepted");
            AssertEq(RunState.Succeeded, flow.RunState, "run state");
            AssertEq(ObjectiveStage.Completed, flow.Stage, "stage");

            var expected = new List<ObjectiveStage>
            {
                ObjectiveStage.FindFuse, ObjectiveStage.InstallFuse, ObjectiveStage.RestoringPower,
                ObjectiveStage.ReachExit, ObjectiveStage.Completed
            };
            AssertEq(expected.Count, flow.StageHistory.Count, "history length");
            for (int i = 0; i < expected.Count; i++)
                AssertEq(expected[i], flow.StageHistory[i], "history[" + i + "]");
        }

        private static void FLOW08()
        {
            var flow = FlowAt(ObjectiveStage.Completed);
            int events = 0;
            flow.StageChanged += _ => events++;
            flow.RunStateChanged += _ => events++;

            AssertTrue(!flow.AcceptsGameplayInput, "gameplay input disabled");
            AssertRefused(flow.TryCollectFuse(), InteractionRefusal.GameplayInactive, "pickup after success");
            AssertRefused(flow.TryInstallFuse(), InteractionRefusal.GameplayInactive, "install after success");
            AssertRefused(flow.TryUseExit(), InteractionRefusal.AlreadyEscaped, "exit after success");
            AssertRefused(flow.ConfirmPowerRestored(), InteractionRefusal.NotAwaitingPower, "confirm after success");
            AssertTrue(!flow.Pause(), "pause refused after success");
            AssertTrue(!flow.Resume(), "resume refused after success");
            AssertTrue(!flow.TogglePause(), "toggle refused after success");

            AssertEq(RunState.Succeeded, flow.RunState, "still succeeded");
            AssertEq(0, events, "no events after success");
        }

        private static void FLOW09()
        {
            var flow = new GameFlow();
            AssertTrue(flow.Pause(), "pause accepted");
            AssertTrue(!flow.Pause(), "second pause is a no-op");
            AssertEq(RunState.Paused, flow.RunState, "paused");
            AssertTrue(!flow.AcceptsGameplayInput, "input blocked while paused");

            AssertRefused(flow.TryCollectFuse(), InteractionRefusal.GameplayInactive, "pickup while paused");
            AssertRefused(flow.TryInstallFuse(), InteractionRefusal.GameplayInactive, "install while paused");
            AssertRefused(flow.TryUseExit(), InteractionRefusal.GameplayInactive, "exit while paused");
            AssertEq(ObjectiveStage.FindFuse, flow.Stage, "stage unchanged while paused");

            AssertTrue(flow.Resume(), "resume accepted");
            AssertTrue(!flow.Resume(), "second resume is a no-op");
            AssertTrue(flow.AcceptsGameplayInput, "input restored");
            AssertTrue(flow.TryCollectFuse().Accepted, "pickup after resume");

            AssertTrue(flow.TogglePause(), "toggle pauses");
            AssertEq(RunState.Paused, flow.RunState, "toggled to paused");
            AssertTrue(flow.TogglePause(), "toggle resumes");
            AssertEq(RunState.Playing, flow.RunState, "toggled to playing");
        }

        private static void FLOW10()
        {
            var flow = FlowAt(ObjectiveStage.ReachExit);
            for (int i = 0; i < 4; i++)
            {
                AssertRefused(flow.TryCollectFuse(), InteractionRefusal.FuseAlreadyCollected, "pickup " + i);
                AssertRefused(flow.TryInstallFuse(), InteractionRefusal.FuseAlreadyInstalled, "install " + i);
                AssertRefused(flow.ConfirmPowerRestored(), InteractionRefusal.NotAwaitingPower, "confirm " + i);
            }
            AssertEq(ObjectiveStage.ReachExit, flow.Stage, "stage unchanged");
            AssertEq(4, flow.StageHistory.Count, "history unchanged");
            AssertTrue(flow.ExitUnlocked, "exit still unlocked");

            var restoring = FlowAt(ObjectiveStage.RestoringPower);
            AssertRefused(restoring.TryInstallFuse(), InteractionRefusal.FuseAlreadyInstalled, "install while restoring");
            AssertEq(ObjectiveStage.RestoringPower, restoring.Stage, "restoring unchanged");
        }

        private static void FLOW11()
        {
            var flow = new GameFlow();
            var stages = new List<ObjectiveStage>();
            var runStates = new List<RunState>();
            flow.StageChanged += s => stages.Add(s);
            flow.RunStateChanged += r => runStates.Add(r);

            flow.TryCollectFuse();
            flow.TryCollectFuse();
            flow.TryInstallFuse();
            flow.TryInstallFuse();
            flow.ConfirmPowerRestored();
            flow.ConfirmPowerRestored();
            flow.Pause();
            flow.Pause();
            flow.Resume();
            flow.TryUseExit();
            flow.TryUseExit();

            AssertEq(4, stages.Count, "stage events");
            AssertEq(ObjectiveStage.InstallFuse, stages[0], "stage event 0");
            AssertEq(ObjectiveStage.Completed, stages[3], "stage event 3");
            AssertEq(3, runStates.Count, "run-state events");
            AssertEq(RunState.Paused, runStates[0], "run event 0");
            AssertEq(RunState.Playing, runStates[1], "run event 1");
            AssertEq(RunState.Succeeded, runStates[2], "run event 2");
        }

        private static void FLOW12()
        {
            var flow = FlowAt(ObjectiveStage.RestoringPower);
            flow.Pause();
            AssertTrue(flow.ConfirmPowerRestored().Accepted, "power system may confirm while paused");
            AssertEq(RunState.Paused, flow.RunState, "still paused");
            AssertRefused(flow.TryUseExit(), InteractionRefusal.GameplayInactive, "exit blocked while paused");
            flow.Resume();
            AssertTrue(flow.TryUseExit().Accepted, "exit after resume");
        }

        private static void FLOW13()
        {
            var finished = FlowAt(ObjectiveStage.Completed);
            AssertTrue(finished.IsSucceeded, "first run succeeded");

            // Restart policy: the game scene is reloaded and a new GameFlow is
            // constructed. Nothing from the finished run may leak into it.
            var fresh = new GameFlow();
            AssertEq(RunState.Playing, fresh.RunState, "fresh run state");
            AssertEq(ObjectiveStage.FindFuse, fresh.Stage, "fresh stage");
            AssertTrue(!fresh.HasFuse && !fresh.FuseInstalled && !fresh.PowerRestored && !fresh.ExitUnlocked,
                "fresh objective flags");
            AssertEq(1, fresh.StageHistory.Count, "fresh history");
            AssertTrue(fresh.TryCollectFuse().Accepted, "fresh fuse collectable");
        }

        private static void FLOW14()
        {
            var session = new GameSession();
            AssertTrue(!session.HasActiveRun, "no run before begin");
            AssertEq(0, session.RunNumber, "run number before begin");

            session.BeginNewRun();
            AssertEq(1, session.RunNumber, "first run");
            AssertEq(1, session.Attempt, "first attempt");

            session.BeginNewRun();
            session.BeginNewRun();
            AssertEq(3, session.RunNumber, "third run");
            AssertEq(1, session.Attempt, "full restart resets attempt");
        }

        private static void FLOW15()
        {
            var random = new Random(20261020);
            for (int trial = 0; trial < 500; trial++)
            {
                var flow = new GameFlow();
                ObjectiveStage previous = flow.Stage;
                for (int step = 0; step < 40; step++)
                {
                    switch (random.Next(7))
                    {
                        case 0: flow.TryCollectFuse(); break;
                        case 1: flow.TryInstallFuse(); break;
                        case 2: flow.ConfirmPowerRestored(); break;
                        case 3: flow.TryUseExit(); break;
                        case 4: flow.Pause(); break;
                        case 5: flow.Resume(); break;
                        default: flow.TogglePause(); break;
                    }

                    AssertTrue(flow.Stage >= previous, "stage never moves backwards (trial " + trial + ")");
                    AssertTrue(flow.Stage - previous <= 1, "stage advances at most one step per call (trial " + trial + ")");
                    AssertEq(flow.Stage == ObjectiveStage.Completed, flow.RunState == RunState.Succeeded,
                        "Completed iff Succeeded (trial " + trial + ")");
                    AssertEq((int)flow.Stage + 1, flow.StageHistory.Count, "history matches stage (trial " + trial + ")");
                    for (int i = 0; i < flow.StageHistory.Count; i++)
                        AssertEq((ObjectiveStage)i, flow.StageHistory[i], "history is consecutive (trial " + trial + ")");
                    AssertTrue(!(flow.ExitUnlocked && !flow.PowerRestored), "exit never unlocked without power");
                    previous = flow.Stage;
                }
            }
        }

        private static void FLOW16()
        {
            AssertTrue(new GameFlow().TryInstallFuse().Message.Length > 0, "fuse-box refusal has a message");
            AssertTrue(new GameFlow().TryUseExit().Message.Length > 0, "exit refusal has a message");
            AssertTrue(FlowAt(ObjectiveStage.InstallFuse).TryCollectFuse().Message.Length > 0, "repeat pickup has a message");
            AssertTrue(FlowAt(ObjectiveStage.ReachExit).TryInstallFuse().Message.Length > 0, "repeat install has a message");
            AssertTrue(FlowAt(ObjectiveStage.Completed).TryUseExit().Message.Length > 0, "repeat exit has a message");
        }
    }
}
