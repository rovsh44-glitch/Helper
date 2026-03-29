using Helper.Api.Conversation;
using Helper.Runtime.Core;

namespace Helper.Api.Backend.Application;

public interface ITurnRouteTelemetryRecorder
{
    void RecordCompletedTurn(ChatTurnContext context);
    void RecordBlockedTurn(ConversationState state, InputRiskScanResult risk);
}

public sealed class TurnRouteTelemetryRecorder : ITurnRouteTelemetryRecorder
{
    private readonly IRouteTelemetryService? _routeTelemetry;

    public TurnRouteTelemetryRecorder(IRouteTelemetryService? routeTelemetry = null)
    {
        _routeTelemetry = routeTelemetry;
    }

    public void RecordCompletedTurn(ChatTurnContext context)
    {
        if (_routeTelemetry is null)
        {
            return;
        }

        var routeKey = context.RequiresClarification
            ? "clarification"
            : context.Intent.Intent.ToString().ToLowerInvariant();
        var degradationReason = ResolveChatDegradationReason(context);
        var quality = ResolveChatQuality(context, degradationReason);
        var outcome = context.RequiresClarification
            ? RouteTelemetryOutcomes.Clarification
            : quality == RouteTelemetryQualities.Degraded
                ? RouteTelemetryOutcomes.Degraded
                : RouteTelemetryOutcomes.Completed;

        _routeTelemetry.Record(new RouteTelemetryEvent(
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Channel: RouteTelemetryChannels.Chat,
            OperationKind: RouteTelemetryOperationKinds.ChatTurn,
            RouteKey: routeKey,
            Quality: quality,
            Outcome: outcome,
            Confidence: context.IntentConfidence > 0 ? context.IntentConfidence : context.Confidence,
            ModelRoute: ChatTurnExecutionSupport.ResolveModelClass(context).ToString(),
            CorrelationId: context.Conversation.Id,
            IntentSource: context.IntentSource,
            ExecutionMode: context.ExecutionMode.ToString(),
            BudgetProfile: TurnBudgetProfileFormatter.Format(context.BudgetProfile),
            DegradationReason: degradationReason,
            RouteMatched: !string.Equals(routeKey, "clarification", StringComparison.OrdinalIgnoreCase),
            RequiresClarification: context.RequiresClarification,
            BudgetExceeded: context.BudgetExceeded,
            Signals: context.IntentSignals.ToArray()));
    }

    public void RecordBlockedTurn(ConversationState state, InputRiskScanResult risk)
    {
        if (_routeTelemetry is null)
        {
            return;
        }

        _routeTelemetry.Record(new RouteTelemetryEvent(
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Channel: RouteTelemetryChannels.Chat,
            OperationKind: RouteTelemetryOperationKinds.ChatTurn,
            RouteKey: "input_risk_blocked",
            Quality: RouteTelemetryQualities.Blocked,
            Outcome: RouteTelemetryOutcomes.Blocked,
            CorrelationId: state.Id,
            DegradationReason: "input_blocked",
            RouteMatched: false,
            RequiresClarification: false,
            BudgetExceeded: false,
            Signals: risk.Flags));
    }

    private static string ResolveChatQuality(ChatTurnContext context, string? degradationReason)
    {
        if (string.Equals(context.GroundingStatus, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTelemetryQualities.Blocked;
        }

        if (context.RequiresClarification || context.ForceBestEffort || context.BudgetExceeded || !string.IsNullOrWhiteSpace(degradationReason))
        {
            return RouteTelemetryQualities.Degraded;
        }

        var confidence = context.IntentConfidence > 0 ? context.IntentConfidence : context.Confidence;
        if (confidence >= 0.85)
        {
            return RouteTelemetryQualities.High;
        }

        if (confidence >= 0.60)
        {
            return RouteTelemetryQualities.Medium;
        }

        if (confidence > 0)
        {
            return RouteTelemetryQualities.Low;
        }

        return RouteTelemetryQualities.Unknown;
    }

    private static string? ResolveChatDegradationReason(ChatTurnContext context)
    {
        if (context.RequiresClarification)
        {
            return "clarification_required";
        }

        if (context.ForceBestEffort)
        {
            return "best_effort";
        }

        if (context.BudgetExceeded)
        {
            return "budget_exceeded";
        }

        if (context.UncertaintyFlags.Any(flag => flag.StartsWith("stage_timeout", StringComparison.OrdinalIgnoreCase)))
        {
            return "stage_timeout";
        }

        if (context.UncertaintyFlags.Any(flag => flag.Equals("turn_pipeline_recovered", StringComparison.OrdinalIgnoreCase)))
        {
            return "recovered_turn";
        }

        if (context.UncertaintyFlags.Any(flag => flag.Equals("generation_disabled", StringComparison.OrdinalIgnoreCase)))
        {
            return "generation_disabled";
        }

        if (context.UncertaintyFlags.Any(flag => flag.Equals("generation_admission_denied", StringComparison.OrdinalIgnoreCase)))
        {
            return "generation_admission_denied";
        }

        if (context.UncertaintyFlags.Any(flag => flag.Equals("research_disabled", StringComparison.OrdinalIgnoreCase)))
        {
            return "research_disabled";
        }

        return string.Equals(context.GroundingStatus, "degraded", StringComparison.OrdinalIgnoreCase)
            ? "grounding_degraded"
            : null;
    }
}

