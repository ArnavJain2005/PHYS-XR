using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Ghost;
using PHYSXR.SessionSystem;
using PHYSXR.Telemetry;

namespace PHYSXR.Testing
{
    // Terminal-state tests (TERM-01..TERM-15). They prove the gate stops
    // ACTUAL execution: counting wrappers around the real GhostActionExecutor
    // and GhostBehaviourController show that no Execute()/ApplyState() call is
    // made, and the controller's state does not move, once the run is
    // terminal - not merely that the decision layer picked some GhostAction.
    //
    // Not covered here, because it does not exist yet: ATTACK (no GhostAction
    // or executor path) and Unity-side movement (no NavMesh/Animator driver).
    // The gate sits upstream of the executor, so a future ATTACK action is
    // blocked by the same mechanism; M4 must add the matching regression test.
    public static class TerminalGateTests
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

            Run("TERM-01_Terminal_BlocksAllExecutionAndApply_Over600Ticks", TERM01);
            Run("TERM-02_WarningBeat_CannotResumeGhostBehaviour_EvenIfTerminalFlagClears", TERM02);
            Run("TERM-03_ObservedTerminalFlag_LatchesGateWithoutSession", TERM03);
            Run("TERM-04_HaltedController_RefusesDirectApplyState", TERM04);
            Run("TERM-05_Terminal_DecisionNotEvaluated_HysteresisHistoryUntouched", TERM05);
            Run("TERM-06_Terminal_TelemetryShowsGateNotDecision", TERM06);
            Run("TERM-07_EachTerminalReason_Gates_NoneRejected_FirstReasonWins", TERM07);
            Run("TERM-08_EnterTerminal_WritesWorldState_AndClearsBeat", TERM08);
            Run("TERM-09_Restart_ClearsGateFlagsAndControllerAndResumesNormally", TERM09);
            Run("TERM-10_Restart_ResetsHysteresisHistory", TERM10);
            Run("TERM-11_PipelineRestartAlone_WithStaleFlag_RelatchesImmediately", TERM11);
            Run("TERM-12_Restart_ClearsInterruptedPhaseAndBeat", TERM12);
            Run("TERM-13_SecondRun_CanReachTerminalAgain", TERM13);
            Run("TERM-14_Session_RejectsNullDependencies", TERM14);
            Run("TERM-15_PhysicalStateSurvivesRestart", TERM15);

            Console.WriteLine("TerminalGateTests: " + passed + " passed, " + failed + " failed");
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

        // A run that has been going normally for three ticks, ghost Following.
        private static RuntimeTestHarness LiveRun()
        {
            var h = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false)
                .Player(false)
                .Virtual(threat: 0.2f);
            for (int i = 0; i < 3; i++)
                h.Tick();
            AssertEq(3, h.Executor.Calls, "setup: executed three times");
            AssertEq(3, h.Controller.ApplyCalls, "setup: applied three times");
            AssertEq(GhostExecutionState.Following, h.Controller.CurrentState, "setup: Following");
            return h;
        }

        // Stimuli that would each trigger a different ordinary ghost behaviour
        // if the pipeline were still live.
        private static void ApplyStimulus(RuntimeTestHarness h, int i)
        {
            switch (i % 5)
            {
                case 0: h.SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false).Player(false).Virtual(0.2f, beat: true); break;
                case 1: h.SetPhysical(PresenceLevel.VERY_NEAR, ConfidenceLevel.HIGH, false).Player(true, 0.4f).Virtual(0.2f, beat: true); break;
                case 2: h.SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false).Player(false).Virtual(0.9f, beat: false); break;
                case 3: h.SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, true).Player(false).Virtual(0.2f, beat: true).Phase(GhostPhase.INTERRUPTED); break;
                default: h.SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false).Player(false).Virtual(0.2f, eventActive: true, beat: true); break;
            }
        }

        private static void TERM01()
        {
            var h = LiveRun();
            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            GhostExecutionState frozen = h.Controller.CurrentState;
            int executes = h.Executor.Calls;
            int applies = h.Controller.ApplyCalls;

            // 600 ticks ~ a 10 s death/ending presentation at 60 Hz, with
            // beats toggling and every kind of ghost-triggering stimulus.
            for (int i = 0; i < 600; i++)
            {
                ApplyStimulus(h, i);
                GhostExecutionState returned = h.Tick();
                AssertEq(frozen, returned, "tick " + i + ": Run() must return the unchanged last state, not a new command");
            }

            AssertEq(executes, h.Executor.Calls, "no Execute() call after terminal");
            AssertEq(applies, h.Controller.ApplyCalls, "no ApplyState() call after terminal");
            AssertEq(frozen, h.Controller.CurrentState, "controller state frozen");
            AssertTrue(h.Controller.IsHalted, "controller halted");
            AssertTrue(h.Pipeline.IsTerminalGateEngaged, "gate engaged");
        }

        private static void TERM02()
        {
            var h = LiveRun();
            h.Session.EnterTerminal(TerminalReason.EndingPlaying);
            int executes = h.Executor.Calls;
            int applies = h.Controller.ApplyCalls;

            // Faulty game flow: clears the terminal flag and starts a warning beat.
            h.SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false).Player(false).Virtual(threat: 0.2f, beat: true, terminal: false);
            for (int i = 0; i < 20; i++)
                h.Tick();

            AssertEq(executes, h.Executor.Calls, "warning beat must not trigger Execute() after terminal");
            AssertEq(applies, h.Controller.ApplyCalls, "warning beat must not trigger ApplyState() after terminal");
            AssertEq(GhostExecutionState.Following, h.Controller.CurrentState, "no Warning state reached");
            AssertTrue(h.Pipeline.IsTerminalGateEngaged, "gate stays latched");
        }

        private static void TERM03()
        {
            // No GameplaySession call at all: the gameplayTerminal flag alone
            // is observed by the pipeline and latches the gate.
            var h = LiveRun();
            h.Virtual(threat: 0.2f, terminal: true);
            h.Tick();
            AssertTrue(h.Pipeline.IsTerminalGateEngaged, "flag observed -> gate latched");
            AssertTrue(h.Controller.IsHalted, "controller halted by the pipeline");
            int executes = h.Executor.Calls;
            int applies = h.Controller.ApplyCalls;

            h.Virtual(threat: 0.2f, beat: true, terminal: false);
            for (int i = 0; i < 10; i++)
                h.Tick();

            AssertEq(executes, h.Executor.Calls, "latched: Execute() count unchanged");
            AssertEq(applies, h.Controller.ApplyCalls, "latched: ApplyState() count unchanged");
        }

        private static void TERM04()
        {
            // A caller that bypasses the pipeline and talks to the controller
            // directly still cannot move a halted ghost.
            var controller = new GhostBehaviourController();
            controller.ApplyState(GhostExecutionState.Following);
            controller.Halt();

            foreach (GhostExecutionState s in Enum.GetValues(typeof(GhostExecutionState)))
            {
                controller.ApplyState(s);
                AssertEq(GhostExecutionState.Following, controller.CurrentState, "halted controller must ignore " + s);
            }

            bool threw = false;
            try { controller.ApplyState((GhostExecutionState)99); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            AssertTrue(threw, "invalid state still throws while halted");

            controller.Reset();
            AssertTrue(!controller.IsHalted, "Reset() clears halt");
            controller.ApplyState(GhostExecutionState.Warning);
            AssertEq(GhostExecutionState.Warning, controller.CurrentState, "after Reset() states apply again");
        }

        private static void TERM05()
        {
            var h = LiveRun();
            var previous = h.RuleBased.TemporalContext.PreviousAction;
            long entered = h.RuleBased.TemporalContext.TimeEnteredCurrentAction;
            string reasonBefore = h.RuleBased.LastDecisionReason;

            h.Session.EnterTerminal(TerminalReason.RunCompleted);
            for (int i = 0; i < 50; i++)
            {
                ApplyStimulus(h, i);
                h.Tick();
            }

            AssertEq(previous, h.RuleBased.TemporalContext.PreviousAction, "inner history not advanced while terminal");
            AssertEq(entered, h.RuleBased.TemporalContext.TimeEnteredCurrentAction, "inner dwell clock not advanced while terminal");
            AssertEq(reasonBefore, h.RuleBased.LastDecisionReason, "inner provider not evaluated while terminal");
        }

        private static void TERM06()
        {
            var h = LiveRun();
            int decisions = h.Count(TelemetryEventType.DecisionMade);
            int executed = h.Count(TelemetryEventType.GhostActionExecuted);
            int changed = h.Count(TelemetryEventType.GhostExecutionStateChanged);

            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            for (int i = 0; i < 5; i++)
                h.Tick();

            AssertEq(decisions, h.Count(TelemetryEventType.DecisionMade), "no DecisionMade after terminal");
            AssertEq(executed, h.Count(TelemetryEventType.GhostActionExecuted), "no GhostActionExecuted after terminal");
            AssertEq(changed, h.Count(TelemetryEventType.GhostExecutionStateChanged), "no GhostExecutionStateChanged after terminal");

            TelemetryRecord gate = h.Last(TelemetryEventType.WorldStateEvaluated);
            AssertTrue(gate.detail != null && gate.detail.StartsWith("TERMINAL GATE"), "gated ticks are visible in telemetry: " + gate.detail);
            AssertTrue(gate.ghostAction == null && gate.ghostExecutionState == null, "a gated tick records no action and no state");
        }

        private static void TERM07()
        {
            foreach (TerminalReason reason in new[] { TerminalReason.PlayerDied, TerminalReason.EndingPlaying, TerminalReason.RunCompleted })
            {
                var h = LiveRun();
                h.Session.EnterTerminal(reason);
                AssertTrue(h.Session.IsTerminal, reason + ": session terminal");
                AssertEq(reason, h.Session.TerminalReason, reason + ": reason recorded");
                int executes = h.Executor.Calls;
                h.SetPhysical(PresenceLevel.VERY_NEAR, ConfidenceLevel.HIGH, false).Player(true, 0.3f).Virtual(0.9f, beat: true);
                h.Tick();
                AssertEq(executes, h.Executor.Calls, reason + ": nothing executed");
            }

            var h2 = LiveRun();
            bool threw = false;
            try { h2.Session.EnterTerminal(TerminalReason.None); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            AssertTrue(threw, "None is not a terminal reason");
            AssertTrue(!h2.Session.IsTerminal && !h2.Pipeline.IsTerminalGateEngaged, "rejected call changes nothing");

            h2.Session.EnterTerminal(TerminalReason.PlayerDied);
            h2.Session.EnterTerminal(TerminalReason.EndingPlaying); // idempotent: first reason wins
            AssertEq(TerminalReason.PlayerDied, h2.Session.TerminalReason, "first reason wins");
        }

        private static void TERM08()
        {
            var h = LiveRun();
            h.Virtual(threat: 0.55f, eventActive: true, beat: true);
            h.Session.EnterTerminal(TerminalReason.PlayerDied);

            VirtualState v = h.World.GetCurrentWorldState().virtualWorld;
            AssertTrue(v.gameplayTerminal, "gameplayTerminal written to WorldState");
            AssertTrue(!v.mandatoryWarningBeat, "pending warning beat cleared");
            AssertEq(0.55f, v.threatLevel, "threatLevel preserved");
            AssertTrue(v.eventActive, "eventActive preserved");
        }

        private static void TERM09()
        {
            var h = LiveRun();
            h.Virtual(threat: 0.2f, beat: true);
            h.Phase(GhostPhase.INTERRUPTED);
            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            h.Tick();

            h.Session.RestartFromCheckpoint();

            WorldState w = h.World.GetCurrentWorldState();
            AssertTrue(!h.Session.IsTerminal, "session no longer terminal");
            AssertEq(TerminalReason.None, h.Session.TerminalReason, "reason cleared");
            AssertEq(1, h.Session.CheckpointRestarts, "restart counted");
            AssertTrue(!h.Pipeline.IsTerminalGateEngaged, "gate cleared");
            AssertTrue(!h.Controller.IsHalted, "controller un-halted");
            AssertEq(GhostExecutionState.Following, h.Controller.CurrentState, "controller back to initial Following");
            AssertTrue(!w.virtualWorld.gameplayTerminal && !w.virtualWorld.mandatoryWarningBeat, "terminal flag and beat cleared in WorldState");
            AssertEq(GhostPhase.NORMAL, w.ghost.phase, "ghost phase back to NORMAL");
            AssertTrue(!w.player.inThreatZone, "player state reset");

            // Ordinary behaviour resumes: a warning beat now produces a real, executed WARN.
            int executes = h.Executor.Calls;
            int applies = h.Controller.ApplyCalls;
            h.SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false).Virtual(threat: 0.2f, beat: true);
            GhostExecutionState state = h.Tick();
            AssertEq(GhostExecutionState.Warning, state, "pipeline live again after restart");
            AssertEq(executes + 1, h.Executor.Calls, "Execute() runs again");
            AssertEq(applies + 1, h.Controller.ApplyCalls, "ApplyState() runs again");
        }

        private static void TERM10()
        {
            // Before the run ends the ghost is in PROTECT (R1), recorded by the
            // inner provider's hysteresis. Without a reset, the 1500 ms dwell
            // would hold PROTECT after a calm restart tick 100 ms later.
            var h = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.VERY_NEAR, ConfidenceLevel.HIGH, false)
                .Player(true, 0.4f)
                .Virtual(0.2f);
            AssertEq(GhostExecutionState.Protecting, h.Tick(), "setup: PROTECT");
            AssertEq(GhostAction.PROTECT, h.RuleBased.TemporalContext.PreviousAction.Value, "setup: inner history holds PROTECT");

            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            h.Session.RestartFromCheckpoint();

            AssertTrue(h.RuleBased.TemporalContext.PreviousAction == null, "hysteresis history cleared by restart");
            h.SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false).Player(false).Virtual(0.2f);
            AssertEq(GhostExecutionState.Following, h.Tick(), "calm tick right after restart is FOLLOW, not a held PROTECT");
        }

        private static void TERM11()
        {
            // Characterisation: restarting only the pipeline while WorldState
            // still says terminal re-latches at once. This is why the session's
            // RestartFromCheckpoint() (which clears the flags) is the restart path.
            var h = LiveRun();
            h.Virtual(threat: 0.2f, terminal: true);
            h.Tick();
            AssertTrue(h.Pipeline.IsTerminalGateEngaged, "setup: latched");

            h.Pipeline.RestartFromCheckpoint();
            AssertTrue(!h.Pipeline.IsTerminalGateEngaged, "restart clears the latch");
            h.Tick(); // WorldState still has gameplayTerminal=true
            AssertTrue(h.Pipeline.IsTerminalGateEngaged, "stale flag re-latches the gate");
        }

        private static void TERM12()
        {
            var h = LiveRun();
            h.Phase(GhostPhase.INTERRUPTED).Virtual(threat: 0.2f, beat: true);
            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            h.Session.RestartFromCheckpoint();

            h.SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false).Player(false);
            GhostExecutionState state = h.Tick();
            AssertEq(GhostExecutionState.Following, state, "no stale E0 interruption or W beat after restart");
            AssertTrue(h.Last(TelemetryEventType.DecisionMade).reason.StartsWith("FOLLOW"), "reason is the plain R8 FOLLOW reason");
        }

        private static void TERM13()
        {
            var h = LiveRun();
            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            h.Session.RestartFromCheckpoint();
            h.SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false).Player(false).Virtual(0.2f);
            h.Tick();

            h.Session.EnterTerminal(TerminalReason.RunCompleted);
            int executes = h.Executor.Calls;
            h.Virtual(0.9f, beat: true);
            h.Tick();

            AssertEq(TerminalReason.RunCompleted, h.Session.TerminalReason, "second run's reason");
            AssertEq(executes, h.Executor.Calls, "second terminal also gates execution");
            h.Session.RestartFromCheckpoint();
            AssertEq(2, h.Session.CheckpointRestarts, "two restarts counted");
        }

        private static void TERM14()
        {
            var h = new RuntimeTestHarness();
            bool a = false, b = false;
            try { new GameplaySession(null, h.Pipeline); } catch (ArgumentNullException) { a = true; }
            try { new GameplaySession(h.World, null); } catch (ArgumentNullException) { b = true; }
            AssertTrue(a && b, "null WorldStateManager / pipeline rejected");
        }

        private static void TERM15()
        {
            var h = LiveRun();
            h.Session.EnterTerminal(TerminalReason.PlayerDied);
            h.Session.RestartFromCheckpoint();

            PhysicalState p = h.World.GetCurrentWorldState().physical;
            AssertEq(PresenceLevel.NEAR, p.presence, "live physical presence kept across restart");
            AssertEq(ConfidenceLevel.HIGH, p.confidence, "live physical confidence kept across restart");
        }
    }
}
