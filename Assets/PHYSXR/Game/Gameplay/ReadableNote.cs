using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // An optional clue (GDD 8: "a small number of optional environmental or
    // audio clues; no long mandatory documents"). Reading one never changes
    // the objective; it shows a short text and is logged as a CLUE record.
    public class ReadableNote : Interactable
    {
        private GameFlowController flowController;

        public string Title { get; private set; }

        public string Body { get; private set; }

        public string PromptText { get; private set; } = "Read note";

        public bool HasBeenRead { get; private set; }

        public void Initialize(GameFlowController flowController, string title, string body, string prompt = null)
        {
            this.flowController = flowController;
            Title = title;
            Body = body;
            if (!string.IsNullOrEmpty(prompt))
                PromptText = prompt;
        }

        public override bool IsAvailable => flowController != null && flowController.Flow.AcceptsGameplayInput;

        public override string Prompt => PromptText;

        public override InteractionResult Interact()
        {
            bool first = !HasBeenRead;
            HasBeenRead = true;
            flowController.ShowNote(this, first);
            return InteractionResult.Accept(string.Empty);
        }
    }
}
