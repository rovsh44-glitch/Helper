using Helper.Api.Conversation;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Helper.Runtime.Tests;

public sealed class RuntimeServiceProfileTests
{
    [Fact]
    public async Task AddHelperApplicationServices_UsesDisabledExecutor_ByDefault()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            [RuntimeServiceProfileSupport.PrototypeRuntimeServicesEnvName] = "false"
        });
        using var temp = new TempDirectoryScope("helper-runtime-profile-");
        using var provider = BuildServices(temp.Path).BuildServiceProvider();

        var executor = provider.GetRequiredService<ICodeExecutor>();
        var result = await executor.ExecuteAsync("print('hello')", "python");

        Assert.IsType<DisabledCodeExecutor>(executor);
        Assert.False(result.Success);
        Assert.Contains("disabled in the production runtime profile", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddHelperApplicationServices_AllowsPrototypeExecutor_WhenExplicitlyEnabled()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            [RuntimeServiceProfileSupport.PrototypeRuntimeServicesEnvName] = "true"
        });
        using var temp = new TempDirectoryScope("helper-runtime-profile-");
        using var provider = BuildServices(temp.Path).BuildServiceProvider();

        var executor = provider.GetRequiredService<ICodeExecutor>();
        var result = await executor.ExecuteAsync("print('hello')", "python");

        Assert.IsType<PythonSandbox>(executor);
        Assert.False(result.Success);
        Assert.Contains("not implemented", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatTurnPlanner_Resolves_When_All_Dependencies_Are_Available_In_DI()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntentClassifier, StaticIntentClassifier>();
        services.AddSingleton<IAmbiguityDetector, HybridAmbiguityDetector>();
        services.AddSingleton<IClarificationPolicy, ClarificationPolicy>();
        services.AddSingleton<IIntentTelemetryService, IntentTelemetryService>();
        services.AddSingleton<ILatencyBudgetPolicy, LatencyBudgetPolicy>();
        services.AddSingleton<IAssumptionCheckPolicy, AssumptionCheckPolicy>();
        services.AddSingleton<IFeatureFlags, FeatureFlags>();
        services.AddSingleton<IConversationStageMetricsService, ConversationStageMetricsService>();
        services.AddSingleton<IUserProfileService, UserProfileService>();
        services.AddSingleton<ITurnLanguageResolver, TurnLanguageResolver>();
        services.AddSingleton<ILiveWebRequirementPolicy, LiveWebRequirementPolicy>();
        services.AddSingleton<ILocalFirstBenchmarkPolicy, LocalFirstBenchmarkPolicy>();
        services.AddSingleton<ICollaborationIntentDetector, CollaborationIntentDetector>();
        services.AddSingleton<ICommunicationQualityPolicy, CommunicationQualityPolicy>();
        services.AddSingleton<IPersonalizationMergePolicy, PersonalizationMergePolicy>();
        services.AddSingleton<IInteractionStateAnalyzer, InteractionStateAnalyzer>();
        services.AddSingleton<IInteractionPolicyProjector, InteractionPolicyProjector>();
        services.AddSingleton<IReasoningEffortPolicy, ReasoningEffortPolicy>();
        services.AddSingleton<IClarificationQualityPolicy, ClarificationQualityPolicy>();
        services.AddSingleton<TurnIntentAnalysisStep>();
        services.AddSingleton<TurnPersonalizationStep>();
        services.AddSingleton<TurnReasoningSelectionStep>();
        services.AddSingleton<TurnLatencyBudgetStep>();
        services.AddSingleton<TurnLiveWebDecisionStep>();
        services.AddSingleton<TurnAmbiguityResolutionStep>();
        services.AddSingleton<TurnIntentOverrideStep>();
        services.AddSingleton<IChatTurnPlanner>(sp => new ChatTurnPlanner(
            sp.GetRequiredService<TurnIntentAnalysisStep>(),
            sp.GetRequiredService<TurnPersonalizationStep>(),
            sp.GetRequiredService<TurnReasoningSelectionStep>(),
            sp.GetRequiredService<TurnLatencyBudgetStep>(),
            sp.GetRequiredService<TurnLiveWebDecisionStep>(),
            sp.GetRequiredService<TurnAmbiguityResolutionStep>(),
            sp.GetRequiredService<TurnIntentOverrideStep>(),
            sp.GetRequiredService<IConversationStageMetricsService>()));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var planner = provider.GetRequiredService<IChatTurnPlanner>();
        Assert.IsType<ChatTurnPlanner>(planner);
    }

    private static IServiceCollection BuildServices(string root)
    {
        var services = new ServiceCollection();
        Directory.CreateDirectory(root);
        var runtimeConfig = new ApiRuntimeConfig(
            root,
            Path.Combine(root, "data"),
            Path.Combine(root, "projects"),
            Path.Combine(root, "library"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "templates"),
            "primary-key");

        services.AddSingleton(runtimeConfig);
        services.AddHelperApplicationServices(runtimeConfig);
        return services;
    }

    private sealed class StaticIntentClassifier : IIntentClassifier
    {
        public Task<IntentClassification> ClassifyAsync(string message, CancellationToken ct)
        {
            return Task.FromResult(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, string.Empty),
                0.0,
                "test",
                Array.Empty<string>()));
        }
    }
}
