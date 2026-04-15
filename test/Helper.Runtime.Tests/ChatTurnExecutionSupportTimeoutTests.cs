using System.Reflection;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class ChatTurnExecutionSupportTimeoutTests
{
    [Fact]
    public void ResolveStageTimeout_ExtendsResearchBudget_ForStrictLiveEvidenceBenchmarkTurn()
    {
        var context = CreateContext(
            "Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.",
            isBenchmark: true,
            mode: LocalFirstBenchmarkMode.WebRequired,
            requirement: "web_required",
            timeBudget: TimeSpan.FromSeconds(30));

        var timeout = InvokeResolveStageTimeout("research", context);

        Assert.Equal(TimeSpan.FromSeconds(30), timeout);
    }

    [Fact]
    public void ResolveStageTimeout_KeepsDefaultResearchBudget_ForNonBenchmarkTurn()
    {
        var context = CreateContext(
            "Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.",
            isBenchmark: false,
            mode: LocalFirstBenchmarkMode.None,
            requirement: "web_required",
            timeBudget: TimeSpan.FromSeconds(30));

        var timeout = InvokeResolveStageTimeout("research", context);

        Assert.Equal(TimeSpan.FromSeconds(20), timeout);
    }

    private static ChatTurnContext CreateContext(
        string message,
        bool isBenchmark,
        LocalFirstBenchmarkMode mode,
        string requirement,
        TimeSpan timeBudget)
    {
        return new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto(
                Message: message,
                ConversationId: null,
                MaxHistory: null,
                SystemInstruction: "You are being evaluated as a local-first librarian-research assistant.",
                BranchId: null,
                Attachments: null,
                IdempotencyKey: null,
                LiveWebMode: null,
                InputMode: "text"),
            Conversation = new ConversationState("conv-timeout"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "research"),
            TimeBudget = timeBudget,
            IsLocalFirstBenchmarkTurn = isBenchmark,
            LocalFirstBenchmarkMode = mode,
            ResolvedLiveWebRequirement = requirement
        };
    }

    private static TimeSpan InvokeResolveStageTimeout(string stage, ChatTurnContext context)
    {
        var method = typeof(ChatTurnExecutionSupport).GetMethod(
            "ResolveStageTimeout",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { stage, context });
        Assert.IsType<TimeSpan>(result);
        return (TimeSpan)result!;
    }
}
