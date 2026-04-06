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
    public async Task ExecuteAsync_UsesIterativeFreshnessQuery_WhenCurrentnessPromptNeedsSecondPass()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(
            new WebSearchDocument(
                "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview",
                "What's new in .NET 9",
                "Overview of .NET 9 features."),
            new WebSearchDocument(
                "javascript:void(0)",
                "Invalid",
                "Should be filtered.")));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("latest .NET 9 features", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal("iterative_live_results", session.ResultBundle.Outcome);
        Assert.False(session.ResultBundle.UsedDeterministicFallback);
        Assert.Single(session.ResultBundle.Documents);
        Assert.Single(session.ResultBundle.SourceUrls);
        Assert.NotNull(session.ResultBundle.Iterations);
        Assert.Equal(2, session.ResultBundle.Iterations!.Count);
        Assert.Equal("primary", session.ResultBundle.Iterations[0].QueryKind);
        Assert.Equal("freshness", session.ResultBundle.Iterations[1].QueryKind);
        Assert.Equal("sufficient:freshness_covered", session.ResultBundle.StopReason);
        Assert.NotNull(session.ResultBundle.ProviderTrace);
        Assert.NotEmpty(session.ResultBundle.ProviderTrace!);
        Assert.Equal("https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview", session.ResultBundle.SourceUrls[0]);
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("search_query.rewrite stage=topic_core", StringComparison.Ordinal));
    }


    [Fact]
    public async Task ExecuteAsync_UsesNarrowQuery_WhenLongInitialPromptIsTooBroad()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(plan =>
        {
            if (plan.QueryKind.Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new WebSearchDocument("https://example.org/primary", "Primary", "Primary result.")
                };
            }

            if (plan.QueryKind.Equals("narrow", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new WebSearchDocument("https://example.net/narrow", "Narrow", "Focused result.")
                };
            }

            return Array.Empty<WebSearchDocument>();
        }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("please find enterprise dotnet observability migration guidance with production tracing patterns and rollout constraints", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal("iterative_live_results", session.ResultBundle.Outcome);
        Assert.NotNull(session.ResultBundle.Iterations);
        Assert.Equal(2, session.ResultBundle.Iterations!.Count);
        Assert.Equal("narrow", session.ResultBundle.Iterations[1].QueryKind);
        Assert.Equal("sufficient:complex_query_covered", session.ResultBundle.StopReason);
        Assert.Equal(2, session.ResultBundle.SourceUrls.Count);
    }


    [Fact]
    public async Task ExecuteAsync_UsesStepBackBranch_WhenBroadPromptNeedsRecallUplift()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(plan =>
        {
            if (plan.QueryKind.Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<WebSearchDocument>();
            }

            if (plan.QueryKind.Equals("step_back", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new WebSearchDocument("https://example.net/overview", "Overview", "High-level overview result.")
                };
            }

            return Array.Empty<WebSearchDocument>();
        }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Объясни общую картину рисков и ограничений внедрения малых модульных реакторов в городской энергетике.", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal("iterative_live_results", session.ResultBundle.Outcome);
        Assert.NotNull(session.ResultBundle.Iterations);
        Assert.Equal(2, session.ResultBundle.Iterations!.Count);
        Assert.Equal("step_back", session.ResultBundle.Iterations[1].QueryKind);
        Assert.Equal("sufficient:query_expansion_covered", session.ResultBundle.StopReason);
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("search_query.expansion branch=step_back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UsesEvidenceThirdPass_WhenFreshMedicalGuidelinePromptNeedsStructuredFollowUp()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(plan =>
        {
            if (plan.QueryKind.Equals("primary", StringComparison.OrdinalIgnoreCase) ||
                plan.QueryKind.Equals("freshness", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new WebSearchDocument("https://diseases.medelement.com/disease/migraine", "Guideline", "Clinical guidance."),
                    new WebSearchDocument("https://legalacts.ru/doc/migraine-guideline", "Official", "Official document.")
                ];
            }

            if (plan.QueryKind.Equals("evidence", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new WebSearchDocument("https://diseases.medelement.com/disease/migraine", "Guideline", "Clinical guidance."),
                    new WebSearchDocument("https://legalacts.ru/doc/migraine-guideline", "Official", "Official document."),
                    new WebSearchDocument("https://headachejournal.onlinelibrary.wiley.com/migraine-review", "Review", "Evidence review.")
                ];
            }

            return Array.Empty<WebSearchDocument>();
        }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.", MaxResults: 5),
            CancellationToken.None);

        Assert.NotNull(session.ResultBundle.Iterations);
        Assert.Equal(3, session.ResultBundle.Iterations!.Count);
        Assert.Equal("freshness", session.ResultBundle.Iterations[1].QueryKind);
        Assert.Equal("evidence", session.ResultBundle.Iterations[2].QueryKind);
    }

    [Fact]
    public async Task ExecuteAsync_UsesPaperFocusThirdPass_WhenFreshLiteratureReviewPromptStillHasSingleSource()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(plan =>
        {
            if (plan.QueryKind.Equals("primary", StringComparison.OrdinalIgnoreCase) ||
                plan.QueryKind.Equals("freshness", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new WebSearchDocument("https://cyberleninka.ru/article/n/review-guidance", "Review guidance", "Single source.")
                ];
            }

            if (plan.QueryKind.Equals("paper_focus", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new WebSearchDocument("https://cyberleninka.ru/article/n/review-guidance", "Review guidance", "Single source."),
                    new WebSearchDocument("https://www.prisma-statement.org/systematic-review-guidance", "PRISMA", "Checklist guidance.")
                ];
            }

            return Array.Empty<WebSearchDocument>();
        }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Оцени мой метод literature review и проверь его по актуальным guidance для systematic reviews.", MaxResults: 5),
            CancellationToken.None);

        Assert.NotNull(session.ResultBundle.Iterations);
        Assert.Equal(3, session.ResultBundle.Iterations!.Count);
        Assert.Equal("freshness", session.ResultBundle.Iterations[1].QueryKind);
        Assert.Equal("paper_focus", session.ResultBundle.Iterations[2].QueryKind);
        Assert.Equal("sufficient:paper_focus_covered", session.ResultBundle.StopReason);
    }


}
