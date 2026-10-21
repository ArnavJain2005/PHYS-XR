using PHYSXR.Game.Audio;
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
        private GameAudio audioPlayer;
        private Transform lever;
        private float leverAngle;
        private float leverTarget;

        public void Initialize(GameFlowController flowController, GameObject installedFuseVisual, GameAudio audioPlayer = null, Transform lever = null)
        {
            this.flowController = flowController;
            this.installedFuseVisual = installedFuseVisual;
            this.audioPlayer = audioPlayer;
            this.lever = lever;
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

        // Checkpoint restore: the fuse is already in and the lever is up.
        public void ShowInstalled()
        {
            if (installedFuseVisual != null)
                installedFuseVisual.SetActive(true);
            leverAngle = leverTarget = -70f;
            ApplyLever();
        }

        public override InteractionResult Interact()
        {
            InteractionResult result = flowController.InstallFuse();
            if (result.Accepted)
            {
                if (installedFuseVisual != null)
                    installedFuseVisual.SetActive(true);
                leverTarget = -70f;
                audioPlayer?.PlayAt(AudioCue.FuseInsert, transform.position);
            }
            else if (result.Refusal == InteractionRefusal.FuseRequired)
            {
                audioPlayer?.PlayAt(AudioCue.Invalid, transform.position);
            }
            return result;
        }

        private void Update()
        {
            if (lever == null || Mathf.Approximately(leverAngle, leverTarget))
                return;
            leverAngle = Mathf.MoveTowards(leverAngle, leverTarget, 220f * Time.deltaTime);
            ApplyLever();
        }

        private void ApplyLever()
        {
            if (lever != null)
                lever.localRotation = Quaternion.Euler(0f, 0f, leverAngle);
        }
    }
}
