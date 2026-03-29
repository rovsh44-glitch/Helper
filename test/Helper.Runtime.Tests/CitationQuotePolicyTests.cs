using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class CitationQuotePolicyTests
{
    [Fact]
    public void PublisherCompliancePolicy_DoesNotPromote_GenericGuideTitle_WithoutOfficialHost()
    {
        var policy = new PublisherCompliancePolicy();

        var decision = policy.Evaluate(
            "https://blog.example.com/retries",
            "Retry guide for production systems",
            "fetched_page",
            CitationRenderSurface.Answer);

        Assert.Equal("general_web", decision.Tier);
        Assert.False(decision.AllowUserFacingExcerpt);
    }

    [Fact]
    public void CitationQuotePolicy_Omits_GeneralWebExcerpt_OnAnswerSurface()
    {
        var policy = new CitationQuotePolicy();

        var decision = policy.Build(
            "Use exponential backoff with jitter to avoid synchronized retry storms.",
            "https://blog.example.com/retries",
            "Retry write-up",
            "fetched_page",
            CitationRenderSurface.Answer);

        Assert.False(decision.Included);
        Assert.Null(decision.Text);
        Assert.False(decision.DirectQuoteAllowed);
        Assert.Contains("publisher_tier:general_web", decision.Flags);
        Assert.Contains("excerpt_omitted_by_policy", decision.Flags);
        Assert.Contains("compliance_reason:surface_answer_restricts_fetched_page", decision.Flags);
    }

    [Fact]
    public void CitationQuotePolicy_Clips_OfficialReferenceExcerpt_OnTraceSurface()
    {
        var policy = new CitationQuotePolicy();

        var decision = policy.Build(
            "Retries should use exponential backoff with jitter and capped retry budgets across multiple services to avoid overload during recovery events.",
            "https://learn.microsoft.com/retries",
            "Retry documentation",
            "fetched_page",
            CitationRenderSurface.Trace);

        Assert.True(decision.Included);
        Assert.True(decision.DirectQuoteAllowed);
        Assert.True(decision.Truncated);
        Assert.NotNull(decision.Text);
        Assert.EndsWith("...", decision.Text!, StringComparison.Ordinal);
        Assert.True(decision.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 16);
        Assert.Contains("publisher_tier:official_reference", decision.Flags);
        Assert.Contains("excerpt_clipped_by_policy", decision.Flags);
        Assert.Contains("direct_quote_allowed", decision.Flags);
    }

    [Fact]
    public void ResearchGroundedSynthesisFormatter_UsesFallbackPhrase_ForGeneralWebEvidence()
    {
        var formatter = new ResearchGroundedSynthesisFormatter(
            new ResearchAnswerSynthesizer(),
            new CitationQuotePolicy());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://blog.example.com/retries",
                Title: "Retry write-up",
                Snippet: "Use exponential backoff with jitter to avoid synchronized retry storms.",
                IsFallback: false,
                EvidenceKind: "fetched_page")
        };
        var groundedClaims = new[]
        {
            new ClaimGrounding(
                Claim: string.Empty,
                Type: ClaimSentenceType.Fact,
                SourceIndex: 1,
                EvidenceGrade: "strong",
                EvidenceCitationLabel: "1",
                EvidenceKind: "fetched_page")
        };

        var result = formatter.TryFormat(groundedClaims, evidenceItems, "en");

        Assert.NotNull(result);
        Assert.Contains("captured evidence on that page", result!.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Use exponential backoff with jitter", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void WebSearchTraceProjector_OmitsGeneralWebSnippet_AndSurfacesComplianceFlags()
    {
        var projector = new WebSearchTraceProjector(new CitationQuotePolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-trace-general-web",
            Request = new ChatRequestDto(
                "latest retry guidance",
                "conv-trace-general-web",
                10,
                null,
                LiveWebMode: "force_search"),
            Conversation = new ConversationState("conv-trace-general-web"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedLiveWebRequirement = "web_required",
            ResolvedLiveWebReason = "currentness"
        };
        context.ToolCalls.Add("research.search");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 1,
            Url: "https://blog.example.com/retries",
            Title: "Retry write-up",
            Snippet: "Use exponential backoff with jitter to avoid synchronized retry storms.",
            IsFallback: false,
            EvidenceKind: "fetched_page"));

        var trace = projector.Build(context);

        Assert.NotNull(trace);
        var source = Assert.Single(trace!.Sources!);
        Assert.Null(source.Snippet);
        Assert.Contains("publisher_tier:general_web", source.SafetyFlags!);
        Assert.Contains("excerpt_omitted_by_policy", source.SafetyFlags!);
        Assert.Contains("compliance_reason:trace_excerpt_restricted_for_fetched_page", source.SafetyFlags!);
    }

    [Fact]
    public void WebSearchTraceProjector_ClipsOfficialReferenceSnippet_InTrace()
    {
        var projector = new WebSearchTraceProjector(new CitationQuotePolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-trace-official",
            Request = new ChatRequestDto(
                "latest retry guidance",
                "conv-trace-official",
                10,
                null,
                LiveWebMode: "force_search"),
            Conversation = new ConversationState("conv-trace-official"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedLiveWebRequirement = "web_required",
            ResolvedLiveWebReason = "currentness"
        };
        context.ToolCalls.Add("research.search");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            Ordinal: 1,
            Url: "https://learn.microsoft.com/retries",
            Title: "Retry documentation",
            Snippet: "Retries should use exponential backoff with jitter and capped retry budgets across multiple services to avoid overload during recovery events.",
            IsFallback: false,
            EvidenceKind: "fetched_page"));

        var trace = projector.Build(context);

        Assert.NotNull(trace);
        var source = Assert.Single(trace!.Sources!);
        Assert.NotNull(source.Snippet);
        Assert.EndsWith("...", source.Snippet!, StringComparison.Ordinal);
        Assert.True(source.Snippet!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 16);
        Assert.Contains("publisher_tier:official_reference", source.SafetyFlags!);
        Assert.Contains("excerpt_clipped_by_policy", source.SafetyFlags!);
        Assert.Contains("direct_quote_allowed", source.SafetyFlags!);
    }

    [Fact]
    public void WebSearchTraceProjector_PrioritizesCriticalFetchVisibilityEvents()
    {
        var projector = new WebSearchTraceProjector(new CitationQuotePolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-trace-fetch-priority",
            Request = new ChatRequestDto(
                "how strong is the evidence for red light therapy recovery",
                "conv-trace-fetch-priority",
                10,
                null,
                LiveWebMode: "force_search"),
            Conversation = new ConversationState("conv-trace-fetch-priority"),
            History = Array.Empty<ChatMessageDto>(),
            ResolvedLiveWebRequirement = "web_helpful",
            ResolvedLiveWebReason = "benchmark_recommended_web"
        };
        context.ToolCalls.Add("research.search");
        for (var index = 0; index < 80; index++)
        {
            context.RetrievalTrace.Add($"provider.trace.{index}");
        }

        context.RetrievalTrace.Add("web_page_fetch.failure_count=2");
        context.RetrievalTrace.Add("web_page_fetch.transport_categories=connection_refused:2");
        context.RetrievalTrace.Add("web_page_fetch.render_recovery_failed target=https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494 outcome=browser_spawn_blocked");
        context.RetrievalTrace.Add("browser_render.unavailable category=browser_spawn_blocked reason=spawn EPERM");
        context.RetrievalTrace.Add("web_search.search_hit_only_guard.drop url=https://openai.com/de-DE/index/chatgpt reason=chat_or_app_landing_for_evidence_query");

        var trace = projector.Build(context);

        Assert.NotNull(trace);
        Assert.Contains(trace!.Events!, entry => entry.Contains("web_page_fetch.failure_count=2", StringComparison.Ordinal));
        Assert.Contains(trace.Events!, entry => entry.Contains("web_page_fetch.transport_categories=connection_refused:2", StringComparison.Ordinal));
        Assert.Contains(trace.Events!, entry => entry.Contains("web_page_fetch.render_recovery_failed", StringComparison.Ordinal));
        Assert.Contains(trace.Events!, entry => entry.Contains("browser_render.unavailable category=browser_spawn_blocked", StringComparison.Ordinal));
        Assert.Contains(trace.Events!, entry => entry.Contains("web_search.search_hit_only_guard.drop", StringComparison.Ordinal));
        Assert.True(trace.Events!.Count <= 64);
    }
}

