using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public sealed class CitationProjectionServiceTests
{
    [Fact]
    public void Build_FlattensEvidenceItemsIntoPassageLevelMatcherSources()
    {
        var service = new CitationProjectionService();
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://example.org/retries",
                Title: "Retry guidance",
                Snippet: "Summary snippet.",
                IsFallback: false,
                EvidenceKind: "fetched_page",
                PublishedAt: "2026-03-21",
                Passages: new[]
                {
                    new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://example.org/retries", "Retry guidance", "2026-03-21", "Retries should use bounded exponential backoff."),
                    new EvidencePassage("e1:p2", 1, 2, "1:p2", "https://example.org/retries", "Retry guidance", "2026-03-21", "Retry budgets should be capped per caller.")
                })
        };

        var projection = service.Build(evidenceItems.Select(static item => item.Url).ToArray(), evidenceItems);

        Assert.Equal(2, projection.MatcherSources.Count);
        Assert.Contains("bounded exponential backoff", projection.MatcherSources[0], StringComparison.Ordinal);
        Assert.Contains("Retry budgets should be capped", projection.MatcherSources[1], StringComparison.Ordinal);

        var second = service.Resolve(projection, 1);
        Assert.NotNull(second);
        Assert.Equal(1, second!.SourceOrdinal);
        Assert.Equal("1:p2", second.CitationLabel);
        Assert.Equal("e1:p2", second.PassageId);
        Assert.Equal(2, second.PassageOrdinal);
    }
}

