using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.Tests;

public sealed class DomainAuthorityFloorPolicyTests
{
    [Fact]
    public void Apply_DropsWeakGeneralSources_ForLawRegulationProfile()
    {
        var policy = new DomainAuthorityFloorPolicy();
        var plan = new WebSearchPlan(
            "latest GDPR compliance enforcement guidance",
            5,
            1,
            "research",
            "standard",
            false,
            "freshness");
        var candidates = new[]
        {
            CreateCandidate("https://gdpr-info.eu/", "GDPR text", "Regulation text", "trusted_legal_reference", 0.70d, 0.70d),
            CreateCandidate("https://www.consultant.ru/document/cons_doc_LAW_12345/", "Compliance note", "Legal reference", "trusted_legal_reference", 0.68d, 0.68d),
            CreateCandidate("https://random-blog.example.com/gdpr-guide", "Simple GDPR guide", "Blog summary.", "standard", 0.61d, 0.61d)
        };

        var trace = new List<string>();
        var result = policy.Apply(plan.Query, plan, candidates, trace);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, candidate => candidate.Document.Url.Contains("random-blog.example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trace, line => line.Contains("law_regulation_authority_floor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_DropsWeakGeneralSources_ForFinanceProfile()
    {
        var policy = new DomainAuthorityFloorPolicy();
        var plan = new WebSearchPlan(
            "latest SEC guidance for spot bitcoin ETF approval",
            5,
            1,
            "research",
            "standard",
            false,
            "freshness");
        var candidates = new[]
        {
            CreateCandidate("https://sec.gov/news/statement/spot-bitcoin-etf", "SEC statement", "Official statement", "government_primary", 0.74d, 0.74d, authoritative: true),
            CreateCandidate("https://bloomberg.com/crypto/spot-bitcoin-etf", "Bloomberg market note", "Market context", "major_business_news", 0.64d, 0.64d, authoritative: true),
            CreateCandidate("https://my-crypto-blog.example.com/etf-soon", "ETF soon?", "Personal opinion blog", "standard", 0.60d, 0.60d)
        };

        var trace = new List<string>();
        var result = policy.Apply(plan.Query, plan, candidates, trace);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, candidate => candidate.Document.Url.Contains("my-crypto-blog.example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trace, line => line.Contains("finance_market_authority_floor", StringComparison.OrdinalIgnoreCase));
    }

    private static RankedWebDocumentCandidate CreateCandidate(
        string url,
        string title,
        string snippet,
        string authorityLabel,
        double authorityScore,
        double finalScore,
        bool authoritative = false)
    {
        return new RankedWebDocumentCandidate(
            new WebSearchDocument(url, title, snippet),
            new SourceAuthorityAssessment(authorityScore, authorityLabel, authoritative, Array.Empty<string>()),
            new SpamSeoAssessment(0.02d, LowTrust: false, Array.Empty<string>()),
            finalScore);
    }
}

