using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // The single fuse. Collecting it hides the object; the objective state,
    // not this component, records that the player carries it, so a repeated
    // call is refused by GameFlow and cannot create a second fuse.
    public class FusePickup : Interactable
    {
        private GameFlowController flowController;

        public void Initialize(GameFlowController flowController)
        {
            this.flowController = flowController;
        }

        public override bool IsAvailable => flowController != null && flowController.Flow.Stage == ObjectiveStage.FindFuse;

        public override string Prompt => "Pick up fuse";

        public override InteractionResult Interact()
        {
            InteractionResult result = flowController.CollectFuse();
            if (result.Accepted)
                gameObject.SetActive(false);
            return result;
        }
    }
}
