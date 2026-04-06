using Helper.Runtime.Core;
using System.Text;

namespace Helper.Api.Conversation;

public interface ISharedUnderstandingService
{
    void CaptureTurnOutcome(ConversationState state, ChatTurnContext context, DateTimeOffset now);
    string? BuildContextBlock(ConversationState state, ChatTurnContext context);
}

public sealed class SharedUnderstandingService : ISharedUnderstandingService
{
    public void CaptureTurnOutcome(ConversationState state, ChatTurnContext context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        lock (state.SyncRoot)
        {
            var current = state.SharedUnderstanding;
            var preferredMode = ResolveInteractionMode(context, current?.PreferredInteractionMode);
            var prefersDecisiveAction = current?.PrefersDecisiveAction == true ||
                                        context.CollaborationIntent.PrefersAnswerOverClarification ||
                                        context.CollaborationIntent.SeeksDelegatedExecution ||
                                        (context.ForceBestEffort && !context.RequiresClarification);
            var acceptedAssumptions = current?.AcceptedAssumptionsRecently == true ||
                                      (context.ForceBestEffort && !context.RequiresClarification);
            var clarificationUnhelpful = current?.ClarificationWasUnhelpfulRecently == true ||
                                         (context.RequiresClarification && context.Conversation.ConsecutiveClarificationTurns >= 2);
            var templateResistance = current?.TemplateResistanceObserved == true ||
                                     (context.StyleTelemetry?.GenericNextStepDetected ?? false) ||
                                     (context.StyleTelemetry?.GenericClarificationDetected ?? false);
            var prefersConciseReassurance = current?.PrefersConciseReassurance == true ||
                                            (context.InteractionPolicy?.IncreaseReassurance == true &&
                                             context.InteractionPolicy?.CompressStructure == true);
            var overloadObserved = current?.OverloadObservedRecently == true ||
                                   context.InteractionState?.OverloadRisk >= InteractionState.InteractionSignalLevel.Moderate;
            var frustrationObserved = current?.FrustrationObservedRecently == true ||
                                      context.InteractionState?.FrustrationLevel >= InteractionState.InteractionSignalLevel.Moderate;

            state.SharedUnderstanding = new SharedUnderstandingState(
                PreferredInteractionMode: preferredMode,
                PrefersDecisiveAction: prefersDecisiveAction,
                AcceptedAssumptionsRecently: acceptedAssumptions,
                ClarificationWasUnhelpfulRecently: clarificationUnhelpful,
                TemplateResistanceObserved: templateResistance,
                PrefersConciseReassurance: prefersConciseReassurance,
                OverloadObservedRecently: overloadObserved,
                FrustrationObservedRecently: frustrationObserved,
                UpdatedAtUtc: now);

            state.UserUnderstanding = new UserUnderstandingState(
                PreferredInteractionMode: preferredMode,
                DecisionAssertiveness: state.DecisionAssertiveness,
                ClarificationTolerance: state.ClarificationTolerance,
                CitationPreference: state.CitationPreference,
                RepairStyle: state.RepairStyle,
                ReasoningStyle: state.ReasoningStyle,
                UpdatedAtUtc: now);

            if (state.ProjectContext is not null)
            {
                state.ProjectUnderstanding = new ProjectUnderstandingState(
                    state.ProjectContext.ProjectId,
                    context.Intent.Intent.ToString(),
                    prefersDecisiveAction ? "bias_to_useful_forward_motion" : "clarify_when_risk_is_real",
                    now);
            }
        }
    }

    public string? BuildContextBlock(ConversationState state, ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        lock (state.SyncRoot)
        {
            var shared = state.SharedUnderstanding;
            if (shared is null)
            {
                return null;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Shared understanding:");
            if (!string.IsNullOrWhiteSpace(shared.PreferredInteractionMode))
            {
                builder.Append("- Preferred interaction mode: ").AppendLine(shared.PreferredInteractionMode);
            }

            if (shared.PrefersDecisiveAction)
            {
                builder.AppendLine("- User usually prefers useful forward motion over extra clarification when safe.");
            }

            if (shared.AcceptedAssumptionsRecently)
            {
                builder.AppendLine("- Recent turns accepted bounded assumptions when they were made explicit.");
            }

            if (shared.ClarificationWasUnhelpfulRecently)
            {
                builder.AppendLine("- Recent clarification loops were not especially helpful; bias toward a small useful start.");
            }

            if (shared.TemplateResistanceObserved)
            {
                builder.AppendLine("- Avoid canned transitions, generic follow-ups, and protocol-heavy phrasing.");
            }

            if (shared.PrefersConciseReassurance)
            {
                builder.AppendLine("- Prefer calm reassurance in a concise form rather than long comforting detours.");
            }

            if (shared.OverloadObservedRecently)
            {
                builder.AppendLine("- Keep structure easier to scan when the answer gets dense.");
            }

            if (shared.FrustrationObservedRecently)
            {
                builder.AppendLine("- Bias toward fixing the most important issue first instead of reopening wide clarification loops.");
            }

            var block = builder.ToString().TrimEnd();
            return block.Equals("Shared understanding:", StringComparison.Ordinal)
                ? null
                : block;
        }
    }

    private static string? ResolveInteractionMode(ChatTurnContext context, string? current)
    {
        if (context.CollaborationIntent.SeeksDelegatedExecution || context.Intent.Intent == IntentType.Generate)
        {
            return "execution";
        }

        if (context.Intent.Intent == IntentType.Research || context.IsFactualPrompt)
        {
            return "research";
        }

        if (context.CollaborationIntent.IsGuidanceSeeking || context.CollaborationIntent.TrustsBestJudgment)
        {
            return "guidance";
        }

        return current;
    }
}
