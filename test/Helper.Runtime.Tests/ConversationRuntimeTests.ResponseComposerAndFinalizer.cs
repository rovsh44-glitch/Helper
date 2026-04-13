using Helper.Api.Conversation;
using Helper.Api.Backend.Application;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public partial class ConversationRuntimeTests
{
    [Fact]
    public async Task ChatTurnFinalizer_AppendsSources_WhenProvided()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService());
        var context = new ChatTurnContext
        {
            TurnId = "turn-1",
            Request = new ChatRequestDto("test", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Base answer",
            IsCritiqueApproved = true
        };

        context.Sources.Add("https://example.org/a");
        context.Sources.Add("https://example.org/b");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("Base answer", context.FinalResponse);
        Assert.Contains("Sources:", context.FinalResponse);
        Assert.Contains("https://example.org/a", context.FinalResponse);
    }

    [Fact]
    public async Task ChatTurnFinalizer_AppendsBestEffortNote_WhenForced()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService());
        var context = new ChatTurnContext
        {
            TurnId = "turn-best-effort",
            Request = new ChatRequestDto("help", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Baseline output",
            IsCritiqueApproved = true,
            ForceBestEffort = true,
            ForceBestEffortReason = "Clarification budget exhausted."
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("Best-effort mode", context.FinalResponse);
        Assert.True(context.Confidence <= 0.5);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesEvidenceBriefComposer_WithoutProtocolFrame()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-structured",
            Request = new ChatRequestDto("Сделай план миграции", null, 12, null),
            Conversation = new ConversationState("conv-structured"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "1. Assess\n2. Migrate\n3. Validate",
            IsCritiqueApproved = true
        };
        context.Sources.Add("https://example.org/migration");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.DoesNotContain("Understanding:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Понимание:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Solution:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Решение:", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("1. Assess", context.FinalResponse, StringComparison.Ordinal);
        Assert.True(
            context.FinalResponse.Contains("Next step:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Useful follow-up:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("If you want to continue:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Следующий шаг:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Что можно сделать дальше:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Если продолжим, следующий шаг:", StringComparison.Ordinal));
        Assert.True(
            context.FinalResponse.Contains("Sources:", StringComparison.Ordinal) ||
            context.FinalResponse.Contains("Источники:", StringComparison.Ordinal));
    }

    [Fact]
    public void ResponseComposer_DoesNotDuplicateSourcesSection_WhenPreparedOutputAlreadyContainsSources()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-no-dup-sources",
            Request = new ChatRequestDto("Проанализируй статью", null, 12, null),
            Conversation = new ConversationState("conv-no-dup-sources"),
            History = Array.Empty<ChatMessageDto>()
        };
        context.Sources.Add("https://example.org/article");

        var result = composer.Compose(
            context,
            "Research request: example\nSources:\n- https://example.org/article");

        Assert.Equal(1, CountOccurrences(result, "Источники:") + CountOccurrences(result, "Sources:"));
        Assert.DoesNotContain("Understanding:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Понимание:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Solution:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Решение:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_UsesFreeformShortMode_ForSimpleTurnWithoutPromptMirroring()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-freeform-short",
            Request = new ChatRequestDto("Can you explain retries?", null, 12, null),
            Conversation = new ConversationState("conv-freeform-short")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var result = composer.Compose(context, "Use retries when the failure is transient.");

        Assert.Equal("Use retries when the failure is transient.", result);
        Assert.DoesNotContain("You asked:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Understanding:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Solution:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Next step:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_CollapsesRepeatedSentenceBlock()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-no-dup-block",
            Request = new ChatRequestDto("Analyze this page", null, 12, null),
            Conversation = new ConversationState("conv-no-dup-block")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        const string repeated = "The article discusses the challenge of measuring AGI progress by focusing on cognitive abilities. It notes that the competition aims to assess evaluation approaches. The page also appears to include parser errors rather than reliable article content. The practical issue is that the source was not read cleanly. The article discusses the challenge of measuring AGI progress by focusing on cognitive abilities. It notes that the competition aims to assess evaluation approaches. The page also appears to include parser errors rather than reliable article content. The practical issue is that the source was not read cleanly.";

        var result = composer.Compose(context, repeated);

        Assert.Equal(
            "The article discusses the challenge of measuring AGI progress by focusing on cognitive abilities. It notes that the competition aims to assess evaluation approaches. The page also appears to include parser errors rather than reliable article content. The practical issue is that the source was not read cleanly.",
            result);
    }

    [Fact]
    public void ResponseComposer_RewritesBenchmarkResearchFallback_IntoRequiredSections()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-fallback",
            Request = new ChatRequestDto(
                "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion"),
            Conversation = new ConversationState("conv-local-first-benchmark-fallback")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };

        var result = composer.Compose(
            context,
            "I could not verify enough grounded sources to answer this responsibly in the current runtime.\nNo verifiable sources were retrieved for this topic.\nSo the safe conclusion is to treat the question as unresolved until the source path works or the relevant text is provided explicitly.\nMy view: it is better to stop at this evidence limit than to smooth it over with a confident-looking but unverified answer.");

        Assert.Contains("## Local Findings", result, StringComparison.Ordinal);
        Assert.Contains("## Web Findings", result, StringComparison.Ordinal);
        Assert.Contains("## Sources", result, StringComparison.Ordinal);
        Assert.Contains("## Analysis", result, StringComparison.Ordinal);
        Assert.Contains("## Conclusion", result, StringComparison.Ordinal);
        Assert.Contains("## Opinion", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Research request:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("restore the search backend", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_NormalizesBenchmarkAnswer_IntoRequiredSections_WhenDraftIgnoresFormat()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-general",
            Request = new ChatRequestDto(
                "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и когда нужны оба типа тренировок.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion"),
            Conversation = new ConversationState("conv-local-first-benchmark-general")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true
        };

        var result = composer.Compose(
            context,
            "**Односторонние и сложные-type load**\n\nvery strong bodies simultaneously improve anaerobic fitness.\n\nЕсли продолжим, следующий шаг:\nЕсли что-то ещё мимо, укажите конкретный фрагмент.");

        Assert.Contains("## Local Findings", result, StringComparison.Ordinal);
        Assert.Contains("## Web Findings", result, StringComparison.Ordinal);
        Assert.Contains("## Sources", result, StringComparison.Ordinal);
        Assert.Contains("## Analysis", result, StringComparison.Ordinal);
        Assert.Contains("## Conclusion", result, StringComparison.Ordinal);
        Assert.Contains("## Opinion", result, StringComparison.Ordinal);
        Assert.Contains("Live web-поиск в этом ходе не использовался", result, StringComparison.Ordinal);
        Assert.Contains("смешанный языковой шум", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Если продолжим, следующий шаг:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_RebuildsLowQualityHeadedBenchmarkAnswer_InsteadOfPreservingIt()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-headed-low-quality",
            Request = new ChatRequestDto(
                "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и когда нужны оба типа тренировок.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion"),
            Conversation = new ConversationState("conv-local-first-benchmark-headed-low-quality")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true
        };

        const string noisyDraft = """
## Local Findings
Аэробные нагрузки связаны с тяжести воздуха, а анаэробные — с тяжести газа.

## Web Findings
Theoretical Physics V2 explains this with resonance.

## Sources
- Theoretical Physics V2

## Analysis
Aэробные нагрузки обсуждаются в contextе airport loads, 比较之下 это стабильнее.

## Conclusion
В绝大多数 случаев это зависит от влажности.

## Opinion
В общем случае это удобно для有些人.
""";

        var result = composer.Compose(context, noisyDraft);

        Assert.Contains("## Local Findings", result, StringComparison.Ordinal);
        Assert.Contains("## Analysis", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Theoretical Physics V2 explains this", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("airport loads", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("有些人", result, StringComparison.Ordinal);
        Assert.Contains("неподтверждённым", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_RebuildsPlaceholderSourceBenchmarkAnswer_WithoutFakeLinks()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-placeholder-links",
            Request = new ChatRequestDto(
                "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и когда нужны оба типа тренировок.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion"),
            Conversation = new ConversationState("conv-local-first-benchmark-placeholder-links")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            ResolvedLiveWebRequirement = "no_web_needed"
        };

        const string fabricatedDraft = """
## Local Findings
Aerobics refers to a physical workout, while anatheldral training is a mental method.

## Web Findings
Several sources provide clear explanations: [Link 1](https://example.com) and [Link 2](https://example.com).

## Sources
- Local Library Resources: "Understanding Aerobics" by Author A
- Academic Articles: "The Role of Anatheldral Training in Mental Health" by Author B

## Analysis
Aerobics is physical, anatheldral training is cognitive.

## Conclusion
Both are necessary.

## Opinion
They should be combined.
""";

        var result = composer.Compose(context, fabricatedDraft);

        Assert.Contains("## Local Findings", result, StringComparison.Ordinal);
        Assert.Contains("Live web-поиск в этом ходе не использовался", result, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.com", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Link 1", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Local Library Resources", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Academic Articles", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_PreservesTopicalFallbackBody_WhenBenchmarkDraftHasTopicButNoSources()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-topical-fallback",
            Request = new ChatRequestDto(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion"),
            Conversation = new ConversationState("conv-local-first-benchmark-topical-fallback")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true
        };

        const string groundedDraft = "Данные о терапии красным светом для восстановления после тренировок выглядят неоднородными: небольшие исследования иногда показывают снижение боли и субъективное ускорение восстановления, но размер выборок и качество дизайна часто ограничены. Это скорее дополнительный инструмент, чем надёжно подтверждённая основа восстановления.\n\nUncertainty: no verifiable source anchors were found for factual claims.";

        var result = composer.Compose(context, groundedDraft);

        Assert.Contains("## Analysis", result, StringComparison.Ordinal);
        Assert.Contains("терапии красным светом", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("неоднород", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("базовый локальный каркас", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("restore the search backend", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_RebuildsEvidenceBackedBenchmarkAnswer_WhenStrongMedicalEvidenceExists()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-evidence-rebuild",
            Request = new ChatRequestDto(
                "Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion\nAnswer in Russian."),
            Conversation = new ConversationState("conv-local-first-benchmark-evidence-rebuild")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            IsLocalFirstBenchmarkTurn = true,
            GroundingStatus = "grounded",
            CitationCoverage = 1,
            VerifiedClaims = 4,
            TotalClaims = 4,
            ResolvedTurnLanguage = "ru"
        };
        context.Sources.Add("https://pmc.ncbi.nlm.nih.gov/articles/PMC6836017");
        context.Sources.Add("https://pubmed.ncbi.nlm.nih.gov/39408357");
        context.Sources.Add("https://mayoclinic.org/healthy-lifestyle/nutrition-and-healthy-eating/expert-answers/intermittent-fasting/faq-20441303");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            1,
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC6836017",
            "Effectiveness of Intermittent Fasting and Time-Restricted Feeding Compared to Continuous Energy Restriction for Weight Loss",
            "Review comparing intermittent fasting and time-restricted feeding for weight loss outcomes.",
            Passages: new[]
            {
                new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://pmc.ncbi.nlm.nih.gov/articles/PMC6836017", "IF review", null, "Intermittent fasting and time-restricted feeding are better supported for weight loss than for a universal claim of preserving lean mass.")
            }));
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            2,
            "https://pubmed.ncbi.nlm.nih.gov/39408357",
            "The Effects of Time-Restricted Eating on Fat Loss in Adults with Overweight and Obese Depend upon the Eating Window and Intervention Strategies",
            "Systematic review and meta-analysis focused on fat loss and intervention strategy dependence.",
            Passages: new[]
            {
                new EvidencePassage("e2:p1", 2, 1, "2:p1", "https://pubmed.ncbi.nlm.nih.gov/39408357", "TRE review", null, "The effect on body composition depends on intervention strategy and eating window rather than supporting a universal no-muscle-loss claim.")
            }));

        const string weakDraft = "Research request: intermittent fasting lean mass body composition. The ketogenic diet article says the United States government website belongs to the United States government.";

        var result = composer.Compose(context, weakDraft);

        Assert.Contains("снижения массы тела", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("мышечной массы", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("passage-level", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("в таком состоянии системы лучше дать осторожный отказ", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("вопрос остаётся неподтверждённым", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_UsesMixedEvidenceLanguage_WhenPassagesExistButGroundingHasContradictions()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-mixed-evidence",
            Request = new ChatRequestDto(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion\nAnswer in Russian."),
            Conversation = new ConversationState("conv-local-first-benchmark-mixed-evidence")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            IsLocalFirstBenchmarkTurn = true,
            GroundingStatus = "grounded_with_contradictions",
            CitationCoverage = 1,
            VerifiedClaims = 3,
            TotalClaims = 4,
            ResolvedTurnLanguage = "ru"
        };
        context.Sources.Add("https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863");
        context.Sources.Add("https://link.springer.com/article/10.1007/s10103-025-04318-w");
        context.Sources.Add("https://mdpi.com/2076-3417/13/5/3147");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            1,
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863",
            "The Effect of Photobiomodulation Therapy on Muscle Performance in Volleyball and Football Players",
            "Meta-analysis of randomized controlled trials.",
            Passages: new[]
            {
                new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863", "PBM meta-analysis", null, "Photobiomodulation therapy improved some recovery and performance outcomes, but protocols varied across studies.")
            }));
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            2,
            "https://link.springer.com/article/10.1007/s10103-025-04318-w",
            "A systematic review on whole-body photobiomodulation for exercise performance and recovery",
            "Systematic review on performance and recovery outcomes.",
            Passages: new[]
            {
                new EvidencePassage("e2:p1", 2, 1, "2:p1", "https://link.springer.com/article/10.1007/s10103-025-04318-w", "PBM review", null, "The review suggests possible recovery benefits, but evidence remains heterogeneous and protocol-sensitive.")
            }));

        const string weakDraft = "Research request: photobiomodulation red light therapy muscle recovery after training. Generic fallback draft with weak phrasing.";

        var result = composer.Compose(context, weakDraft);

        Assert.Contains("page/passage evidence", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("неоднород", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("page/document extraction оказалось недостаточно устойчивым", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_DescribesPartialWebVerification_WhenSourcesExistButExtractionStayedLimited()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-partial-web",
            Request = new ChatRequestDto(
                "Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion"),
            Conversation = new ConversationState("conv-local-first-benchmark-partial-web")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            GroundingStatus = "degraded",
            CitationCoverage = 1,
            VerifiedClaims = 3
        };
        context.Sources.Add("https://news.un.org/ru/story/2024/01/1448497");
        context.Sources.Add("https://unicef.org/eca/ru/example");
        context.Sources.Add("https://who.int/europe/ru/news/item/example");
        context.UncertaintyFlags.Add("uncertainty.extraction_limited");

        const string draft = "Удалось удержать только базовый локальный каркас по теме: случаи кори выросли, а вакцинация остаётся основной мерой профилактики.\n\nUncertainty: sources were found but page/document extraction remained limited.";

        var result = composer.Compose(context, draft);

        Assert.Contains("источники были найдены", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("частич", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("внешняя проверка не состоялась", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_RebuildsMixedLanguageBenchmarkAnswer_WhenRussianIsRequired()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-local-first-benchmark-mixed-language",
            Request = new ChatRequestDto(
                "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и когда нужны оба типа тренировок.",
                null,
                12,
                "Use exactly these markdown headings in this exact order:\n## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion\nAnswer in Russian."),
            Conversation = new ConversationState("conv-local-first-benchmark-mixed-language")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            IsLocalFirstBenchmarkTurn = true,
            LocalFirstBenchmarkMode = LocalFirstBenchmarkMode.LocalOnly,
            ResolvedTurnLanguage = "ru"
        };

        const string mixedDraft = """
## Local Findings
Этот ход остался в local-first режиме.

## Web Findings
Live web-поиск в этом ходе не использовался.

## Sources
- Live web-источники в этом ходе не использовались.

## Analysis
**Answer:**

Aerobic training involves steady cardio work, while anaerobic training focuses on short high-intensity effort.

## Conclusion
Итог пока черновой.

## Opinion
Моё мнение: draft still needs cleanup.
""";

        var result = composer.Compose(context, mixedDraft);

        Assert.Contains("## Analysis", result, StringComparison.Ordinal);
        Assert.Contains("смешанный языковой шум", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("**Answer:**", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aerobic training involves", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseComposer_UsesBulletAnswerShape_ForPlainMultiSentenceAnswer()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-bullets-shape",
            Request = new ChatRequestDto("Summarize the rollout", null, 12, null),
            Conversation = new ConversationState("conv-bullets-shape")
            {
                PreferredLanguage = "en",
                DefaultAnswerShape = "bullets"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var result = composer.Compose(
            context,
            "Start with a small canary rollout. Watch latency and error rate before widening the blast radius.");

        Assert.Contains("- Start with a small canary rollout.", result, StringComparison.Ordinal);
        Assert.Contains("- Watch latency and error rate before widening the blast radius.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_KeepsShortAnswerAsParagraph_WhenBulletPreferenceWouldOverformat()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-anti-overformat-short",
            Request = new ChatRequestDto("What is retry jitter?", null, 12, null),
            Conversation = new ConversationState("conv-anti-overformat-short")
            {
                PreferredLanguage = "en",
                DefaultAnswerShape = "bullets"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var result = composer.Compose(context, "Retry jitter randomizes retry delays to reduce synchronized retry spikes.");

        Assert.Equal("Retry jitter randomizes retry delays to reduce synchronized retry spikes.", result);
        Assert.DoesNotContain("- ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_PromotesProceduralParagraph_ToStructuredSteps()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-anti-overformat-procedural",
            Request = new ChatRequestDto("Give me a step-by-step rollout plan", null, 12, null),
            Conversation = new ConversationState("conv-anti-overformat-procedural")
            {
                PreferredLanguage = "en",
                PreferredStructure = "step_by_step",
                DefaultAnswerShape = "paragraph"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var result = composer.Compose(
            context,
            "Start with a small canary rollout. Watch error rate and latency closely. Expand only after the first stage stays stable.");

        Assert.Contains("1. Start with a small canary rollout.", result, StringComparison.Ordinal);
        Assert.Contains("2. Watch error rate and latency closely.", result, StringComparison.Ordinal);
        Assert.Contains("3. Expand only after the first stage stays stable.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_CollapsesUnnecessaryListification_ForShortExplanatoryAnswer()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-anti-overformat-collapse",
            Request = new ChatRequestDto("Explain retry jitter briefly", null, 12, null),
            Conversation = new ConversationState("conv-anti-overformat-collapse")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var result = composer.Compose(
            context,
            "- Retry jitter randomizes retry delays.\n- It reduces synchronized retry spikes.");

        Assert.DoesNotContain("\n- ", result, StringComparison.Ordinal);
        Assert.Contains("Retry jitter randomizes retry delays; then It reduces synchronized retry spikes.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_UsesStructuredAnswerMode_ForChecklistWithoutProtocolFrame()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-structured-answer",
            Request = new ChatRequestDto("Сделай короткий rollout", null, 12, null),
            Conversation = new ConversationState("conv-structured-answer")
            {
                PreferredLanguage = "ru"
            },
            History = Array.Empty<ChatMessageDto>(),
            NextStep = "Если нужно, я могу развернуть каждый шаг в отдельный чек-лист."
        };

        var result = composer.Compose(context, "1. Assess\n2. Migrate\n3. Validate");

        Assert.Contains("1. Assess", result, StringComparison.Ordinal);
        Assert.True(
            result.Contains("Следующий шаг:", StringComparison.Ordinal) ||
            result.Contains("Что можно сделать дальше:", StringComparison.Ordinal) ||
            result.Contains("Если продолжим, следующий шаг:", StringComparison.Ordinal));
        Assert.DoesNotContain("Понимание:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Решение:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Вы попросили:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_DerivesPlanningNextStep_WhenChecklistHasNoExplicitCta()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-planning-derived-next-step",
            Request = new ChatRequestDto("Сделай rollout plan", null, 12, null),
            Conversation = new ConversationState("conv-planning-derived-next-step")
            {
                PreferredLanguage = "ru",
                PreferredStructure = "step_by_step"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var result = composer.Compose(context, "1. Assess\n2. Migrate\n3. Validate");

        Assert.NotNull(context.NextStep);
        Assert.Contains(context.NextStep!, new[]
        {
            "Могу превратить это в milestones, чек-лист или самый первый исполнимый шаг.",
            "Могу расставить приоритеты в плане, ужать его в короткий checklist или развернуть один этап подробнее.",
            "Могу разбить это на owner-by-owner задачи или на короткую последовательность rollout-шагов."
        });
        Assert.Contains(context.NextStep!, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurnFinalizer_OmitsNextStep_ForSimpleNonFactualAnswer()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-no-next-step-needed",
            Request = new ChatRequestDto("Explain retries briefly", null, 10, null),
            Conversation = new ConversationState("conv-no-next-step-needed")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Use retries when the failure is transient.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.True(string.IsNullOrWhiteSpace(context.NextStep));
        Assert.Equal("Use retries when the failure is transient.", context.FinalResponse);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesResearchSpecificNextStep_ForGroundedResearchTurn()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-research-next-step",
            Request = new ChatRequestDto("Research migration options", null, 12, null),
            Conversation = new ConversationState("conv-research-next-step")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Migration option A minimizes downtime.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };
        context.Sources.Add("https://example.org/source-a");
        context.Sources.Add("https://example.org/source-b");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.NotNull(context.NextStep);
        Assert.Contains(context.NextStep!, new[]
        {
            "Name the exact claim to verify, and I will focus retrieval on that point.",
            "I can narrow this to one factual question and gather stronger sources around it.",
            "If you want a grounded answer, point me to the most important fact to verify first."
        });
        Assert.False(IntentAwareNextStepPolicy.IsGenericTemplate(context.NextStep));
    }

    [Fact]
    public async Task ChatTurnFinalizer_RewritesResearchTurn_IntoSourceSpecificGroundedSynthesis()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-research-grounded-synthesis",
            Request = new ChatRequestDto("Compare retry and timeout guidance", null, 12, null),
            Conversation = new ConversationState("conv-research-grounded-synthesis")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Retries should use exponential backoff for transient faults. Timeouts should be coordinated with retries to avoid overload.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };
        context.Sources.Add("https://learn.microsoft.com/retries");
        context.Sources.Add("https://aws.amazon.com/timeouts");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 1,
            Url: "https://learn.microsoft.com/retries",
            Title: "Retry guidance",
            Snippet: "Retries should use exponential backoff for transient faults.",
            IsFallback: false));
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 2,
            Url: "https://aws.amazon.com/timeouts",
            Title: "Timeout guidance",
            Snippet: "Timeouts should be coordinated with retries to avoid overload.",
            IsFallback: false));

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("The strongest supported reading is this:", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Retry guidance [1] emphasizes", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("while Timeout guidance [2] emphasizes", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Overview", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesAnalystMode_ForDocumentAnalysisRequest()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-document-analysis",
            Request = new ChatRequestDto("Проанализируй и предоставь своё мнение: https://example.org/paper.pdf", null, 12, null),
            Conversation = new ConversationState("conv-document-analysis")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };
        context.Sources.Add("https://example.org/paper.pdf");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 1,
            Url: "https://example.org/paper.pdf",
            Title: "Attention Residuals",
            Snippet: "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.",
            IsFallback: false,
            EvidenceKind: "fetched_document_pdf",
            PublishedAt: "2026",
            Passages: new[]
            {
                new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://example.org/paper.pdf", "Attention Residuals", "2026", "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.")
            }));

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("My view:", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Main limitation:", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHub Advanced Security", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Attention Residuals [1:p1]", context.FinalResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesPassageLevelGroundingLabels_WhenEvidencePassagesExist()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-research-passage-grounding",
            Request = new ChatRequestDto("Compare retry and timeout guidance", null, 12, null),
            Conversation = new ConversationState("conv-research-passage-grounding")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Retries should use exponential backoff for transient faults. Timeouts should be coordinated with retries to avoid overload.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };
        context.Sources.Add("https://learn.microsoft.com/retries");
        context.Sources.Add("https://aws.amazon.com/timeouts");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 1,
            Url: "https://learn.microsoft.com/retries",
            Title: "Retry guidance",
            Snippet: "Summary snippet.",
            IsFallback: false,
            EvidenceKind: "fetched_page",
            PublishedAt: "2026-03-21",
            Passages: new[]
            {
                new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://learn.microsoft.com/retries", "Retry guidance", "2026-03-21", "Retries should use exponential backoff for transient faults.")
            }));
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 2,
            Url: "https://aws.amazon.com/timeouts",
            Title: "Timeout guidance",
            Snippet: "Summary snippet.",
            IsFallback: false,
            EvidenceKind: "fetched_page",
            PublishedAt: "2026-03-21",
            Passages: new[]
            {
                new EvidencePassage("e2:p1", 2, 1, "2:p1", "https://aws.amazon.com/timeouts", "Timeout guidance", "2026-03-21", "Timeouts should be coordinated with retries to avoid overload.")
            }));

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("[1:p1]", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("[2:p1]", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains(context.ClaimGroundings, claim => claim.EvidenceCitationLabel == "1:p1");
        Assert.Contains(context.ClaimGroundings, claim => claim.EvidenceCitationLabel == "2:p1");
    }

    [Fact]
    public async Task ChatTurnFinalizer_SurfacesResearchDisagreement_Explicitly()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-research-disagreement",
            Request = new ChatRequestDto("Compare release-year claims", null, 12, null),
            Conversation = new ConversationState("conv-research-disagreement")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Release note A says PostgreSQL 16 was released in 2023 with improved replication. Release note B says PostgreSQL 16 was released in 2018 with improved replication.",
            IsCritiqueApproved = true,
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IsFactualPrompt = true
        };
        context.Sources.Add("https://source-a.example/release");
        context.Sources.Add("https://source-b.example/release");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 1,
            Url: "https://source-a.example/release",
            Title: "Release note A",
            Snippet: "PostgreSQL 16 was released in 2023 with improved replication.",
            IsFallback: false,
            EvidenceKind: "fetched_page",
            Passages: new[]
            {
                new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://source-a.example/release", "Release note A", null, "PostgreSQL 16 was released in 2023 with improved replication.")
            }));
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 2,
            Url: "https://source-b.example/release",
            Title: "Release note B",
            Snippet: "PostgreSQL 16 was released in 2018 with improved replication.",
            IsFallback: false,
            EvidenceKind: "fetched_page",
            Passages: new[]
            {
                new EvidencePassage("e2:p1", 2, 1, "2:p1", "https://source-b.example/release", "Release note B", null, "PostgreSQL 16 was released in 2018 with improved replication.")
            }));

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("The main disagreement is explicit:", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("unresolved", context.FinalResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("grounded_with_contradictions", context.GroundingStatus);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesRepairSpecificNextStep_WhenCritiqueNeedsCorrection()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-repair-next-step",
            Request = new ChatRequestDto("Rewrite the summary", null, 10, null),
            Conversation = new ConversationState("conv-repair-next-step")
            {
                PreferredLanguage = "en"
            },
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Initial answer",
            CorrectedContent = "Corrected answer",
            CritiqueFeedback = "Too vague.",
            IsCritiqueApproved = false,
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.NotNull(context.NextStep);
        Assert.Contains(context.NextStep!, new[]
        {
            "If one part still misses the mark, point to the exact section and I will rewrite only that part.",
            "I can restate this in a different format or tighten only the piece that feels off.",
            "If you want, I can adjust the answer around the specific misunderstanding instead of redoing everything."
        });
    }

}
