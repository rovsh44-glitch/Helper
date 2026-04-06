using Helper.Api.Conversation.InteractionState;

namespace Helper.Api.Conversation;

public sealed class TurnPersonalizationStep
{
    private readonly IUserProfileService _userProfileService;
    private readonly ITurnLanguageResolver _turnLanguageResolver;
    private readonly ICollaborationIntentDetector _collaborationIntentDetector;
    private readonly ICommunicationQualityPolicy _communicationQualityPolicy;
    private readonly IPersonalizationMergePolicy _personalizationMergePolicy;
    private readonly ILocalFirstBenchmarkPolicy _localFirstBenchmarkPolicy;
    private readonly IInteractionStateAnalyzer _interactionStateAnalyzer;
    private readonly IInteractionPolicyProjector _interactionPolicyProjector;

    public TurnPersonalizationStep(
        IUserProfileService userProfileService,
        ITurnLanguageResolver turnLanguageResolver,
        ICollaborationIntentDetector collaborationIntentDetector,
        ICommunicationQualityPolicy communicationQualityPolicy,
        IPersonalizationMergePolicy personalizationMergePolicy,
        ILocalFirstBenchmarkPolicy localFirstBenchmarkPolicy,
        IInteractionStateAnalyzer interactionStateAnalyzer,
        IInteractionPolicyProjector interactionPolicyProjector)
    {
        _userProfileService = userProfileService;
        _turnLanguageResolver = turnLanguageResolver;
        _collaborationIntentDetector = collaborationIntentDetector;
        _communicationQualityPolicy = communicationQualityPolicy;
        _personalizationMergePolicy = personalizationMergePolicy;
        _localFirstBenchmarkPolicy = localFirstBenchmarkPolicy;
        _interactionStateAnalyzer = interactionStateAnalyzer;
        _interactionPolicyProjector = interactionPolicyProjector;
    }

    public Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        _ = ct;
        var turn = planningContext.Turn;
        planningState.Profile = _userProfileService.Resolve(turn.Conversation);
        planningState.Personalization = _personalizationMergePolicy.Resolve(turn.Conversation, turn);
        turn.ResolvedTurnLanguage = _turnLanguageResolver.Resolve(planningState.Profile, planningContext.TrimmedMessage, turn.History);
        turn.CollaborationIntent = _collaborationIntentDetector.Analyze(planningContext.TrimmedMessage, turn.ResolvedTurnLanguage);
        turn.IntentSignals.AddRange(turn.CollaborationIntent.Signals.Select(signal => $"collaboration:{signal}"));
        turn.CommunicationQualitySnapshot = _communicationQualityPolicy.GetSnapshot(turn.Conversation);
        turn.InteractionState = _interactionStateAnalyzer.Analyze(turn);
        turn.InteractionPolicy = _interactionPolicyProjector.Project(turn, turn.InteractionState);
        if (turn.InteractionPolicy.PreferAnswerFirst)
        {
            turn.IntentSignals.Add("interaction:prefer_answer_first");
        }

        if (turn.InteractionPolicy.CompressStructure)
        {
            turn.IntentSignals.Add("interaction:compress_structure");
        }
        turn.ActiveProjectId = turn.Conversation.ProjectContext?.ProjectId;
        planningState.BenchmarkDecision = _localFirstBenchmarkPolicy.Evaluate(turn.Request, turn.ResolvedTurnLanguage);
        planningState.PriorClarificationTurns = TurnPlanningRules.GetPriorClarificationTurns(turn.Conversation);
        turn.IsLocalFirstBenchmarkTurn = planningState.BenchmarkDecision.IsBenchmark;
        turn.LocalFirstBenchmarkMode = planningState.BenchmarkDecision.Mode;
        turn.RequireExplicitBenchmarkUncertainty = planningState.BenchmarkDecision.RequireExplicitUncertainty;
        turn.IsFactualPrompt = TurnPlanningRules.IsLikelyFactualPrompt(planningContext.TrimmedMessage);
        return Task.CompletedTask;
    }
}
