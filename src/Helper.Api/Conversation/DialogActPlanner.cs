namespace Helper.Api.Conversation;

public enum DialogAct
{
    Answer,
    Clarify,
    AckMemory,
    Repair,
    Summarize,
    NextStep,
    UncertaintyAck
}

public sealed record DialogActPlan(DialogAct PrimaryAct, IReadOnlyList<DialogAct> Acts)
{
    public bool Contains(DialogAct act)
    {
        return Acts.Contains(act);
    }
}

internal interface IDialogActPlanner
{
    DialogActPlan BuildPlan(ChatTurnContext context, ResponseCompositionMode compositionMode, string preparedOutput);
}

internal sealed class DialogActPlanner : IDialogActPlanner
{
    public DialogActPlan BuildPlan(ChatTurnContext context, ResponseCompositionMode compositionMode, string preparedOutput)
    {
        ArgumentNullException.ThrowIfNull(context);

        var acts = new List<DialogAct>();
        var primary = ResolvePrimaryAct(context, compositionMode, preparedOutput);
        acts.Add(primary);

        if (ShouldAddUncertaintyAck(context, primary))
        {
            acts.Add(DialogAct.UncertaintyAck);
        }

        if (ShouldAddNextStep(context, primary))
        {
            acts.Add(DialogAct.NextStep);
        }

        return new DialogActPlan(primary, acts.Distinct().ToArray());
    }

    private static DialogAct ResolvePrimaryAct(ChatTurnContext context, ResponseCompositionMode compositionMode, string preparedOutput)
    {
        if (context.RequiresClarification)
        {
            return DialogAct.Clarify;
        }

        if (string.Equals(context.GroundingStatus, "memory_captured", StringComparison.OrdinalIgnoreCase) ||
            context.UncertaintyFlags.Contains("deterministic_memory_capture"))
        {
            return DialogAct.AckMemory;
        }

        if (!context.IsCritiqueApproved || !string.IsNullOrWhiteSpace(context.CritiqueFeedback))
        {
            return DialogAct.Repair;
        }

        if (compositionMode is ResponseCompositionMode.StructuredAnswer or ResponseCompositionMode.EvidenceBrief or ResponseCompositionMode.OperatorSummary)
        {
            return DialogAct.Summarize;
        }

        if (string.IsNullOrWhiteSpace(preparedOutput))
        {
            return DialogAct.Repair;
        }

        return DialogAct.Answer;
    }

    private static bool ShouldAddUncertaintyAck(ChatTurnContext context, DialogAct primary)
    {
        if (primary == DialogAct.Clarify)
        {
            return false;
        }

        return context.ForceBestEffort ||
               context.BudgetExceeded ||
               context.UncertaintyFlags.Contains("factual_without_sources") ||
               context.UncertaintyFlags.Contains("soft_best_effort_entry") ||
               context.UncertaintyFlags.Contains("timeout_degraded_response");
    }

    private static bool ShouldAddNextStep(ChatTurnContext context, DialogAct primary)
    {
        if (primary == DialogAct.Clarify)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(context.NextStep);
    }
}

