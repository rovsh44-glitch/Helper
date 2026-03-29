using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.Tests;

public sealed class MedicalEvidenceFloorPolicyTests
{
    [Fact]
    public void Apply_DropsMedicalOpinionAndWiki_WhenStrongMedicalSourcesExist()
    {
        var policy = new MedicalEvidenceFloorPolicy();
        var plan = new WebSearchPlan(
            "photobiomodulation red light therapy muscle recovery after training",
            5,
            1,
            "research",
            "standard",
            false);
        var candidates = new[]
        {
            CreateCandidate(
                "https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863/",
                "The Effect of Photobiomodulation Therapy on Muscle Performance",
                "Meta-analysis of randomized controlled trials.",
                "medical_research_fulltext",
                0.92d,
                0.92d,
                authoritative: true),
            CreateCandidate(
                "https://mayoclinic.org/healthy-lifestyle/fitness/expert-answers/red-light-therapy/faq-20441303",
                "Red light therapy: can it help recovery?",
                "Major medical reference with recovery discussion.",
                "major_medical_reference",
                0.78d,
                0.78d,
                authoritative: true),
            CreateCandidate(
                "https://jeffreypengmd.com/post/red-light-therapy-muscle-recovery",
                "Red Light Therapy for Muscle Recovery: Does It Actually Work?",
                "Sports medicine perspective blog.",
                "medical_opinion_blog",
                0.24d,
                0.24d),
            CreateCandidate(
                "https://physio-pedia.com/Red_Light_Therapy_and_Muscle_Recovery",
                "Red Light Therapy and Muscle Recovery - Physiopedia",
                "Educational wiki-style overview.",
                "medical_wiki_reference",
                0.22d,
                0.22d)
        };

        var trace = new List<string>();
        var result = policy.Apply(plan.Query, plan, candidates, trace);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, candidate => candidate.Document.Url.Contains("jeffreypengmd.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result, candidate => candidate.Document.Url.Contains("physio-pedia.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trace, line => line.Contains("medical_authority_floor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_DropsSecondaryResearchAggregator_WhenStrongMedicalSourcesExist()
    {
        var policy = new MedicalEvidenceFloorPolicy();
        var plan = new WebSearchPlan(
            "photobiomodulation red light therapy muscle recovery after training",
            5,
            1,
            "research",
            "standard",
            false);
        var candidates = new[]
        {
            CreateCandidate(
                "https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863/",
                "PBM meta-analysis",
                "Recovery meta-analysis.",
                "medical_research_fulltext",
                0.84d,
                0.84d,
                authoritative: true),
            CreateCandidate(
                "https://link.springer.com/article/10.1007/s10103-025-04318-w",
                "Whole-body PBM review",
                "Systematic review on exercise performance and recovery.",
                "academic_publisher_article",
                0.78d,
                0.78d,
                authoritative: true),
            CreateCandidate(
                "https://researchgate.net/publication/388513692_A_systematic_review_on_whole-body_photobiomodulation_for_exercise_performance_and_recovery",
                "ResearchGate mirror",
                "Secondary aggregator mirror of the same review.",
                "secondary_research_aggregator",
                0.42d,
                0.42d)
        };

        var trace = new List<string>();
        var result = policy.Apply(plan.Query, plan, candidates, trace);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, candidate => candidate.Document.Url.Contains("researchgate.net", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trace, line => line.Contains("medical_authority_floor", StringComparison.OrdinalIgnoreCase));
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

