using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // The fuse box. It stays targetable for the whole run so the player
    // always gets an answer: "needs a fuse" before pickup, installation once,
    // and "already installed" afterwards. GameFlowController starts the power
    // restoration only on the accepted installation.
    public class FuseBox : Interactable
    {
        private GameFlowController flowController;
        private GameObject installedFuseVisual;

        public void Initialize(GameFlowController flowController, GameObject installedFuseVisual)
        {
            this.flowController = flowController;
            this.installedFuseVisual = installedFuseVisual;
            if (installedFuseVisual != null)
                installedFuseVisual.SetActive(false);
        }

        public override bool IsAvailable => flowController != null && !flowController.Flow.IsSucceeded;

        public override string Prompt
        {
            get
            {
                switch (flowController.Flow.Stage)
                {
                    case ObjectiveStage.FindFuse: return "Fuse box (empty slot)";
                    case ObjectiveStage.InstallFuse: return "Install fuse";
                    default: return "Fuse box";
                }
            }
        }

        public override InteractionResult Interact()
        {
            InteractionResult result = flowController.InstallFuse();
            if (result.Accepted && installedFuseVisual != null)
                installedFuseVisual.SetActive(true);
            return result;
        }
    }
}
