using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed class TurnLiveWebDecisionStep
{
    private readonly ILiveWebRequirementPolicy _liveWebRequirementPolicy;

    public TurnLiveWebDecisionStep(ILiveWebRequirementPolicy liveWebRequirementPolicy)
    {
        _liveWebRequirementPolicy = liveWebRequirementPolicy;
    }

    public Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        _ = ct;
        ApplyLiveWebRequirement(planningContext.Turn, planningContext.TrimmedMessage);
        ApplyLocalFirstBenchmarkPolicy(planningContext.Turn, planningState.BenchmarkDecision);
        return Task.CompletedTask;
    }

    private void ApplyLocalFirstBenchmarkPolicy(ChatTurnContext turn, LocalFirstBenchmarkDecision decision)
    {
        if (!decision.IsBenchmark)
        {
            return;
        }

        turn.IntentSignals.Add("planner:local_first_benchmark");
        turn.IsFactualPrompt = true;

        if (string.Equals(TurnPlanningRules.NormalizeLiveWebMode(turn.Request.LiveWebMode), "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (decision.Mode)
        {
            case LocalFirstBenchmarkMode.LocalOnly:
                TurnPlanningRules.PromoteBenchmarkResearch(turn, decision.ReasonCode, 0.64, "planner:benchmark_local_only");
                if (string.IsNullOrWhiteSpace(turn.ResolvedLiveWebRequirement) ||
                    !string.Equals(turn.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase))
                {
                    turn.ResolvedLiveWebRequirement = "no_web_needed";
                    turn.ResolvedLiveWebReason = decision.ReasonCode;
                }

                turn.UncertaintyFlags.Add("benchmark_local_first_local_only_route");
                break;

            case LocalFirstBenchmarkMode.WebRecommended:
                TurnPlanningRules.PromoteBenchmarkResearch(turn, decision.ReasonCode, 0.68, "planner:benchmark_web_recommended");
                if (string.Equals(turn.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase))
                {
                    turn.ResolvedLiveWebRequirement = "web_helpful";
                    turn.ResolvedLiveWebReason = decision.ReasonCode;
                    turn.LiveWebSignals.Add("benchmark:recommended_web");
                }

                turn.UncertaintyFlags.Add("benchmark_local_first_web_recommended_route");
                break;

            case LocalFirstBenchmarkMode.WebRequired:
                TurnPlanningRules.PromoteBenchmarkResearch(turn, decision.ReasonCode, 0.74, "planner:benchmark_web_required");
                turn.ResolvedLiveWebRequirement = "web_required";
                turn.ResolvedLiveWebReason = decision.ReasonCode;
                turn.LiveWebSignals.Add("benchmark:mandatory_web");
                turn.UncertaintyFlags.Add("benchmark_local_first_web_required_route");
                break;
        }
    }

    private void ApplyLiveWebRequirement(ChatTurnContext turn, string trimmed)
    {
        var requestedMode = TurnPlanningRules.NormalizeLiveWebMode(turn.Request.LiveWebMode);
        var decision = requestedMode switch
        {
            "force_search" => new LiveWebRequirementDecision(
                LiveWebRequirementLevel.WebRequired,
                "user_forced_search",
                ["user:force_search"]),
            "no_web" => new LiveWebRequirementDecision(
                LiveWebRequirementLevel.NoWebNeeded,
                "user_disabled_web",
                ["user:no_web"]),
            _ => _liveWebRequirementPolicy.Evaluate(trimmed, turn.Intent)
        };
        turn.ResolvedLiveWebRequirement = decision.Requirement switch
        {
            LiveWebRequirementLevel.WebRequired => "web_required",
            LiveWebRequirementLevel.WebHelpful => "web_helpful",
            _ => "no_web_needed"
        };
        turn.ResolvedLiveWebReason = decision.ReasonCode;
        turn.LiveWebSignals.Clear();
        turn.LiveWebSignals.AddRange(decision.Signals);

        if (string.Equals(requestedMode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            if (turn.Intent.Intent == IntentType.Research)
            {
                turn.Intent = turn.Intent with { Intent = IntentType.Unknown };
                turn.IntentConfidence = Math.Min(turn.IntentConfidence, 0.49);
                turn.IntentSource = string.IsNullOrWhiteSpace(turn.IntentSource)
                    ? "planner_live_web_disabled_by_user"
                    : $"{turn.IntentSource}+planner_live_web_disabled_by_user";
                turn.IntentSignals.Add("planner:live_web_disabled_by_user");
            }

            turn.UncertaintyFlags.Add("live_web_disabled_by_user");
            return;
        }

        if (decision.Requirement != LiveWebRequirementLevel.NoWebNeeded)
        {
            turn.IsFactualPrompt = true;
        }

        if (string.Equals(requestedMode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            if (turn.Intent.Intent != IntentType.Research)
            {
                turn.Intent = turn.Intent with { Intent = IntentType.Research };
            }

            turn.IntentConfidence = Math.Max(turn.IntentConfidence, 0.85);
            turn.IntentSource = string.IsNullOrWhiteSpace(turn.IntentSource)
                ? "planner_live_web_forced_by_user"
                : $"{turn.IntentSource}+planner_live_web_forced_by_user";
            turn.IntentSignals.Add("planner:live_web_forced_by_user");
            if (TurnPlanningRules.IsProtectedGeneratePrompt(trimmed))
            {
                turn.UncertaintyFlags.Add("live_web_force_search_overrode_generate_route");
            }

            return;
        }

        if (!TurnPlanningRules.ShouldPromoteToResearch(turn.Intent.Intent, trimmed, decision))
        {
            return;
        }

        if (turn.Intent.Intent != IntentType.Research)
        {
            turn.Intent = turn.Intent with { Intent = IntentType.Research };
        }

        var required = decision.Requirement == LiveWebRequirementLevel.WebRequired;
        turn.IntentConfidence = Math.Max(turn.IntentConfidence, required ? 0.72 : 0.60);
        turn.IntentSource = string.IsNullOrWhiteSpace(turn.IntentSource)
            ? (required ? "planner_live_web_required_override" : "planner_live_web_helpful_override")
            : $"{turn.IntentSource}+{(required ? "planner_live_web_required_override" : "planner_live_web_helpful_override")}";
        turn.IntentSignals.Add(required ? "planner:live_web_required" : "planner:live_web_helpful");
        turn.UncertaintyFlags.Add(required ? "live_web_required_route_override" : "live_web_helpful_route_override");
    }
}
