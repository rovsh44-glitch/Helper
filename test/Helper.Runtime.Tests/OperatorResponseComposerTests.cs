using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class OperatorResponseComposerTests
{
    [Fact]
    public void Compose_UsesOperationalFormat_ForGenerateIntent()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1");
        Environment.SetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", "false");

        try
        {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-op-1",
            Request = new ChatRequestDto("сгенерируй инженерный калькулятор", null, 12, null),
            Conversation = new ConversationState("conv-op-1"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Generate, "test"),
            ExecutionMode = TurnExecutionMode.Balanced,
            Confidence = 0.72
        };
        context.ToolCalls.Add("helper.generate");
        context.NextStep = "Open generated project and run build.";
        var output = "Project successfully generated at: D:\\PROJECTS\\calc";

        var result = composer.Compose(context, output);

        Assert.True(
            result.Contains("Execution Summary:", StringComparison.Ordinal) ||
            result.Contains("Run Summary:", StringComparison.Ordinal) ||
            result.Contains("What Happened:", StringComparison.Ordinal));
        Assert.Contains("Result:", result);
        Assert.True(
            result.Contains("Next step:", StringComparison.Ordinal) ||
            result.Contains("Useful follow-up:", StringComparison.Ordinal) ||
            result.Contains("If you want to continue:", StringComparison.Ordinal));
        Assert.DoesNotContain("Understanding:", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", previous);
        }
    }

    [Fact]
    public void Compose_UsesRussianOperationalHeaders_WhenLanguageAwareFlagEnabled()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1");
        Environment.SetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", "true");

        try
        {
            var composer = ResponseComposerServiceFactory.CreateDefault();
            var context = new ChatTurnContext
            {
                TurnId = "turn-op-2",
                Request = new ChatRequestDto("сгенерируй инженерный калькулятор", null, 12, null),
                Conversation = new ConversationState("conv-op-2"),
                History = Array.Empty<ChatMessageDto>(),
                Intent = new IntentAnalysis(IntentType.Generate, "test"),
                ExecutionMode = TurnExecutionMode.Balanced,
                Confidence = 0.72
            };
            context.ToolCalls.Add("helper.generate");

            var result = composer.Compose(context, "ok");

            Assert.True(
                result.Contains("Сводка выполнения:", StringComparison.Ordinal) ||
                result.Contains("Итог по ходу:", StringComparison.Ordinal) ||
                result.Contains("Коротко по выполнению:", StringComparison.Ordinal));
            Assert.Contains("Результат:", result);
            Assert.True(
                result.Contains("Следующий шаг:", StringComparison.Ordinal) ||
                result.Contains("Что можно сделать дальше:", StringComparison.Ordinal) ||
                result.Contains("Если продолжим, следующий шаг:", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", previous);
        }
    }

    [Fact]
    public void Compose_DerivesCodingNextStep_WhenOperationalTurnHasNoExplicitNextStep()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1");
        Environment.SetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", "false");

        try
        {
            var composer = ResponseComposerServiceFactory.CreateDefault();
            var context = new ChatTurnContext
            {
                TurnId = "turn-op-derived-next-step",
                Request = new ChatRequestDto("generate project", null, 12, null),
                Conversation = new ConversationState("conv-op-derived-next-step")
                {
                    PreferredLanguage = "en"
                },
                History = Array.Empty<ChatMessageDto>(),
                Intent = new IntentAnalysis(IntentType.Generate, "test"),
                ExecutionMode = TurnExecutionMode.Balanced,
                Confidence = 0.72
            };
            context.ToolCalls.Add("helper.generate");

            var result = composer.Compose(context, "Project successfully generated at: D:\\PROJECTS\\calc");

            Assert.NotNull(context.NextStep);
            Assert.Contains(context.NextStep!, new[]
            {
                "I can turn this into the first implementation slice, required tests, and a file-by-file change list.",
                "I can inspect the generated structure, wire the first feature, or debug the first build issue.",
                "I can turn this into concrete follow-through: which files to open, which tests to run, and the first command to execute."
            });
            Assert.DoesNotContain("Retry with narrower scope or provide stricter constraints.", result, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", previous);
        }
    }

    [Fact]
    public void Compose_BenchmarkFallback_AddsExplicitUncertainty_WhenRequired()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-benchmark-uncertainty",
            Request = new ChatRequestDto(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                null,
                12,
                """
                ## Local Findings
                ## Web Findings
                ## Sources
                ## Analysis
                ## Conclusion
                ## Opinion
                """),
            Conversation = new ConversationState("conv-benchmark-uncertainty"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test"),
            ExecutionMode = TurnExecutionMode.Balanced,
            Confidence = 0.58,
            IsFactualPrompt = true,
            GroundingStatus = "grounded_with_limits",
            RequireExplicitBenchmarkUncertainty = true
        };
        context.Sources.Add("https://example.org/source-one");
        context.UncertaintyFlags.Add("uncertainty.search_hit_only_evidence");

        var result = composer.Compose(
            context,
            "Research request: could not retrieve grounded live search results. Неопределённость: page/document extraction остался неполным.");

        Assert.Contains("неопределён", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("огранич", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("частично", result, StringComparison.OrdinalIgnoreCase);
    }
}


