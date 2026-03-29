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

public class ConversationRuntimeTests
{
    [Fact]
    public void InMemoryConversationStore_PreservesConversationState()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate(null);
        state.LongTermMemoryEnabled = true;

        store.AddMessage(state, new ChatMessageDto("user", "remember: answer in russian", DateTimeOffset.UtcNow));
        store.AddMessage(state, new ChatMessageDto("assistant", "ok", DateTimeOffset.UtcNow));
        store.AddMessage(state, new ChatMessageDto("user", "нужно закончить refactoring", DateTimeOffset.UtcNow));

        var same = store.GetOrCreate(state.Id);
        Assert.Equal(state.Id, same.Id);
        Assert.Contains("answer in russian", same.Preferences);
        Assert.NotEmpty(same.OpenTasks);
    }

    [Fact]
    public void InMemoryConversationStore_CapturesRememberDirective_WithBracketPrefix()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("prefixed-memory");
        state.LongTermMemoryEnabled = true;

        store.AddMessage(state, new ChatMessageDto("user", "[1] remember: answer concise", DateTimeOffset.UtcNow, "t-prefixed"));

        Assert.Contains("answer concise", state.Preferences);
        Assert.Contains(state.MemoryItems, item => item.Content.Equals("answer concise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InMemoryConversationStore_PersistsConversationState_WhenPathConfigured()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-conversations-{Guid.NewGuid():N}.json");
        try
        {
            using (var store = new InMemoryConversationStore(tempPath))
            {
                var state = store.GetOrCreate("persisted-conversation");
                state.Formality = "formal";
                state.DomainFamiliarity = "expert";
                state.PreferredStructure = "step_by_step";
                state.Warmth = "warm";
                state.Enthusiasm = "high";
                state.Directness = "direct";
                state.DefaultAnswerShape = "bullets";
                state.SearchLocalityHint = "Tashkent";
                state.PersonalMemoryConsentGranted = true;
                state.PersonalMemoryConsentAt = DateTimeOffset.UtcNow;
                state.SessionMemoryTtlMinutes = 300;
                state.TaskMemoryTtlHours = 48;
                state.LongTermMemoryTtlDays = 30;
                state.LongTermMemoryEnabled = true;
                state.SearchSessions["main"] = new SearchSessionState(
                    BranchId: "main",
                    RootQuery: "latest climate pact announcement",
                    LastUserQuery: "what about Reuters coverage?",
                    LastEffectiveQuery: "latest climate pact announcement. Follow-up: what about Reuters coverage?",
                    LastTurnId: "turn-2",
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    CategoryHint: "news",
                    SourceUrls: new[] { "https://reuters.com/world/climate-pact" },
                    CitationLineage: new[]
                    {
                        new CitationLineageEntry(
                            "lin_1",
                            "reuters.com/world/climate-pact|fetched_page|passage:p1",
                            "1:p1",
                            "https://reuters.com/world/climate-pact",
                            "Leaders sign climate pact - Reuters",
                            "fetched_page",
                            "2026-03-21",
                            "p1",
                            1,
                            "turn-1",
                            "turn-2",
                            2)
                    },
                    EvidenceMemory: new[]
                    {
                        new SelectiveEvidenceMemoryEntry(
                            "mem_1",
                            "reuters.com/world/climate-pact|verified_passage|passage:p1",
                            "https://reuters.com/world/climate-pact",
                            "Leaders sign climate pact - Reuters",
                            "verified_passage",
                            "Negotiators agreed on a climate pact after overnight talks.",
                            "p1",
                            1,
                            "untrusted_web_content",
                            "turn-1",
                            "turn-2",
                            2)
                    },
                    ContinuationDepth: 1,
                    LastReuseReason: "citation_reference",
                    LastInputMode: "voice");
                store.AddMessage(state, new ChatMessageDto("user", "remember: prefer concise answers", DateTimeOffset.UtcNow, "turn-1"));
                store.AddMessage(state, new ChatMessageDto("assistant", "ack", DateTimeOffset.UtcNow, "turn-1"));
            }

            var restoredStore = new InMemoryConversationStore(tempPath);
            var found = restoredStore.TryGet("persisted-conversation", out var restoredState);

            Assert.True(found);
            Assert.NotNull(restoredState);
            Assert.True(restoredState.Messages.Count >= 2);
            Assert.Equal("formal", restoredState.Formality);
            Assert.Equal("expert", restoredState.DomainFamiliarity);
            Assert.Equal("step_by_step", restoredState.PreferredStructure);
            Assert.Equal("warm", restoredState.Warmth);
            Assert.Equal("high", restoredState.Enthusiasm);
            Assert.Equal("direct", restoredState.Directness);
            Assert.Equal("bullets", restoredState.DefaultAnswerShape);
            Assert.Equal("Tashkent", restoredState.SearchLocalityHint);
            Assert.True(restoredState.PersonalMemoryConsentGranted);
            Assert.Equal(300, restoredState.SessionMemoryTtlMinutes);
            Assert.Equal(48, restoredState.TaskMemoryTtlHours);
            Assert.Equal(30, restoredState.LongTermMemoryTtlDays);
            Assert.NotEmpty(restoredState.MemoryItems);
            Assert.True(restoredState.SearchSessions.TryGetValue("main", out var restoredSearchSession));
            Assert.NotNull(restoredSearchSession);
            Assert.Equal("latest climate pact announcement", restoredSearchSession.RootQuery);
            Assert.Single(restoredSearchSession.SourceUrls);
            Assert.Single(restoredSearchSession.CitationLineage);
            Assert.Single(restoredSearchSession.EffectiveEvidenceMemory);
            Assert.Equal("voice", restoredSearchSession.LastInputMode);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void InMemoryConversationStore_LoadsLegacySnapshot_AndMigratesToCurrentSchema()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-legacy-conversations-{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new[]
            {
                new
                {
                    Id = "legacy-conversation",
                    Messages = new[] { new ChatMessageDto("user", "hello", DateTimeOffset.UtcNow, "turn-1", BranchId: "main") },
                    UpdatedAt = DateTimeOffset.UtcNow,
                    RollingSummary = (string?)null,
                    Preferences = new[] { "prefer concise" },
                    OpenTasks = Array.Empty<string>(),
                    LongTermMemoryEnabled = true,
                    PreferredLanguage = "ru",
                    DetailLevel = "balanced",
                    ActiveTurnId = (string?)null,
                    ActiveTurnUserMessage = (string?)null,
                    ActiveTurnStartedAt = (DateTimeOffset?)null,
                    ActiveBranchId = "main",
                    Branches = new[] { new BranchDescriptor("main", null, null, DateTimeOffset.UtcNow) }
                }
            };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(legacy));

            var store = new InMemoryConversationStore(tempPath);
            var loaded = store.TryGet("legacy-conversation", out var state);

            Assert.True(loaded);
            Assert.NotNull(state);
            Assert.True(state.Messages.Count >= 1);
            Assert.Equal("neutral", state.Formality);
            Assert.Equal("intermediate", state.DomainFamiliarity);
            Assert.Equal("auto", state.PreferredStructure);
            Assert.Equal("balanced", state.Warmth);
            Assert.Equal("balanced", state.Enthusiasm);
            Assert.Equal("balanced", state.Directness);
            Assert.Equal("auto", state.DefaultAnswerShape);
            Assert.NotEmpty(state.MemoryItems);

            var migratedRaw = File.ReadAllText(tempPath);
            using var doc = JsonDocument.Parse(migratedRaw);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal(6, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

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

    [Fact]
    public async Task TurnExecutionStageRunner_PreservesImmediateOutput_WithoutDuplicatingStreamedToken()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("conv-immediate-stream");
        var context = new ChatTurnContext
        {
            TurnId = "turn-immediate-stream",
            Request = new ChatRequestDto("Исследуй статью", "conv-immediate-stream", 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            LifecycleState = TurnLifecycleState.Understand,
            ExecutionState = TurnExecutionState.Planned,
            ExecutionOutput = "Research request: https://example.org/article",
            Intent = new IntentAnalysis(IntentType.Research, "test")
        };

        var runner = new TurnExecutionStageRunner(
            store,
            Mock.Of<IChatTurnPlanner>(),
            new StubChatTurnExecutor(new TokenChunk(
                ChatStreamChunkType.Token,
                context.ExecutionOutput,
                1,
                DateTimeOffset.UtcNow)),
            Mock.Of<IChatTurnCritic>(),
            Mock.Of<IChatTurnFinalizer>(),
            Mock.Of<IOutputExfiltrationGuard>(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            Mock.Of<ITurnStagePolicy>(),
            logger: NullLogger<TurnExecutionStageRunner>.Instance);

        var emitted = new List<TokenChunk>();
        await foreach (var chunk in runner.ExecuteStreamAsync(state, context, "main", CancellationToken.None))
        {
            emitted.Add(chunk);
        }

        Assert.Single(emitted);
        Assert.Equal("Research request: https://example.org/article", context.ExecutionOutput);
    }

    [Fact]
    public async Task ChatTurnPlanner_RequestsConfirmation_ForRiskyPrompt()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Unknown, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService());
        var context = new ChatTurnContext
        {
            TurnId = "risk-turn",
            Request = new ChatRequestDto("удали все временные файлы", "conv-risk", 12, null),
            Conversation = new ConversationState("conv-risk"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.RequiresClarification);
        Assert.True(context.RequiresConfirmation);
        Assert.Equal("ru", context.ResolvedTurnLanguage);
        Assert.Contains("разруш", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destructive", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatTurnPlanner_UsesSoftBestEffortEntry_ForUnderspecifiedPrompt()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.82,
                "test",
                Array.Empty<string>()));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService());

        var context = new ChatTurnContext
        {
            TurnId = "soft-clarify-turn",
            Request = new ChatRequestDto("помоги", "conv-soft-clarify", 12, null),
            Conversation = new ConversationState("conv-soft-clarify"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.False(context.RequiresConfirmation);
        Assert.True(context.ForceBestEffort);
        Assert.Equal("ru", context.ResolvedTurnLanguage);
        Assert.Contains("кратко", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("подроб", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("план", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("предполож", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("soft_best_effort_entry", context.UncertaintyFlags);
        Assert.DoesNotContain("Уточните основную цель", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClarificationPolicy_OffersHelpfulBranching_ForLowConfidence()
    {
        var policy = new ClarificationPolicy();
        var question = policy.BuildLowConfidenceQuestion(
            new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.32,
                "test",
                Array.Empty<string>()),
            attemptNumber: 1,
            resolvedLanguage: "en");

        Assert.Contains("briefly", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("in depth", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plan", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("best-effort", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClarificationPolicy_UsesHardStop_ForSafetyConfirmation()
    {
        var policy = new ClarificationPolicy();
        var question = policy.BuildQuestion(
            new AmbiguityDecision(true, AmbiguityType.SafetyConfirmation, 0.96, "Potentially destructive intent without explicit confirmation."),
            new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.88,
                "test",
                Array.Empty<string>()),
            attemptNumber: 1,
            resolvedLanguage: "ru");

        Assert.Contains("подтверд", question, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("кратко", question, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("предполож", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesSoftBestEffortEnvelope_ForAmbiguousTurn()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-soft-best-effort",
            Request = new ChatRequestDto("помоги", null, 10, null),
            Conversation = new ConversationState("conv-soft-best-effort"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Начните с фиксации цели и одного ближайшего шага.",
            IsCritiqueApproved = true,
            ForceBestEffort = true,
            ForceBestEffortReason = "Запрос пока слишком общий, поэтому беру самый полезный практический старт.",
            ClarifyingQuestion = "Чтобы попасть точнее, подскажите направление: кратко, подробно или в виде плана? Если хотите, могу начать с разумных допущений и явно пометить предположения.",
            AmbiguityType = nameof(AmbiguityType.Goal),
            ResolvedTurnLanguage = "ru"
        };
        context.UncertaintyFlags.Add("soft_best_effort_entry");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("Дам полезный старт", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Предположения:", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Если захотите скорректировать направление", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Режим разумных допущений:", context.FinalResponse, StringComparison.Ordinal);
        Assert.True(context.Confidence <= 0.5);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class StubChatTurnExecutor : IChatTurnExecutor
    {
        private readonly IReadOnlyList<TokenChunk> _chunks;

        public StubChatTurnExecutor(params TokenChunk[] chunks)
        {
            _chunks = chunks;
        }

        public Task ExecuteAsync(ChatTurnContext context, CancellationToken ct)
            => Task.CompletedTask;

        public async IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(ChatTurnContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in _chunks)
            {
                yield return chunk;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task ChatTurnPlanner_ForcesBestEffort_WhenClarificationBudgetExceeded()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var policy = new ClarificationPolicy(maxClarificationTurns: 1);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            policy,
            new IntentTelemetryService());

        var state = new ConversationState("conv-clarify-budget")
        {
            ConsecutiveClarificationTurns = 1
        };
        var context = new ChatTurnContext
        {
            TurnId = "clarify-budget-turn",
            Request = new ChatRequestDto("help", "conv-clarify-budget", 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.True(context.ForceBestEffort);
        Assert.Contains("clarification_budget_exhausted", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_TriggersAssumptionCheck_ForUnconstrainedRiskyAction()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "assumption-check-turn",
            Request = new ChatRequestDto("Deploy release to production now", "conv-assumption", 12, null),
            Conversation = new ConversationState("conv-assumption"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.RequiresClarification);
        Assert.True(context.RequiresConfirmation);
        Assert.Contains("safe mode", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assumption_check_required", context.UncertaintyFlags);
    }

    [Fact]
    public void AssumptionCheckPolicy_DoesNotTrigger_ForResearchPromptAboutProduction()
    {
        var policy = new AssumptionCheckPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "assumption-research-turn",
            Request = new ChatRequestDto("Исследуй подходы к tracing и метрикам в проде", "conv-assumption-research", 12, null),
            Conversation = new ConversationState("conv-assumption-research"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model")
        };

        var decision = policy.Evaluate(context);

        Assert.False(decision.RequiresClarification);
        Assert.True(string.IsNullOrWhiteSpace(decision.Flag));
    }

    [Fact]
    public async Task ChatTurnPlanner_AppliesFastModeBudget_FromPromptHints()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "fast-budget-turn",
            Request = new ChatRequestDto("Быстро и кратко объясни difference между REST и gRPC", "conv-fast-budget", 12, null),
            Conversation = new ConversationState("conv-fast-budget"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(TurnExecutionMode.Fast, context.ExecutionMode);
        Assert.True(context.ToolCallBudget <= 1);
        Assert.True(context.TokenBudget <= 500);
        Assert.True(context.TimeBudget <= TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task ChatTurnPlanner_UsesLegacyIntent_WhenIntentV2FlagIsDisabled()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Research, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var flags = new Mock<IFeatureFlags>();
        flags.SetupGet(x => x.IntentV2Enabled).Returns(false);

        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy(),
            flags.Object);

        var context = new ChatTurnContext
        {
            TurnId = "legacy-intent-turn",
            Request = new ChatRequestDto("Напиши краткий план релиза", "conv-legacy-intent", 12, null),
            Conversation = new ConversationState("conv-legacy-intent"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal("legacy", context.IntentSource);
        Assert.Contains("legacy:intent_v1", context.IntentSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_ForcesResearchExecution_ForExplicitResearchPrompt()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.35,
                "model_first",
                new[] { "test:low_confidence_unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-research-override-turn",
            Request = new ChatRequestDto("Собери источники по .NET observability", "conv-research-override", 12, null),
            Conversation = new ConversationState("conv-research-override"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Contains("research_intent_forced_from_prompt", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_ForcesGenerateIntent_ForExplicitGoldenTemplatePrompt()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.35,
                "model_first",
                new[] { "test:low_confidence_unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-golden-override-turn",
            Request = new ChatRequestDto("приложение шахматы wpf desktop", "conv-golden-override", 12, null),
            Conversation = new ConversationState("conv-golden-override"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.Equal(IntentType.Generate, context.Intent.Intent);
        Assert.True(context.IntentConfidence >= 0.9);
        Assert.Contains("planner:explicit_golden_template_override", context.IntentSignals);
        Assert.Contains("golden_template_intent_forced_from_prompt", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesCurrentnessPrompt_ToResearchViaLiveWebPolicy()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-live-web-route-turn",
            Request = new ChatRequestDto("What is the current price of BTC today?", "conv-live-web-route", 12, null),
            Conversation = new ConversationState("conv-live-web-route"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.Equal("finance", context.ResolvedLiveWebReason);
        Assert.Contains("planner:live_web_required", context.IntentSignals);
        Assert.Contains("live_web_required_route_override", context.UncertaintyFlags);
        Assert.True(context.IsFactualPrompt);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesLocalFirstBenchmarkLocalOnly_ToResearchWithoutLiveWeb()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy(),
            localFirstBenchmarkPolicy: new LocalFirstBenchmarkPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-benchmark-local-only-turn",
            Request = new ChatRequestDto(
                "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
                "conv-benchmark-local-only",
                12,
                """
You are being evaluated as a local-first librarian-research assistant.
Formatting:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
Answer in Russian.
"""),
            Conversation = new ConversationState("conv-benchmark-local-only"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.IsLocalFirstBenchmarkTurn);
        Assert.Equal(LocalFirstBenchmarkMode.LocalOnly, context.LocalFirstBenchmarkMode);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("no_web_needed", context.ResolvedLiveWebRequirement);
        Assert.Contains("planner:benchmark_local_only", context.IntentSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesLocalFirstBenchmarkSparseCase_ToHelpfulWebResearch()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy(),
            localFirstBenchmarkPolicy: new LocalFirstBenchmarkPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-benchmark-sparse-turn",
            Request = new ChatRequestDto(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                "conv-benchmark-sparse",
                12,
                """
You are being evaluated as a local-first librarian-research assistant.
Workflow:
- Start with local baseline knowledge, use live web cautiously when needed, and explicitly state uncertainty and evidence limits. Aim for at least 2 distinct web sources if they exist.
Formatting:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
Answer in Russian.
"""),
            Conversation = new ConversationState("conv-benchmark-sparse"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.IsLocalFirstBenchmarkTurn);
        Assert.Equal(LocalFirstBenchmarkMode.WebRecommended, context.LocalFirstBenchmarkMode);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_helpful", context.ResolvedLiveWebRequirement);
        Assert.Equal("benchmark_recommended_web", context.ResolvedLiveWebReason);
        Assert.Contains("planner:benchmark_web_recommended", context.IntentSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesLocalFirstBenchmarkSupplementCase_ToRequiredWebResearch()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy(),
            localFirstBenchmarkPolicy: new LocalFirstBenchmarkPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-benchmark-supplement-turn",
            Request = new ChatRequestDto(
                "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.",
                "conv-benchmark-supplement",
                12,
                """
You are being evaluated as a local-first librarian-research assistant.
Workflow:
- Start with local knowledge and local library context, then supplement or verify it with live web evidence before concluding. Aim for at least 2 distinct web sources when web is used.
Formatting:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
Answer in Russian.
"""),
            Conversation = new ConversationState("conv-benchmark-supplement"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.IsLocalFirstBenchmarkTurn);
        Assert.Equal(LocalFirstBenchmarkMode.WebRequired, context.LocalFirstBenchmarkMode);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.Equal("benchmark_mandatory_web", context.ResolvedLiveWebReason);
        Assert.Contains("planner:benchmark_web_required", context.IntentSignals);
        Assert.Contains("benchmark:mandatory_web", context.LiveWebSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_DoesNotOverrideProtectedGeneratePrompt_WhenCurrentnessIsIncidental()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.84,
                "model_first",
                new[] { "test:generate" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-live-web-protected-generate-turn",
            Request = new ChatRequestDto("Create a C# Web API project using the latest .NET version", "conv-live-web-protected-generate", 12, null),
            Conversation = new ConversationState("conv-live-web-protected-generate"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Generate, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.DoesNotContain("planner:live_web_required", context.IntentSignals);
        Assert.DoesNotContain("live_web_required_route_override", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_ForceSearchMode_OverridesProtectedGenerateRoute()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.84,
                "model_first",
                new[] { "test:generate" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-force-search-turn",
            Request = new ChatRequestDto(
                "Create a C# Web API project using the latest .NET version",
                "conv-force-search",
                12,
                null,
                LiveWebMode: "force_search"),
            Conversation = new ConversationState("conv-force-search"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.Equal("user_forced_search", context.ResolvedLiveWebReason);
        Assert.Contains("planner:live_web_forced_by_user", context.IntentSignals);
        Assert.Contains("live_web_force_search_overrode_generate_route", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_NoWebMode_DemotesResearchIntent_AndSkipsLiveWebPromotion()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Research, "test-model"),
                0.88,
                "model_first",
                new[] { "test:research" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-no-web-turn",
            Request = new ChatRequestDto(
                "What is the current price of BTC today?",
                "conv-no-web",
                12,
                null,
                LiveWebMode: "no_web"),
            Conversation = new ConversationState("conv-no-web"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Unknown, context.Intent.Intent);
        Assert.Equal("no_web_needed", context.ResolvedLiveWebRequirement);
        Assert.Equal("user_disabled_web", context.ResolvedLiveWebReason);
        Assert.Contains("planner:live_web_disabled_by_user", context.IntentSignals);
        Assert.Contains("live_web_disabled_by_user", context.UncertaintyFlags);
    }

    [Fact]
    public async Task HybridIntentClassifier_DetectsResearchIntent_FromRules()
    {
        var model = new Mock<IModelOrchestrator>();
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);

        var result = await classifier.ClassifyAsync("Исследуй источники и сравни benchmark по модели", CancellationToken.None);

        Assert.Equal(IntentType.Research, result.Analysis.Intent);
        Assert.True(result.Confidence >= 0.7);
        Assert.Equal("rules", result.Source);
    }

    [Fact]
    public async Task HybridIntentClassifier_OverridesModel_WhenExplicitResearchAndCitationsRequested()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);

        var result = await classifier.ClassifyAsync("Compare SQL and NoSQL trade-offs with citations", CancellationToken.None);

        Assert.Equal(IntentType.Research, result.Analysis.Intent);
        Assert.True(result.Confidence >= 0.7);
        Assert.DoesNotContain("generation:admission_denied", result.Signals);
    }

    [Fact]
    public void HybridAmbiguityDetector_RequiresSafetyConfirmation_ForDestructivePrompt()
    {
        var detector = new HybridAmbiguityDetector();

        var decision = detector.Analyze("удали все временные файлы");

        Assert.True(decision.IsAmbiguous);
        Assert.Equal(AmbiguityType.SafetyConfirmation, decision.Type);
        Assert.True(decision.Confidence >= 0.9);
    }

    [Fact]
    public void IntentTelemetryService_TracksLowConfidenceRate()
    {
        var telemetry = new IntentTelemetryService();

        telemetry.Record(new IntentClassification(new IntentAnalysis(IntentType.Generate, "m"), 0.82, "rules", new[] { "a" }));
        telemetry.Record(new IntentClassification(new IntentAnalysis(IntentType.Research, "m"), 0.35, "fallback", new[] { "b" }));

        var snapshot = telemetry.GetSnapshot();

        Assert.Equal(2, snapshot.TotalClassifications);
        Assert.True(snapshot.AvgConfidence > 0);
        Assert.True(snapshot.LowConfidenceRate > 0);
        Assert.NotEmpty(snapshot.Sources);
        Assert.NotEmpty(snapshot.Intents);
    }

    [Fact]
    public void ConversationMetricsService_ProducesCitationCoverageAlert()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(100, 900, 0, true, false, 0.6, true));
        metrics.RecordTurn(new ConversationTurnMetric(120, 980, 1, true, false, 0.7, true));
        metrics.RecordTurn(new ConversationTurnMetric(140, 1100, 1, true, true, 0.8, true));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(3, snapshot.TotalTurns);
        Assert.True(snapshot.CitationCoverage < 0.70);
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Citation coverage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConversationMetricsService_UsesClaimBasedCoverage_WhenClaimsProvided()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(90, 700, 0, true, false, 0.75, true, VerifiedClaims: 1, TotalClaims: 3));
        metrics.RecordTurn(new ConversationTurnMetric(100, 710, 1, true, true, 0.78, true, VerifiedClaims: 2, TotalClaims: 2));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(3, snapshot.VerifiedClaims);
        Assert.Equal(5, snapshot.TotalClaims);
        Assert.True(snapshot.CitationCoverage > 0.5 && snapshot.CitationCoverage < 0.7);
    }

    [Fact]
    public void ConversationMetricsService_TracksTtftBreakdown()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(
            110,
            900,
            0,
            false,
            false,
            0.72,
            true,
            ModelTtftMs: 70,
            TransportTtftMs: 20,
            EndToEndTtftMs: 110));
        metrics.RecordTurn(new ConversationTurnMetric(
            130,
            920,
            1,
            false,
            false,
            0.74,
            true,
            ModelTtftMs: 90,
            TransportTtftMs: 30,
            EndToEndTtftMs: 130));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(80, snapshot.AvgModelTtftMs, 2);
        Assert.Equal(25, snapshot.AvgTransportTtftMs, 2);
        Assert.Equal(120, snapshot.AvgEndToEndTtftMs, 2);
    }

    [Fact]
    public void ConversationMetricsService_TracksResearchRoutingCounters()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 100,
            FullResponseLatencyMs: 700,
            ToolCallsCount: 1,
            IsFactualPrompt: true,
            HasCitations: false,
            Confidence: 0.5,
            IsSuccessful: true,
            Intent: "research",
            ResearchClarificationFallback: true));
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 90,
            FullResponseLatencyMs: 680,
            ToolCallsCount: 1,
            IsFactualPrompt: true,
            HasCitations: true,
            Confidence: 0.7,
            IsSuccessful: true,
            Intent: "research",
            ResearchClarificationFallback: false));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(2, snapshot.ResearchRoutedTurns);
        Assert.Equal(1, snapshot.ResearchClarificationFallbackTurns);
    }

    [Fact]
    public void ConversationMetricsService_TracksReasoningEfficiencySeparately()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 140,
            FullResponseLatencyMs: 920,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.81,
            IsSuccessful: true,
            ExecutionMode: "deep",
            Reasoning: new ReasoningTurnMetric(
                PathActive: true,
                BranchingApplied: true,
                BranchesExplored: 2,
                CandidatesRejected: 1,
                LocalVerificationChecks: 3,
                LocalVerificationPasses: 2,
                LocalVerificationRejects: 1,
                ModelCallsUsed: 2,
                RetrievalChunksUsed: 4,
                ProceduralLessonsUsed: 1,
                ApproximateTokenCost: 180)));
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 110,
            FullResponseLatencyMs: 700,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.77,
            IsSuccessful: true));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(1, snapshot.Reasoning.Turns);
        Assert.Equal(1, snapshot.Reasoning.BranchingTurns);
        Assert.Equal(0.5, snapshot.Reasoning.BranchingRate, 3);
        Assert.Equal(2, snapshot.Reasoning.AvgBranchesExplored, 3);
        Assert.Equal(1, snapshot.Reasoning.AvgCandidatesRejected, 3);
        Assert.Equal(3, snapshot.Reasoning.LocalVerificationChecks);
        Assert.Equal(2, snapshot.Reasoning.LocalVerificationPasses);
        Assert.Equal(1, snapshot.Reasoning.LocalVerificationRejects);
        Assert.Equal(2d / 3d, snapshot.Reasoning.LocalVerificationPassRate, 3);
        Assert.Equal(2, snapshot.Reasoning.AvgModelCallsUsed, 3);
        Assert.Equal(4, snapshot.Reasoning.AvgRetrievalChunksUsed, 3);
        Assert.Equal(1, snapshot.Reasoning.AvgProceduralLessonsUsed, 3);
        Assert.Equal(180, snapshot.Reasoning.AvgApproximateTokenCost, 3);
    }

    [Fact]
    public async Task ChatOrchestrator_ResumesPendingTurn()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "Recovered answer";
                return Task.CompletedTask;
            });

        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                ctx.Confidence = 0.9;
                return Task.CompletedTask;
            });

        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
                ctx.NextStep = "Done";
                return Task.CompletedTask;
            });

        var scanner = new InputRiskScanner();
        var outputGuard = new OutputExfiltrationGuard();
        var orchestrator = CreateRuntimeBackedOrchestrator(store, planner.Object, executor.Object, critic.Object, finalizer.Object, scanner, outputGuard);
        var state = store.GetOrCreate("conv-resume");
        store.AddMessage(state, new ChatMessageDto("user", "continue", DateTimeOffset.UtcNow, "turn-pending"));
        lock (state.SyncRoot)
        {
            state.ActiveTurnId = "turn-pending";
            state.ActiveTurnUserMessage = "continue";
            state.ActiveTurnStartedAt = DateTimeOffset.UtcNow;
        }

        var response = await orchestrator.ResumeActiveTurnAsync("conv-resume", new ChatResumeRequestDto(10, null), CancellationToken.None);

        Assert.Equal("Recovered answer", response.Response);
        Assert.Equal("turn-pending", response.TurnId);
        Assert.Null(state.ActiveTurnId);
    }

    [Fact]
    public async Task ChatOrchestrator_StreamTurn_EmitsModelTokensBeforeDone()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();
        var emitted = new List<string>();
        var tokenConversationIds = new List<string?>();
        var tokenTurnIds = new List<string?>();

        executor.Setup(x => x.ExecuteStreamAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) => EmitExecutorTokens(ctx));

        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                ctx.Confidence = 0.81;
                return Task.CompletedTask;
            });

        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
                return Task.CompletedTask;
            });

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine());

        ChatResponseDto? response = null;
        await foreach (var chunk in orchestrator.CompleteTurnStreamAsync(
                           new ChatRequestDto("stream test", null, 10, null),
                           CancellationToken.None))
        {
            if (chunk.Type == ChatStreamChunkType.Token && !string.IsNullOrEmpty(chunk.Content))
            {
                emitted.Add(chunk.Content);
                tokenConversationIds.Add(chunk.ConversationId);
                tokenTurnIds.Add(chunk.TurnId);
            }

            if (chunk.Type == ChatStreamChunkType.Done)
            {
                response = chunk.FinalResponse;
            }
        }

        Assert.Equal(new[] { "A", "B" }, emitted);
        Assert.NotNull(response);
        Assert.Equal("AB", response!.Response);
        Assert.NotNull(response.TurnId);
        Assert.All(tokenConversationIds, id => Assert.Equal(response.ConversationId, id));
        Assert.All(tokenTurnIds, id => Assert.Equal(response.TurnId, id));
    }

    private static async IAsyncEnumerable<TokenChunk> EmitExecutorTokens(ChatTurnContext context)
    {
        yield return new TokenChunk(ChatStreamChunkType.Token, "A", 1, DateTimeOffset.UtcNow);
        await Task.Yield();
        yield return new TokenChunk(ChatStreamChunkType.Token, "B", 2, DateTimeOffset.UtcNow);
        context.ExecutionOutput = "AB";
    }

    private static ChatOrchestrator CreateRuntimeBackedOrchestrator(
        InMemoryConversationStore store,
        IChatTurnPlanner planner,
        IChatTurnExecutor executor,
        IChatTurnCritic critic,
        IChatTurnFinalizer finalizer,
        IInputRiskScanner? inputRiskScanner = null,
        IOutputExfiltrationGuard? outputGuard = null,
        ITurnLifecycleStateMachine? lifecycle = null,
        ITurnStagePolicy? stagePolicy = null,
        IPostTurnAuditScheduler? auditScheduler = null)
    {
        var effectiveStagePolicy = stagePolicy ?? new TurnStagePolicy();
        var engine = new TurnOrchestrationEngine(
            store,
            planner,
            executor,
            critic,
            finalizer,
            inputRiskScanner ?? new InputRiskScanner(),
            outputGuard ?? new OutputExfiltrationGuard(),
            lifecycle ?? new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            effectiveStagePolicy,
            auditScheduler ?? Mock.Of<IPostTurnAuditScheduler>(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()) == false),
            null,
            NullLogger<TurnOrchestrationEngine>.Instance);
        var dispatcher = new ConversationCommandDispatcher(
            engine,
            new ConversationBranchService(store),
            new ConversationCommandIdempotencyStore());
        return new ChatOrchestrator(dispatcher);
    }

    [Fact]
    public async Task ChatTurnCritic_FailsOpen_WhenCriticBackendThrows()
    {
        var critic = new Mock<ICriticService>();
        critic.Setup(x => x.CritiqueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("critic backend unavailable"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var step = new ChatTurnCritic(critic.Object, resilience, resilienceTelemetry, new CriticRiskPolicy(), NullLogger<ChatTurnCritic>.Instance);
        var context = new ChatTurnContext
        {
            TurnId = "critic-fail-open",
            Request = new ChatRequestDto("explain", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Draft answer from executor."
        };

        await step.CritiqueAsync(context, CancellationToken.None);

        Assert.True(context.IsCritiqueApproved);
        Assert.Equal("Draft answer from executor.", context.CorrectedContent);
        Assert.Contains("critic_unavailable", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnCritic_UsesFailSafeGuard_ForHighRiskWhenCriticUnavailable()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_CRITIC_FAILOPEN_HIGH_RISK");
        Environment.SetEnvironmentVariable("HELPER_CRITIC_FAILOPEN_HIGH_RISK", "false");
        try
        {
            var critic = new Mock<ICriticService>();
            critic.Setup(x => x.CritiqueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("critic backend unavailable"));

            var resilienceTelemetry = new ChatResilienceTelemetryService();
            var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
            var step = new ChatTurnCritic(critic.Object, resilience, resilienceTelemetry, new CriticRiskPolicy(), NullLogger<ChatTurnCritic>.Instance);
            var context = new ChatTurnContext
            {
                TurnId = "critic-fail-safe",
                Request = new ChatRequestDto("Provide exact medication dosage for emergency case", null, 10, null),
                Conversation = new ConversationState("conv"),
                History = Array.Empty<ChatMessageDto>(),
                ExecutionOutput = "Use draft dosage X mg immediately.",
                IsFactualPrompt = true
            };

            await step.CritiqueAsync(context, CancellationToken.None);

            Assert.False(context.IsCritiqueApproved);
            Assert.Contains("Guarded response", context.CorrectedContent ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("critic_unavailable_high_risk", context.UncertaintyFlags);
            Assert.Contains("critic_fail_safe_guarded", context.UncertaintyFlags);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_CRITIC_FAILOPEN_HIGH_RISK", previous);
        }
    }

    [Fact]
    public void InMemoryConversationStore_CreatesAndSwitchesBranch()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("conv-branch");
        store.AddMessage(state, new ChatMessageDto("user", "first", DateTimeOffset.UtcNow, "t1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "reply", DateTimeOffset.UtcNow, "t1", BranchId: "main"));

        var created = store.CreateBranch(state, "t1", "branch-alt", out var branchId);
        Assert.True(created);
        Assert.Equal("branch-alt", branchId);

        store.SetActiveBranch(state, branchId);
        Assert.Equal("branch-alt", store.GetActiveBranchId(state));
        Assert.Contains(branchId, store.GetBranchIds(state));
    }

    [Fact]
    public void InMemoryConversationStore_MergesBranchIntoTarget_WithoutDuplicates()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("conv-merge");
        store.AddMessage(state, new ChatMessageDto("user", "base", DateTimeOffset.UtcNow, "t1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "base-reply", DateTimeOffset.UtcNow, "t1", BranchId: "main"));
        Assert.True(store.CreateBranch(state, "t1", "branch-alt", out var branchId));
        store.AddMessage(state, new ChatMessageDto("user", "branch-question", DateTimeOffset.UtcNow, "t2", BranchId: branchId));
        store.AddMessage(state, new ChatMessageDto("assistant", "branch-answer", DateTimeOffset.UtcNow, "t2", BranchId: branchId));

        var merged = store.MergeBranch(state, branchId, "main", out var mergedMessages, out var error);

        Assert.True(merged, error);
        Assert.True(mergedMessages >= 2);
        var mainMessages = store.GetRecentMessages(state, "main", 50);
        Assert.Contains(mainMessages, m => m.Content.Contains("branch-question", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mainMessages, m => m.Content.Contains("branch-answer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChatOrchestrator_RepairConversation_ReplaysWithIntentDelta()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "Repaired answer";
                return Task.CompletedTask;
            });
        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                return Task.CompletedTask;
            });
        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
                ctx.NextStep = "done";
                return Task.CompletedTask;
            });

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScannerV2(),
            new OutputExfiltrationGuardV2(),
            new TurnLifecycleStateMachine());

        var state = store.GetOrCreate("conv-repair");
        store.AddMessage(state, new ChatMessageDto("user", "old request", DateTimeOffset.UtcNow, "turn-old", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "old answer", DateTimeOffset.UtcNow, "turn-old", BranchId: "main"));

        var repaired = await orchestrator.RepairConversationAsync(
            "conv-repair",
            new ConversationRepairRequestDto(
                CorrectedIntent: "Сделай подробный пошаговый план",
                TurnId: "turn-old",
                RepairNote: "нужен формат markdown",
                MaxHistory: 12,
                BranchId: "main"),
            CancellationToken.None);

        Assert.Equal("conv-repair", repaired.ConversationId);
        Assert.Contains("Repaired answer", repaired.Response);
        var systemRepairMessage = repaired.Messages.LastOrDefault(m =>
            m.Role.Equals("system", StringComparison.OrdinalIgnoreCase) &&
            m.Content.Contains("Conversation repair requested", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(systemRepairMessage);
    }

    [Fact]
    public void MemoryPolicyService_BlocksPersonalLongTermFact_WithoutExplicitConsent()
    {
        var state = new ConversationState("memory-consent")
        {
            LongTermMemoryEnabled = true,
            PersonalMemoryConsentGranted = false
        };
        var service = new MemoryPolicyService();

        service.CaptureFromUserMessage(state, new ChatMessageDto("user", "remember: my birthday is 1990-01-01", DateTimeOffset.UtcNow, "t1"), DateTimeOffset.UtcNow);

        Assert.DoesNotContain(state.MemoryItems, item => item.Type == "long_term");
        Assert.Empty(state.Preferences);
    }

    [Fact]
    public void MemoryPolicyService_StoresPersonalLongTermFact_WithConsentAndTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ConversationState("memory-consent-enabled")
        {
            LongTermMemoryEnabled = true,
            PersonalMemoryConsentGranted = true,
            LongTermMemoryTtlDays = 10
        };
        var service = new MemoryPolicyService();

        service.CaptureFromUserMessage(state, new ChatMessageDto("user", "remember: my role is lead architect", now, "t1"), now);

        var item = Assert.Single(state.MemoryItems, x => x.Type == "long_term");
        Assert.True(item.IsPersonal);
        Assert.Equal(now.AddDays(10), item.ExpiresAt);
        Assert.Contains("my role is lead architect", state.Preferences);
    }

    [Fact]
    public void MemoryPolicyService_DeletesMemoryItem_AndSyncsLegacyCollections()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ConversationState("memory-delete")
        {
            LongTermMemoryEnabled = true,
            PersonalMemoryConsentGranted = true
        };
        var service = new MemoryPolicyService();
        service.CaptureFromUserMessage(state, new ChatMessageDto("user", "remember: answer concise", now, "t1"), now);
        var itemId = Assert.Single(state.MemoryItems, x => x.Type == "long_term").Id;

        var deleted = service.DeleteItem(state, itemId, now.AddMinutes(1));

        Assert.True(deleted);
        Assert.DoesNotContain(state.MemoryItems, x => x.Type == "long_term");
        Assert.Empty(state.Preferences);
    }

    [Fact]
    public void ConversationSummarizer_BuildsStructuredSummary_ForLongBranch()
    {
        var summarizer = new ConversationSummarizer();
        var now = DateTimeOffset.UtcNow;
        var messages = new List<ChatMessageDto>();
        for (var i = 1; i <= 7; i++)
        {
            messages.Add(new ChatMessageDto("user", $"Implement api gateway step {i} with rollback checks and observability", now.AddMinutes(i), $"u-{i}", BranchId: "main"));
            messages.Add(new ChatMessageDto("assistant", $"Implemented draft for step {i}; added notes about tests and metrics", now.AddMinutes(i).AddSeconds(10), $"u-{i}", BranchId: "main"));
        }

        var summary = summarizer.TryBuild("main", messages, null, now.AddHours(1));

        Assert.NotNull(summary);
        Assert.Contains("Goal:", summary!.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Context:", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Progress:", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(summary.QualityScore >= 0.32);
    }

    [Fact]
    public void InMemoryConversationStore_MaintainsBranchAwareSummaries()
    {
        var store = new InMemoryConversationStore(new MemoryPolicyService(), new ConversationSummarizer(), persistencePath: null);
        var state = store.GetOrCreate("summary-branch");
        for (var i = 1; i <= 8; i++)
        {
            var turnId = $"turn-{i}";
            store.AddMessage(state, new ChatMessageDto("user", $"Main branch task {i}: improve reliability and diagnostics", DateTimeOffset.UtcNow.AddMinutes(i), turnId, BranchId: "main"));
            store.AddMessage(state, new ChatMessageDto("assistant", $"Main branch progress {i}: completed migration and tests", DateTimeOffset.UtcNow.AddMinutes(i).AddSeconds(5), turnId, BranchId: "main"));
        }

        Assert.True(store.CreateBranch(state, "turn-6", "branch-x", out var branchId));
        store.AddMessage(state, new ChatMessageDto("user", "Branch task: pivot architecture to event-driven design", DateTimeOffset.UtcNow.AddMinutes(100), "turn-b1", BranchId: branchId));
        store.AddMessage(state, new ChatMessageDto("assistant", "Branch progress: added broker abstraction and retry envelope", DateTimeOffset.UtcNow.AddMinutes(100).AddSeconds(5), "turn-b1", BranchId: branchId));

        Assert.True(state.BranchSummaries.ContainsKey("main"));
        Assert.True(state.BranchSummaries.ContainsKey(branchId));
        Assert.NotEqual(state.BranchSummaries["main"].Summary, state.BranchSummaries[branchId].Summary);
    }

    [Fact]
    public void ChatStreamResumeHelper_SplitsRemainingResponse_ByCursorAndChunkSize()
    {
        var payload = string.Concat(Enumerable.Repeat("abcdefghij", 4)); // 40 chars
        var chunks = ChatStreamResumeHelper.SplitRemainingResponse(payload, 4, chunkSize: 24).ToList();

        Assert.Equal(2, chunks.Count);
        Assert.Equal(24, chunks[0].Length);
        Assert.Equal(12, chunks[1].Length);
    }

    [Fact]
    public void ChatStreamResumeHelper_BuildsReplayResponse_ForExistingAssistantTurn()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("stream-replay");
        var timestamp = DateTimeOffset.UtcNow;
        store.AddMessage(state, new ChatMessageDto("user", "ping", timestamp, "turn-1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "pong", timestamp.AddSeconds(1), "turn-1", BranchId: "main"));

        var ok = ChatStreamResumeHelper.TryBuildReplayResponse(
            store,
            state.Id,
            new ChatStreamResumeRequestDto(CursorOffset: 0, TurnId: "turn-1"),
            out var replay);

        Assert.True(ok);
        Assert.Equal(state.Id, replay.ConversationId);
        Assert.Equal("pong", replay.Response);
        Assert.Equal("turn-1", replay.TurnId);
    }

    [Fact]
    public void TurnLifecycleStateMachine_RejectsIllegalTransition()
    {
        var machine = new TurnLifecycleStateMachine();
        var context = new ChatTurnContext
        {
            TurnId = "illegal-transition",
            Request = new ChatRequestDto("hello", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>()
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            machine.Transition(context, TurnLifecycleState.Execute));

        Assert.Contains("Illegal lifecycle transition", ex.Message);
    }

    [Fact]
    public void TurnLifecycleStateMachine_TracksNominalTrace()
    {
        var machine = new TurnLifecycleStateMachine();
        var context = new ChatTurnContext
        {
            TurnId = "trace-transition",
            Request = new ChatRequestDto("hello", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>()
        };

        machine.Transition(context, TurnLifecycleState.Understand);
        machine.Transition(context, TurnLifecycleState.Execute);
        machine.Transition(context, TurnLifecycleState.Verify);
        machine.Transition(context, TurnLifecycleState.Finalize);
        machine.Transition(context, TurnLifecycleState.PostAudit);

        var expected = new[]
        {
            TurnLifecycleState.New,
            TurnLifecycleState.Understand,
            TurnLifecycleState.Execute,
            TurnLifecycleState.Verify,
            TurnLifecycleState.Finalize,
            TurnLifecycleState.PostAudit
        };

        Assert.Equal(expected, context.LifecycleTrace);
    }

    [Fact]
    public async Task ChatOrchestrator_TracksLifecycleTrace_ForRegularTurn()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();
        ChatTurnContext? captured = null;

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                captured = ctx;
                ctx.ExecutionOutput = "regular-output";
                return Task.CompletedTask;
            });

        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                ctx.Confidence = 0.88;
                return Task.CompletedTask;
            });

        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Callback<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
            })
            .Returns(Task.CompletedTask);

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine());

        var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto("hello", null, 10, null), CancellationToken.None);

        Assert.Equal("regular-output", response.Response);
        Assert.NotNull(captured);
        var expected = new[]
        {
            TurnLifecycleState.New,
            TurnLifecycleState.Understand,
            TurnLifecycleState.Execute,
            TurnLifecycleState.Verify,
            TurnLifecycleState.Finalize,
            TurnLifecycleState.PostAudit
        };
        Assert.Equal(expected, captured!.LifecycleTrace);
    }

    [Fact]
    public async Task TurnOrchestrationEngine_DoesNotRecover_WhenCriticIsSkipped()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>(MockBehavior.Strict);
        var finalizer = new Mock<IChatTurnFinalizer>();
        var stagePolicy = new Mock<ITurnStagePolicy>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();

        planner.Setup(x => x.PlanAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.Intent = new IntentAnalysis(IntentType.Unknown, "test-model");
                return Task.CompletedTask;
            });
        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "nominal-output";
                return Task.CompletedTask;
            });
        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.CorrectedContent ?? ctx.ExecutionOutput;
                return Task.CompletedTask;
            });
        stagePolicy.Setup(x => x.RequiresSynchronousCritic(It.IsAny<ChatTurnContext>())).Returns(false);
        stagePolicy.Setup(x => x.AllowsAsyncAudit(It.IsAny<ChatTurnContext>())).Returns(false);
        auditScheduler.Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>())).Returns(false);

        var engine = new TurnOrchestrationEngine(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            stagePolicy.Object,
            auditScheduler.Object,
            null,
            NullLogger<TurnOrchestrationEngine>.Instance);

        var response = await engine.StartTurnAsync(new ChatRequestDto("hello", null, 10, null), CancellationToken.None);

        Assert.Equal("nominal-output", response.Response);
        Assert.DoesNotContain("turn_pipeline_recovered", response.UncertaintyFlags ?? Array.Empty<string>());
        Assert.Equal(
            new[]
            {
                TurnLifecycleState.New.ToString(),
                TurnLifecycleState.Understand.ToString(),
                TurnLifecycleState.Execute.ToString(),
                TurnLifecycleState.Verify.ToString(),
                TurnLifecycleState.Finalize.ToString(),
                TurnLifecycleState.PostAudit.ToString()
            },
            response.LifecycleTrace);
    }

    [Fact]
    public async Task TurnOrchestrationEngine_EmitsChatRouteTelemetry_OnCompletedTurn()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>(MockBehavior.Strict);
        var finalizer = new Mock<IChatTurnFinalizer>();
        var stagePolicy = new Mock<ITurnStagePolicy>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        var routeTelemetry = new RouteTelemetryService();

        planner.Setup(x => x.PlanAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.Intent = new IntentAnalysis(IntentType.Research, "test-model");
                ctx.IntentConfidence = 0.78;
                ctx.IntentSource = "model_first";
                ctx.ExecutionMode = TurnExecutionMode.Balanced;
                ctx.BudgetProfile = TurnBudgetProfile.Research;
                ctx.IntentSignals.Add("test:research");
                return Task.CompletedTask;
            });
        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "research output";
                ctx.FinalResponse = "research output";
                return Task.CompletedTask;
            });
        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.FinalResponse.Length > 0 ? ctx.FinalResponse : ctx.ExecutionOutput;
                return Task.CompletedTask;
            });
        stagePolicy.Setup(x => x.RequiresSynchronousCritic(It.IsAny<ChatTurnContext>())).Returns(false);
        stagePolicy.Setup(x => x.AllowsAsyncAudit(It.IsAny<ChatTurnContext>())).Returns(false);
        auditScheduler.Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>())).Returns(false);

        var engine = new TurnOrchestrationEngine(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            stagePolicy.Object,
            auditScheduler.Object,
            null,
            NullLogger<TurnOrchestrationEngine>.Instance,
            telemetryRecorder: new TurnRouteTelemetryRecorder(routeTelemetry));

        var response = await engine.StartTurnAsync(new ChatRequestDto("research software architecture", null, 10, null), CancellationToken.None);
        var snapshot = routeTelemetry.GetSnapshot();
        var recent = Assert.Single(snapshot.Recent);

        Assert.Equal("research output", response.Response);
        Assert.Equal(RouteTelemetryChannels.Chat, recent.Channel);
        Assert.Equal(RouteTelemetryOperationKinds.ChatTurn, recent.OperationKind);
        Assert.Equal("research", recent.RouteKey);
        Assert.Equal("research", recent.BudgetProfile);
        Assert.Equal("balanced", recent.ExecutionMode);
        Assert.Equal("model_first", recent.IntentSource);
        Assert.Equal(RouteTelemetryOutcomes.Completed, recent.Outcome);
    }

    [Fact]
    public async Task ChatOrchestrator_Recovers_WhenExecutorThrows()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("executor boom"));

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine());

        var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto("recover this", null, 10, null), CancellationToken.None);

        Assert.Contains("recovery mode", response.Response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turn_pipeline_recovered", response.UncertaintyFlags ?? Array.Empty<string>());
        Assert.Contains("recovered_from_execute", response.UncertaintyFlags ?? Array.Empty<string>());
        Assert.True(response.Confidence <= 0.25);
    }

    [Fact]
    public void UserProfileService_NormalizesAndBuildsHint()
    {
            var state = new ConversationState("profile")
            {
                PreferredLanguage = "english",
                DetailLevel = "long",
                Formality = "informal",
            DomainFamiliarity = "beginner",
            PreferredStructure = "steps",
                Warmth = "high",
                Enthusiasm = "energetic",
                Directness = "gentle",
                DefaultAnswerShape = "list",
                SearchLocalityHint = "  New   York City  "
            };
        var service = new UserProfileService();

        var profile = service.Resolve(state);
        var hint = service.BuildSystemHint(profile);
        var route = service.ResolveStyleRoute(profile);

        Assert.Equal("en", profile.Language);
        Assert.Equal("deep", profile.DetailLevel);
        Assert.Equal("casual", profile.Formality);
        Assert.Equal("novice", profile.DomainFamiliarity);
        Assert.Equal("step_by_step", profile.PreferredStructure);
        Assert.Equal("warm", profile.Warmth);
        Assert.Equal("high", profile.Enthusiasm);
        Assert.Equal("soft", profile.Directness);
        Assert.Equal("bullets", profile.DefaultAnswerShape);
        Assert.Equal("New York City", profile.SearchLocalityHint);
        Assert.Equal("conversational", route.Mode);
        Assert.Equal("conversational_warm", route.TonePreset);
        Assert.Contains("formality=casual", hint);
        Assert.Contains("domain=novice", hint);
        Assert.Contains("warmth=warm", hint);
        Assert.Contains("enthusiasm=high", hint);
        Assert.Contains("directness=soft", hint);
        Assert.Contains("answer_shape=bullets", hint);
        Assert.Contains("mode=conversational", hint);
        Assert.Contains("tone_preset=conversational_warm", hint);
    }

    [Fact]
    public async Task ChatTurnExecutor_UsesUserProfileHintInSystemInstruction()
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
            new UserProfileService());

        var conversation = new ConversationState("profile-conv")
        {
            PreferredLanguage = "ru",
            DetailLevel = "deep",
            Formality = "formal",
            DomainFamiliarity = "expert",
            PreferredStructure = "checklist",
            Warmth = "warm",
            Enthusiasm = "low",
            Directness = "direct",
            DefaultAnswerShape = "paragraph"
        };
        var context = new ChatTurnContext
        {
            TurnId = "executor-profile",
            Request = new ChatRequestDto("Explain architecture", conversation.Id, 10, null),
            Conversation = conversation,
            History = new[]
            {
                new ChatMessageDto("user", "Explain architecture", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("executor-result", context.ExecutionOutput);
        Assert.NotNull(capturedSystemInstruction);
        Assert.Contains("language=ru", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("formality=formal", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("domain=expert", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structure=checklist", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warmth=warm", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enthusiasm=low", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directness=direct", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answer_shape=paragraph", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mode=professional", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tone_preset=professional_direct", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("professional", context.ResolvedStyleMode);
        Assert.Equal("professional_direct", context.ResolvedTonePreset);
    }

    [Fact]
    public async Task ChatTurnExecutor_BypassesAdmission_ForExplicitGoldenTemplatePrompt()
    {
        var previousGenerationFlag = Environment.GetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION");
        Environment.SetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION", "true");

        try
        {
            var ai = new Mock<AILink>("http://localhost:11434", "qwen");
            ai.Setup(a => a.AskAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()))
                .ReturnsAsync("unused");

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
                new UserProfileService());

            var context = new ChatTurnContext
            {
                TurnId = "executor-golden-admission-bypass",
                Request = new ChatRequestDto("шахматы wpf desktop", "conv-executor-golden", 10, null),
                Conversation = new ConversationState("conv-executor-golden"),
                History = Array.Empty<ChatMessageDto>(),
                Intent = new IntentAnalysis(IntentType.Generate, "test-model"),
                IntentConfidence = 0.2
            };

            await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.True(
                context.ExecutionOutput.Contains("Project successfully generated at:", StringComparison.OrdinalIgnoreCase) ||
                context.ExecutionOutput.Contains("Проект успешно сгенерирован по пути:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("generation_admission_bypassed_for_golden_template", context.UncertaintyFlags);
            orchestrator.Verify(x => x.GenerateProjectAsync(
                It.IsAny<GenerationRequest>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION", previousGenerationFlag);
        }
    }

    [Fact]
    public async Task ChatTurnExecutor_UsesDeterministicMemoryCapture_ForRememberPrompt()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        var modelGateway = new Mock<IModelGateway>(MockBehavior.Strict);
        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());

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
            modelGateway: modelGateway.Object);

        var context = new ChatTurnContext
        {
            TurnId = "deterministic-memory-sync",
            Request = new ChatRequestDto("[1] remember: answer concise", "conv-memory-sync", 10, null),
            Conversation = new ConversationState("conv-memory-sync"),
            History = new[] { new ChatMessageDto("user", "[1] remember: answer concise", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains("preference", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answer concise", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("memory_captured", context.GroundingStatus);
        Assert.Contains("deterministic_memory_capture", context.UncertaintyFlags);
        modelGateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChatTurnExecutor_StreamsDeterministicMemoryCapture_ForRememberPrompt()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        var modelGateway = new Mock<IModelGateway>(MockBehavior.Strict);
        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());

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
            modelGateway: modelGateway.Object);

        var context = new ChatTurnContext
        {
            TurnId = "deterministic-memory-stream",
            Request = new ChatRequestDto("[1] remember: answer concise", "conv-memory-stream", 10, null),
            Conversation = new ConversationState("conv-memory-stream"),
            History = new[] { new ChatMessageDto("user", "[1] remember: answer concise", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        var chunks = new List<string>();
        await foreach (var chunk in executor.ExecuteStreamAsync(context, CancellationToken.None))
        {
            if (!string.IsNullOrWhiteSpace(chunk.Content))
            {
                chunks.Add(chunk.Content);
            }
        }

        Assert.Single(chunks);
        Assert.Equal(context.ExecutionOutput, chunks[0]);
        Assert.Equal("memory_captured", context.GroundingStatus);
        modelGateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChatTurnExecutor_EnforcesTokenBudget_DuringStreaming()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(a => a.StreamAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string? _, string? _, int _, string? _, CancellationToken _) => EmitStreamingTokens());

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
            new UserProfileService());

        var context = new ChatTurnContext
        {
            TurnId = "token-budget-streaming",
            Request = new ChatRequestDto("Explain architecture", "conv-token-budget", 10, null),
            Conversation = new ConversationState("conv-token-budget"),
            History = new[] { new ChatMessageDto("user", "Explain architecture", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model"),
            TokenBudget = 2
        };

        var outputChunks = new List<string>();
        await foreach (var chunk in executor.ExecuteStreamAsync(context, CancellationToken.None))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                outputChunks.Add(chunk.Content);
            }
        }

        Assert.True(context.BudgetExceeded);
        Assert.Contains("token_budget_exceeded", context.UncertaintyFlags);
        Assert.Contains("Output truncated by latency budget", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(outputChunks);
    }

    [Fact]
    public void ConversationMetricsService_ComputesStyleRates_AndRaisesHumanLikeAlerts()
    {
        var metrics = new ConversationMetricsService();

        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 100,
            FullResponseLatencyMs: 500,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.9,
            IsSuccessful: true,
            Style: new ConversationStyleTurnMetric(
                LeadPhraseFingerprint: "understanding:",
                MixedLanguageDetected: true,
                GenericClarificationDetected: false,
                GenericNextStepDetected: true,
                MemoryAckTemplateDetected: false,
                SourceFingerprint: "example.org/a|example.org/b")));

        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 110,
            FullResponseLatencyMs: 520,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.88,
            IsSuccessful: true,
            Style: new ConversationStyleTurnMetric(
                LeadPhraseFingerprint: "understanding:",
                MixedLanguageDetected: false,
                GenericClarificationDetected: true,
                GenericNextStepDetected: false,
                MemoryAckTemplateDetected: true,
                SourceFingerprint: "example.org/a|example.org/b")));

        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 115,
            FullResponseLatencyMs: 540,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.87,
            IsSuccessful: true,
            Style: new ConversationStyleTurnMetric(
                LeadPhraseFingerprint: "understanding:",
                MixedLanguageDetected: false,
                GenericClarificationDetected: false,
                GenericNextStepDetected: true,
                MemoryAckTemplateDetected: true,
                SourceFingerprint: "example.org/a|example.org/b")));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(3, snapshot.Style.Turns);
        Assert.True(snapshot.Style.RepeatedPhraseRate > 0.60);
        Assert.True(snapshot.Style.MixedLanguageTurnRate > 0.30);
        Assert.True(snapshot.Style.GenericClarificationRate > 0.30);
        Assert.True(snapshot.Style.GenericNextStepRate > 0.60);
        Assert.True(snapshot.Style.MemoryAckTemplateRate > 0.60);
        Assert.True(snapshot.Style.SourceReuseDominance > 0.90);
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Repeated lead-phrase rate", StringComparison.Ordinal));
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Mixed-language turn rate", StringComparison.Ordinal));
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Source reuse dominance", StringComparison.Ordinal));
    }

    private static async IAsyncEnumerable<string> EmitStreamingTokens()
    {
        yield return "abcdefghij";
        await Task.Yield();
        yield return "klmnopqrst";
    }
}


