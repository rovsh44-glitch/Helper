using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ResponseComposerCollaboratorTests
{
    [Fact]
    public void ResponseTextDeduplicator_CollapsesRepeatedSentenceBlock()
    {
        var deduplicator = new ResponseTextDeduplicator();

        var result = deduplicator.NormalizePreparedOutput(
            "Retry jitter reduces synchronized spikes. It spreads retries over time. Retry jitter reduces synchronized spikes. It spreads retries over time.",
            ComposerLocalization.English);

        Assert.Equal("Retry jitter reduces synchronized spikes. It spreads retries over time.", result);
    }

    [Fact]
    public void AnswerShapePolicy_PromotesProceduralParagraph_ToStructuredSteps()
    {
        var policy = new AnswerShapePolicy();
        var context = CreateContext(
            "Give me a step-by-step rollout plan",
            preferredLanguage: "en",
            preferredStructure: "step_by_step",
            defaultAnswerShape: "paragraph");

        var result = policy.ApplyTaskClassFormatting(
            context,
            "Start with a small canary rollout. Watch error rate and latency closely. Expand only after the first stage stays stable.",
            ComposerLocalization.English);

        Assert.Contains("1. Start with a small canary rollout.", result, StringComparison.Ordinal);
        Assert.Contains("2. Watch error rate and latency closely.", result, StringComparison.Ordinal);
        Assert.Contains("3. Expand only after the first stage stays stable.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NextStepComposer_UsesOperationalFallback_WhenNextStepIsMissing()
    {
        var composer = new NextStepComposer(new ConversationVariationPolicy());
        var context = CreateContext("Generate a project", preferredLanguage: "en");

        var result = composer.ResolveEffectiveNextStep(
            context,
            solution: "Project generated.",
            localization: ComposerLocalization.English,
            mode: ResponseCompositionMode.OperatorSummary);

        Assert.Equal("Retry with narrower scope or provide stricter constraints.", result);
    }

    [Fact]
    public void BenchmarkResponseFormatter_RebuildsRequiredSections_FromFallbackDraft()
    {
        var formatter = BenchmarkResponseFormatterFactory.CreateDefault();
        var context = CreateContext(
            "Сравни local и web evidence",
            preferredLanguage: "ru",
            systemInstruction: "## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion");
        context.IsFactualPrompt = true;
        context.RequireExplicitBenchmarkUncertainty = true;

        var handled = formatter.TryComposeLocalFirstBenchmarkResponse(
            context,
            "Research request: compare evidence\n\nНеопределённость: для этого фактического утверждения не удалось получить проверяемые источники.",
            out var formatted);

        Assert.True(handled);
        Assert.Contains("## Local Findings", formatted, StringComparison.Ordinal);
        Assert.Contains("## Web Findings", formatted, StringComparison.Ordinal);
        Assert.Contains("## Conclusion", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void BenchmarkResponseFormatter_PreservesStructuredDraft_WhenQualityIsAlreadyAcceptable()
    {
        var formatter = BenchmarkResponseFormatterFactory.CreateDefault();
        var context = CreateContext(
            "Compare local and web evidence",
            preferredLanguage: "en",
            systemInstruction: "## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion");
        var solution = """
            ## Local Findings
            Local evidence establishes the baseline tradeoff.

            ## Web Findings
            External sources confirm the same pattern.

            ## Sources
            - https://example.org/source

            ## Analysis
            The local-first answer is directionally correct and externally corroborated.

            ## Conclusion
            Use the local answer as the baseline and keep the web corroboration attached.

            ## Opinion
            This is a strong benchmark response because both sides agree.
            """;

        var handled = formatter.TryComposeLocalFirstBenchmarkResponse(context, solution, out var formatted);

        Assert.True(handled);
        Assert.Equal(solution, formatted);
    }

    [Fact]
    public void BenchmarkResponseFormatter_ReconstructsLowQualityRussianStructuredDraft()
    {
        var formatter = BenchmarkResponseFormatterFactory.CreateDefault();
        var context = CreateContext(
            "Сравни локальный и web evidence",
            preferredLanguage: "ru",
            systemInstruction: "## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion");

        var solution = """
            ## Local Findings
            **Answer:** Theoretical Physics V2 gives airport loads and mixed misunderstanding.

            ## Web Findings
            Link 1 says something, link 2 says something.

            ## Sources
            - https://example.com

            ## Analysis
            type load simultaneously improve misunderstanding

            ## Conclusion
            uncertain

            ## Opinion
            generic fallback
            """;

        var handled = formatter.TryComposeLocalFirstBenchmarkResponse(context, solution, out var formatted);

        Assert.True(handled);
        Assert.Contains("## Local Findings", formatted, StringComparison.Ordinal);
        Assert.Contains("## Conclusion", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("**Answer:**", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Theoretical Physics V2", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkResponseFormatter_AppendsExplicitUncertainty_ForSparseEvidenceRussianDraft()
    {
        var formatter = BenchmarkResponseFormatterFactory.CreateDefault();
        var context = CreateContext(
            "Объясни, насколько надёжны текущие claims о fully autonomous AI software engineers.",
            preferredLanguage: "ru",
            systemInstruction: "## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion");
        context.RequireExplicitBenchmarkUncertainty = true;
        context.ResolvedTurnLanguage = "ru";
        context.Sources.Add("https://openai.com/index/introducing-gpt-5");

        var handled = formatter.TryComposeLocalFirstBenchmarkResponse(
            context,
            "Сейчас я не могу ответственно утверждать это как установленный факт.",
            out var formatted);

        Assert.True(handled);
        Assert.Contains("неопредел", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatTurnFinalizer_ComposesStructuredBenchmarkClarification_WhenBenchmarkSectionsAreRequired()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = CreateContext(
            "Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.",
            preferredLanguage: "ru",
            systemInstruction: "## Local Findings\n## Web Findings\n## Sources\n## Analysis\n## Conclusion\n## Opinion");
        context.RequiresClarification = true;
        context.ClarifyingQuestion = "Какое ограничение здесь главное: срок, стек, риск, запрет на изменения или формат результата?";
        context.IsFactualPrompt = true;
        context.ResolvedTurnLanguage = "ru";
        context.UncertaintyFlags.Add("soft_best_effort_entry");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Equal("clarification_required", context.GroundingStatus);
        Assert.Contains("## Local Findings", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("## Analysis", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Какое ограничение здесь главное", context.FinalResponse, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseComposer_Appends_Verification_Framing_For_NeedsVerification_Mode()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = CreateContext("Проверь вывод", preferredLanguage: "ru");
        context.EpistemicAnswerMode = Helper.Api.Conversation.Epistemic.EpistemicAnswerMode.NeedsVerification;

        var result = composer.Compose(context, "Промежуточный вывод по теме.");

        Assert.Contains("ответ пока предварительный", result, StringComparison.OrdinalIgnoreCase);
    }

    private static ChatTurnContext CreateContext(
        string message,
        string preferredLanguage,
        string? preferredStructure = null,
        string? defaultAnswerShape = null,
        string? systemInstruction = null)
    {
        return new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto(message, null, 12, systemInstruction),
            Conversation = new ConversationState("response-composer-collaborator")
            {
                PreferredLanguage = preferredLanguage,
                PreferredStructure = preferredStructure ?? "auto",
                DefaultAnswerShape = defaultAnswerShape ?? "auto"
            },
            History = Array.Empty<ChatMessageDto>()
        };
    }
}

