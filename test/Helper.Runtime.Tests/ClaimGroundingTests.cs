using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public class ClaimGroundingTests
{
    [Fact]
    public void ClaimSourceMatcher_PrefersBestLexicalNumericSource()
    {
        var matcher = new ClaimSourceMatcher();

        var match = matcher.Match(
            "PostgreSQL 16 was released in 2023 with improved logical replication.",
            new[]
            {
                "https://example.com/weather-forecast",
                "https://www.postgresql.org/docs/16/release-16.html"
            });

        Assert.Equal(1, match.SourceIndex);
        Assert.True(match.Score > 0.1);
        Assert.NotEqual("none", match.MatchMode);
    }

    [Fact]
    public void ClaimExtractionService_ClassifiesFactOpinionInstruction()
    {
        var service = new ClaimExtractionService();

        var claims = service.Extract("PostgreSQL 16 was released in 2023. I think this version is excellent. Use pg_upgrade to migrate.");

        Assert.True(claims.Count >= 3);
        Assert.Contains(claims, c => c.Type == ClaimSentenceType.Fact && c.Text.Contains("PostgreSQL 16", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(claims, c => c.Type == ClaimSentenceType.Opinion && c.Text.Contains("I think", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(claims, c => c.Type == ClaimSentenceType.Instruction && c.Text.Contains("Use pg_upgrade", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CitationGroundingService_GroundsFactualClaims_WithCoverageFromFactClaims()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService());
        var response = grounding.Apply(
            "PostgreSQL 16 was released in 2023. I think this version is excellent. Use pg_upgrade to migrate.",
            new[] { "https://www.postgresql.org/docs/16/release-16.html" },
            factualPrompt: true);

        Assert.Equal("grounded_with_limits", response.Status);
        Assert.True(response.CitationCoverage >= 0.99);
        Assert.Contains("[1]", response.Content, StringComparison.Ordinal);
        Assert.Contains("uncertainty.source_url_only_evidence", response.UncertaintyFlags);
        Assert.NotNull(response.Claims);
        Assert.Contains(response.Claims!, claim => claim.Type == ClaimSentenceType.Fact && claim.SourceIndex == 1);
        Assert.DoesNotContain(response.Claims!, claim => claim.Type == ClaimSentenceType.Opinion);
    }

    [Fact]
    public void CitationGroundingService_ReturnsUnverified_WhenSourcesMissing()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService());
        var response = grounding.Apply(
            "C# 12 introduced collection expressions in 2023.",
            Array.Empty<string>(),
            factualPrompt: true);

        Assert.Equal("unverified", response.Status);
        Assert.Equal(0, response.CitationCoverage);
        Assert.Contains("missing_sources_for_factual_claims", response.UncertaintyFlags);
        Assert.Contains("uncertainty.no_verified_claims", response.UncertaintyFlags);
        Assert.Contains("uncertainty.evidence_none", response.UncertaintyFlags);
    }

    [Fact]
    public void ClaimSourceMatcher_DetectsNumericContradiction()
    {
        var matcher = new ClaimSourceMatcher();

        var match = matcher.Match(
            "PostgreSQL 16 was released in 2023.",
            new[] { "Official release note: PostgreSQL 16 was released in 2018." });

        Assert.True(match.ContradictionDetected);
        Assert.True(match.Confidence <= match.Score);
    }

    [Fact]
    public void CitationGroundingService_AddsContradictionUncertaintyFlag()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService());
        var response = grounding.Apply(
            "PostgreSQL 16 was released in 2023.",
            new[] { "Official release note: PostgreSQL 16 was released in 2018." },
            factualPrompt: true);

        Assert.Equal("grounded_with_contradictions", response.Status);
        Assert.Contains("uncertainty.contradiction_detected", response.UncertaintyFlags);
    }

    [Fact]
    public void CitationGroundingService_DoesNotTreatNewsUrlYear_AsRealContradiction()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService(), new CitationProjectionService());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://who.int/europe/ru/news/item/13-03-2025-european-region-reports-highest-number-of-measles-cases-in-more-than-25-years---unicef--who-europe",
                Title: "По данным ЮНИСЕФ и ЕРБ ВОЗ, в 2024 г. в Европейском регионе было ...",
                Snippet: "По данным ЮНИСЕФ и ЕРБ ВОЗ, в 2024 г. в Европейском регионе было зарегистрировано рекордное число случаев кори.",
                IsFallback: false),
            new ResearchEvidenceItem(
                Ordinal: 2,
                Url: "https://news.un.org/ru/story/2024/01/1448497",
                Title: "Почему возникла вспышка кори в Европейском регионе ВОЗ и как ...",
                Snippet: "В 2024 году случаи кори выросли в Европейском регионе ВОЗ.",
                IsFallback: false)
        };

        var response = grounding.Apply(
            "В 2024 году случаи кори выросли в Европейском регионе ВОЗ.",
            evidenceItems.Select(static item => item.Url).ToArray(),
            factualPrompt: true,
            intent: IntentType.Research,
            language: "ru",
            evidenceItems: evidenceItems);

        Assert.Equal("grounded_with_limits", response.Status);
        Assert.DoesNotContain("uncertainty.contradiction_detected", response.UncertaintyFlags);
    }

    [Fact]
    public void CitationGroundingService_RewritesResearchOutput_IntoSourceSpecificComparison()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService(), new CitationProjectionService());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://learn.microsoft.com/retries",
                Title: "Retry guidance",
                Snippet: "Retries should use exponential backoff for transient faults.",
                IsFallback: false),
            new ResearchEvidenceItem(
                Ordinal: 2,
                Url: "https://aws.amazon.com/timeouts",
                Title: "Timeout guidance",
                Snippet: "Timeouts should be coordinated with retries to avoid overload.",
                IsFallback: false)
        };

        var response = grounding.Apply(
            "Retries should use exponential backoff for transient faults. Timeouts should be coordinated with retries to avoid overload.",
            evidenceItems.Select(static item => item.Url).ToArray(),
            factualPrompt: true,
            intent: IntentType.Research,
            language: "en",
            evidenceItems: evidenceItems);

        Assert.Equal("grounded_with_limits", response.Status);
        Assert.Contains("The strongest supported reading is this:", response.Content, StringComparison.Ordinal);
        Assert.Contains("Retry guidance [1] emphasizes", response.Content, StringComparison.Ordinal);
        Assert.Contains("while Timeout guidance [2] emphasizes", response.Content, StringComparison.Ordinal);
        Assert.Contains("search-result snippets", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Overview", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CitationGroundingService_AnchorsClaims_ToVerifiedPassages_WhenPassagesExist()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService(), new CitationProjectionService());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://learn.microsoft.com/retries",
                Title: "Retry guidance",
                Snippet: "Summary snippet.",
                IsFallback: false,
                EvidenceKind: "fetched_page",
                PublishedAt: "2026-03-21",
                Passages: new[]
                {
                    new EvidencePassage("e1:p1", 1, 1, "1:p1", "https://learn.microsoft.com/retries", "Retry guidance", "2026-03-21", "Retries should use exponential backoff for transient faults."),
                    new EvidencePassage("e1:p2", 1, 2, "1:p2", "https://learn.microsoft.com/retries", "Retry guidance", "2026-03-21", "Retry budgets should be capped to avoid overload.")
                }),
            new ResearchEvidenceItem(
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
                })
        };

        var response = grounding.Apply(
            "Retries should use exponential backoff for transient faults. Timeouts should be coordinated with retries to avoid overload.",
            evidenceItems.Select(static item => item.Url).ToArray(),
            factualPrompt: true,
            intent: IntentType.Research,
            language: "en",
            evidenceItems: evidenceItems);

        Assert.Equal("grounded", response.Status);
        Assert.Contains("[1:p1]", response.Content, StringComparison.Ordinal);
        Assert.Contains("[2:p1]", response.Content, StringComparison.Ordinal);
        Assert.NotNull(response.Claims);
        Assert.Contains(response.Claims!, claim => claim.EvidenceCitationLabel == "1:p1" && claim.EvidencePassageId == "e1:p1");
        Assert.Contains(response.Claims!, claim => claim.EvidenceCitationLabel == "2:p1" && claim.EvidencePassageId == "e2:p1");
    }

    [Fact]
    public void CitationGroundingService_SurfacesExplicitSourceDisagreement_ForResearchComparisons()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService(), new CitationProjectionService());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://source-a.example/release",
                Title: "Release note A",
                Snippet: "PostgreSQL 16 was released in 2023 with improved replication.",
                IsFallback: false),
            new ResearchEvidenceItem(
                Ordinal: 2,
                Url: "https://source-b.example/release",
                Title: "Release note B",
                Snippet: "PostgreSQL 16 was released in 2018 with improved replication.",
                IsFallback: false)
        };

        var response = grounding.Apply(
            "Release note A says PostgreSQL 16 was released in 2023 with improved replication. Release note B says PostgreSQL 16 was released in 2018 with improved replication.",
            evidenceItems.Select(static item => item.Url).ToArray(),
            factualPrompt: true,
            intent: IntentType.Research,
            language: "en",
            evidenceItems: new[]
            {
                evidenceItems[0] with
                {
                    EvidenceKind = "fetched_page",
                    Passages = new[]
                    {
                        new EvidencePassage("e1:p1", 1, 1, "1:p1", evidenceItems[0].Url, evidenceItems[0].Title, null, evidenceItems[0].Snippet)
                    }
                },
                evidenceItems[1] with
                {
                    EvidenceKind = "fetched_page",
                    Passages = new[]
                    {
                        new EvidencePassage("e2:p1", 2, 1, "2:p1", evidenceItems[1].Url, evidenceItems[1].Title, null, evidenceItems[1].Snippet)
                    }
                }
            });

        Assert.Equal("grounded_with_contradictions", response.Status);
        Assert.Contains("The main disagreement is explicit:", response.Content, StringComparison.Ordinal);
        Assert.Contains("unresolved", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uncertainty.source_disagreement", response.UncertaintyFlags);
        Assert.Contains("uncertainty.contradiction_detected", response.UncertaintyFlags);
    }

    [Fact]
    public void CitationGroundingService_DowngradesSearchHitOnlyEvidence_ToGroundedWithLimits()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService(), new CitationProjectionService());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://example.org/clinical-summary",
                Title: "Clinical summary",
                Snippet: "Migraine prevention usually combines trigger management with evidence-based preventive therapy when attack burden is high.",
                IsFallback: false,
                EvidenceKind: "search_hit")
        };

        var response = grounding.Apply(
            "Migraine prevention usually combines trigger management with evidence-based preventive therapy when attack burden is high.",
            evidenceItems.Select(static item => item.Url).ToArray(),
            factualPrompt: true,
            intent: IntentType.Research,
            language: "en",
            evidenceItems: evidenceItems);

        Assert.Equal("grounded_with_limits", response.Status);
        Assert.Contains("uncertainty.search_hit_only_evidence", response.UncertaintyFlags);
    }

    [Fact]
    public void CitationGroundingService_DoesNotRaiseHardContradiction_ForSearchHitOnlyEvidence()
    {
        var grounding = new CitationGroundingService(new ClaimExtractionService(), new ClaimSourceMatcher(), new EvidenceGradingService(), new CitationProjectionService());
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://example.org/clinical-summary",
                Title: "Clinical summary",
                Snippet: "PostgreSQL 16 was released in 2018 according to this short search snippet.",
                IsFallback: false,
                EvidenceKind: "search_hit")
        };

        var response = grounding.Apply(
            "PostgreSQL 16 was released in 2023.",
            evidenceItems.Select(static item => item.Url).ToArray(),
            factualPrompt: true,
            intent: IntentType.Research,
            language: "en",
            evidenceItems: evidenceItems);

        Assert.Equal("grounded_with_limits", response.Status);
        Assert.DoesNotContain("uncertainty.contradiction_detected", response.UncertaintyFlags);
    }

    [Fact]
    public void EvidenceGradingService_BuildsMixedUncertaintyTaxonomy()
    {
        var service = new EvidenceGradingService();
        var claims = new[]
        {
            new ClaimGrounding("Claim A", ClaimSentenceType.Fact, 1, "strong"),
            new ClaimGrounding("Claim B", ClaimSentenceType.Fact, 2, "weak"),
            new ClaimGrounding("Claim C", ClaimSentenceType.Fact, null, "none")
        };

        var flags = service.BuildUncertaintyFlags(claims, totalFactualClaims: 3, verifiedClaims: 2);

        Assert.Contains("uncertainty.low_coverage", flags);
        Assert.Contains("uncertainty.evidence_weak", flags);
        Assert.Contains("uncertainty.evidence_none", flags);
        Assert.Contains("uncertainty.evidence_mixed", flags);
    }

    [Fact]
    public void EvidenceGradingService_DoesNotEscalateSingleWeakBridgeClaim_WhenCoverageIsComplete()
    {
        var service = new EvidenceGradingService();
        var claims = new[]
        {
            new ClaimGrounding("Claim A", ClaimSentenceType.Fact, 1, "strong"),
            new ClaimGrounding("Claim B", ClaimSentenceType.Fact, 2, "strong"),
            new ClaimGrounding("Claim C", ClaimSentenceType.Fact, 3, "strong"),
            new ClaimGrounding("Claim D", ClaimSentenceType.Fact, 1, "medium"),
            new ClaimGrounding("Claim E", ClaimSentenceType.Fact, 2, "medium"),
            new ClaimGrounding("Claim F", ClaimSentenceType.Fact, 3, "strong"),
            new ClaimGrounding("Synthetic bridge claim", ClaimSentenceType.Fact, 2, "weak")
        };

        var flags = service.BuildUncertaintyFlags(claims, totalFactualClaims: 7, verifiedClaims: 7);

        Assert.DoesNotContain("uncertainty.evidence_weak", flags);
        Assert.Contains("uncertainty.evidence_mixed", flags);
    }
}

