using PHYSXR.Game.Audio;
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
        private GameAudio audioPlayer;

        public void Initialize(GameFlowController flowController, GameAudio audioPlayer = null)
        {
            this.flowController = flowController;
            this.audioPlayer = audioPlayer;
        }

        public override bool IsAvailable => flowController != null && flowController.Flow.Stage == ObjectiveStage.FindFuse;

        public override string Prompt => "Pick up fuse";

        public override InteractionResult Interact()
        {
            InteractionResult result = flowController.CollectFuse();
            if (result.Accepted)
            {
                audioPlayer?.PlayAt(AudioCue.FusePickup, transform.position);
                gameObject.SetActive(false);
            }
            return result;
        }
    }
}
