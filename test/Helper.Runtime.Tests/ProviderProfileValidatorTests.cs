using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderProfileValidatorTests
{
    [Fact]
    public void Validate_FlagsMissingRequiredFields_AndDuplicateBindings()
    {
        var validator = new ProviderProfileValidator();
        var profile = new ProviderProfile(
            string.Empty,
            string.Empty,
            ProviderKind.OpenAiCompatible,
            ProviderTransportKind.OpenAiCompatible,
            "not-a-url",
            Enabled: true,
            IsBuiltIn: false,
            IsLocal: false,
            ProviderTrustMode.RemoteTrusted,
            Array.Empty<ProviderWorkloadGoal>(),
            new[]
            {
                new ProviderModelClassBinding(HelperModelClass.Reasoning, "gpt-4.1-mini"),
                new ProviderModelClassBinding(HelperModelClass.Reasoning, "gpt-4.1")
            },
            new ProviderCredentialReference(null, Required: true),
            PreferredReasoningEffort: "turbo");

        var result = validator.Validate(profile, new[] { string.Empty, string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Alerts, alert => alert.Contains("Profile id is required.", StringComparison.Ordinal));
        Assert.Contains(result.Alerts, alert => alert.Contains("absolute http/https base URL", StringComparison.Ordinal));
        Assert.Contains(result.Alerts, alert => alert.Contains("duplicate model-class bindings", StringComparison.Ordinal));
        Assert.Contains(result.Alerts, alert => alert.Contains("API key environment variable", StringComparison.Ordinal));
        Assert.Contains(result.Alerts, alert => alert.Contains("unsupported preferred reasoning effort", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WarnsWhenCredentialMissing_AndLocalityLooksInconsistent()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_OPENAI_API_KEY"] = null
        });

        var validator = new ProviderProfileValidator();
        var profile = new ProviderProfile(
            "remote_but_loopback",
            "Remote But Loopback",
            ProviderKind.OpenAiCompatible,
            ProviderTransportKind.OpenAiCompatible,
            "http://localhost:1234/v1",
            Enabled: true,
            IsBuiltIn: false,
            IsLocal: false,
            ProviderTrustMode.RemoteTrusted,
            new[] { ProviderWorkloadGoal.HostedReasoning },
            new[] { new ProviderModelClassBinding(HelperModelClass.Reasoning, "gpt-4.1-mini") },
            new ProviderCredentialReference("HELPER_OPENAI_API_KEY", Required: true),
            PreferredReasoningEffort: "deep");

        var result = validator.Validate(profile);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("Credential env 'HELPER_OPENAI_API_KEY' is not configured.", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("marked remote but base URL host 'localhost' is loopback", StringComparison.Ordinal));
    }
}
