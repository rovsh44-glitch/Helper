using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed class TurnIntentOverrideStep
{
    private readonly IClarificationPolicy _clarificationPolicy;

    public TurnIntentOverrideStep(IClarificationPolicy clarificationPolicy)
    {
        _clarificationPolicy = clarificationPolicy;
    }

    public Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        _ = ct;
        var turn = planningContext.Turn;
        var trimmed = planningContext.TrimmedMessage;

        if (!turn.RequiresClarification &&
            !turn.ForceBestEffort &&
            turn.Intent.Intent == IntentType.Unknown &&
            GoldenTemplateIntentPolicy.HasExplicitGoldenTemplateRequest(trimmed))
        {
            turn.Intent = turn.Intent with { Intent = IntentType.Generate };
            turn.IntentConfidence = Math.Max(turn.IntentConfidence, 0.9);
            turn.IntentSource = string.IsNullOrWhiteSpace(turn.IntentSource)
                ? "planner_golden_template_override"
                : $"{turn.IntentSource}+planner_golden_template_override";
            turn.IntentSignals.Add("planner:explicit_golden_template_override");
            turn.UncertaintyFlags.Add("golden_template_intent_forced_from_prompt");
        }

        if (!turn.RequiresClarification &&
            !turn.ForceBestEffort &&
            turn.Intent.Intent == IntentType.Unknown &&
            TurnPlanningRules.IsExplicitResearchPrompt(trimmed))
        {
            turn.Intent = turn.Intent with { Intent = IntentType.Research };
            turn.IntentConfidence = Math.Max(turn.IntentConfidence, 0.55);
            turn.IntentSource = string.IsNullOrWhiteSpace(turn.IntentSource)
                ? "planner_research_override"
                : $"{turn.IntentSource}+planner_research_override";
            turn.IntentSignals.Add("planner:explicit_research_override");
            turn.UncertaintyFlags.Add("research_intent_forced_from_prompt");
        }

        if (!turn.ForceBestEffort && turn.IntentConfidence > 0 && turn.IntentConfidence <= 0.50)
        {
            if (turn.Intent.Intent == IntentType.Research && TurnPlanningRules.IsExplicitResearchPrompt(trimmed))
            {
                turn.UncertaintyFlags.Add("intent_low_confidence_research_exec");
                turn.IntentConfidence = Math.Max(turn.IntentConfidence, 0.5);
                turn.IsFactualPrompt = true;
                return Task.CompletedTask;
            }

            if (planningState.PriorClarificationTurns >= _clarificationPolicy.MaxClarificationTurns)
            {
                turn.ForceBestEffort = true;
                turn.ForceBestEffortReason = string.Equals(turn.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "Даже после уточнений осталось несколько трактовок. Продолжу с разумными допущениями и явно отмечу их."
                    : "Even after clarification, multiple interpretations remain. I will continue with best-effort assumptions and label them clearly.";
                turn.UncertaintyFlags.Add("intent_low_confidence_fallback");
            }
            else
            {
                turn.RequiresClarification = true;
                turn.ClarifyingQuestion = _clarificationPolicy.BuildLowConfidenceQuestion(
                    planningState.IntentClassification,
                    planningState.PriorClarificationTurns + 1,
                    turn.ResolvedTurnLanguage);
                planningState.Stop();
            }
        }

        return Task.CompletedTask;
    }
}
