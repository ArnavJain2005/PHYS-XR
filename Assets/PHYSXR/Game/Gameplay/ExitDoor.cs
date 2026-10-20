using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // The front door. Locked until GameFlow reports the power restored; the
    // lock indicator shows red while locked and green once usable. Using it
    // when unlocked completes the run (GameFlow -> Succeeded).
    public class ExitDoor : Interactable
    {
        private static readonly Color LockedColor = new Color(0.8f, 0.05f, 0.05f);
        private static readonly Color UnlockedColor = new Color(0.1f, 0.85f, 0.2f);

        private GameFlowController flowController;
        private Transform hinge;
        private Light indicatorLight;
        private Renderer indicatorRenderer;
        private bool shownUnlocked;

        public void Initialize(GameFlowController flowController, Transform hinge, Light indicatorLight, Renderer indicatorRenderer)
        {
            this.flowController = flowController;
            this.hinge = hinge;
            this.indicatorLight = indicatorLight;
            this.indicatorRenderer = indicatorRenderer;
            ShowLock(false);
        }

        public override bool IsAvailable => flowController != null && !flowController.Flow.IsSucceeded;

        public override string Prompt => flowController.Flow.ExitUnlocked ? "Open front door" : "Front door (locked)";

        public bool ShowsUnlocked => shownUnlocked;

        private void Update()
        {
            if (flowController != null && flowController.Flow.ExitUnlocked != shownUnlocked)
                ShowLock(flowController.Flow.ExitUnlocked);
        }

        public override InteractionResult Interact()
        {
            InteractionResult result = flowController.UseExit();
            if (result.Accepted && hinge != null)
                hinge.localRotation = Quaternion.Euler(0f, -100f, 0f);
            return result;
        }

        private void ShowLock(bool unlocked)
        {
            shownUnlocked = unlocked;
            Color color = unlocked ? UnlockedColor : LockedColor;
            if (indicatorLight != null)
                indicatorLight.color = color;
            if (indicatorRenderer != null)
                indicatorRenderer.material.color = color;
        }
    }
}
