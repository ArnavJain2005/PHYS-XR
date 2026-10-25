using System;
using PHYSXR.Testing;

namespace PHYSXR.TestHarness
{
    // Standalone runner for every RunAll() suite that has no UnityEngine
    // dependency. Exit code is the total failure count (0 = all passed).
    //
    //   dotnet run -c Release --project Tools/TestHarness
    //
    // Passing "--suite <Name>" runs only suites whose name contains <Name>.
    //
    // Hardware bring-up without Unity (see SerialMonitor.cs):
    //   dotnet run -c Release --project Tools/TestHarness -- --serial-monitor AUTO 30
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--serial-monitor")
                return SerialMonitor.Run(args);

            string filter = args.Length >= 2 && args[0] == "--suite" ? args[1] : null;

            var suites = new (string name, Func<int> run)[]
            {
                ("SimulatedPhysicalStateSourceTests", SimulatedPhysicalStateSourceTests.RunAll),
                ("CommunicationTests", CommunicationTests.RunAll),
                ("WorldStateManagerTests", WorldStateManagerTests.RunAll),
                ("RuleBasedDecisionProviderTests", RuleBasedDecisionProviderTests.RunAll),
                ("GhostActionExecutorTests", GhostActionExecutorTests.RunAll),
                ("GhostBehaviourControllerTests", GhostBehaviourControllerTests.RunAll),
                ("TelemetryTests", TelemetryTests.RunAll),
                ("GhostRuntimePipelineTests", GhostRuntimePipelineTests.RunAll),
                ("EndToEndSimulationTests", EndToEndSimulationTests.RunAll),
                ("DecisionHistoryTests", DecisionHistoryTests.RunAll),
                ("RuleBasedDecisionProviderTemporalContextTests", RuleBasedDecisionProviderTemporalContextTests.RunAll),
                ("RuleBasedDecisionProviderHysteresisTests", RuleBasedDecisionProviderHysteresisTests.RunAll),
                ("EncounterDecisionLayerTests", EncounterDecisionLayerTests.RunAll),
                ("RuntimePipelineIntegrationTests", RuntimePipelineIntegrationTests.RunAll),
                ("TerminalGateTests", TerminalGateTests.RunAll),
                ("GameFlowTests", GameFlowTests.RunAll),
                ("DefectRegressionTests", DefectRegressionTests.RunAll),
                ("EncounterRuleTests", EncounterRuleTests.RunAll),
                ("SensorScenarioTests", SensorScenarioTests.RunAll),
                ("SerialTransportTests", SerialTransportTests.RunAll),
                ("SensorResponseProfileTests", SensorResponseProfileTests.RunAll),
                ("TelemetryJsonLinesTests", TelemetryJsonLinesTests.RunAll),
                ("GameFlowEncounterTests", GameFlowEncounterTests.RunAll),
                ("EncounterDirectorTests", EncounterDirectorTests.RunAll),
                ("PlayerLocomotionTests", PlayerLocomotionTests.RunAll),
                ("SensorPanelModelTests", SensorPanelModelTests.RunAll),
            };

            int totalFailed = 0;
            int suitesRun = 0;
            foreach (var (name, run) in suites)
            {
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Console.WriteLine("=== " + name + " ===");
                int failed;
                try
                {
                    failed = run();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SUITE CRASHED: " + name + " - " + ex);
                    failed = 1;
                }
                totalFailed += failed;
                suitesRun++;
            }

            Console.WriteLine();
            Console.WriteLine("SUITES RUN: " + suitesRun + ", TOTAL FAILED: " + totalFailed);
            return totalFailed;
        }
    }
}
