using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public class ChatTurnFinalizerRegressionTests
{
    [Fact]
    public async Task Finalizer_DoesNotForceRepairNextStep_ForApprovedResearchCritiqueFeedback()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "approved-research-critic",
            Request = new ChatRequestDto("Найди сведения о модели", null, 12, null),
            Conversation = new ConversationState("conv-approved-research") { PreferredLanguage = "ru" },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Краткий обзор модели и найденных материалов.",
            CorrectedContent = "Краткий обзор модели и найденных материалов.",
            IsCritiqueApproved = true,
            CritiqueFeedback = "Critic skipped for bounded-latency research execution.",
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };
        context.Sources.Add("https://example.org/model");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.DoesNotContain("Если что-то ещё мимо", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("перепишу только его", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("If one part still misses the mark", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalizer_DoesNotAppendActionSummaryFooter_ForLongHistoryConversation()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var history = Enumerable.Range(0, 10)
            .Select(index => new ChatMessageDto(index % 2 == 0 ? "user" : "assistant", $"msg-{index}", DateTimeOffset.UtcNow))
            .ToArray();
        var context = new ChatTurnContext
        {
            TurnId = "long-history-no-summary-footer",
            Request = new ChatRequestDto("Продолжай", null, 12, null),
            Conversation = new ConversationState("conv-long-history") { PreferredLanguage = "ru" },
            History = history,
            ExecutionOutput = "Нормальный содержательный ответ.",
            CorrectedContent = "Нормальный содержательный ответ.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.DoesNotContain("Сводка действия:", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Action summary:", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalizer_RewritesMetaOnlyResearchOutput_IntoExplicitFailure()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "meta-only-research-output",
            Request = new ChatRequestDto("найди в интернете всю информацию о новой embedding-модели и предоставь анализ", null, 12, null),
            Conversation = new ConversationState("conv-meta-only") { PreferredLanguage = "ru" },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "If youd like me to clarify something or adjust my response, please specify what youd like me to focus on next.",
            CorrectedContent = "If youd like me to clarify something or adjust my response, please specify what youd like me to focus on next.",
            IsCritiqueApproved = true,
            CritiqueFeedback = "Critic skipped for bounded-latency research execution.",
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("служебный meta-ответ", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("не могу ответственно утверждать", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("If youd like me to clarify something", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("повторить поиск с нуля", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meta_only_research_output_rewritten", context.UncertaintyFlags);
    }

    [Fact]
    public async Task Finalizer_Computes_Epistemic_Mode_And_Abstains_On_Unsupported_HighRisk_Factual_Ask()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "epistemic-high-risk-abstain",
            Request = new ChatRequestDto("Какая сегодня лучшая доза лекарства для мигрени?", null, 12, null),
            Conversation = new ConversationState("conv-epistemic-high-risk") { PreferredLanguage = "ru" },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Вероятно, подойдёт стандартная схема.",
            CorrectedContent = "Вероятно, подойдёт стандартная схема.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true,
            Confidence = 0.79
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.NotNull(context.EpistemicRiskSnapshot);
        Assert.Equal(Helper.Api.Conversation.Epistemic.EpistemicAnswerMode.Abstain, context.EpistemicAnswerMode);
        Assert.True(context.Confidence <= context.EpistemicRiskSnapshot!.ConfidenceCeiling);
        Assert.Contains("не могу ответственно утверждать", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalizer_Sets_Interaction_Repair_Driver_When_Repair_Is_Shaped_By_Interaction_Signals()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "interaction-repair-driver",
            Request = new ChatRequestDto("тон не тот", null, 12, null),
            Conversation = new ConversationState("conv-interaction-repair-driver") { PreferredLanguage = "ru" },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Исправленный ответ.",
            CorrectedContent = "Исправленный ответ.",
            IsCritiqueApproved = false,
            CritiqueFeedback = "Need a calmer tone.",
            InteractionPolicy = new Helper.Api.Conversation.InteractionState.InteractionPolicyProjection(
                PreferAnswerFirst: true,
                SoftenClarification: true,
                CompressStructure: true,
                UseCalmTone: true,
                IncreaseReassurance: true,
                NarrowRepairScope: true,
                SuppressGenericNextStep: true)
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Equal("interaction", context.RepairDriver);
    }
}
