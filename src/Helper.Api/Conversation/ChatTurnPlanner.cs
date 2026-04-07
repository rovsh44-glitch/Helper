using System.Diagnostics;
using Helper.Api.Hosting;
using Helper.Api.Conversation.InteractionState;

namespace Helper.Api.Conversation;

public sealed class ChatTurnPlanner : IChatTurnPlanner
{
    private readonly TurnIntentAnalysisStep _intentAnalysisStep;
    private readonly TurnPersonalizationStep _personalizationStep;
    private readonly TurnReasoningSelectionStep _reasoningSelectionStep;
    private readonly TurnLatencyBudgetStep _latencyBudgetStep;
    private readonly TurnLiveWebDecisionStep _liveWebDecisionStep;
    private readonly TurnAmbiguityResolutionStep _ambiguityResolutionStep;
    private readonly TurnIntentOverrideStep _intentOverrideStep;
    private readonly IConversationStageMetricsService? _stageMetrics;

    public ChatTurnPlanner(
        IIntentClassifier intentClassifier,
        IAmbiguityDetector ambiguityDetector,
        IClarificationPolicy clarificationPolicy,
        IIntentTelemetryService intentTelemetry,
        ILatencyBudgetPolicy? latencyBudgetPolicy = null,
        IAssumptionCheckPolicy? assumptionCheckPolicy = null,
        IFeatureFlags? featureFlags = null,
        IConversationStageMetricsService? stageMetrics = null,
        IUserProfileService? userProfileService = null,
        ITurnLanguageResolver? turnLanguageResolver = null,
        ILiveWebRequirementPolicy? liveWebRequirementPolicy = null,
        ILocalFirstBenchmarkPolicy? localFirstBenchmarkPolicy = null,
        ICollaborationIntentDetector? collaborationIntentDetector = null,
        ICommunicationQualityPolicy? communicationQualityPolicy = null,
        IPersonalizationMergePolicy? personalizationMergePolicy = null,
        IInteractionStateAnalyzer? interactionStateAnalyzer = null,
        IInteractionPolicyProjector? interactionPolicyProjector = null,
        IReasoningEffortPolicy? reasoningEffortPolicy = null,
        IClarificationQualityPolicy? clarificationQualityPolicy = null)
        : this(
            new TurnIntentAnalysisStep(
                intentClassifier,
                intentTelemetry,
                featureFlags ?? new FeatureFlags()),
            new TurnPersonalizationStep(
                userProfileService ?? new UserProfileService(),
                turnLanguageResolver ?? new TurnLanguageResolver(),
                collaborationIntentDetector ?? new CollaborationIntentDetector(),
                communicationQualityPolicy ?? new CommunicationQualityPolicy(),
                personalizationMergePolicy ?? new PersonalizationMergePolicy(),
                localFirstBenchmarkPolicy ?? new LocalFirstBenchmarkPolicy(),
                interactionStateAnalyzer ?? new InteractionStateAnalyzer(),
                interactionPolicyProjector ?? new InteractionPolicyProjector()),
            new TurnReasoningSelectionStep(reasoningEffortPolicy ?? new ReasoningEffortPolicy()),
            new TurnLatencyBudgetStep(latencyBudgetPolicy ?? new LatencyBudgetPolicy()),
            new TurnLiveWebDecisionStep(liveWebRequirementPolicy ?? new LiveWebRequirementPolicy()),
            new TurnAmbiguityResolutionStep(
                ambiguityDetector,
                clarificationPolicy,
                clarificationQualityPolicy ?? new ClarificationQualityPolicy(),
                assumptionCheckPolicy ?? new AssumptionCheckPolicy()),
            new TurnIntentOverrideStep(clarificationPolicy),
            stageMetrics)
    {
    }

    public ChatTurnPlanner(
        TurnIntentAnalysisStep intentAnalysisStep,
        TurnPersonalizationStep personalizationStep,
        TurnReasoningSelectionStep reasoningSelectionStep,
        TurnLatencyBudgetStep latencyBudgetStep,
        TurnLiveWebDecisionStep liveWebDecisionStep,
        TurnAmbiguityResolutionStep ambiguityResolutionStep,
        TurnIntentOverrideStep intentOverrideStep,
        IConversationStageMetricsService? stageMetrics = null)
    {
        _intentAnalysisStep = intentAnalysisStep;
        _personalizationStep = personalizationStep;
        _reasoningSelectionStep = reasoningSelectionStep;
        _latencyBudgetStep = latencyBudgetStep;
        _liveWebDecisionStep = liveWebDecisionStep;
        _ambiguityResolutionStep = ambiguityResolutionStep;
        _intentOverrideStep = intentOverrideStep;
        _stageMetrics = stageMetrics;
    }

    public async Task PlanAsync(ChatTurnContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var planningContext = new TurnPlanningContext(context);
        var planningState = new TurnPlanningState();

        var classifyTimer = Stopwatch.StartNew();
        await _intentAnalysisStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        _stageMetrics?.Record("classify", classifyTimer.ElapsedMilliseconds, success: true);

        var planTimer = Stopwatch.StartNew();
        await _personalizationStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        if (!planningState.StopRequested)
        {
            await _reasoningSelectionStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        }

        if (!planningState.StopRequested)
        {
            await _latencyBudgetStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        }

        if (!planningState.StopRequested)
        {
            await _liveWebDecisionStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        }

        if (!planningState.StopRequested)
        {
            await _ambiguityResolutionStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        }

        if (!planningState.StopRequested)
        {
            await _intentOverrideStep.ExecuteAsync(planningContext, planningState, ct).ConfigureAwait(false);
        }

        _stageMetrics?.Record("plan", planTimer.ElapsedMilliseconds, success: true);
    }
}

