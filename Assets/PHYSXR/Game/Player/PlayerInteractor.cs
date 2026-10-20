using System;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using UnityEngine;

namespace PHYSXR.Game.Player
{
    // Finds the Interactable under the centre of the view and forwards an
    // interaction request to it. This is the gameplay-facing interaction API:
    // the desktop adapter reaches it through PlayerController, and a future
    // XR adapter can call TryInteract() the same way (or supply its own ray
    // origin via the head transform).
    public class PlayerInteractor : MonoBehaviour
    {
        public const float DefaultRange = 2f;

        [SerializeField] private float range = DefaultRange;

        private Transform rayOrigin;
        private GameFlowController flowController;
        private readonly RaycastHit[] hits = new RaycastHit[8];

        // The interactable currently targeted, or null. Refreshed every frame.
        public Interactable CurrentTarget { get; private set; }

        public float Range => range;

        // Raised for every interaction attempt that reached an interactable.
        public event Action<Interactable, InteractionResult> Interacted;

        public void Initialize(Transform rayOrigin, GameFlowController flowController)
        {
            this.rayOrigin = rayOrigin;
            this.flowController = flowController;
        }

        private void Update()
        {
            CurrentTarget = FindTarget();
        }

        // Re-evaluates the target immediately (used after teleports and by tests).
        public Interactable RefreshTarget()
        {
            CurrentTarget = FindTarget();
            return CurrentTarget;
        }

        public InteractionResult TryInteract()
        {
            if (flowController == null || !flowController.Flow.AcceptsGameplayInput)
                return InteractionResult.Refuse(InteractionRefusal.GameplayInactive, GameFlow.MsgGameplayInactive);

            Interactable target = RefreshTarget();
            if (target == null)
                return InteractionResult.Refuse(InteractionRefusal.None, string.Empty);

            InteractionResult result = target.Interact();
            Interacted?.Invoke(target, result);
            return result;
        }

        private Interactable FindTarget()
        {
            if (rayOrigin == null || flowController == null || !flowController.Flow.AcceptsGameplayInput)
                return null;

            // Nearest non-trigger hit wins, so walls block interaction.
            // The ray starts inside the player's own CharacterController,
            // which Physics.Raycast does not report.
            int count = Physics.RaycastNonAlloc(rayOrigin.position, rayOrigin.forward, hits, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Collider nearestCollider = null;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform))
                    continue;
                if (hits[i].distance < nearest)
                {
                    nearest = hits[i].distance;
                    nearestCollider = hits[i].collider;
                }
            }

            if (nearestCollider == null)
                return null;

            Interactable interactable = nearestCollider.GetComponentInParent<Interactable>();
            return interactable != null && interactable.isActiveAndEnabled && interactable.IsAvailable ? interactable : null;
        }
    }
}
