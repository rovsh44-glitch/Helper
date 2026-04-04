using Helper.Api.Conversation;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public class ConversationStylePolicyTests
{
    [Fact]
    public void ConversationStylePolicy_UsesConversationalProfessionalDefault_ForOrdinaryChatTurn()
    {
        var policy = new ConversationStylePolicy();
        var profile = new ConversationUserProfile("ru", "balanced", "neutral", "intermediate", "auto");
        var context = new ChatTurnContext
        {
            TurnId = "style-default",
            Request = new Helper.Api.Hosting.ChatRequestDto("Помоги составить ответ", "style-default", 10, null),
            Conversation = new ConversationState("style-default"),
            History = Array.Empty<Helper.Api.Hosting.ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        var route = policy.Resolve(profile, context);

        Assert.Equal("conversational", route.Mode);
        Assert.Equal("conversational_professional", route.TonePreset);
        Assert.Contains("avoid cold distance", route.ModeDirective, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConversationStylePolicy_UsesProfessionalMode_ForFormalProfile()
    {
        var policy = new ConversationStylePolicy();
        var profile = new ConversationUserProfile("en", "deep", "formal", "expert", "checklist");

        var route = policy.Resolve(profile);

        Assert.Equal("professional", route.Mode);
        Assert.Equal("professional", route.TonePreset);
    }

    [Fact]
    public void ConversationStylePolicy_UsesOperatorMode_ForGenerationTurn()
    {
        var policy = new ConversationStylePolicy();
        var profile = new ConversationUserProfile("en", "balanced", "neutral", "intermediate", "auto");
        var context = new ChatTurnContext
        {
            TurnId = "style-operator",
            Request = new Helper.Api.Hosting.ChatRequestDto("Generate a console app", "style-operator", 10, null),
            Conversation = new ConversationState("style-operator"),
            History = Array.Empty<Helper.Api.Hosting.ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Generate, "test-model")
        };

        var route = policy.Resolve(profile, context);

        Assert.Equal("operator", route.Mode);
        Assert.Equal("operator", route.TonePreset);
    }

    [Fact]
    public void PersonalityJson_UsesPreciseCalmHumanCommunicationStyle()
    {
        var path = TestWorkspaceRoot.ResolveFile("personality.json");
        var json = File.ReadAllText(path);

        Assert.Contains("Precise, calm, respectful, and naturally conversational", json, StringComparison.Ordinal);
        Assert.DoesNotContain("slightly cold", json, StringComparison.OrdinalIgnoreCase);
    }
}

