using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime;
using Helper.Runtime.Core;
using Helper.Runtime.Evolution;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge;
using Helper.Runtime.Knowledge.Chunking;
using Helper.Runtime.Swarm;

internal static class HelperCliRuntimeBuilder
{
    public static async Task<HelperCliRuntime> BuildAsync(string helperRoot, CancellationToken ct)
    {
        var ai = new AILink();
        await ai.DiscoverModelsAsync(ct);

        var memory = new QdrantStore();
        var sanitizer = new CodeSanitizer();
        var searcher = new WebSearcher();
        var dotnetService = new DotnetService();
        var buildExecutor = new LocalBuildExecutor(dotnetService);
        var validator = new MultiLanguageValidator(buildExecutor);
        var forgeArtifactValidator = new ForgeArtifactValidator();
        var health = new HealthMonitor();
        var scanner = new SystemScanner();
        var goalManager = new GoalManager();
        var strategy = new StrategicPlanner(ai);
        var inspector = new VisualInspector(ai);
        var complexity = new ComplexityAnalyzer(ai, health);
        var distiller = new ContextDistiller();
        var extensionRegistry = new ExtensionRegistry();
        var mcpProxy = new McpProxyService();
        var nodeManager = new SwarmNodeManager(scanner);
        var processGuard = new ProcessGuard(ai);
        var fileGuard = new FileSystemGuard();
        var toolAudit = new ToolAuditService();
        var toolPermit = new ToolPermitService(extensionRegistry);
        var identifierSanitizer = new IdentifierSanitizer();
        var generationPathSanitizer = new GenerationPathSanitizer();
        var signatureValidator = new MethodSignatureValidator();
        var signatureNormalizer = new MethodSignatureNormalizer(signatureValidator);
        var typeTokenExtractor = new TypeTokenExtractor();
        var usingInference = new UsingInferenceService(typeTokenExtractor);
        var methodBodySemanticGuard = new MethodBodySemanticGuard();
        var schemaValidator = new BlueprintJsonSchemaValidator();
        var blueprintContractValidator = new BlueprintContractValidator(generationPathSanitizer, identifierSanitizer, signatureNormalizer);
        var generatedFileAstValidator = new GeneratedFileAstValidator();
        var generationPathPolicy = new GenerationPathPolicy(identifierSanitizer);
        var compileGateRepairService = new CompileGateRepairService(usingInference, methodBodySemanticGuard);
        var generationCompileGate = new GenerationCompileGate(dotnetService, compileGateRepairService);
        var generationReportWriter = new GenerationValidationReportWriter();
        var generationHealthReporter = new GenerationHealthReporter();
        var generationMetrics = new GenerationMetricsService();
        var promotionProfile = new TemplatePromotionFeatureProfileService();
        var generationStageTimeoutPolicy = new GenerationStageTimeoutPolicy();
        var fixStrategyHistory = new FileFixStrategyHistoryProvider();
        var fixSafetyPolicy = new FixSafetyPolicy();
        var fixInvariantEvaluator = new FixInvariantEvaluator();
        var fixPlanner = new GenerationFixPlanner(fixStrategyHistory);
        var fixVerifier = new GenerationFixVerifier(generationCompileGate);
        var fixLedger = new FileFixAttemptLedger();
        var deterministicFixApplier = new DeterministicCompileGatePatchApplier(generationCompileGate);
        var runtimeConfigFixApplier = new RuntimeConfigPatchApplier();
        var generationPromotion = new GenerationPromotionService(generationMetrics);
        var parityCertification = new ParityCertificationService(generationMetrics, toolAudit);
        var parityGateEvaluator = new ParityGateEvaluator();
        var parityWindowGate = new ParityWindowGateService(parityGateEvaluator);
        var parityDailyBackfill = new ParityDailyBackfillService();

        var personas = PersonaRuntimeFactory.Create(ai, memory, searcher);
        var critic = new LlmCritic(ai, personas);
        var healer = new AutoHealer(ai, validator);
        var llmFixApplier = new LlmAutoHealerPatchApplier(healer);
        var fixRunner = new FixStrategyRunner(
            fixPlanner,
            fixVerifier,
            fixLedger,
            generationMetrics,
            new IFixPatchApplier[] { deterministicFixApplier, runtimeConfigFixApplier, llmFixApplier },
            fixSafetyPolicy,
            fixInvariantEvaluator);
        var testGenerator = new SimpleTestGenerator(ai);
        var executor = new PythonSandbox();
        var researcher = ResearchRuntimeFactory.CreateSimpleResearcher(ai, executor, searcher, memory);
        var selector = new ModelOrchestrator(ai);
        var deployer = new CloudDeployer();
        var recursiveTester = new RecursiveTester(ai, dotnetService);
        var shadow = new ShadowWorkspace(helperRoot, dotnetService);
        var surgeon = new SurgeonAgent(ai, shadow);
        var evolution = new EvolutionEngine(ai, (ISurgeonAgent)surgeon);
        var indexingTelemetry = new IndexingTelemetrySink();
        var domainResolver = new KnowledgeDomainResolver(ai);
        var documentNormalizer = new DocumentNormalizationService();
        var structureRecovery = new StructureRecoveryService();
        var chunkingStrategyResolver = new ChunkingStrategyResolver();
        var semanticBoundaryService = new SemanticChunkBoundaryService();
        var chunkBuilders = new IChunkBuilder[]
        {
            new RecursiveChunkBuilder(semanticBoundaryService),
            new StructuralChunkBuilder(semanticBoundaryService),
            new ParentChildChunkBuilder(semanticBoundaryService)
        };
        var structuredParsers = new IStructuredDocumentParser[]
        {
            new StructuredPdfParser(ai),
            new StructuredEpubParser(),
            new StructuredHtmlParser(),
            new StructuredDocxParser(),
            new StructuredFb2Parser(),
            new StructuredMarkdownParser(),
            new StructuredDjvuParser(ai),
            new StructuredChmParser(),
            new StructuredZimParser()
        };
        var parsers = new IDocumentParser[]
        {
            new PdfParser(ai),
            new DocxParser(),
            new ImageParser(ai),
            new DjvuParser(ai),
            new EpubParser(),
            new HtmlParser(),
            new Fb2Parser(),
            new MarkdownParser(),
            new ChmParser(),
            new ZimParser()
        };
        var librarianV2Pipeline = new StructuredLibrarianV2Pipeline(
            memory,
            ai,
            documentNormalizer,
            structureRecovery,
            chunkingStrategyResolver,
            chunkBuilders,
            indexingTelemetry,
            domainResolver);
        var librarian = new LibrarianAgent(memory, ai, parsers, structuredParsers, librarianV2Pipeline, indexingTelemetry, domainResolver);
        var reflection = new ReflectionService(ai, memory);
        var templatesRoot = HelperWorkspacePathResolver.ResolveTemplatesRoot();
        var generalizer = new TemplateGeneralizer(ai, sanitizer, templatesRoot);
        var tools = new ToolService(mcpProxy, processGuard, goalManager, fileGuard, permit: toolPermit, audit: toolAudit, extensionRegistry: extensionRegistry);
        var consciousness = new ConsciousnessService(memory, ai);
        var debugger = new AutoDebugger(ai, reflection);
        var failureEnvelopeFactory = new FailureEnvelopeFactory();
        var mutation = new ArchitectMutationService(ai, critic);
        var pruner = new KnowledgePruner(memory);
        var philosophy = new PhilosophyEngine(ai, reflection, memory);
        var expertConsultant = new ExpertConsultant(ai, memory);

        var blueprints = new BlueprintEngine(ai, personas);
        var surgery = new SurgicalToolbox();
        var platforms = new PlatformGuard();
        var observer = new InternalObserver(surgery, platforms);
        var bcaster = new IntentBcaster();
        var coder = new SimpleCoder(ai, sanitizer);
        var atomic = new AtomicOrchestrator(coder, critic, bcaster, ai);
        var metacognitive = new MetacognitiveAgent(ai, surgery);

        var templateManager = new ProjectTemplateManager(templatesRoot);
        var templateFactory = new ProjectTemplateFactory(ai, processGuard);
        var templateLifecycle = new TemplateLifecycleService(templatesRoot);
        var templateRouting = new TemplateRoutingService(templateManager);
        var generationParityBenchmark = new GenerationParityBenchmarkService(templateRouting, failureEnvelopeFactory);
        var closedLoopPredictability = new ClosedLoopPredictabilityService();
        var templateCertification = GenerationRuntimeFactory.CreateTemplateCertificationService(
            templateManager,
            templateLifecycle,
            generationCompileGate,
            validator,
            forgeArtifactValidator,
            templatesRoot,
            helperRoot);
        var templatePromotionService = GenerationRuntimeFactory.CreateTemplatePromotionService(
            templateRouting,
            generalizer,
            templateLifecycle,
            generationCompileGate,
            promotionProfile,
            generationMetrics,
            templateCertification,
            templatesRoot);
        var auditor = new IntegrityAuditor(ai);
        var forge = ProjectForgeOrchestratorFactory.Create(
            templateManager,
            templateFactory,
            new SimplePlanner(ai),
            coder,
            validator,
            forgeArtifactValidator,
            healer,
            auditor,
            ai);

        var researchEngine = ResearchRuntimeFactory.CreateResearchEngine(researcher, (ILibrarianAgent)librarian, goalManager, strategy, critic, ai);
        var securityCritic = new SecurityCriticAgent(ai);
        var swarm = new SwarmOrchestrator(ai, sanitizer, buildExecutor, nodeManager, mcpProxy, securityCritic, schemaValidator, blueprintContractValidator);
        var tumen = TumenRuntimeFactory.CreateTumenOrchestrator(
            ai,
            sanitizer,
            forge,
            templateRouting,
            buildExecutor,
            critic,
            reflection,
            observer,
            atomic,
            bcaster,
            identifierSanitizer,
            generationPathSanitizer,
            signatureValidator,
            signatureNormalizer,
            usingInference,
            methodBodySemanticGuard,
            schemaValidator,
            blueprintContractValidator,
            generatedFileAstValidator,
            generationPathPolicy,
            generationCompileGate,
            generationReportWriter,
            generationHealthReporter,
            generationMetrics);
        var graphOrchestrator = TumenRuntimeFactory.CreateGraphOrchestrator(ai, tumen, critic, inspector, complexity, distiller);

        var learningPathPolicy = new LearningPathPolicy();
        var indexingQueueStore = new IndexingQueueStore(learningPathPolicy);
        var learningLifecycle = new LearningLifecycleController();
        var syntheticTaskRunner = LearningRuntimeFactory.CreateSyntheticTaskRunner(ai, graphOrchestrator, learningPathPolicy);
        var learner = LearningRuntimeFactory.CreateSyntheticLearningService(
            librarian,
            indexingTelemetry,
            learningPathPolicy,
            indexingQueueStore,
            learningLifecycle,
            syntheticTaskRunner);
        var maintenance = new MaintenanceService(memory, ai, health, recursiveTester, learner, pruner);

        IHelperOrchestrator orchestrator = HelperOrchestratorFactory.Create(
            selector,
            researchEngine,
            maintenance,
            forge,
            tumen,
            reflection,
            graphOrchestrator,
            tools,
            consciousness,
            debugger,
            mutation,
            expertConsultant,
            blueprints,
            surgery,
            platforms,
            observer,
            bcaster,
            metacognitive,
            templateRouting,
            generationMetrics,
            generationReportWriter,
            generationHealthReporter,
            failureEnvelopeFactory,
            generationStageTimeoutPolicy,
            fixRunner,
            templatePromotionService);
        var mcpServer = new McpServerHost(tools, orchestrator, extensionRegistry);

        return new HelperCliRuntime(
            helperRoot,
            scanner,
            extensionRegistry,
            tools,
            orchestrator,
            templateLifecycle,
            templateManager,
            promotionProfile,
            templatePromotionService,
            templateCertification,
            parityCertification,
            parityDailyBackfill,
            parityGateEvaluator,
            parityWindowGate,
            generationParityBenchmark,
            closedLoopPredictability,
            critic,
            mcpServer);
    }
}

