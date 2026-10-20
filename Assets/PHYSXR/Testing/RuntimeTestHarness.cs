using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.Ghost;
using PHYSXR.PhysicalWorld;
using PHYSXR.SessionSystem;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // Test-only composition of the runtime, built exactly like GhostRuntime
    // builds it: the pipeline comes from GhostRuntimePipeline.CreateRuntimeChain
    // (R1-R8 provider -> EncounterDecisionLayer), fed from a real
    // WorldStateManager, with a real GameplaySession on top. GhostRuntime
    // itself is a MonoBehaviour and cannot be instantiated outside Unity.
    //
    // The executor and controller are counting wrappers around the REAL
    // GhostActionExecutor / GhostBehaviourController, so tests can prove that
    // an action was (or was not) actually executed/applied, not merely chosen.
    internal sealed class RuntimeTestHarness
    {
        public readonly PhysicalStateManager Physical = new PhysicalStateManager();
        public readonly WorldStateManager World;
        public readonly RuleBasedDecisionProvider RuleBased = new RuleBasedDecisionProvider();
        public readonly CountingExecutor Executor = new CountingExecutor();
        public readonly CountingController Controller = new CountingController();
        public readonly InMemoryTelemetrySink Telemetry = new InMemoryTelemetrySink();
        public readonly GhostRuntimePipeline Pipeline;
        public readonly GameplaySession Session;

        private long clock = 1000L;

        public RuntimeTestHarness()
        {
            World = new WorldStateManager(Physical);
            Pipeline = GhostRuntimePipeline.CreateRuntimeChain(RuleBased, Executor, Controller);
            Pipeline.TelemetrySink = Telemetry;
            Session = new GameplaySession(World, Pipeline);
        }

        public long Clock => clock;

        public RuntimeTestHarness SetPhysical(PresenceLevel presence, ConfidenceLevel confidence, bool disturbance)
        {
            Physical.UpdateState(new PhysicalState { presence = presence, confidence = confidence, disturbance = disturbance, timestamp = clock });
            return this;
        }

        public RuntimeTestHarness Player(bool inThreatZone, float distanceToGhost = 5f)
        {
            World.UpdatePlayerState(new PlayerState { distanceToGhost = distanceToGhost, movement = true, inThreatZone = inThreatZone });
            return this;
        }

        public RuntimeTestHarness Virtual(float threat = 0.2f, bool eventActive = false, bool beat = false, bool terminal = false)
        {
            World.UpdateVirtualState(new VirtualState { threatLevel = threat, eventActive = eventActive, mandatoryWarningBeat = beat, gameplayTerminal = terminal });
            return this;
        }

        public RuntimeTestHarness Phase(GhostPhase phase)
        {
            World.UpdateGhostState(new GhostState { currentState = GhostAction.FOLLOW, phase = phase });
            return this;
        }

        // One runtime tick over the current WorldState snapshot. Advances the
        // harness clock (and the physical timestamp, which R1-R8 hysteresis
        // reads) by 100 ms so successive ticks look like successive frames.
        public GhostExecutionState Tick()
        {
            clock += 100;
            PhysicalState p = Physical.GetCurrentState() ?? new PhysicalState();
            Physical.UpdateState(new PhysicalState { presence = p.presence, confidence = p.confidence, disturbance = p.disturbance, timestamp = clock });
            return Pipeline.Run(World.GetCurrentWorldState(), clock);
        }

        public TelemetryRecord Last(TelemetryEventType type)
        {
            TelemetryRecord found = null;
            foreach (var r in Telemetry.Records)
                if (r.eventType == type)
                    found = r;
            return found;
        }

        public int Count(TelemetryEventType type)
        {
            int n = 0;
            foreach (var r in Telemetry.Records)
                if (r.eventType == type)
                    n++;
            return n;
        }

        internal sealed class CountingExecutor : IGhostActionExecutor
        {
            private readonly GhostActionExecutor real = new GhostActionExecutor();
            public int Calls;
            public GhostAction? LastAction;

            public GhostExecutionState Execute(GhostAction action)
            {
                Calls++;
                LastAction = action;
                return real.Execute(action);
            }
        }

        internal sealed class CountingController : IGhostBehaviourController
        {
            private readonly GhostBehaviourController real = new GhostBehaviourController();
            public int ApplyCalls;

            public GhostExecutionState CurrentState => real.CurrentState;
            public bool IsHalted => real.IsHalted;

            public void ApplyState(GhostExecutionState state)
            {
                ApplyCalls++;
                real.ApplyState(state);
            }

            public void Halt() => real.Halt();

            public void Reset() => real.Reset();
        }
    }
}
