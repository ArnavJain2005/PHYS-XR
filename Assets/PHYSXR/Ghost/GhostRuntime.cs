using System;
using PHYSXR.Core.Data;
using PHYSXR.Decision;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;
using UnityEngine;

namespace PHYSXR.Ghost
{
    // Unity-facing runtime adapter. This is the ONLY class in the PHYSXR
    // Ghost pipeline that may depend on UnityEngine - everything it
    // composes (RuleBasedDecisionProvider, GhostActionExecutor,
    // GhostBehaviourController) remains plain C# and Unity-independent.
    //
    // Ownership: GhostRuntime does NOT construct its own PhysicalStateManager
    // or WorldStateManager. The eventual owner of WorldStateManager is the
    // (not yet built) Communication bootstrap layer, so WorldStateManager
    // must be supplied externally via Initialize(). No singleton, no
    // FindObjectOfType/FindFirstObjectByType, no static global state, and
    // no silent fallback WorldStateManager is ever constructed here.
    //
    // Lifecycle: intentionally has no Awake()/Update() driving the pipeline
    // automatically. There is no scene bootstrap or ordering guarantee yet
    // for when/whether Initialize() has been called relative to Unity's own
    // lifecycle callbacks, so an automatic Update() could silently run (or
    // silently skip) the pipeline in ways that mask real wiring bugs.
    // Instead, Tick() is explicit: the caller controls both initialization
    // ordering and ticking cadence, and Tick() fails loudly if called
    // before Initialize().
    public class GhostRuntime : MonoBehaviour
    {
        private WorldStateManager worldStateManager;

        // Decision chain: RuleBasedDecisionProvider (R1-R8, frozen) is wrapped
        // by EncounterDecisionLayer (terminal / interruption / safety /
        // mandatory-warning-beat arbitration). The pipeline is handed the
        // OUTER layer, so action, reason and telemetry are all the final ones.
        private readonly RuleBasedDecisionProvider ruleBasedProvider = new RuleBasedDecisionProvider();
        private readonly IGhostActionExecutor actionExecutor = new GhostActionExecutor();
        private readonly IGhostBehaviourController behaviourController = new GhostBehaviourController();

        // Plain C#, Unity-independent orchestration of decide -> execute ->
        // apply (+ optional telemetry), extracted so it stays directly
        // testable outside Unity. See GhostRuntimePipeline for details.
        private readonly GhostRuntimePipeline pipeline;

        // A field initializer cannot reference another instance field
        // (CS0236), so this composition happens in a constructor instead.
        // This is safe for a MonoBehaviour here because it only wires
        // together plain C# objects already created by field initializers
        // above - it calls no UnityEngine API and touches no serialized
        // field, which is what Unity's "avoid MonoBehaviour constructors"
        // guidance actually warns against.
        public GhostRuntime()
        {
            pipeline = GhostRuntimePipeline.CreateRuntimeChain(ruleBasedProvider, actionExecutor, behaviourController);
        }

        public bool IsInitialized { get; private set; }

        // The run's terminal-state owner (EnterTerminal / RestartFromCheckpoint).
        // Null until Initialize() has supplied the WorldStateManager it needs.
        public GameplaySession Session { get; private set; }

        // True once the run is terminal: the ghost's decision/execution/
        // behaviour pipeline is gated off and presentation must not drive
        // ordinary ghost movement or attacks. CurrentState then stays at the
        // last state it had; the ghost is not given any new command.
        public bool IsHalted => behaviourController.IsHalted;

        // Current execution-level behaviour state, read-only, for future
        // Unity presentation code (Animator/NavMesh/audio/VFX - none of
        // which are implemented yet).
        public GhostExecutionState CurrentState => behaviourController.CurrentState;

        // Optional telemetry sink. Left null by default, so existing
        // runtime behaviour is completely unaffected if no sink is ever
        // supplied - telemetry is never required for initialization or
        // for Tick() to function.
        public ITelemetrySink TelemetrySink
        {
            get => pipeline.TelemetrySink;
            set => pipeline.TelemetrySink = value;
        }

        // Explicit injection point for the externally-owned WorldStateManager.
        // A future Unity-facing composition/bootstrap layer calls this once
        // it exists; GhostRuntime never looks WorldStateManager up itself.
        public void Initialize(WorldStateManager worldStateManager)
        {
            this.worldStateManager = worldStateManager ?? throw new ArgumentNullException(nameof(worldStateManager));
            Session = new GameplaySession(this.worldStateManager, pipeline);
            IsInitialized = true;
        }

        // Runs one pass of the existing pipeline:
        // WorldState -> RuleBasedDecisionProvider -> EncounterDecisionLayer ->
        // GhostAction -> GhostExecutionState -> GhostBehaviourController
        // (plus optional telemetry, see GhostRuntimePipeline). Once the run is
        // terminal the pipeline's gate makes this a no-op for the ghost. Contains no
        // decision logic itself - it only fetches WorldState and hands it
        // to the already Unity-independent, already-tested pipeline.
        public void Tick()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "GhostRuntime.Tick() was called before Initialize(WorldStateManager) - no WorldStateManager has been supplied.");

            WorldState worldState = worldStateManager.GetCurrentWorldState();
            pipeline.Run(worldState, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
}
