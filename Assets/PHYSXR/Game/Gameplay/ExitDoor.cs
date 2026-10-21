using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // The front door. Locked until GameFlow reports the power restored; the
    // lock indicator shows red while locked and green once usable. Using it
    // when unlocked completes the run (GameFlow -> Succeeded) and swings the
    // door open.
    public class ExitDoor : Interactable
    {
        private GameFlowController flowController;
        private Transform hinge;
        private Light indicatorLight;
        private Renderer indicatorRenderer;
        private GameAudio audioPlayer;
        private bool shownUnlocked;
        private float openAngle;
        private float openTarget;

        public void Initialize(GameFlowController flowController, Transform hinge, Light indicatorLight, Renderer indicatorRenderer, GameAudio audioPlayer = null)
        {
            this.flowController = flowController;
            this.hinge = hinge;
            this.indicatorLight = indicatorLight;
            this.indicatorRenderer = indicatorRenderer;
            this.audioPlayer = audioPlayer;
            ShowLock(false);
        }

        public override bool IsAvailable => flowController != null && !flowController.Flow.IsSucceeded;

        public override string Prompt => flowController.Flow.ExitUnlocked ? "Open front door" : "Front door (locked)";

        public bool ShowsUnlocked => shownUnlocked;

        private void Update()
        {
            if (flowController != null && flowController.Flow.ExitUnlocked != shownUnlocked && !flowController.Flow.IsSucceeded)
                ShowLock(flowController.Flow.ExitUnlocked);

            if (hinge != null && !Mathf.Approximately(openAngle, openTarget))
            {
                openAngle = Mathf.MoveTowards(openAngle, openTarget, 75f * Time.deltaTime);
                hinge.localRotation = Quaternion.Euler(0f, openAngle, 0f);
            }
        }

        public override InteractionResult Interact()
        {
            InteractionResult result = flowController.UseExit();
            if (result.Accepted)
            {
                openTarget = -100f;
                audioPlayer?.PlayAt(AudioCue.DoorOpen, transform.position + Vector3.up);
            }
            else if (result.Refusal == InteractionRefusal.ExitLocked)
            {
                audioPlayer?.PlayAt(AudioCue.DoorLocked, transform.position + Vector3.up);
            }
            return result;
        }

        private void ShowLock(bool unlocked)
        {
            shownUnlocked = unlocked;
            if (indicatorLight != null)
                indicatorLight.color = unlocked ? new Color(0.1f, 0.85f, 0.2f) : new Color(0.8f, 0.05f, 0.05f);
            if (indicatorRenderer != null)
                indicatorRenderer.sharedMaterial = HouseMaterials.Get(unlocked ? HouseMaterials.Surface.LockGreen : HouseMaterials.Surface.LockRed);
        }
    }
}
