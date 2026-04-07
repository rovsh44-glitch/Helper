namespace Helper.Api.Conversation;

public sealed class TurnAmbiguityResolutionStep
{
    private readonly IAmbiguityDetector _ambiguityDetector;
    private readonly IClarificationPolicy _clarificationPolicy;
    private readonly IClarificationQualityPolicy _clarificationQualityPolicy;
    private readonly IAssumptionCheckPolicy _assumptionCheckPolicy;

    public TurnAmbiguityResolutionStep(
        IAmbiguityDetector ambiguityDetector,
        IClarificationPolicy clarificationPolicy,
        IClarificationQualityPolicy clarificationQualityPolicy,
        IAssumptionCheckPolicy assumptionCheckPolicy)
    {
        _ambiguityDetector = ambiguityDetector;
        _clarificationPolicy = clarificationPolicy;
        _clarificationQualityPolicy = clarificationQualityPolicy;
        _assumptionCheckPolicy = assumptionCheckPolicy;
    }

    public Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        _ = ct;
        var turn = planningContext.Turn;
        var ambiguity = _ambiguityDetector.Analyze(planningContext.TrimmedMessage);
        planningState.Ambiguity = ambiguity;
        turn.AmbiguityType = ambiguity.Type.ToString();
        turn.AmbiguityConfidence = ambiguity.Confidence;
        turn.AmbiguityReason = ambiguity.Reason;

        if (ambiguity.IsAmbiguous)
        {
            if (ambiguity.Type == AmbiguityType.SafetyConfirmation)
            {
                turn.RequiresClarification = true;
                turn.RequiresConfirmation = true;
                var safetyDecision = _clarificationQualityPolicy.BuildDecision(
                    ambiguity,
                    turn.CollaborationIntent,
                    planningState.IntentClassification,
                    planningState.PriorClarificationTurns + 1,
                    turn.ResolvedTurnLanguage,
                    turn.InteractionPolicy);
                turn.ClarificationBoundary = safetyDecision.Boundary.ToString().ToLowerInvariant();
                turn.ClarifyingQuestion = safetyDecision.Question;
                planningState.Stop();
                return Task.CompletedTask;
            }

            var qualityDecision = _clarificationQualityPolicy.BuildDecision(
                ambiguity,
                turn.CollaborationIntent,
                planningState.IntentClassification,
                planningState.PriorClarificationTurns + 1,
                turn.ResolvedTurnLanguage,
                turn.InteractionPolicy);
            turn.ClarificationBoundary = qualityDecision.Boundary.ToString().ToLowerInvariant();

            if (_clarificationPolicy.ShouldForceBestEffort(ambiguity, planningState.PriorClarificationTurns))
            {
                turn.RequiresClarification = false;
                turn.ForceBestEffort = true;
                turn.ForceBestEffortReason = string.Equals(turn.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "Лимит уточнений исчерпан. Продолжу с разумными допущениями и явно отмечу их."
                    : "We have used up the clarification budget. I will continue with best-effort assumptions and label them clearly.";
                turn.UncertaintyFlags.Add("clarification_budget_exhausted");
            }
            else if (!qualityDecision.ShouldBlockForClarification)
            {
                turn.RequiresClarification = false;
                turn.ForceBestEffort = true;
                turn.ForceBestEffortReason = string.Equals(turn.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "Пользователь ожидает полезный старт с минимальной блокировкой на уточнения."
                    : "The user appears to want a useful start rather than a blocking clarification.";
                turn.ClarifyingQuestion = qualityDecision.Question;
                turn.UncertaintyFlags.Add("soft_best_effort_entry");
                turn.UncertaintyFlags.Add($"soft_best_effort_{ambiguity.Type.ToString().ToLowerInvariant()}");
                turn.UncertaintyFlags.Add("collaboration_intent_softened_ambiguity");
            }
            else
            {
                turn.RequiresClarification = true;
                turn.ClarifyingQuestion = qualityDecision.Question;
                turn.ForceBestEffort = true;
                turn.ForceBestEffortReason = TurnPlanningRules.BuildSoftBestEffortReason(ambiguity.Type, turn.ResolvedTurnLanguage);
                turn.UncertaintyFlags.Add("soft_best_effort_entry");
                turn.UncertaintyFlags.Add($"soft_best_effort_{ambiguity.Type.ToString().ToLowerInvariant()}");
                planningState.Stop();
                return Task.CompletedTask;
            }
        }

        if (!turn.RequiresClarification)
        {
            var assumption = _assumptionCheckPolicy.Evaluate(turn);
            if (assumption.RequiresClarification)
            {
                turn.RequiresClarification = true;
                turn.RequiresConfirmation = true;
                turn.ClarifyingQuestion = assumption.ClarifyingQuestion;
                if (!string.IsNullOrWhiteSpace(assumption.Flag))
                {
                    turn.UncertaintyFlags.Add(assumption.Flag);
                }

                planningState.Stop();
            }
        }

        return Task.CompletedTask;
    }
}
