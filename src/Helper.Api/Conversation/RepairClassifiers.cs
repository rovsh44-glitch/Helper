namespace Helper.Api.Conversation;

public interface IRepairClassifiers
{
    string Classify(ChatTurnContext context);
}

public sealed class RepairClassifiers : IRepairClassifiers
{
    public string Classify(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.UncertaintyFlags.Contains("factual_without_sources") ||
            string.Equals(context.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase))
        {
            return "evidence_insufficiency";
        }

        if (!context.IsCritiqueApproved)
        {
            return "misunderstanding";
        }

        if (context.RequiresClarification || context.RequiresConfirmation)
        {
            return "missing_constraint";
        }

        if (context.IntentConfidence > 0 && context.IntentConfidence < 0.55)
        {
            return "low_confidence";
        }

        return "none";
    }
}
