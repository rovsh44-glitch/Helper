using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Ranking;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;
using Helper.Testing.WebResearch;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Integration")]
public sealed partial class WebSearchSessionCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_EnrichesDocuments_WithFetchedPageEvidence()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://example.org/article?ref=search",
                    "Search title",
                    "Search snippet.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new StubPageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("dotnet retry policy guidance", MaxResults: 5),
            CancellationToken.None);

        var document = Assert.Single(session.ResultBundle.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.Equal("https://example.org/article", document.Url);
        Assert.Equal("Canonical article title", document.Title);
        Assert.Contains("full page evidence", document.Snippet, StringComparison.Ordinal);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.extracted", StringComparison.Ordinal));
        Assert.Equal("https://example.org/article", session.ResultBundle.SourceUrls[0]);
    }


    [Fact]
    public async Task ExecuteAsync_BackfillsAdditionalFetchAttempts_ForMedicalEvidenceQuery()
    {
        var pageFetcher = new OrdinalOutcomePageFetcher(successOrdinals: new[] { 2, 3 });

        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(_ =>
                new[]
                {
                    new WebSearchDocument(
                        "https://pubmed.ncbi.nlm.nih.gov/37512345/",
                        "Photobiomodulation therapy and exercise recovery",
                        "Systematic review of recovery outcomes."),
                    new WebSearchDocument(
                        "https://pmc.ncbi.nlm.nih.gov/articles/PMC1234567/",
                        "Red light therapy for muscle recovery",
                        "Primary full-text review article."),
                    new WebSearchDocument(
                        "https://clinicaltrials.gov/study/NCT12345678",
                        "Photobiomodulation therapy for post-exercise muscle recovery",
                        "Clinical trial record for red light therapy, photobiomodulation, and muscle recovery after training."),
                    new WebSearchDocument(
                        "https://mayoclinic.org/healthy-lifestyle/fitness/expert-answers/red-light-therapy/faq-20441303",
                        "Red light therapy: can it help recovery?",
                        "Major medical reference page with recovery guidance."),
                    new WebSearchDocument(
                        "https://www.nccih.nih.gov/health/red-light-therapy",
                        "Red light therapy: what the evidence says",
                        "NIH-style overview of red light therapy evidence, muscle recovery, and safety considerations.")
                }),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            pageFetcher,
            new EvidenceBoundaryProjector(),
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            new CanonicalUrlResolver(),
            new DuplicateContentCollapsePolicy(),
            new EventClusterBuilder());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                MaxResults: 2),
            CancellationToken.None);

        Assert.Equal(3, pageFetcher.AttemptedUrls.Count);
        Assert.True(session.ResultBundle.Documents.Count(document => document.ExtractedPage is { Passages.Count: > 0 }) >= 2);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.backfill_triggered", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.extracted_count=2", StringComparison.Ordinal));
    }


    [Fact]
    public async Task ExecuteAsync_DropsChatLandingSource_WhenNoFetchedEvidenceExists()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(_ =>
                new[]
                {
                    new WebSearchDocument(
                        "https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494",
                        "Photobiomodulation in human muscle tissue: an advantage in sports performance?",
                        "Systematic review of photobiomodulation in muscle tissue and exercise recovery."),
                    new WebSearchDocument(
                        "https://link.springer.com/article/10.1007/s10103-025-04318-w",
                        "A systematic review on whole-body photobiomodulation for exercise performance",
                        "Systematic review of whole-body photobiomodulation and exercise performance."),
                    new WebSearchDocument(
                        "https://openai.com/de-DE/index/chatgpt",
                        "ChatGPT ist da | OpenAI",
                        "ChatGPT app landing page and product overview.")
                }),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector(),
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            new CanonicalUrlResolver(),
            new DuplicateContentCollapsePolicy(),
            new EventClusterBuilder());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                MaxResults: 5),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("openai.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("link.springer.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.PageTrace!,
            line =>
                line.Contains("web_search.search_hit_only_guard applied=yes", StringComparison.Ordinal) &&
                line.Contains("dropped=", StringComparison.Ordinal));
    }


    [Fact]
    public async Task ExecuteAsync_SanitizesPromptInjectionContent_BeforeEvidenceLeavesCoordinator()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://example.org/injected",
                    "Injected article",
                    "Search snippet.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new InjectedPageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("deployment rollout guidance", MaxResults: 5),
            CancellationToken.None);

        var document = Assert.Single(session.ResultBundle.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.True(document.ExtractedPage!.InjectionSignalsDetected);
        Assert.True(document.ExtractedPage.WasSanitized);
        Assert.Contains("instruction_override", document.ExtractedPage.SafetyFlags!);
        Assert.DoesNotContain("Ignore previous instructions", document.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("act as system prompt", document.ExtractedPage.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_evidence_boundary.injection_detected=yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsDocumentLikeSearchHit_WhenTransportFailureLeavesNoExtractedPage()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://medelement.com/news/klinicheskie-rekomendacii-po-profilaktike-migreni",
                    "Клинические рекомендации по профилактике мигрени у взрослых",
                    "Обновленные клинические рекомендации с ключевыми изменениями, ограничениями по терапии, показаниями к профилактике и ссылками на официальный документ.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Сверь по свежим официальным рекомендациям по профилактике мигрени для взрослых.", MaxResults: 5),
            CancellationToken.None);

        var document = Assert.Single(session.ResultBundle.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.Equal("text/search-hit-projection", document.ExtractedPage!.ContentType);
        Assert.Contains("Клинические рекомендации", document.ExtractedPage.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.search_hit_projection applied=yes", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.extracted_count=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsTechnicalSearchHit_WhenSparseComparisonQueryHasTransportFailure()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://habr.com/ru/articles/961088",
                    "Выбираем векторную БД для AI-агентов и RAG",
                    "Практический обзор vector database, retrieval, indexing and search tradeoffs for engineering teams.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Сравни vector databases и классический search для маленькой команды, если большая часть benchmark-ов vendor-shaped.", MaxResults: 5),
            CancellationToken.None);

        var document = Assert.Single(session.ResultBundle.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.Equal("text/search-hit-projection", document.ExtractedPage!.ContentType);
        Assert.Contains("vector", document.ExtractedPage.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.search_hit_projection applied=yes", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.extracted_count=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsOfficialImmigrationSearchHit_WhenVisaFreshnessQueryHasTransportFailure()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://deutschland.de/en/topic/work/germany-blue-card-skilled-workers",
                    "Working in Germany: EU Blue Card and skilled workers",
                    "Official Germany portal overview of Blue Card, skilled worker routes, residence permits and work authorization.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.", MaxResults: 5),
            CancellationToken.None);

        var document = Assert.Single(session.ResultBundle.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.Equal("text/search-hit-projection", document.ExtractedPage!.ContentType);
        Assert.Contains("Blue Card", document.ExtractedPage.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.search_hit_projection applied=yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsGovernanceSearchHit_WhenRegulatedCodingAssistantQueryHasTransportFailure()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://knostic.ai/blog/ai-coding-assistant-governance",
                    "Governance for your AI Coding Assistant",
                    "Governance, policy controls, auditability and rollout considerations for enterprise AI coding assistants.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Составь осторожный план внедрения AI coding assistants в регулируемой компании, где claims о продуктивности пока спорные.", MaxResults: 5),
            CancellationToken.None);

        var document = Assert.Single(session.ResultBundle.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.Equal("text/search-hit-projection", document.ExtractedPage!.ContentType);
        Assert.Contains("Governance", document.ExtractedPage.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.search_hit_projection applied=yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_AddsEuAiActAuthoritativeFamilies_ForFreshRegulationQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.",
                MaxResults: 3),
            CancellationToken.None);

        Assert.Equal(3, session.ResultBundle.SourceUrls.Count);
        Assert.Contains(session.ResultBundle.SourceUrls, url => url.Contains("ai-act-service-desk.ec.europa.eu/en", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.SourceUrls, url => url.Contains("guidelines-gpai-providers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.SourceUrls, url => url.Contains("regulatory-framework-ai", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.ProviderTrace!,
            line => line.Contains("web_search.authoritative_source_family added=yes family=eu_ai_act_service_desk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.ProviderTrace!,
            line => line.Contains("web_search.authoritative_source_family added=yes family=eu_ai_act_gpai_provider_guidelines", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.ProviderTrace!,
            line => line.Contains("web_search.authoritative_source_family added=yes family=eu_ai_act_regulatory_framework", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.Iterations!,
            iteration => iteration.QueryKind == "freshness" && iteration.ResultCount >= 1 && iteration.AggregateResultCount >= 3);
    }


}
