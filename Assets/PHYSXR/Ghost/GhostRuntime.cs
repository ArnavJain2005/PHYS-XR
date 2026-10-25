using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;
using UnityEngine;

namespace PHYSXR.Ghost
{
    // Unity-facing runtime adapter. This is the ONLY class in the PHYSXR
    // Ghost pipeline that may depend on UnityEngine - everything it
    // composes (RuleBasedDecisionProvider, EncounterDecisionLayer,
    // GhostActionExecutor, GhostBehaviourController) remains plain C# and
    // Unity-independent.
    //
    // Ownership: GhostRuntime does NOT construct its own PhysicalStateManager
    // or WorldStateManager. The game composition (GameFlowController) owns
    // WorldStateManager and supplies it via Initialize(). No singleton, no
    // FindObjectOfType/FindFirstObjectByType, no static global state, and
    // no silent fallback WorldStateManager is ever constructed here.
    //
    // Lifecycle: intentionally has no Awake()/Update() driving the pipeline
    // automatically. Tick() is explicit: GameFlowController calls it once per
    // frame after pushing Player/Virtual/Ghost state, and only while the run
    // is playing. Tick() fails loudly if called before Initialize().
    //
    // DECISION CLOCK (fix P1): Initialize(worldStateManager, clock) rebuilds
    // the chain with that monotonic clock injected into both decision layers,
    // and Tick() stamps telemetry with it. The game passes accumulated scaled
    // game time, so hysteresis keeps counting while the sensor is silent and
    // stops while the game is paused. Initialize(worldStateManager) keeps the
    // original composition (sensor timestamps, wall-clock telemetry).
    public class GhostRuntime : MonoBehaviour
    {
        private WorldStateManager worldStateManager;
        private Func<long> decisionClock;

        private IGhostActionExecutor actionExecutor = new GhostActionExecutor();
        private IGhostBehaviourController behaviourController = new GhostBehaviourController();

        // Plain C#, Unity-independent orchestration of decide -> execute ->
        // apply (+ optional telemetry). See GhostRuntimePipeline.
        private GhostRuntimePipeline pipeline;

        // A field initializer cannot reference another instance field
        // (CS0236), so the default composition happens in a constructor.
        // It only wires plain C# objects and calls no UnityEngine API, which
        // is what Unity's "avoid MonoBehaviour constructors" guidance is about.
        public GhostRuntime()
        {
            pipeline = GhostRuntimePipeline.CreateRuntimeChain(new RuleBasedDecisionProvider(), actionExecutor, behaviourController);
        }

        public bool IsInitialized { get; private set; }

        // The run's terminal-state owner (EnterTerminal / RestartFromCheckpoint).
        // Null until Initialize() has supplied the WorldStateManager it needs.
        public GameplaySession Session { get; private set; }

        // True once the run is terminal: the ghost's decision/execution/
        // behaviour pipeline is gated off and presentation must not drive
        // ordinary ghost movement or attacks.
        public bool IsHalted => behaviourController.IsHalted;

        // Current execution-level behaviour state, read-only, for the
        // presentation layer (GhostMotionController).
        public GhostExecutionState CurrentState => behaviourController.CurrentState;

        public GhostRuntimePipeline Pipeline => pipeline;

        // Most recent decision outputs, for the motion controller's telemetry
        // and the dev overlay.
        public GhostAction? LastAction => pipeline.LastAction;
        public string LastRuleId => pipeline.LastRuleId;
        public string LastReason => pipeline.LastReason;
        public long CurrentDecisionId => pipeline.CurrentDecisionId;
        public long LastCausedEventId => pipeline.LastCausedEventId;

        // Optional telemetry sink. Left null by default; telemetry is never
        // required for initialization or for Tick() to function.
        public ITelemetrySink TelemetrySink
        {
            get => pipeline.TelemetrySink;
            set => pipeline.TelemetrySink = value;
        }

        // Explicit injection point for the externally-owned WorldStateManager.
        public void Initialize(WorldStateManager worldStateManager)
        {
            this.worldStateManager = worldStateManager ?? throw new ArgumentNullException(nameof(worldStateManager));
            Session = new GameplaySession(this.worldStateManager, pipeline);
            IsInitialized = true;
        }

        // Game composition: the clocked chain (P1) with change-only telemetry.
        public void Initialize(WorldStateManager worldStateManager, Func<long> decisionClock, ITelemetrySink telemetrySink = null)
        {
            this.decisionClock = decisionClock ?? throw new ArgumentNullException(nameof(decisionClock));
            actionExecutor = new GhostActionExecutor();
            behaviourController = new GhostBehaviourController();
            pipeline = GhostRuntimePipeline.CreateRuntimeChain(decisionClock, actionExecutor, behaviourController);
            pipeline.ChangeOnlyTelemetry = true;
            pipeline.TelemetrySink = telemetrySink;
            Initialize(worldStateManager);
        }

        // Runs one pass of the pipeline:
        // WorldState -> RuleBasedDecisionProvider -> EncounterDecisionLayer ->
        // GhostAction -> GhostExecutionState -> GhostBehaviourController
        // (plus optional telemetry). Once the run is terminal the pipeline's
        // gate makes this a no-op for the ghost.
        public void Tick()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "GhostRuntime.Tick() was called before Initialize(WorldStateManager) - no WorldStateManager has been supplied.");

            WorldState worldState = worldStateManager.GetCurrentWorldState();
            long now = decisionClock != null ? decisionClock() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            pipeline.Run(worldState, now);
        }
    }
}
