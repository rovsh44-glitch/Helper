using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ConversationWebQueryPlannerTests
{
    [Fact]
    public void Build_RewritesNearMeQuery_WhenExplicitLocalityHintExists()
    {
        var planner = new WebQueryPlanner();
        var context = new ChatTurnContext
        {
            TurnId = "turn-locality",
            Request = new ChatRequestDto("best restaurant near me", "conv-locality", 10, null),
            Conversation = new ConversationState("conv-locality"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedTurnLanguage = "en"
        };
        var profile = new ConversationUserProfile("en", "balanced", "neutral", "intermediate", "auto", SearchLocalityHint: "Tashkent");

        var plan = planner.Build(context, profile, context.Request.Message);

        Assert.Equal("best restaurant in Tashkent", plan.Query);
        Assert.True(plan.LocalityApplied);
        Assert.Contains(plan.Trace, line => line.Contains("profile_locality_hint", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PreservesPrivacyBoundary_WhenNoExplicitLocalityHintExists()
    {
        var planner = new WebQueryPlanner();
        var context = new ChatTurnContext
        {
            TurnId = "turn-no-locality-hint",
            Request = new ChatRequestDto("best restaurant near me", "conv-locality", 10, null),
            Conversation = new ConversationState("conv-locality"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedTurnLanguage = "en"
        };
        var profile = new ConversationUserProfile("en", "balanced", "neutral", "intermediate", "auto");

        var plan = planner.Build(context, profile, context.Request.Message);

        Assert.Equal("best restaurant near me", plan.Query);
        Assert.False(plan.LocalityApplied);
        Assert.Contains(plan.Trace, line => line.Contains("privacy_boundary=explicit", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_AppendsPreferenceModifiers_ForTechnicalExpertQuery()
    {
        var planner = new WebQueryPlanner();
        var context = new ChatTurnContext
        {
            TurnId = "turn-expert-query",
            Request = new ChatRequestDto("grpc retry policy", "conv-expert", 10, null),
            Conversation = new ConversationState("conv-expert"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedTurnLanguage = "en"
        };
        var profile = new ConversationUserProfile("en", "deep", "neutral", "expert", "auto");

        var plan = planner.Build(context, profile, context.Request.Message);

        Assert.Contains("official docs", plan.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deep dive", plan.Query, StringComparison.OrdinalIgnoreCase);
        Assert.True(plan.PreferenceApplied);
    }

    [Fact]
    public void Build_CleansVoiceTranscript_BeforeApplyingLocalityRewrite()
    {
        var planner = new WebQueryPlanner();
        var context = new ChatTurnContext
        {
            TurnId = "turn-voice-locality",
            Request = new ChatRequestDto(
                "Hey Helper, can you look up best restaurant near me please",
                "conv-voice-locality",
                10,
                null,
                InputMode: "voice"),
            Conversation = new ConversationState("conv-voice-locality"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedTurnLanguage = "en"
        };
        var profile = new ConversationUserProfile("en", "balanced", "neutral", "intermediate", "auto", SearchLocalityHint: "Tashkent");

        var plan = planner.Build(context, profile, context.Request.Message);

        Assert.Equal("best restaurant in Tashkent", plan.Query);
        Assert.True(plan.VoiceRewriteApplied);
        Assert.True(plan.LocalityApplied);
        Assert.Contains(plan.Trace, line => line.Contains("web_query.voice applied=yes", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RewritesHumanPrompt_ToTopicalCore_ForBenchmarkStyleQuery()
    {
        var planner = new WebQueryPlanner();
        var context = new ChatTurnContext
        {
            TurnId = "turn-human-prompt",
            Request = new ChatRequestDto(
                "Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.",
                "conv-human-prompt",
                10,
                null),
            Conversation = new ConversationState("conv-human-prompt"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedTurnLanguage = "ru"
        };
        var profile = new ConversationUserProfile("ru", "balanced", "neutral", "intermediate", "auto");

        var plan = planner.Build(context, profile, context.Request.Message);

        Assert.True(plan.TopicCoreApplied);
        Assert.DoesNotContain("Объясни", plan.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("вспышке", plan.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("кори", plan.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Европе", plan.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Trace, line => line.Contains("search_query.rewrite stage=topic_core", StringComparison.Ordinal));
    }
}

