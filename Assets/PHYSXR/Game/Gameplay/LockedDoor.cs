using PHYSXR.Game.Audio;
using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // A door that never opens. It answers every attempt with the same
    // refusal and a rattle, so the player knows the room is out of bounds.
    public class LockedDoor : Interactable
    {
        private GameFlowController flowController;
        private GameAudio audioPlayer;
        private string message;

        public void Initialize(GameFlowController flowController, GameAudio audioPlayer, string message)
        {
            this.flowController = flowController;
            this.audioPlayer = audioPlayer;
            this.message = message;
        }

        public override bool IsAvailable => flowController != null && flowController.Flow.AcceptsGameplayInput;

        public override string Prompt => "Door";

        public override InteractionResult Interact()
        {
            audioPlayer?.PlayAt(AudioCue.DoorLocked, transform.position);
            return flowController.Message(InteractionResult.Refuse(InteractionRefusal.Locked, message));
        }
    }
}
