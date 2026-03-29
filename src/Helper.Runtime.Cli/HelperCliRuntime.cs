using Helper.Runtime;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

internal sealed record HelperCliRuntime(
    string HelperRoot,
    SystemScanner Scanner,
    ExtensionRegistry ExtensionRegistry,
    IToolService Tools,
    IHelperOrchestrator Orchestrator,
    TemplateLifecycleService TemplateLifecycle,
    ProjectTemplateManager TemplateManager,
    TemplatePromotionFeatureProfileService PromotionProfile,
    IGenerationTemplatePromotionService TemplatePromotionService,
    TemplateCertificationService TemplateCertification,
    ParityCertificationService ParityCertification,
    ParityDailyBackfillService ParityDailyBackfill,
    ParityGateEvaluator ParityGateEvaluator,
    ParityWindowGateService ParityWindowGate,
    GenerationParityBenchmarkService GenerationParityBenchmark,
    ClosedLoopPredictabilityService ClosedLoopPredictability,
    LlmCritic Critic,
    McpServerHost McpServer);

