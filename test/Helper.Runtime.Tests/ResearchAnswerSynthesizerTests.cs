using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public sealed class ResearchAnswerSynthesizerTests
{
    [Fact]
    public void Build_DetectsNumericDisagreement_AcrossComparableSources()
    {
        var synthesizer = new ResearchAnswerSynthesizer();
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
        var claims = new[]
        {
            new ClaimGrounding(
                "PostgreSQL 16 was released in 2023 with improved replication",
                ClaimSentenceType.Fact,
                SourceIndex: 1,
                EvidenceGrade: "strong",
                EvidenceCitationLabel: "1"),
            new ClaimGrounding(
                "PostgreSQL 16 was released in 2018 with improved replication",
                ClaimSentenceType.Fact,
                SourceIndex: 2,
                EvidenceGrade: "strong",
                EvidenceCitationLabel: "2")
        };

        var plan = synthesizer.Build(claims, evidenceItems);

        Assert.NotNull(plan);
        Assert.NotNull(plan!.Disagreement);
        Assert.Equal("numeric_conflict", plan.Disagreement!.Kind);
        Assert.Equal("1", plan.Disagreement.Left.CitationLabel);
        Assert.Equal("2", plan.Disagreement.Right.CitationLabel);
    }

    [Fact]
    public void Build_DoesNotTreatNewsAndFactSheet_AsDisagreement()
    {
        var synthesizer = new ResearchAnswerSynthesizer();
        var evidenceItems = new[]
        {
            new ResearchEvidenceItem(
                Ordinal: 1,
                Url: "https://news.un.org/ru/story/2024/01/1448497",
                Title: "Почему возникла вспышка кори в Европейском регионе ВОЗ и как ...",
                Snippet: "В 2024 году случаи кори выросли в Европейском регионе ВОЗ.",
                IsFallback: false),
            new ResearchEvidenceItem(
                Ordinal: 2,
                Url: "https://who.int/ru/news-room/fact-sheets/detail/measles",
                Title: "Корь",
                Snippet: "Наиболее эффективным способом профилактики кори является вакцинация.",
                IsFallback: false)
        };
        var claims = new[]
        {
            new ClaimGrounding(
                "В 2024 году случаи кори выросли в Европейском регионе ВОЗ.",
                ClaimSentenceType.Fact,
                SourceIndex: 1,
                EvidenceGrade: "strong",
                EvidenceCitationLabel: "1"),
            new ClaimGrounding(
                "Наиболее эффективным способом профилактики кори является вакцинация.",
                ClaimSentenceType.Fact,
                SourceIndex: 2,
                EvidenceGrade: "strong",
                EvidenceCitationLabel: "2")
        };

        var plan = synthesizer.Build(claims, evidenceItems);

        Assert.NotNull(plan);
        Assert.Null(plan!.Disagreement);
    }
}

