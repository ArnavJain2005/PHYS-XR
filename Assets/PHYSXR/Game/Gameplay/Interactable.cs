using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // Gameplay-facing interaction contract. PlayerInteractor (desktop centre-
    // of-camera targeting today, an XR ray or grab later) is the only caller;
    // no interactable reads a key or controller button itself.
    //
    // IsAvailable: whether the object can be targeted at all right now.
    // Prompt:      what the targeting UI shows while it is targeted.
    // Interact():  the single entry point. It must refuse safely (returning a
    //              refusal with a reason) rather than throw, and must be
    //              idempotent: repeated calls cannot repeat side effects.
    public abstract class Interactable : MonoBehaviour
    {
        public abstract bool IsAvailable { get; }

        public abstract string Prompt { get; }

        public abstract InteractionResult Interact();
    }
}
