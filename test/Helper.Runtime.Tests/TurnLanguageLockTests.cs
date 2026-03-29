using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Helper.Runtime.Tests;

public class TurnLanguageLockTests
{
    [Fact]
    public void TurnLanguageResolver_ResolvesRussian_ForAutoProfile_WithMixedTechnicalPrompt()
    {
        var resolver = new TurnLanguageResolver();
        var profile = new ConversationUserProfile("auto", "balanced", "neutral", "intermediate", "auto");

        var language = resolver.Resolve(profile, "Подскажи next step для REST API");

        Assert.Equal("ru", language);
    }

    [Fact]
    public void TurnLanguageResolver_ResolvesEnglish_ForAutoProfile_WithEnglishPrompt_ContainingRussianBrand()
    {
        var resolver = new TurnLanguageResolver();
        var profile = new ConversationUserProfile("auto", "balanced", "neutral", "intermediate", "auto");

        var language = resolver.Resolve(profile, "Explain the differences between Яндекс Browser and Chrome");

        Assert.Equal("en", language);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesRussianFallbacks_WhenTurnLanguageIsRussian()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var conversation = new ConversationState("finalizer-ru")
        {
            PreferredLanguage = "ru"
        };
        var context = new ChatTurnContext
        {
            TurnId = "finalizer-ru-turn",
            Request = new ChatRequestDto("Что делать дальше по задаче", conversation.Id, 10, null),
            Conversation = conversation,
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Базовый ответ",
            IsCritiqueApproved = true,
            IsFactualPrompt = true,
            ResolvedTurnLanguage = "ru"
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("Неопределённость:", context.FinalResponse, StringComparison.Ordinal);
        Assert.True(
            context.FinalResponse.Contains("Следующий шаг:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Что можно сделать дальше:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Если продолжим, следующий шаг:", StringComparison.Ordinal));
        Assert.True(
            context.FinalResponse.Contains("Назовите точный тезис для проверки, и я сфокусирую поиск источников именно на нём.", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Могу сузить вопрос до одного факта и собрать под него более надёжные источники.", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Если нужен grounded-ответ, укажите самый важный факт для верификации, и я начну с него.", StringComparison.Ordinal));
        Assert.DoesNotContain("Понимание:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Решение:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Next step:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("If you need stronger verification", context.FinalResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurnExecutor_UsesResolvedAutoTurnLanguageInSystemInstruction()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        string? capturedSystemInstruction = null;
        ai.Setup(a => a.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .Callback<string, CancellationToken, string?, string?, int, string?>((_, _, _, _, _, instruction) =>
            {
                capturedSystemInstruction = instruction;
            })
            .ReturnsAsync("executor-result");

        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());
        orchestrator.Setup(x => x.GenerateProjectAsync(
                It.IsAny<GenerationRequest>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenerationRequest request, bool _, Action<string>? __, CancellationToken ___) =>
                new GenerationResult(
                    true,
                    new List<GeneratedFile>(),
                    request.OutputPath,
                    new List<BuildError>(),
                    TimeSpan.Zero));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            new UserProfileService(),
            new TurnLanguageResolver());

        var conversation = new ConversationState("executor-auto-profile")
        {
            PreferredLanguage = "auto",
            DetailLevel = "balanced",
            Formality = "neutral",
            DomainFamiliarity = "intermediate",
            PreferredStructure = "auto"
        };
        var context = new ChatTurnContext
        {
            TurnId = "executor-auto-language",
            Request = new ChatRequestDto("Объясни difference между REST и gRPC", conversation.Id, 10, null),
            Conversation = conversation,
            History = new[]
            {
                new ChatMessageDto("user", "Объясни difference между REST и gRPC", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("executor-result", context.ExecutionOutput);
        Assert.Equal("ru", context.ResolvedTurnLanguage);
        Assert.NotNull(capturedSystemInstruction);
        Assert.Contains("language=ru", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_UsesConversationLanguageLock_WhenPreferredLanguageIsRussian()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var conversation = new ConversationState("composer-language-lock")
        {
            PreferredLanguage = "ru"
        };
        var context = new ChatTurnContext
        {
            TurnId = "composer-language-lock-turn",
            Request = new ChatRequestDto("remember: keep responses short", conversation.Id, 10, null),
            Conversation = conversation,
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        var result = composer.Compose(context, "Короткий ответ");

        Assert.Equal("Короткий ответ", result);
        Assert.DoesNotContain("Понимание:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Решение:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Understanding:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Next step:", result, StringComparison.Ordinal);
    }
}


