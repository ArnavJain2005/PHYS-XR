using System;
using PHYSXR.Game.Flow;

namespace PHYSXR.Testing
{
    // FLOW-17..26: the run-level additions for the encounter milestone -
    // death, checkpoint resume, encounter commit and the session's restart
    // bookkeeping. FLOW-01..16 (GameFlowTests) stay unchanged.
    public static class GameFlowEncounterTests
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

            Run("FLOW-17_Death_OnlyFromPlaying_Once", FLOW17);
            Run("FLOW-18_Dead_RefusesInteractionsAndPause", FLOW18);
            Run("FLOW-19_CheckpointFlow_StartsAtReachExitWithHistory", FLOW19);
            Run("FLOW-20_CheckpointFlow_RejectsUnstableStages", FLOW20);
            Run("FLOW-21_CommitEncounter_OnlyAfterPowerAndOnce", FLOW21);
            Run("FLOW-22_CommitEncounter_ChangesObjectiveText", FLOW22);
            Run("FLOW-23_Session_CheckpointRestartIncrementsAttempt", FLOW23);
            Run("FLOW-24_Session_NoCheckpoint_FallsBackToNewRun", FLOW24);
            Run("FLOW-25_Session_NewRunClearsCheckpointDeathsAndTime", FLOW25);
            Run("FLOW-26_RandomSequencesWithDeathAndCommit_PreserveInvariants", FLOW26);

            Console.WriteLine("GameFlowEncounterTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static GameFlow Powered()
        {
            var flow = new GameFlow();
            flow.TryCollectFuse();
            flow.TryInstallFuse();
            flow.ConfirmPowerRestored();
            return flow;
        }

        private static void FLOW17()
        {
            var flow = Powered();
            flow.Pause();
            AssertTrue(!flow.ReportDeath().Accepted, "no death while paused");
            flow.Resume();
            InteractionResult death = flow.ReportDeath();
            AssertTrue(death.Accepted && flow.IsDead && death.Message == GameFlow.MsgDied, "death accepted");
            AssertTrue(!flow.ReportDeath().Accepted, "second death refused");

            var won = Powered();
            won.TryUseExit();
            AssertTrue(!won.ReportDeath().Accepted && won.IsSucceeded, "no death after escaping");
        }

        private static void FLOW18()
        {
            var flow = Powered();
            flow.ReportDeath();
            AssertEq(InteractionRefusal.GameplayInactive, flow.TryUseExit().Refusal, "exit");
            AssertEq(InteractionRefusal.GameplayInactive, flow.TryInstallFuse().Refusal, "fuse box");
            AssertTrue(!flow.Pause() && !flow.TogglePause() && !flow.Resume(), "pause/resume refused");
            AssertTrue(!flow.AcceptsGameplayInput && !flow.CommitEncounter(), "no input, no commit");
            AssertEq(RunState.Dead, flow.RunState, "still dead");
        }

        private static void FLOW19()
        {
            var flow = new GameFlow(ObjectiveStage.ReachExit);
            AssertEq(ObjectiveStage.ReachExit, flow.Stage, "stage");
            AssertTrue(flow.PowerRestored && flow.ExitUnlocked && flow.FuseInstalled && !flow.HasFuse, "objective flags");
            AssertEq(4, flow.StageHistory.Count, "history FindFuse..ReachExit");
            for (int i = 0; i < 4; i++)
                AssertEq((ObjectiveStage)i, flow.StageHistory[i], "history " + i);
            AssertTrue(!flow.EncounterCommitted, "restored uncommitted");
            AssertTrue(!flow.TryCollectFuse().Accepted && !flow.TryInstallFuse().Accepted, "earlier objectives cannot repeat");
            AssertTrue(flow.TryUseExit().Accepted, "exit works");
        }

        private static void FLOW20()
        {
            foreach (ObjectiveStage bad in new[] { ObjectiveStage.RestoringPower, ObjectiveStage.Completed, (ObjectiveStage)42 })
            {
                bool threw = false;
                try { new GameFlow(bad); }
                catch (ArgumentOutOfRangeException) { threw = true; }
                AssertTrue(threw, "cannot resume at " + bad);
            }
            AssertEq(ObjectiveStage.InstallFuse, new GameFlow(ObjectiveStage.InstallFuse).Stage, "InstallFuse is resumable");
        }

        private static void FLOW21()
        {
            var flow = new GameFlow();
            AssertTrue(!flow.CommitEncounter(), "not before power");
            flow = Powered();
            int events = 0;
            flow.EncounterCommittedChanged += () => events++;
            flow.Pause();
            AssertTrue(!flow.CommitEncounter(), "not while paused");
            flow.Resume();
            AssertTrue(flow.CommitEncounter(), "after power");
            AssertTrue(!flow.CommitEncounter(), "only once");
            AssertEq(1, events, "one event");
        }

        private static void FLOW22()
        {
            var flow = Powered();
            string before = flow.ObjectiveText;
            flow.CommitEncounter();
            AssertTrue(flow.ObjectiveText != before && flow.ObjectiveText.Length > 0, "objective text changes on commit");
        }

        private static void FLOW23()
        {
            var session = new GameSession();
            session.BeginNewRun();
            session.SaveCheckpoint(new CheckpointData { stage = ObjectiveStage.ReachExit, x = 1, y = 0, z = 2, yaw = 90 });
            session.RecordDeath();
            AssertTrue(session.RequestCheckpointRestart(42.5f), "checkpoint requested");
            AssertEq(GameSession.StartKind.Checkpoint, session.NextStart, "next start");
            CheckpointData cp = session.BeginCheckpointAttempt();
            AssertEq(2, session.Attempt, "attempt 2");
            AssertEq(1, session.RunNumber, "same run");
            AssertEq(ObjectiveStage.ReachExit, cp.stage, "checkpoint stage");
            AssertEq(90f, cp.yaw, "checkpoint pose");
            AssertEq(42.5f, session.SecondsBeforeAttempt, "earlier attempt time kept");
            AssertEq(GameSession.StartKind.NewRun, session.NextStart, "request consumed");
        }

        private static void FLOW24()
        {
            var session = new GameSession();
            session.BeginNewRun();
            AssertTrue(!session.RequestCheckpointRestart(10f), "no checkpoint yet");
            AssertEq(GameSession.StartKind.NewRun, session.NextStart, "falls back to a new run");
        }

        private static void FLOW25()
        {
            var session = new GameSession();
            session.BeginNewRun();
            session.SaveCheckpoint(new CheckpointData { stage = ObjectiveStage.ReachExit });
            session.RecordDeath();
            session.RequestCheckpointRestart(30f);
            session.BeginCheckpointAttempt();
            session.BeginNewRun();
            AssertTrue(!session.HasCheckpoint && session.Deaths == 0 && session.SecondsBeforeAttempt == 0f && session.Attempt == 1,
                "full restart is clean");
            AssertEq(2, session.RunNumber, "run 2");
        }

        private static void FLOW26()
        {
            var random = new Random(4242);
            for (int trial = 0; trial < 400; trial++)
            {
                var flow = random.Next(2) == 0 ? new GameFlow() : new GameFlow(ObjectiveStage.ReachExit);
                for (int step = 0; step < 50; step++)
                {
                    RunState before = flow.RunState;
                    switch (random.Next(9))
                    {
                        case 0: flow.TryCollectFuse(); break;
                        case 1: flow.TryInstallFuse(); break;
                        case 2: flow.ConfirmPowerRestored(); break;
                        case 3: flow.TryUseExit(); break;
                        case 4: flow.Pause(); break;
                        case 5: flow.Resume(); break;
                        case 6: flow.ReportDeath(); break;
                        case 7: flow.CommitEncounter(); break;
                        default: flow.TogglePause(); break;
                    }

                    if (before == RunState.Dead || before == RunState.Succeeded)
                        AssertEq(before, flow.RunState, "terminal states are absorbing (trial " + trial + ")");
                    AssertTrue(!(flow.EncounterCommitted && !flow.PowerRestored), "never committed without power");
                    AssertTrue(!(flow.IsDead && flow.IsSucceeded), "dead xor succeeded");
                    AssertEq((int)flow.Stage + 1, flow.StageHistory.Count, "history matches stage");
                }
            }
        }
    }
}
