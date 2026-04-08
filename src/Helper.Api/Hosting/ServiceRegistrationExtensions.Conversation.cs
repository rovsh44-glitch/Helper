using Helper.Api.Conversation;
using Helper.Api.Conversation.Epistemic;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Backend.Application;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.Persistence;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperConversationServices(this IServiceCollection services)
    {
        services.AddSingleton<IMemoryPriorityPolicy, MemoryPriorityPolicy>();
        services.AddSingleton<IMemoryInspectionService, MemoryInspectionService>();
        services.AddSingleton<IMemoryPolicyService, MemoryPolicyService>();
        services.AddSingleton<IConversationSummarizer, ConversationSummarizer>();
        services.AddSingleton<IConversationPersistenceEngine>(sp =>
        {
            var persistencePath = sp.GetRequiredService<IBackendOptionsCatalog>().Persistence.StorePath;
            return new FileConversationPersistence(
                persistencePath,
                sp.GetRequiredService<IConversationStageMetricsService>());
        });
        services.AddSingleton<InMemoryConversationStore>(sp => new InMemoryConversationStore(
            sp.GetRequiredService<IMemoryPolicyService>(),
            sp.GetRequiredService<IConversationSummarizer>(),
            sp.GetRequiredService<IConversationPersistenceEngine>(),
            sp.GetRequiredService<IConversationWriteBehindQueue>(),
            sp.GetRequiredService<IConversationStageMetricsService>()));
        services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemoryConversationStore>());
        services.AddSingleton<IConversationPersistenceHealth>(sp =>
            (IConversationPersistenceHealth)sp.GetRequiredService<InMemoryConversationStore>());
        services.AddSingleton<IConversationWriteBehindStore>(sp => sp.GetRequiredService<InMemoryConversationStore>());
        services.AddSingleton<IEvalCorpusLoader, EvalCorpusLoader>();
        services.AddSingleton<IEvalRunnerV2, EvalRunnerV2>();
        services.AddSingleton<IHumanLikeCommunicationEvalRubricLoader, HumanLikeCommunicationEvalRubricLoader>();
        services.AddSingleton<IHumanLikeCommunicationEvalReportWriter, HumanLikeCommunicationEvalReportWriter>();
        services.AddSingleton<IHumanLikeCommunicationEvalService, HumanLikeCommunicationEvalService>();
        services.AddSingleton<IWebResearchParityEvalRubricLoader, WebResearchParityEvalRubricLoader>();
        services.AddSingleton<IWebResearchParityEvalReportWriter, WebResearchParityEvalReportWriter>();
        services.AddSingleton<IWebResearchParityEvalService, WebResearchParityEvalService>();
        services.AddSingleton<WebEvidenceCache>();
        services.AddSingleton<IFreshnessWindowPolicy, FreshnessWindowPolicy>();
        services.AddSingleton<IEvidenceRefreshPolicy, EvidenceRefreshPolicy>();
        services.AddSingleton<IStaleEvidenceDisclosurePolicy, StaleEvidenceDisclosurePolicy>();
        services.AddSingleton<IShortHorizonResearchCache>(sp => new ShortHorizonResearchCache(sp.GetRequiredService<WebEvidenceCache>()));
        services.AddSingleton<ISelectiveEvidenceMemoryPolicy, SelectiveEvidenceMemoryPolicy>();
        services.AddSingleton<ISelectiveEvidenceMemoryStore, SelectiveEvidenceMemoryStore>();
        services.AddSingleton<IEvidenceReusePolicy, EvidenceReusePolicy>();
        services.AddSingleton<ICitationLineageStore, CitationLineageStore>();
        services.AddSingleton<IChatResiliencePolicy, ChatResiliencePolicy>();
        services.AddSingleton<IConversationStylePolicy, ConversationStylePolicy>();
        services.AddSingleton<ICollaborationIntentDetector, CollaborationIntentDetector>();
        services.AddSingleton<IClarificationQualityPolicy, ClarificationQualityPolicy>();
        services.AddSingleton<ISharedUnderstandingService, SharedUnderstandingService>();
        services.AddSingleton<IBehavioralCalibrationPolicy, BehavioralCalibrationPolicy>();
        services.AddSingleton<IEpistemicAnswerModePolicy, EpistemicAnswerModePolicy>();
        services.AddSingleton<IInteractionStateAnalyzer, InteractionStateAnalyzer>();
        services.AddSingleton<IInteractionPolicyProjector, InteractionPolicyProjector>();
        services.AddSingleton<IProjectInstructionPolicy, ProjectInstructionPolicy>();
        services.AddSingleton<IProjectMemoryBoundaryPolicy, ProjectMemoryBoundaryPolicy>();
        services.AddSingleton<IPersonalizationMergePolicy, PersonalizationMergePolicy>();
        services.AddSingleton<IConversationPromptPolicy, ConversationPromptPolicy>();
        services.AddSingleton<ICommunicationQualityPolicy, CommunicationQualityPolicy>();
        services.AddSingleton<IMisunderstandingRepairPolicy, MisunderstandingRepairPolicy>();
        services.AddSingleton<IReasoningEffortPolicy, ReasoningEffortPolicy>();
        services.AddSingleton<IDecisionExplanationProjector, DecisionExplanationProjector>();
        services.AddSingleton<IRepairClassifiers, RepairClassifiers>();
        services.AddSingleton<IProactiveTopicPolicy, ProactiveTopicPolicy>();
        services.AddSingleton<IFollowThroughScheduler, FollowThroughScheduler>();
        services.AddSingleton<IConversationFollowThroughProcessor, ConversationFollowThroughProcessor>();
        services.AddSingleton<IConversationModelCapabilityCatalog, ConversationModelCapabilityCatalog>();
        services.AddSingleton<IConversationModelSelectionPolicy, ConversationModelSelectionPolicy>();
        services.AddSingleton<IConversationContinuityCoordinator, ConversationContinuityCoordinator>();
        services.AddSingleton<ITurnLanguageResolver, TurnLanguageResolver>();
        services.AddSingleton<IDialogActPlanner, DialogActPlanner>();
        services.AddSingleton<ILiveWebRequirementPolicy, LiveWebRequirementPolicy>();
        services.AddSingleton<ILocationAwareRewritePolicy, LocationAwareRewritePolicy>();
        services.AddSingleton<IVoiceSearchRewritePolicy, VoiceSearchRewritePolicy>();
        services.AddSingleton<IPreferenceAwareRewritePolicy, PreferenceAwareRewritePolicy>();
        services.AddSingleton<IConversationWebQueryPlanner, WebQueryPlanner>();
        services.AddSingleton<IConversationVariationPolicy, ConversationVariationPolicy>();
        services.AddSingleton<IResponseTextDeduplicator, ResponseTextDeduplicator>();
        services.AddSingleton<IAnswerShapePolicy, AnswerShapePolicy>();
        services.AddSingleton<INextStepComposer, NextStepComposer>();
        services.AddSingleton<IComposerLocalizationResolver, ComposerLocalizationResolver>();
        services.AddSingleton<IBenchmarkResponseStructurePolicy, BenchmarkResponseStructurePolicy>();
        services.AddSingleton<IBenchmarkDraftQualityPolicy, BenchmarkDraftQualityPolicy>();
        services.AddSingleton<IBenchmarkTopicalBodyExtractor, BenchmarkTopicalBodyExtractor>();
        services.AddSingleton<IBenchmarkResponseAssessmentWriter, BenchmarkResponseAssessmentWriter>();
        services.AddSingleton<IBenchmarkResponseSectionRenderer, BenchmarkResponseSectionRenderer>();
        services.AddSingleton<IBenchmarkResponseFormatter, BenchmarkResponseFormatter>();
        services.AddSingleton<IUserProfileService, UserProfileService>();
        services.AddSingleton<IIntentClassifier, HybridIntentClassifier>();
        services.AddSingleton<IAmbiguityDetector, HybridAmbiguityDetector>();
        services.AddSingleton<IClarificationPolicy, ClarificationPolicy>();
        services.AddSingleton<IAssumptionCheckPolicy, AssumptionCheckPolicy>();
        services.AddSingleton<ILatencyBudgetPolicy, LatencyBudgetPolicy>();
        services.AddSingleton<IPostTurnAuditQueue, PostTurnAuditQueue>();
        services.AddSingleton<IInputRiskScanner, InputRiskScannerV2>();
        services.AddSingleton<IOutputExfiltrationGuard, OutputExfiltrationGuardV2>();
        services.AddSingleton<IResponseComposerService>(sp => new ResponseComposerService(
            sp.GetRequiredService<IDialogActPlanner>(),
                sp.GetRequiredService<IConversationVariationPolicy>(),
                sp.GetRequiredService<IResponseTextDeduplicator>(),
                sp.GetRequiredService<IAnswerShapePolicy>(),
                sp.GetRequiredService<INextStepComposer>(),
                sp.GetRequiredService<IComposerLocalizationResolver>(),
                sp.GetRequiredService<IBenchmarkResponseFormatter>()));
        services.AddSingleton<IConversationStyleTelemetryAnalyzer, ConversationStyleTelemetryAnalyzer>();
        services.AddSingleton<IPublisherCompliancePolicy, PublisherCompliancePolicy>();
        services.AddSingleton<IExcerptBudgetPolicy, ExcerptBudgetPolicy>();
        services.AddSingleton<ICitationQuotePolicy, CitationQuotePolicy>();
        services.AddSingleton<IResearchAnswerSynthesizer, ResearchAnswerSynthesizer>();
        services.AddSingleton(sp => new ResearchGroundedSynthesisFormatter(
            sp.GetRequiredService<IResearchAnswerSynthesizer>(),
            sp.GetRequiredService<ICitationQuotePolicy>()));
        services.AddSingleton<IWebSearchTraceProjector>(sp =>
            new WebSearchTraceProjector(sp.GetRequiredService<ICitationQuotePolicy>()));
        services.AddSingleton<IClaimExtractionService, ClaimExtractionService>();
        services.AddSingleton<IClaimSourceMatcher, ClaimSourceMatcher>();
        services.AddSingleton<ICitationProjectionService, CitationProjectionService>();
        services.AddSingleton<IEvidenceGradingService, EvidenceGradingService>();
        services.AddSingleton<ICitationGroundingService>(sp =>
            new CitationGroundingService(
                sp.GetRequiredService<IClaimExtractionService>(),
                sp.GetRequiredService<IClaimSourceMatcher>(),
                sp.GetRequiredService<IEvidenceGradingService>(),
                sp.GetRequiredService<ICitationProjectionService>(),
                sp.GetRequiredService<ResearchGroundedSynthesisFormatter>()));
        services.AddSingleton<ICriticRiskPolicy, CriticRiskPolicy>();
        services.AddSingleton<ITurnStagePolicy, TurnStagePolicy>();
        services.AddSingleton<IPostTurnAuditDeadLetterStore, PostTurnAuditDeadLetterStore>();
        services.AddSingleton<IPostTurnAuditTraceStore, PostTurnAuditTraceStore>();
        services.AddSingleton<ITurnLifecycleStateMachine, TurnLifecycleStateMachine>();
        services.AddSingleton<ITurnExecutionStateMachine, TurnExecutionStateMachine>();
        services.AddSingleton<ITurnCheckpointManager, TurnCheckpointManager>();
        services.AddSingleton<ITurnRouteTelemetryRecorder, TurnRouteTelemetryRecorder>();
        services.AddSingleton<ITurnExecutionStageRunner, TurnExecutionStageRunner>();
        services.AddSingleton<ITurnResponseWriter>(sp =>
            new TurnResponseWriter(
                sp.GetRequiredService<IConversationStore>(),
                sp.GetRequiredService<ITurnCheckpointManager>(),
                sp.GetRequiredService<IPostTurnAuditScheduler>(),
                sp.GetRequiredService<ITurnLifecycleStateMachine>(),
                sp.GetRequiredService<ITurnExecutionStateMachine>(),
                sp.GetRequiredService<ITurnRouteTelemetryRecorder>(),
                sp.GetRequiredService<IConversationStyleTelemetryAnalyzer>(),
                sp.GetRequiredService<IWebSearchTraceProjector>(),
                sp.GetRequiredService<ISharedUnderstandingService>(),
                sp.GetRequiredService<ICommunicationQualityPolicy>(),
                sp.GetRequiredService<IFollowThroughScheduler>()));
        services.AddSingleton<IConversationCommandIdempotencyStore, ConversationCommandIdempotencyStore>();
        services.AddSingleton<IConversationBranchService, ConversationBranchService>();
        services.AddSingleton<IPostTurnAuditScheduler, PostTurnAuditScheduler>();
        services.AddSingleton<ISourceNormalizationService, SourceNormalizationService>();
        services.AddSingleton<ILocalFirstBenchmarkPolicy, LocalFirstBenchmarkPolicy>();
        services.AddSingleton<IWebSearchOrchestrator, WebSearchOrchestrator>();
        services.AddSingleton<IReasoningAwareRetrievalPolicy, ReasoningAwareRetrievalPolicy>();
        services.AddSingleton<IConversationContextAssembler, ConversationContextAssembler>();
        services.AddSingleton<IReasoningOutputVerifier, JsonSchemaReasoningVerifier>();
        services.AddSingleton<IReasoningOutputVerifier, DiscreteTransformVerifier>();
        services.AddSingleton<StructuredOutputVerifier>();
        services.AddSingleton<IReasoningVerifier, ReasoningVerifier>();
        services.AddSingleton<ReasoningSelectionPolicy>();
        services.AddSingleton<IReasoningBranchExecutor, ReasoningBranchExecutor>();
        services.AddSingleton<TurnIntentAnalysisStep>();
        services.AddSingleton<TurnPersonalizationStep>();
        services.AddSingleton<TurnReasoningSelectionStep>();
        services.AddSingleton<TurnLatencyBudgetStep>();
        services.AddSingleton<TurnLiveWebDecisionStep>();
        services.AddSingleton<TurnAmbiguityResolutionStep>();
        services.AddSingleton<TurnIntentOverrideStep>();
        services.AddSingleton<IChatTurnPlanner, ChatTurnPlanner>();
        services.AddSingleton<IChatTurnExecutor, ChatTurnExecutor>();
        services.AddSingleton<IChatTurnCritic, ChatTurnCritic>();
        services.AddSingleton<IChatTurnFinalizer, ChatTurnFinalizer>();
        services.AddSingleton<ITurnOrchestrationEngine, TurnOrchestrationEngine>();
        services.AddSingleton<IConversationCommandDispatcher, ConversationCommandDispatcher>();
        services.AddSingleton<IChatOrchestrator, ChatOrchestrator>();

        return services;
    }
}

