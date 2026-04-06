using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Backend.Providers;

public sealed class ProviderCapabilityMatrix : IProviderCapabilityMatrix
{
    public ProviderCapabilitySummary Summarize(ProviderProfile profile)
    {
        bool HasBinding(HelperModelClass modelClass) =>
            profile.ModelBindings.Any(binding => binding.ModelClass == modelClass && !string.IsNullOrWhiteSpace(binding.ModelName));

        var supportsVision = HasBinding(HelperModelClass.Vision);
        var supportsReasoning = HasBinding(HelperModelClass.Reasoning);
        var supportsFast = HasBinding(HelperModelClass.Fast);
        var supportsCoder = HasBinding(HelperModelClass.Coder);
        var supportsBackground = HasBinding(HelperModelClass.Background) || supportsReasoning;
        var supportsResearchVerified = supportsReasoning;
        var supportsPrivacyFirst = profile.IsLocal || profile.SupportedGoals.Contains(ProviderWorkloadGoal.PrivacyFirst);
        var requiresLocalRuntime = profile.IsLocal;

        return new ProviderCapabilitySummary(
            SupportsFast: supportsFast,
            SupportsReasoning: supportsReasoning,
            SupportsCoder: supportsCoder,
            SupportsVision: supportsVision,
            SupportsBackground: supportsBackground,
            SupportsResearchVerified: supportsResearchVerified,
            SupportsPrivacyFirst: supportsPrivacyFirst,
            RequiresLocalRuntime: requiresLocalRuntime);
    }
}
