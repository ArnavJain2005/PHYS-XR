namespace PHYSXR.Game.Flow
{
    // Outcome of one gameplay request (an interaction or a flow call).
    // Message is player-facing copy; it is set for refusals too, so the UI
    // can always explain why nothing happened.
    public readonly struct InteractionResult
    {
        public readonly bool Accepted;
        public readonly InteractionRefusal Refusal;
        public readonly string Message;

        private InteractionResult(bool accepted, InteractionRefusal refusal, string message)
        {
            Accepted = accepted;
            Refusal = refusal;
            Message = message ?? string.Empty;
        }

        public static InteractionResult Accept(string message)
        {
            return new InteractionResult(true, InteractionRefusal.None, message);
        }

        public static InteractionResult Refuse(InteractionRefusal refusal, string message)
        {
            return new InteractionResult(false, refusal, message);
        }

        public override string ToString()
        {
            return Accepted ? "Accepted: " + Message : "Refused(" + Refusal + "): " + Message;
        }
    }
}
