using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ConversationModelSelectionPolicyProfileOverlayTests
{
    [Fact]
    public void Select_UsesProviderBinding_WhenAvailable()
    {
        var policy = new ConversationModelSelectionPolicy(providerProfileResolver: new StubResolver(
            new Dictionary<HelperModelClass, string>
            {
                [HelperModelClass.Coder] = "profile-coder"
            }));
        var context = new ChatTurnContext
        {
            TurnId = "turn-generate",
            Request = new ChatRequestDto("Generate a patch for this repository.", "conv", 12, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Generate, "test")
        };

        var decision = policy.Select(context, new[] { "qwen2.5-coder:14b" });

        Assert.Equal("profile-coder", decision.PreferredModel);
        Assert.Contains("profile_binding:coder", decision.Reasons);
    }

    private sealed class StubResolver : IProviderProfileResolver
    {
        private readonly IReadOnlyDictionary<HelperModelClass, string> _bindings;

        public StubResolver(IReadOnlyDictionary<HelperModelClass, string> bindings)
        {
            _bindings = bindings;
        }

        public ProviderProfileSummary? GetActiveProfile() => null;
        public ProviderRuntimeConfiguration? GetRuntimeConfiguration() => null;
        public string? ResolveModelBinding(HelperModelClass modelClass) => _bindings.TryGetValue(modelClass, out var model) ? model : null;
        public string? ResolvePreferredReasoningEffort() => null;
        public bool SupportsVision() => false;
        public bool PrefersResearchVerification() => false;
        public bool IsLocalOnly() => false;
        public string? ApplyToRuntime() => null;
    }
}
