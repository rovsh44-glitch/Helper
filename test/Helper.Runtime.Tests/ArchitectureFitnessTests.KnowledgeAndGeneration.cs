namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void HelperCoreContracts_AreSplitAcrossBoundedFiles()
    {
        var legacyPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Contracts.cs");
        var boundedRoot = ResolveWorkspaceFile("src", "Helper.Runtime", "Core", "Contracts");
        var boundedFiles = Directory.GetFiles(boundedRoot, "*.cs", SearchOption.TopDirectoryOnly);

        Assert.False(File.Exists(legacyPath), "Legacy Contracts.cs tombstone should be removed from src.");
        Assert.True(boundedFiles.Length >= 8, "Core contracts should be split across multiple bounded files.");
        Assert.All(boundedFiles, file =>
        {
            var lines = File.ReadAllLines(file).Length;
            Assert.True(lines <= 260, $"{Path.GetFileName(file)} grew too large: {lines} lines.");
        });
    }

    [Fact]
    public void RetrievalDomainKnowledge_RemainsCentralized()
    {
        var catalogPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RetrievalDomainProfileCatalog.cs");
        var contextPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "ContextAssemblyService.cs");
        var routerPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RetrievalCollectionRouter.cs");
        var profileStorePath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RetrievalCollectionProfileStore.cs");
        var routingPolicyPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RetrievalCollectionRoutingPolicy.cs");
        var expansionPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RetrievalContextExpansionService.cs");
        var rerankPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RerankingService.cs");
        var rerankPolicyPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RerankingPolicy.cs");
        var rerankQueryModelPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RerankingQueryModel.cs");
        var rerankCandidateScorerPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RerankingCandidateScorer.cs");
        var rerankDomainPolicyPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RerankingDomainIntentPolicy.cs");
        var rerankSelectionPath = ResolveWorkspaceFile("src", "Helper.Runtime.Knowledge", "Retrieval", "RerankingSelectionPolicy.cs");
        var context = File.ReadAllText(contextPath);
        var router = File.ReadAllText(routerPath);
        var profileStore = File.ReadAllText(profileStorePath);
        var routingPolicy = File.ReadAllText(routingPolicyPath);
        var rerank = File.ReadAllText(rerankPath);
        var rerankPolicy = File.ReadAllText(rerankPolicyPath);
        var rerankCandidateScorer = File.ReadAllText(rerankCandidateScorerPath);
        var rerankDomainPolicy = File.ReadAllText(rerankDomainPolicyPath);
        var contextLines = File.ReadAllLines(contextPath).Length;
        var rerankPolicyLines = File.ReadAllLines(rerankPolicyPath).Length;

        Assert.True(File.Exists(catalogPath));
        Assert.True(File.Exists(expansionPath));
        Assert.True(File.Exists(rerankQueryModelPath));
        Assert.True(File.Exists(rerankCandidateScorerPath));
        Assert.True(File.Exists(rerankDomainPolicyPath));
        Assert.True(File.Exists(rerankSelectionPath));
        Assert.True(contextLines <= 140, $"ContextAssemblyService should stay thin, actual lines: {contextLines}.");
        Assert.True(rerankPolicyLines <= 120, $"RerankingPolicy should stay thin, actual lines: {rerankPolicyLines}.");
        Assert.Contains("RetrievalCollectionRouter", context, StringComparison.Ordinal);
        Assert.Contains("RetrievalContextExpansionService", context, StringComparison.Ordinal);
        Assert.Contains("RerankingPolicy.Rerank", rerank, StringComparison.Ordinal);
        Assert.Contains("RetrievalCollectionRoutingPolicy.ScoreCollection", router, StringComparison.Ordinal);
        Assert.Contains("RetrievalDomainProfileCatalog", profileStore, StringComparison.Ordinal);
        Assert.Contains("RetrievalDomainProfileCatalog", routingPolicy, StringComparison.Ordinal);
        Assert.Contains("RerankingCandidateScorer", rerankPolicy, StringComparison.Ordinal);
        Assert.Contains("RerankingSelectionPolicy", rerankPolicy, StringComparison.Ordinal);
        Assert.Contains("RetrievalDomainProfileCatalog", rerankDomainPolicy, StringComparison.Ordinal);
        Assert.Contains("RerankingDomainIntentPolicy", rerankCandidateScorer, StringComparison.Ordinal);
        Assert.DoesNotContain("new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)", context, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileGateRepairPipeline_IsSplitIntoBoundedRepairSets()
    {
        var servicePath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateRepairService.cs");
        var projectRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateProjectRepairSet.cs");
        var symbolRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateSymbolRecoveryRepairSet.cs");
        var typeRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateTypeContractRepairSet.cs");
        var overrideRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateOverrideRepairSet.cs");
        var memberContractRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateMemberContractRepairSet.cs");
        var xamlBindingRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateXamlBindingRepairSet.cs");
        var constructorRepairPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateConstructorRepairSet.cs");
        var diagnosticsPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateRepairDiagnostics.cs");
        var syntaxHelpersPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateRepairSyntaxHelpers.cs");
        var patternsPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateRepairPatterns.cs");
        var service = File.ReadAllText(servicePath);
        var typeRepair = File.ReadAllText(typeRepairPath);
        var serviceLines = File.ReadAllLines(servicePath).Length;
        var typeRepairLines = File.ReadAllLines(typeRepairPath).Length;

        Assert.True(File.Exists(projectRepairPath));
        Assert.True(File.Exists(symbolRepairPath));
        Assert.True(File.Exists(typeRepairPath));
        Assert.True(File.Exists(overrideRepairPath));
        Assert.True(File.Exists(memberContractRepairPath));
        Assert.True(File.Exists(xamlBindingRepairPath));
        Assert.True(File.Exists(constructorRepairPath));
        Assert.True(File.Exists(diagnosticsPath));
        Assert.True(File.Exists(syntaxHelpersPath));
        Assert.True(File.Exists(patternsPath));
        Assert.True(serviceLines <= 160, $"CompileGateRepairService should stay thin, actual lines: {serviceLines}.");
        Assert.True(typeRepairLines <= 120, $"CompileGateTypeContractRepairSet should stay thin, actual lines: {typeRepairLines}.");
        Assert.Contains("CompileGateProjectRepairSet", service, StringComparison.Ordinal);
        Assert.Contains("CompileGateSymbolRecoveryRepairSet", service, StringComparison.Ordinal);
        Assert.Contains("CompileGateTypeContractRepairSet", service, StringComparison.Ordinal);
        Assert.Contains("CompileGateOverrideRepairSet", typeRepair, StringComparison.Ordinal);
        Assert.Contains("CompileGateMemberContractRepairSet", typeRepair, StringComparison.Ordinal);
        Assert.Contains("CompileGateXamlBindingRepairSet", typeRepair, StringComparison.Ordinal);
        Assert.Contains("CompileGateConstructorRepairSet", typeRepair, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.CodeAnalysis.CSharp", service, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationFallbackDebt_IsCentralized()
    {
        var registryPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "GenerationFallbackRegistry.cs");
        var policyPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "GenerationFallbackPolicy.cs");
        var scannerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "GeneratedArtifactPlaceholderScanner.cs");
        var guardPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "MethodBodySemanticGuard.cs");
        var normalizerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "MethodSignatureNormalizer.cs");
        var validatorPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "MethodSignatureValidator.cs");
        var extractorPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TypeTokenExtractor.cs");
        var assemblerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Swarm", "Agents", "ZuunAssembler.cs");
        var probeBuilderPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "GenerationSyntaxProbeBuilder.cs");

        Assert.True(File.Exists(registryPath));
        Assert.True(File.Exists(probeBuilderPath));
        Assert.Contains("GenerationFallbackRegistry", File.ReadAllText(policyPath), StringComparison.Ordinal);
        Assert.Contains("GenerationFallbackRegistry", File.ReadAllText(scannerPath), StringComparison.Ordinal);
        Assert.Contains("GenerationFallbackRegistry", File.ReadAllText(guardPath), StringComparison.Ordinal);
        Assert.Contains("GenerationFallbackRegistry", File.ReadAllText(normalizerPath), StringComparison.Ordinal);
        Assert.Contains("GenerationFallbackRegistry", File.ReadAllText(assemblerPath), StringComparison.Ordinal);
        Assert.Contains("GenerationSyntaxProbeBuilder", File.ReadAllText(normalizerPath), StringComparison.Ordinal);
        Assert.Contains("GenerationSyntaxProbeBuilder", File.ReadAllText(validatorPath), StringComparison.Ordinal);
        Assert.Contains("GenerationSyntaxProbeBuilder", File.ReadAllText(extractorPath), StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateCertification_UsesSingleSmokeEvaluationSourceOfTruth()
    {
        var servicePath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplateCertificationService.cs");
        var runnerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplateSmokeScenarioRunner.cs");
        var service = File.ReadAllText(servicePath);
        var runner = File.ReadAllText(runnerPath);

        Assert.Contains("TemplateSmokeScenarioRunner.EvaluateAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSmokeScenariosAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveSmokeScenarioIds(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateSmokeScenario(", service, StringComparison.Ordinal);
        Assert.Contains("EvaluateAsync", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeAndGenerationCoordinators_StayBehindBoundedCollaborators()
    {
        var turnPath = ResolveWorkspaceFile("src", "Helper.Api", "Backend", "Application", "TurnOrchestrationEngine.cs");
        var stageRunnerPath = ResolveWorkspaceFile("src", "Helper.Api", "Backend", "Application", "TurnExecutionStageRunner.cs");
        var responseWriterPath = ResolveWorkspaceFile("src", "Helper.Api", "Backend", "Application", "TurnResponseWriter.cs");
        var telemetryRecorderPath = ResolveWorkspaceFile("src", "Helper.Api", "Backend", "Application", "TurnRouteTelemetryRecorder.cs");
        var compileGatePath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "GenerationCompileGate.cs");
        var workspacePreparerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateWorkspacePreparer.cs");
        var formatVerifierPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "CompileGateFormatVerifier.cs");
        var smokeRunnerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplateSmokeScenarioRunner.cs");
        var smokeCatalogPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplateSmokeScenarioCatalog.cs");
        var pdfRunnerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "PdfEpubSmokeScenarioRunner.cs");
        var backfillPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "ParityDailyBackfillService.cs");
        var backfillReaderPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "ParityBackfillRunHistoryReader.cs");
        var backfillMetricsPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "ParityDailyBackfillMetricsCalculator.cs");
        var backfillWriterPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "ParityDailyBackfillReportWriter.cs");
        var promotionPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "GenerationTemplatePromotionService.cs");
        var promotionMetadataPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplatePromotionMetadataNormalizer.cs");
        var promotionVersionPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplatePromotionVersionPlanner.cs");
        var promotionScaffoldPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplatePromotionScaffoldService.cs");
        var promotionFormatPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplatePromotionFormatRunner.cs");
        var promotionVerifierPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Generation", "TemplatePostActivationVerifier.cs");

        Assert.True(File.ReadAllLines(turnPath).Length <= 520, "TurnOrchestrationEngine should stay sequencing-focused.");
        Assert.True(File.ReadAllLines(compileGatePath).Length <= 140, "GenerationCompileGate should stay bounded.");
        Assert.True(File.ReadAllLines(smokeRunnerPath).Length <= 60, "TemplateSmokeScenarioRunner should stay thin.");
        Assert.True(File.ReadAllLines(backfillPath).Length <= 340, "ParityDailyBackfillService should stay sequencing-focused.");
        Assert.True(File.ReadAllLines(promotionPath).Length <= 360, "GenerationTemplatePromotionService should stay sequencing-focused.");

        Assert.True(File.Exists(stageRunnerPath));
        Assert.True(File.Exists(responseWriterPath));
        Assert.True(File.Exists(telemetryRecorderPath));
        Assert.True(File.Exists(workspacePreparerPath));
        Assert.True(File.Exists(formatVerifierPath));
        Assert.True(File.Exists(smokeCatalogPath));
        Assert.True(File.Exists(pdfRunnerPath));
        Assert.True(File.Exists(backfillReaderPath));
        Assert.True(File.Exists(backfillMetricsPath));
        Assert.True(File.Exists(backfillWriterPath));
        Assert.True(File.Exists(promotionMetadataPath));
        Assert.True(File.Exists(promotionVersionPath));
        Assert.True(File.Exists(promotionScaffoldPath));
        Assert.True(File.Exists(promotionFormatPath));
        Assert.True(File.Exists(promotionVerifierPath));

        var turn = File.ReadAllText(turnPath);
        var compileGate = File.ReadAllText(compileGatePath);
        var backfill = File.ReadAllText(backfillPath);
        var promotion = File.ReadAllText(promotionPath);

        Assert.Contains("ITurnExecutionStageRunner", turn, StringComparison.Ordinal);
        Assert.Contains("ITurnResponseWriter", turn, StringComparison.Ordinal);
        Assert.Contains("CompileGateWorkspacePreparer", compileGate, StringComparison.Ordinal);
        Assert.Contains("CompileGateFormatVerifier", compileGate, StringComparison.Ordinal);
        Assert.Contains("ParityBackfillRunHistoryReader", backfill, StringComparison.Ordinal);
        Assert.Contains("ParityDailyBackfillMetricsCalculator", backfill, StringComparison.Ordinal);
        Assert.Contains("ParityDailyBackfillReportWriter", backfill, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionMetadataNormalizer", promotion, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionVersionPlanner", promotion, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionScaffoldService", promotion, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionFormatRunner", promotion, StringComparison.Ordinal);
        Assert.Contains("TemplatePostActivationVerifier", promotion, StringComparison.Ordinal);
        Assert.Contains("PostActivationFullRecertifyEnabled", promotion, StringComparison.Ordinal);
    }

    [Fact]
    public void TumenOrchestrator_RemainsCoordinator_OverExtractedCollaborators()
    {
        var orchestratorPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Swarm", "TumenOrchestrator.cs");
        var fileBatchPath = ResolveWorkspaceFile("src", "Helper.Runtime", "Swarm", "TumenFileBatchService.cs");
        var fileStorePath = ResolveWorkspaceFile("src", "Helper.Runtime", "Swarm", "TumenGeneratedFileStore.cs");
        var reportServicePath = ResolveWorkspaceFile("src", "Helper.Runtime", "Swarm", "TumenRunReportService.cs");
        var orchestrator = File.ReadAllText(orchestratorPath);
        var lineCount = File.ReadAllLines(orchestratorPath).Length;

        Assert.True(File.Exists(fileBatchPath));
        Assert.True(File.Exists(fileStorePath));
        Assert.True(File.Exists(reportServicePath));
        Assert.True(lineCount <= 340, $"TumenOrchestrator should stay bounded, actual lines: {lineCount}.");
        Assert.Contains("TumenRuntimeSettings", orchestrator, StringComparison.Ordinal);
        Assert.Contains("TumenFileBatchService", orchestrator, StringComparison.Ordinal);
        Assert.Contains("TumenGeneratedFileStore", orchestrator, StringComparison.Ordinal);
        Assert.Contains("TumenRunReportService", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedArtifactPlaceholderScanner.ScanContent", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("TumenFallbackContentBuilder.BuildFallbackFile", orchestrator, StringComparison.Ordinal);
    }
}
