using Helper.Api.Backend.Application;
using Helper.Api.Backend.Configuration;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class PostTurnAuditSchedulerTests
{
    [Fact]
    public void PostTurnAuditScheduler_SkipsWhenOutstandingAuditAlreadyExists()
    {
        var config = new ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "key");
        var options = new BackendOptionsCatalog(config);
        var queue = new PostTurnAuditQueue(options: options);
        var stagePolicy = new Mock<ITurnStagePolicy>();
        stagePolicy.Setup(x => x.AllowsAsyncAudit(It.IsAny<ChatTurnContext>())).Returns(true);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext.Items["CorrelationId"] = "cid-test-001";

        var scheduler = new PostTurnAuditScheduler(stagePolicy.Object, queue, httpContextAccessor, options);
        var existing = new PostTurnAuditItem(
            "conv-existing",
            "turn-existing",
            "what is .net",
            "answer",
            true,
            new List<string> { "https://example.org" },
            DateTimeOffset.UtcNow);
        Assert.True(queue.Enqueue(existing));

        var context = new ChatTurnContext
        {
            TurnId = "turn-new",
            Request = new ChatRequestDto("Research .NET observability with sources", "conv-new", 10, null),
            Conversation = new ConversationState("conv-new"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            BudgetProfile = TurnBudgetProfile.Research
        };
        var response = new ChatResponseDto(
            "conv-new",
            "answer",
            Array.Empty<ChatMessageDto>(),
            DateTimeOffset.UtcNow,
            Sources: new[] { "https://example.org" },
            TurnId: "turn-new");

        var scheduled = scheduler.TrySchedule(context, response);

        Assert.False(scheduled);
        Assert.True(context.AuditEligible);
        Assert.False(context.AuditExpectedTrace);
        Assert.Equal("skipped_outstanding_limit", context.AuditDecision);
        Assert.False(context.AuditStrictMode);
        Assert.Equal(1, context.AuditMaxOutstandingAudits);
    }

    [Fact]
    public void PostTurnAuditScheduler_StrictMode_AllowsLimitedOutstandingAuditConcurrency()
    {
        var previousStrict = Environment.GetEnvironmentVariable("HELPER_POST_TURN_AUDIT_STRICT");
        var previousMaxOutstanding = Environment.GetEnvironmentVariable("HELPER_POST_TURN_AUDIT_MAX_OUTSTANDING");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_POST_TURN_AUDIT_STRICT", "true");
            Environment.SetEnvironmentVariable("HELPER_POST_TURN_AUDIT_MAX_OUTSTANDING", "4");

            var config = new ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "key");
            var options = new BackendOptionsCatalog(config);
            var queue = new PostTurnAuditQueue(options: options);
            var stagePolicy = new Mock<ITurnStagePolicy>();
            stagePolicy.Setup(x => x.AllowsAsyncAudit(It.IsAny<ChatTurnContext>())).Returns(true);
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            };

            var scheduler = new PostTurnAuditScheduler(stagePolicy.Object, queue, httpContextAccessor, options);
            Assert.True(queue.Enqueue(new PostTurnAuditItem(
                "conv-existing",
                "turn-existing",
                "what is .net",
                "answer",
                true,
                new List<string> { "https://example.org" },
                DateTimeOffset.UtcNow)));

            var context = new ChatTurnContext
            {
                TurnId = "turn-new",
                Request = new ChatRequestDto("Research .NET observability with sources", "conv-new", 10, null),
                Conversation = new ConversationState("conv-new"),
                History = Array.Empty<ChatMessageDto>(),
                Intent = new IntentAnalysis(IntentType.Research, "test-model"),
                BudgetProfile = TurnBudgetProfile.Research
            };
            var response = new ChatResponseDto(
                "conv-new",
                "answer",
                Array.Empty<ChatMessageDto>(),
                DateTimeOffset.UtcNow,
                Sources: new[] { "https://example.org" },
                TurnId: "turn-new");

            var scheduled = scheduler.TrySchedule(context, response);

            Assert.True(scheduled);
            Assert.True(context.AuditEligible);
            Assert.True(context.AuditExpectedTrace);
            Assert.True(context.AuditStrictMode);
            Assert.Equal("scheduled", context.AuditDecision);
            Assert.Equal(1, context.AuditOutstandingAtDecision);
            Assert.Equal(4, context.AuditMaxOutstandingAudits);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_POST_TURN_AUDIT_STRICT", previousStrict);
            Environment.SetEnvironmentVariable("HELPER_POST_TURN_AUDIT_MAX_OUTSTANDING", previousMaxOutstanding);
        }
    }

    [Fact]
    public void TurnStagePolicy_AllowsAsyncAudit_OnlyForResearchOrHighRisk()
    {
        var config = new ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "key");
        var policy = new TurnStagePolicy(new BackendOptionsCatalog(config));

        var chatContext = new ChatTurnContext
        {
            TurnId = "chat",
            Request = new ChatRequestDto("remember: answer concise", "conv", 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model"),
            BudgetProfile = TurnBudgetProfile.ChatLight
        };
        var researchContext = new ChatTurnContext
        {
            TurnId = "research",
            Request = new ChatRequestDto("Research .NET observability with sources", "conv", 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            BudgetProfile = TurnBudgetProfile.Research
        };
        var highRiskContext = new ChatTurnContext
        {
            TurnId = "risk",
            Request = new ChatRequestDto("This requires confirmation", "conv", 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model"),
            BudgetProfile = TurnBudgetProfile.HighRisk
        };

        Assert.False(policy.AllowsAsyncAudit(chatContext));
        Assert.True(policy.AllowsAsyncAudit(researchContext));
        Assert.True(policy.AllowsAsyncAudit(highRiskContext));
    }
}

