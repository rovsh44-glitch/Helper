using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.Tests;

public sealed class WebDocumentRerankerTests
{
    [Fact]
    public void Rerank_PrefersPaperSources_ForPaperAnalysisProfile()
    {
        var reranker = new WebDocumentReranker();
        var plan = new WebSearchPlan("Attention Residuals paper pdf", 5, 1, "research", "standard", true, "paper_focus");
        var candidates = new[]
        {
            CreateCandidate(
                "https://example.org/blog/attention-residuals",
                "Blog note about Attention Residuals",
                "Short high-level mention of the paper.",
                authorityLabel: "standard",
                authorityScore: 0.74d,
                finalScore: 0.74d),
            CreateCandidate(
                "https://arxiv.org/abs/2603.15031",
                "Attention Residuals",
                "Abstract and paper metadata for Attention Residuals.",
                authorityLabel: "academic",
                authorityScore: 0.70d,
                finalScore: 0.68d),
            CreateCandidate(
                "https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf",
                "Attention Residuals PDF",
                "Direct PDF source for the paper.",
                authorityLabel: "source_repository",
                authorityScore: 0.66d,
                finalScore: 0.67d)
        };

        var result = reranker.Rerank(plan.Query, plan, candidates, 3);

        Assert.Equal("paper_analysis", result.ProfileName);
        Assert.NotEmpty(result.Trace);
        Assert.NotEqual("https://example.org/blog/attention-residuals", result.Candidates[0].Document.Url);
        Assert.Contains(
            result.Candidates.Take(2),
            candidate => candidate.Document.Url.Contains("arxiv.org", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Document.Url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rerank_PrefersClinicalEvidence_ForMedicalEvidenceProfile()
    {
        var reranker = new WebDocumentReranker();
        var plan = new WebSearchPlan(
            "интервальное голодание снижение веса без потери мышц systematic review clinical guidance",
            5,
            1,
            "research",
            "verification",
            true,
            "evidence");
        var candidates = new[]
        {
            CreateCandidate(
                "https://doctor.rambler.ru/healthylife/intermittent-fasting-guide",
                "Интервальное голодание: гид",
                "Популярный healthy life обзор по интервальному голоданию.",
                authorityLabel: "health_media_portal",
                authorityScore: 0.48d,
                finalScore: 0.71d,
                authorityReasons: new[] { "lifestyle_health_media_mismatch" }),
            CreateCandidate(
                "https://pubmed.ncbi.nlm.nih.gov/35565749/",
                "Intermittent Fasting versus Continuous Calorie Restriction",
                "Systematic review and meta-analysis of randomized trials.",
                authorityLabel: "medical_research_index",
                authorityScore: 0.84d,
                finalScore: 0.69d,
                authorityReasons: new[] { "primary_medical_research_match", "medical_evidence_match" }),
            CreateCandidate(
                "https://pmc.ncbi.nlm.nih.gov/articles/PMC9762455/",
                "Time-restricted eating as a novel strategy for treatment of obesity",
                "Review of body composition outcomes and muscle preservation.",
                authorityLabel: "medical_research_fulltext",
                authorityScore: 0.82d,
                finalScore: 0.68d,
                authorityReasons: new[] { "primary_medical_research_match", "medical_evidence_match" })
        };

        var result = reranker.Rerank(plan.Query, plan, candidates, 3);

        Assert.Equal("medical_evidence", result.ProfileName);
        Assert.DoesNotContain(result.Candidates.Take(2), candidate => candidate.Document.Url.Contains("rambler.ru", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Trace, line => line.Contains("profile=medical_evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rerank_PullsAlternativeHost_ForContrastiveProfile()
    {
        var reranker = new WebDocumentReranker();
        var plan = new WebSearchPlan(
            "measles outbreak europe official measures compare sources",
            3,
            1,
            "research",
            "standard",
            true,
            "contradiction");
        var candidates = new[]
        {
            CreateCandidate(
                "https://who.int/news/item/one",
                "WHO update one",
                "Official update on measles outbreak measures in Europe.",
                authorityLabel: "global_public_health",
                authorityScore: 0.90d,
                finalScore: 0.78d,
                authoritative: true),
            CreateCandidate(
                "https://who.int/news/item/two",
                "WHO update two",
                "Another official WHO update on measles outbreak measures in Europe.",
                authorityLabel: "global_public_health",
                authorityScore: 0.89d,
                finalScore: 0.77d,
                authoritative: true),
            CreateCandidate(
                "https://news.un.org/en/story/2026/03/measles",
                "UN News measles update",
                "UN News coverage summarizing official measures in Europe.",
                authorityLabel: "multilateral_news",
                authorityScore: 0.78d,
                finalScore: 0.73d,
                authoritative: true)
        };

        var result = reranker.Rerank(plan.Query, plan, candidates, 3);

        Assert.Equal("medical_evidence", result.ProfileName);
        Assert.Contains(result.Candidates.Take(2), candidate => candidate.Document.Url.Contains("news.un.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Trace, line => line.Contains("distinct_hosts=2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rerank_PrefersResearchSources_OverCommercialLightTherapyVendors_ForRecoveryEvidence()
    {
        var reranker = new WebDocumentReranker();
        var plan = new WebSearchPlan(
            "photobiomodulation muscle recovery pubmed pmc systematic review",
            5,
            1,
            "research",
            "verification",
            true,
            "evidence");
        var candidates = new[]
        {
            CreateCandidate(
                "https://lighttherapyhome.com/ru/red-light-therapy-workout-boost-recovery",
                "Терапия красным светом после тренировки",
                "Guide to product-supported recovery and LED benefits.",
                authorityLabel: "commercial_health_vendor",
                authorityScore: 0.28d,
                finalScore: 0.56d),
            CreateCandidate(
                "https://pubmed.ncbi.nlm.nih.gov/37512345/",
                "Photobiomodulation therapy and exercise recovery",
                "Systematic review of muscle recovery and performance outcomes.",
                authorityLabel: "medical_research_index",
                authorityScore: 0.84d,
                finalScore: 0.66d,
                authorityReasons: new[] { "primary_medical_research_match", "medical_evidence_match" }),
            CreateCandidate(
                "https://pmc.ncbi.nlm.nih.gov/articles/PMC1234567/",
                "Red light therapy for muscle recovery",
                "Review of randomized trials and recovery markers.",
                authorityLabel: "medical_research_fulltext",
                authorityScore: 0.82d,
                finalScore: 0.65d,
                authorityReasons: new[] { "primary_medical_research_match", "medical_evidence_match" })
        };

        var result = reranker.Rerank(plan.Query, plan, candidates, 3);

        Assert.DoesNotContain(result.Candidates.Take(2), candidate => candidate.Document.Url.Contains("lighttherapyhome.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rerank_PrefersTrustedLegalAndOfficialSources_ForLawRegulationProfile()
    {
        var reranker = new WebDocumentReranker();
        var plan = new WebSearchPlan(
            "latest GDPR compliance enforcement guidance",
            3,
            1,
            "research",
            "standard",
            true,
            "freshness");
        var candidates = new[]
        {
            CreateCandidate(
                "https://random-blog.example.com/gdpr-guide",
                "Simple GDPR guide",
                "General blog summary.",
                authorityLabel: "standard",
                authorityScore: 0.68d,
                finalScore: 0.72d),
            CreateCandidate(
                "https://gdpr-info.eu/",
                "GDPR text",
                "Consolidated legal text and articles.",
                authorityLabel: "trusted_legal_reference",
                authorityScore: 0.70d,
                finalScore: 0.69d),
            CreateCandidate(
                "https://ec.europa.eu/commission/presscorner/gdpr-update",
                "Commission update",
                "Official regulatory update.",
                authorityLabel: "government_primary",
                authorityScore: 0.74d,
                finalScore: 0.68d,
                authoritative: true)
        };

        var result = reranker.Rerank(plan.Query, plan, candidates, 3);

        Assert.Contains(result.Trace, line => line.Contains("domain_profile=law_regulation", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual("https://random-blog.example.com/gdpr-guide", result.Candidates[0].Document.Url);
    }

    [Fact]
    public void Rerank_PrefersAcademicAndRepositorySources_ForScienceReferenceProfile()
    {
        var reranker = new WebDocumentReranker();
        var plan = new WebSearchPlan(
            "analyze paper Attention Residuals arxiv pdf",
            3,
            1,
            "research",
            "standard",
            true,
            "paper_focus");
        var candidates = new[]
        {
            CreateCandidate(
                "https://generic-tech-blog.example.com/attention-residuals",
                "Attention Residuals overview",
                "General article.",
                authorityLabel: "standard",
                authorityScore: 0.72d,
                finalScore: 0.74d),
            CreateCandidate(
                "https://arxiv.org/abs/2603.15031",
                "Attention Residuals",
                "Paper abstract.",
                authorityLabel: "academic",
                authorityScore: 0.70d,
                finalScore: 0.69d,
                authoritative: true),
            CreateCandidate(
                "https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf",
                "Attention Residuals PDF",
                "Direct PDF.",
                authorityLabel: "source_repository",
                authorityScore: 0.69d,
                finalScore: 0.68d,
                authoritative: true)
        };

        var result = reranker.Rerank(plan.Query, plan, candidates, 3);

        Assert.Contains(result.Trace, line => line.Contains("domain_profile=science_reference", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual("https://generic-tech-blog.example.com/attention-residuals", result.Candidates[0].Document.Url);
    }

    private static RankedWebDocumentCandidate CreateCandidate(
        string url,
        string title,
        string snippet,
        string authorityLabel,
        double authorityScore,
        double finalScore,
        bool authoritative = false,
        IReadOnlyList<string>? authorityReasons = null)
    {
        return new RankedWebDocumentCandidate(
            new WebSearchDocument(url, title, snippet),
            new SourceAuthorityAssessment(
                authorityScore,
                authorityLabel,
                authoritative,
                authorityReasons ?? Array.Empty<string>()),
            new SpamSeoAssessment(0.02d, LowTrust: false, Array.Empty<string>()),
            finalScore);
    }
}

