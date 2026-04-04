using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.Evolution;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge;
using Helper.Runtime.Swarm;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperCoreInfrastructureServices(this IServiceCollection services, ApiRuntimeConfig runtimeConfig)
    {
        services.AddSingleton<AILink>();
        services.AddSingleton<IVectorStore, QdrantStore>();
        services.AddSingleton<IStructuredVectorStore>(sp => (QdrantStore)sp.GetRequiredService<IVectorStore>());
        services.AddSingleton<ICodeSanitizer, CodeSanitizer>();
        services.AddSingleton<IWebSearcher, WebSearcher>();
        services.AddSingleton<IDotnetService, DotnetService>();
        services.AddSingleton<IBuildExecutor, LocalBuildExecutor>();
        services.AddSingleton<IBuildValidator, MultiLanguageValidator>();
        services.AddSingleton<IForgeArtifactValidator, ForgeArtifactValidator>();
        services.AddSingleton<IAutoHealer, AutoHealer>();
        services.AddSingleton<ITestGenerator, SimpleTestGenerator>();
        services.AddSingleton<IProjectPlanner, SimplePlanner>();
        services.AddSingleton<ICodeGenerator, SimpleCoder>();
        services.AddSingleton<ICodeExecutor, PythonSandbox>();
        services.AddSingleton<IHealthMonitor, HealthMonitor>();
        services.AddSingleton<SystemScanner>();
        services.AddSingleton<IGoalManager, GoalManager>();
        services.AddSingleton<IStrategicPlanner, StrategicPlanner>();
        services.AddSingleton<IFileSystemGuard>(_ => new FileSystemGuard(
            runtimeConfig.RootPath,
            new[]
            {
                runtimeConfig.DataRoot,
                runtimeConfig.ProjectsRoot,
                runtimeConfig.LibraryRoot,
                runtimeConfig.LogsRoot,
                runtimeConfig.TemplatesRoot
            }));
        services.AddSingleton<IProcessGuard, ProcessGuard>();
        services.AddSingleton<IVisualInspector, VisualInspector>();
        services.AddSingleton<IComplexityAnalyzer, ComplexityAnalyzer>();
        services.AddSingleton<IContextDistiller, NeuralContextDistiller>();
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddSingleton<IDeclaredCapabilityCatalogProvider, DeclaredCapabilityCatalogProvider>();
        services.AddSingleton<IMcpProxyService, McpProxyService>();
        services.AddSingleton<ISwarmNodeManager, SwarmNodeManager>();
        services.AddSingleton<IRecursiveTester, RecursiveTester>();
        services.AddSingleton<IReflectionService, ReflectionService>();
        services.AddSingleton<ITemplateGeneralizer>(sp => new TemplateGeneralizer(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<ICodeSanitizer>(),
            runtimeConfig.TemplatesRoot));
        services.AddSingleton<ILibrarianAgent, LibrarianAgent>();
        services.AddSingleton<IKnowledgePruner, KnowledgePruner>();
        services.AddSingleton<IConsciousnessService, ConsciousnessService>();
        services.AddSingleton<IAutoDebugger, AutoDebugger>();
        services.AddSingleton<IFailureEnvelopeFactory, FailureEnvelopeFactory>();
        services.AddSingleton<IArchitectMutation, ArchitectMutationService>();
        services.AddSingleton<IExpertConsultant, ExpertConsultant>();
        services.AddSingleton<IPhilosophyEngine, PhilosophyEngine>();
        services.AddSingleton<IPersonalityManager, PersonalityManager>();
        services.AddSingleton<IntegrityAuditor>();
        services.AddSingleton(sp => new ShadowWorkspace(runtimeConfig.RootPath, sp.GetRequiredService<IDotnetService>()));
        services.AddSingleton<ISafetyGuard, SafetyGuard>();
        services.AddSingleton<IToolPermitService, ToolPermitService>();
        services.AddSingleton<IToolAuditService, ToolAuditService>();
        services.AddSingleton<IRouteTelemetryService, RouteTelemetryService>();
        services.AddSingleton<ISurgicalToolbox, SurgicalToolbox>();
        services.AddSingleton<IPlatformGuard, PlatformGuard>();

        return services;
    }
}

