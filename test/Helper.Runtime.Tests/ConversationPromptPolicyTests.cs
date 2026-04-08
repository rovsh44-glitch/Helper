using Helper.Api.Conversation;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class ConversationPromptPolicyTests
{
    [Fact]
    public void ConversationPromptPolicy_EmphasizesCollaborationAndAntiMetaRules()
    {
        var policy = new ConversationPromptPolicy();
        var state = new ConversationState("conv-prompt-policy");
        var context = new ChatTurnContext
        {
            TurnId = "turn-prompt-policy",
            Request = new Helper.Api.Hosting.ChatRequestDto("guide me", state.Id, 12, null),
            Conversation = state,
            History = Array.Empty<Helper.Api.Hosting.ChatMessageDto>(),
            CollaborationIntent = new CollaborationIntentAnalysis(
                IsGuidanceSeeking: true,
                TrustsBestJudgment: true,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "guidance",
                Signals: new[] { "guidance_seeking" })
        };
        var profile = new ConversationUserProfile("en", "balanced", "neutral", "intermediate", "auto", "balanced", "balanced", "balanced", "auto", null);
        var styleRoute = new ConversationStylePolicy().Resolve(profile, context);

        var instruction = policy.BuildSystemInstruction(context, profile, styleRoute, "en");

        Assert.Contains("collaborative", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clarifications must be minimal", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not expose internal process", instruction, StringComparison.OrdinalIgnoreCase);
    }
}
