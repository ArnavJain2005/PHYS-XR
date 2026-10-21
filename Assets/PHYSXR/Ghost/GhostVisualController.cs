using UnityEngine;

namespace PHYSXR.Ghost
{
    // Minimal, deliberately unpolished Unity-facing visual adapter. Sits
    // strictly AFTER the GhostBehaviourController/runtime boundary: its
    // only input is a GhostExecutionState value that has already been
    // decided elsewhere.
    //
    // Contains NO decision logic and knows nothing about PhysicalState,
    // PhysicalEvent, WorldState, IDecisionProvider, RuleBasedDecisionProvider,
    // telemetry, sensors, UDP, or XR/Quest - its only inputs are
    // GhostExecutionState (already Unity-independent, defined in
    // PHYSXR.Ghost) and plain UnityEngine primitives for the visual itself.
    //
    // Distinguishes the five states through simple, code-only differences
    // (no external assets, no Animator, no VFX/audio/NavMesh): a vertical
    // offset, a uniform scale, and a flat instanced material colour applied
    // to whatever primitive this component is attached to.
    [DisallowMultipleComponent]
    public class GhostVisualController : MonoBehaviour
    {
        private Renderer targetRenderer;
        private Material materialInstance;

        public GhostExecutionState CurrentVisualState { get; private set; } = GhostExecutionState.Following;

        private void Awake()
        {
            targetRenderer = GetComponent<Renderer>();

            // Renderer.material returns an auto-instanced per-object copy,
            // so changing its colour never touches a shared/external asset.
            if (targetRenderer != null)
                materialInstance = targetRenderer.material;
        }

        // The only public entry point. This class never calls itself -
        // it is always driven by something else (GhostVisualBinding, or
        // the development-only demo driver) that already knows the
        // current GhostExecutionState.
        public void ApplyState(GhostExecutionState state)
        {
            CurrentVisualState = state;

            var (color, verticalOffset, scale) = Describe(state);

            Vector3 position = transform.localPosition;
            transform.localPosition = new Vector3(position.x, verticalOffset, position.z);
            transform.localScale = Vector3.one * scale;

            if (materialInstance != null)
                materialInstance.color = color;
        }

        // Pure state -> visual-parameters mapping. Not itself a decision -
        // GhostExecutionState was already decided upstream; this only
        // chooses how to *represent* a value it was told to represent.
        private static (Color color, float verticalOffset, float scale) Describe(GhostExecutionState state)
        {
            switch (state)
            {
                case GhostExecutionState.Following:
                    return (Color.white, 1f, 1f);
                case GhostExecutionState.Warning:
                    return (Color.yellow, 1f, 1f);
                case GhostExecutionState.Investigating:
                    return (new Color(1f, 0.5f, 0f), 1.3f, 1f); // orange, raised
                case GhostExecutionState.Protecting:
                    return (Color.cyan, 1f, 1.4f); // enlarged
                case GhostExecutionState.Retreating:
                    return (Color.red, 0.5f, 0.7f); // lowered, shrunk
                case GhostExecutionState.Attacking:
                    return (new Color(0.5f, 0f, 0.6f), 1f, 1.2f); // violet, enlarged
                default:
                    // GhostBehaviourController.ApplyState already rejects any
                    // undefined GhostExecutionState before this could ever be
                    // reached; this is a defensive fallback only.
                    return (Color.magenta, 1f, 1f);
            }
        }
    }
}
