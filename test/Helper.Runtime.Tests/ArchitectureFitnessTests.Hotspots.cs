namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void WebSearchSessionCoordinator_RemainsFacade_And_UsesFactoryBasedWiring()
    {
        var coordinatorPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "WebSearchSessionCoordinator.cs");
        var factoryPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "WebSearchSessionCoordinatorFactory.cs");
        var compositionRootPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "ServiceRegistrationExtensions.ResearchAndTooling.cs");
        var coordinator = File.ReadAllText(coordinatorPath);
        var compositionRoot = File.ReadAllText(compositionRootPath);
        var maxLines = ReadIntBudget("architecture", "services", "webSearchSessionCoordinator", "maxLines");
        var maxCollaborators = ReadIntBudget("architecture", "services", "webSearchSessionCoordinator", "maxCollaborators");
        var maxMembers = ReadIntBudget("architecture", "services", "webSearchSessionCoordinator", "maxMembers");

        Assert.True(File.Exists(factoryPath));
        Assert.True(File.ReadAllLines(coordinatorPath).Length <= maxLines, $"WebSearchSessionCoordinator should stay bounded, actual lines: {File.ReadAllLines(coordinatorPath).Length}.");
        Assert.True(CountReadonlyFields(coordinator) <= maxCollaborators, $"WebSearchSessionCoordinator exceeded collaborator budget: {CountReadonlyFields(coordinator)} > {maxCollaborators}.");
        Assert.True(CountMemberLikeDeclarations(coordinator) <= maxMembers, $"WebSearchSessionCoordinator exceeded member budget: {CountMemberLikeDeclarations(coordinator)} > {maxMembers}.");
        Assert.Contains("IWebQueryPlanner", coordinator, StringComparison.Ordinal);
        Assert.Contains("ISearchIterationPolicy", coordinator, StringComparison.Ordinal);
        Assert.Contains("ISearchEvidenceSufficiencyPolicy", coordinator, StringComparison.Ordinal);
        Assert.Contains("IWebSearchDocumentPipeline", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("new WebQueryPlanner()", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("new SearchIterationPolicy()", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("new SearchEvidenceSufficiencyPolicy()", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("new WebSearchDocumentPipeline(", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDocumentPipeline(", coordinator, StringComparison.Ordinal);
        Assert.Contains("WebSearchSessionCoordinatorFactory.Create(", compositionRoot, StringComparison.Ordinal);
        Assert.DoesNotContain("new WebSearchSessionCoordinator(", compositionRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_RemainsFacade_OverExtractedCollaborators()
    {
        var servicePath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ResponseComposerService.cs");
        var deduplicatorPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ResponseTextDeduplicator.cs");
        var answerShapePath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "AnswerShapePolicy.cs");
        var nextStepPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "NextStepComposer.cs");
        var localizationPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ComposerLocalizationResolver.cs");
        var benchmarkPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkResponseFormatter.cs");
        var service = File.ReadAllText(servicePath);
        var maxLines = ReadIntBudget("architecture", "services", "responseComposer", "maxLines");
        var maxCollaborators = ReadIntBudget("architecture", "services", "responseComposer", "maxCollaborators");
        var maxMembers = ReadIntBudget("architecture", "services", "responseComposer", "maxMembers");

        Assert.True(File.Exists(deduplicatorPath));
        Assert.True(File.Exists(answerShapePath));
        Assert.True(File.Exists(nextStepPath));
        Assert.True(File.Exists(localizationPath));
        Assert.True(File.Exists(benchmarkPath));
        Assert.True(File.ReadAllLines(servicePath).Length <= maxLines, $"ResponseComposerService.cs should stay bounded, actual lines: {File.ReadAllLines(servicePath).Length}.");
        Assert.True(CountReadonlyFields(service) <= maxCollaborators, $"ResponseComposerService exceeded collaborator budget: {CountReadonlyFields(service)} > {maxCollaborators}.");
        Assert.True(CountMemberLikeDeclarations(service) <= maxMembers, $"ResponseComposerService exceeded member budget: {CountMemberLikeDeclarations(service)} > {maxMembers}.");
        Assert.Contains("IResponseTextDeduplicator", service, StringComparison.Ordinal);
        Assert.Contains("IAnswerShapePolicy", service, StringComparison.Ordinal);
        Assert.Contains("INextStepComposer", service, StringComparison.Ordinal);
        Assert.Contains("IComposerLocalizationResolver", service, StringComparison.Ordinal);
        Assert.Contains("IBenchmarkResponseFormatter", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new TurnLanguageResolver()", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new DialogActPlanner()", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new ConversationVariationPolicy()", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentOperationalHotspots_StayWithin_Configured_Budgets_And_Thin_Wiring()
    {
        var programPath = ResolveWorkspaceFile("src", "Helper.Api", "Program.cs");
        var formatterPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkResponseFormatter.cs");
        var formatterStructurePolicyPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkResponseStructurePolicy.cs");
        var formatterQualityPolicyPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkDraftQualityPolicy.cs");
        var formatterTopicalExtractorPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkTopicalBodyExtractor.cs");
        var formatterAssessmentPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkResponseAssessmentWriter.cs");
        var formatterSectionRendererPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "BenchmarkResponseSectionRenderer.cs");
        var toolServicePath = ResolveWorkspaceFile("src", "Helper.Runtime", "ToolService.cs");
        var dotnetTestToolingPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Infrastructure", "DotnetTestTooling.cs");
        var researchSynthesisSupportPath = ResolveWorkspaceFile("src", "Helper.Runtime", "ResearchSynthesisSupport.cs");
        var templateCertificationServicePath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplateCertificationService.cs");
        var conversationMetricsServicePath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "ConversationMetricsService.cs");
        var webPageFetcherPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "Fetching", "WebPageFetcher.cs");
        var pipelinePath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "WebSearchDocumentPipeline.cs");
        var candidateNormalizerPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "WebSearchCandidateNormalizer.cs");
        var fetchEnricherPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "WebSearchFetchEnricher.cs");
        var fetchDiagnosticsPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "WebSearchFetchDiagnosticsSummarizer.cs");
        var postFetchSelectionPath = ResolveWorkspaceFile("src", "Helper.Runtime.WebResearch", "PostFetchSelectionPolicy.cs");
        var builderHookPath = ResolveWorkspaceFile("hooks", "useBuilderWorkspaceSession.ts");
        var builderLaunchHandoffPath = ResolveWorkspaceFile("hooks", "useBuilderLaunchHandoff.ts");
        var builderFileSelectionPath = ResolveWorkspaceFile("hooks", "useBuilderFileSelection.ts");
        var builderMutationFlowPath = ResolveWorkspaceFile("hooks", "useBuilderMutationFlow.ts");
        var builderWorkspaceRefreshPath = ResolveWorkspaceFile("hooks", "useBuilderWorkspaceRefresh.ts");
        var builderNodeSheetFlowPath = ResolveWorkspaceFile("hooks", "useBuilderNodeSheetFlow.ts");
        var registrationPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "ServiceRegistrationExtensions.ResearchAndTooling.cs");

        var program = File.ReadAllText(programPath);
        var formatter = File.ReadAllText(formatterPath);
        var toolService = File.ReadAllText(toolServicePath);
        var webPageFetcher = File.ReadAllText(webPageFetcherPath);
        var pipeline = File.ReadAllText(pipelinePath);
        var registration = File.ReadAllText(registrationPath);

        Assert.True(File.ReadAllLines(programPath).Length <= ReadIntBudget("architecture", "services", "apiProgram", "maxLines"), $"Program.cs exceeded budget: {File.ReadAllLines(programPath).Length} lines.");
        Assert.True(File.Exists(formatterStructurePolicyPath));
        Assert.True(File.Exists(formatterQualityPolicyPath));
        Assert.True(File.Exists(formatterTopicalExtractorPath));
        Assert.True(File.Exists(formatterAssessmentPath));
        Assert.True(File.Exists(formatterSectionRendererPath));
        Assert.True(File.ReadAllLines(formatterPath).Length <= ReadIntBudget("architecture", "services", "benchmarkResponseFormatter", "maxLines"), $"BenchmarkResponseFormatter exceeded budget: {File.ReadAllLines(formatterPath).Length} lines.");
        Assert.True(CountMemberLikeDeclarations(formatter) <= ReadIntBudget("architecture", "services", "benchmarkResponseFormatter", "maxMembers"), $"BenchmarkResponseFormatter exceeded member budget: {CountMemberLikeDeclarations(formatter)}.");
        Assert.True(File.ReadAllLines(toolServicePath).Length <= ReadIntBudget("architecture", "services", "toolService", "maxLines"), $"ToolService exceeded budget: {File.ReadAllLines(toolServicePath).Length} lines.");
        Assert.True(CountReadonlyFields(toolService) <= ReadIntBudget("architecture", "services", "toolService", "maxCollaborators"), $"ToolService exceeded collaborator budget: {CountReadonlyFields(toolService)}.");
        Assert.True(CountMemberLikeDeclarations(toolService) <= ReadIntBudget("architecture", "services", "toolService", "maxMembers"), $"ToolService exceeded member budget: {CountMemberLikeDeclarations(toolService)}.");
        Assert.True(File.ReadAllLines(dotnetTestToolingPath).Length <= ReadIntBudget("architecture", "services", "dotnetTestTooling", "maxLines"), $"DotnetTestTooling exceeded budget: {File.ReadAllLines(dotnetTestToolingPath).Length} lines.");
        Assert.True(File.ReadAllLines(researchSynthesisSupportPath).Length <= ReadIntBudget("architecture", "services", "researchSynthesisSupport", "maxLines"), $"ResearchSynthesisSupport exceeded budget: {File.ReadAllLines(researchSynthesisSupportPath).Length} lines.");
        Assert.True(File.ReadAllLines(templateCertificationServicePath).Length <= ReadIntBudget("architecture", "services", "templateCertificationService", "maxLines"), $"TemplateCertificationService exceeded budget: {File.ReadAllLines(templateCertificationServicePath).Length} lines.");
        Assert.True(File.ReadAllLines(conversationMetricsServicePath).Length <= ReadIntBudget("architecture", "services", "conversationMetricsService", "maxLines"), $"ConversationMetricsService exceeded budget: {File.ReadAllLines(conversationMetricsServicePath).Length} lines.");
        Assert.True(File.Exists(candidateNormalizerPath));
        Assert.True(File.Exists(fetchEnricherPath));
        Assert.True(File.Exists(fetchDiagnosticsPath));
        Assert.True(File.Exists(postFetchSelectionPath));
        Assert.True(File.ReadAllLines(webPageFetcherPath).Length <= ReadIntBudget("architecture", "services", "webPageFetcher", "maxLines"), $"WebPageFetcher exceeded budget: {File.ReadAllLines(webPageFetcherPath).Length} lines.");
        Assert.True(CountReadonlyFields(webPageFetcher) <= ReadIntBudget("architecture", "services", "webPageFetcher", "maxCollaborators"), $"WebPageFetcher exceeded collaborator budget: {CountReadonlyFields(webPageFetcher)}.");
        Assert.True(CountMemberLikeDeclarations(webPageFetcher) <= ReadIntBudget("architecture", "services", "webPageFetcher", "maxMembers"), $"WebPageFetcher exceeded member budget: {CountMemberLikeDeclarations(webPageFetcher)}.");
        Assert.True(File.ReadAllLines(pipelinePath).Length <= ReadIntBudget("architecture", "services", "webSearchDocumentPipeline", "maxLines"), $"WebSearchDocumentPipeline exceeded budget: {File.ReadAllLines(pipelinePath).Length} lines.");
        Assert.True(CountReadonlyFields(pipeline) <= ReadIntBudget("architecture", "services", "webSearchDocumentPipeline", "maxCollaborators"), $"WebSearchDocumentPipeline exceeded collaborator budget: {CountReadonlyFields(pipeline)}.");
        Assert.True(CountMemberLikeDeclarations(pipeline) <= ReadIntBudget("architecture", "services", "webSearchDocumentPipeline", "maxMembers"), $"WebSearchDocumentPipeline exceeded member budget: {CountMemberLikeDeclarations(pipeline)}.");
        Assert.True(File.ReadAllLines(builderHookPath).Length <= ReadIntBudget("frontend", "maxBuilderWorkspaceSessionLines"), $"useBuilderWorkspaceSession.ts exceeded budget: {File.ReadAllLines(builderHookPath).Length} lines.");
        Assert.True(File.Exists(builderLaunchHandoffPath));
        Assert.True(File.Exists(builderFileSelectionPath));
        Assert.True(File.Exists(builderMutationFlowPath));
        Assert.True(File.Exists(builderWorkspaceRefreshPath));
        Assert.True(File.Exists(builderNodeSheetFlowPath));

        Assert.Contains("ApiStartupCommands.TryHandleConfigInventoryCommand", program, StringComparison.Ordinal);
        Assert.Contains("ApiBindingPlanResolver.Resolve(", program, StringComparison.Ordinal);
        Assert.Contains("ApiPortFileWriter.TryWrite", program, StringComparison.Ordinal);

        Assert.Contains("IBenchmarkResponseStructurePolicy", formatter, StringComparison.Ordinal);
        Assert.Contains("IBenchmarkDraftQualityPolicy", formatter, StringComparison.Ordinal);
        Assert.Contains("IBenchmarkTopicalBodyExtractor", formatter, StringComparison.Ordinal);
        Assert.Contains("IBenchmarkResponseSectionRenderer", formatter, StringComparison.Ordinal);
        Assert.Contains("IWebSearchCandidateNormalizer", pipeline, StringComparison.Ordinal);
        Assert.Contains("IWebSearchFetchEnricher", pipeline, StringComparison.Ordinal);
        Assert.Contains("IPostFetchSelectionPolicy", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("new ExtensionRegistry()", toolService, StringComparison.Ordinal);
        Assert.DoesNotContain("?? new ", webPageFetcher, StringComparison.Ordinal);
        Assert.Contains("WebPageFetcherFactory.Create(", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeCliAndCoreHotspots_AreSplitIntoBoundedModules()
    {
        var parserCorePath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "StructuredDocumentParsers.Textual.cs");
        var parserVisionPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "StructuredPdfParser.Vision.cs");
        var parserSupportPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "StructuredParserSupport.cs");
        var parserHtmlPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "StructuredParserUtilities.Html.cs");
        var parserVisionSupportPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "StructuredParserUtilities.Vision.cs");
        var parserBasePath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "StructuredDocumentParserBase.cs");
        var aiLinkPath = ResolveWorkspaceFile("src", "Helper.Runtime", "AILink.cs");
        var aiLinkChatPath = ResolveWorkspaceFile("src", "Helper.Runtime", "AILink.Chat.cs");
        var aiLinkEmbeddingsPath = ResolveWorkspaceFile("src", "Helper.Runtime", "AILink.Embeddings.cs");
        var aiLinkHttpPath = ResolveWorkspaceFile("src", "Helper.Runtime", "AILink.Http.cs");
        var orchestratorPath = ResolveWorkspaceFile("src", "Helper.Runtime", "HelperOrchestrator.cs");
        var orchestratorReportsPath = ResolveWorkspaceFile("src", "Helper.Runtime", "HelperOrchestrator.RunReports.cs");
        var orchestratorTelemetryPath = ResolveWorkspaceFile("src", "Helper.Runtime", "HelperOrchestrator.RouteTelemetry.cs");
        var storePath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InMemoryConversationStore.cs");
        var storeBranchesPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InMemoryConversationStore.Branches.cs");
        var storePersistencePath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InMemoryConversationStore.Persistence.cs");
        var programPath = ResolveWorkspaceFile("src", "Helper.Runtime.Cli", "Program.cs");
        var runtimeBuilderPath = ResolveWorkspaceFile("src", "Helper.Runtime.Cli", "HelperCliRuntimeBuilder.cs");
        var dispatcherPath = ResolveWorkspaceFile("src", "Helper.Runtime.Cli", "HelperCliCommandDispatcher.cs");
        var templateDispatcherPath = ResolveWorkspaceFile("src", "Helper.Runtime.Cli", "HelperCliCommandDispatcher.Templates.cs");
        var certificationDispatcherPath = ResolveWorkspaceFile("src", "Helper.Runtime.Cli", "HelperCliCommandDispatcher.Certification.cs");

        Assert.True(File.Exists(parserVisionPath));
        Assert.True(File.Exists(parserHtmlPath));
        Assert.True(File.Exists(parserVisionSupportPath));
        Assert.True(File.Exists(parserBasePath));
        Assert.True(File.Exists(aiLinkChatPath));
        Assert.True(File.Exists(aiLinkEmbeddingsPath));
        Assert.True(File.Exists(aiLinkHttpPath));
        Assert.True(File.Exists(orchestratorReportsPath));
        Assert.True(File.Exists(orchestratorTelemetryPath));
        Assert.True(File.Exists(storeBranchesPath));
        Assert.True(File.Exists(storePersistencePath));
        Assert.True(File.Exists(runtimeBuilderPath));
        Assert.True(File.Exists(dispatcherPath));
        Assert.True(File.Exists(templateDispatcherPath));
        Assert.True(File.Exists(certificationDispatcherPath));

        Assert.True(File.ReadAllLines(parserCorePath).Length <= 280, "StructuredDocumentParsers.Textual.cs should stay bounded.");
        Assert.True(File.ReadAllLines(parserVisionPath).Length <= 330, "StructuredPdfParser.Vision.cs should stay bounded.");
        Assert.True(File.ReadAllLines(parserSupportPath).Length <= 460, "StructuredParserSupport.cs should stay bounded.");
        Assert.True(File.ReadAllLines(aiLinkPath).Length <= 260, "AILink.cs should stay bounded.");
        Assert.True(File.ReadAllLines(orchestratorPath).Length <= 420, "HelperOrchestrator.cs should stay bounded.");
        Assert.True(File.ReadAllLines(storePath).Length <= 220, "InMemoryConversationStore.cs should stay bounded.");
        Assert.True(File.ReadAllLines(programPath).Length <= 80, "CLI Program.cs should stay minimal.");
        Assert.True(File.ReadAllLines(runtimeBuilderPath).Length <= 320, "CLI runtime builder should stay bounded.");
        Assert.True(File.ReadAllLines(dispatcherPath).Length <= 180, "CLI dispatcher root should stay bounded.");
        Assert.True(File.ReadAllLines(templateDispatcherPath).Length <= 300, "CLI template dispatcher should stay bounded.");
        Assert.True(File.ReadAllLines(certificationDispatcherPath).Length <= 260, "CLI certification dispatcher should stay bounded.");

        var parserCore = File.ReadAllText(parserCorePath);
        var parserSupport = File.ReadAllText(parserSupportPath);
        var aiLink = File.ReadAllText(aiLinkPath);
        var orchestrator = File.ReadAllText(orchestratorPath);
        var store = File.ReadAllText(storePath);
        var program = File.ReadAllText(programPath);

        Assert.DoesNotContain("StructuredMarkdownParser", parserCore, StringComparison.Ordinal);
        Assert.Contains("partial class StructuredPdfParser", parserCore, StringComparison.Ordinal);
        Assert.Contains("partial class StructuredParserUtilities", parserSupport, StringComparison.Ordinal);
        Assert.Contains("public partial class AILink", aiLink, StringComparison.Ordinal);
        Assert.Contains("public partial class HelperOrchestrator", orchestrator, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class InMemoryConversationStore", store, StringComparison.Ordinal);
        Assert.Contains("HelperCliRuntimeBuilder.BuildAsync", program, StringComparison.Ordinal);
        Assert.Contains("HelperCliCommandDispatcher.TryHandleAsync", program, StringComparison.Ordinal);
    }
}
