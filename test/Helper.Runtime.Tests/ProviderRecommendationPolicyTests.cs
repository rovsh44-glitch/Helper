using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderRecommendationPolicyTests
{
    [Fact]
    public void Recommend_PrefersLocalCoder_ForHeavyLocalCoding()
    {
        var policy = new ProviderRecommendationPolicy();
        var candidates = new[]
        {
            BuildSummary("local_coder", isLocal: true, goals: new[] { ProviderWorkloadGoal.LocalCoder, ProviderWorkloadGoal.PrivacyFirst }),
            BuildSummary("hosted_reasoning", isLocal: false, goals: new[] { ProviderWorkloadGoal.HostedReasoning, ProviderWorkloadGoal.ResearchVerified })
        };

        var result = policy.Recommend(
            new ProviderRecommendationRequest("local_coder", PreferLocal: true, CodingIntensity: "heavy"),
            candidates);

        Assert.Equal("local_coder", result.RecommendedProfileId);
        Assert.Contains("locality_match", result.ReasonCodes);
        Assert.Contains("coder_supported", result.ReasonCodes);
    }

    [Fact]
    public void Recommend_ReturnsWarnings_WhenRequestedCapabilityIsMissing()
    {
        var policy = new ProviderRecommendationPolicy();
        var candidates = new[]
        {
            BuildSummary("local_fast", isLocal: true, goals: new[] { ProviderWorkloadGoal.LocalFast }, supportsVision: false)
        };

        var result = policy.Recommend(
            new ProviderRecommendationRequest("research_verified", PreferLocal: true, NeedVision: true),
            candidates);

        Assert.Equal("local_fast", result.RecommendedProfileId);
        Assert.Contains(result.Warnings, warning => warning.Contains("vision support", StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderProfileSummary BuildSummary(
        string id,
        bool isLocal,
        IReadOnlyList<ProviderWorkloadGoal> goals,
        bool supportsVision = true)
    {
        var bindings = new List<ProviderModelClassBinding>
        {
            new(HelperModelClass.Fast, "fast"),
            new(HelperModelClass.Reasoning, "reasoning"),
            new(HelperModelClass.Coder, "coder"),
        };
        if (supportsVision)
        {
            bindings.Add(new ProviderModelClassBinding(HelperModelClass.Vision, "vision"));
        }

        return new ProviderProfileSummary(
            new ProviderProfile(
                id,
                id,
                isLocal ? ProviderKind.Ollama : ProviderKind.OpenAiCompatible,
                isLocal ? ProviderTransportKind.Ollama : ProviderTransportKind.OpenAiCompatible,
                isLocal ? "http://localhost:11434" : "https://api.example.com/v1",
                Enabled: true,
                IsBuiltIn: false,
                IsLocal: isLocal,
                isLocal ? ProviderTrustMode.Local : ProviderTrustMode.RemoteTrusted,
                goals,
                bindings),
            new ProviderProfileValidationResult(true, Array.Empty<string>(), Array.Empty<string>()),
            new ProviderCapabilitySummary(
                SupportsFast: true,
                SupportsReasoning: true,
                SupportsCoder: true,
                SupportsVision: supportsVision,
                SupportsBackground: true,
                SupportsResearchVerified: true,
                SupportsPrivacyFirst: isLocal,
                RequiresLocalRuntime: isLocal),
            IsActive: false);
    }
}
