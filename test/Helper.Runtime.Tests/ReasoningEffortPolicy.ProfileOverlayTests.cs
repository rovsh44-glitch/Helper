using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ReasoningEffortPolicyProfileOverlayTests
{
    [Fact]
    public void Resolve_UsesProviderDefault_WhenUserProfileIsNeutral()
    {
        var policy = new ReasoningEffortPolicy(new StubResolver("deep"));
        var context = new ChatTurnContext
        {
            TurnId = "turn-default",
            Request = new ChatRequestDto("Explain the tradeoffs.", "conv", 12, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionMode = TurnExecutionMode.Balanced
        };

        var effort = policy.Resolve(context, PersonalizationProfile.Default);

        Assert.Equal("deep", effort);
    }

    [Fact]
    public void Resolve_FastExecutionMode_StillWinsOverProviderDefault()
    {
        var policy = new ReasoningEffortPolicy(new StubResolver("deep"));
        var context = new ChatTurnContext
        {
            TurnId = "turn-fast",
            Request = new ChatRequestDto("Summarize quickly.", "conv", 12, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionMode = TurnExecutionMode.Fast
        };

        var effort = policy.Resolve(context, PersonalizationProfile.Default);

        Assert.Equal("fast", effort);
    }

    private sealed class StubResolver : IProviderProfileResolver
    {
        private readonly string? _preferredEffort;

        public StubResolver(string? preferredEffort)
        {
            _preferredEffort = preferredEffort;
        }

        public ProviderProfileSummary? GetActiveProfile() => null;
        public ProviderRuntimeConfiguration? GetRuntimeConfiguration() => null;
        public string? ResolveModelBinding(HelperModelClass modelClass) => null;
        public string? ResolvePreferredReasoningEffort() => _preferredEffort;
        public bool SupportsVision() => false;
        public bool PrefersResearchVerification() => false;
        public bool IsLocalOnly() => false;
        public string? ApplyToRuntime() => null;
    }
}
