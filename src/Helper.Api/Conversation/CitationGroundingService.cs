using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public interface ICitationGroundingService
{
    GroundingResult Apply(
        string output,
        IReadOnlyList<string> sources,
        bool factualPrompt,
        IntentType intent = IntentType.Unknown,
        string? language = null,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems = null,
        string? requestPrompt = null);
}

public sealed record GroundingResult(
    string Content,
    string Status,
    double CitationCoverage,
    IReadOnlyList<string> UncertaintyFlags,
    IReadOnlyList<ClaimGrounding>? Claims = null);

public sealed record ClaimGrounding(
    string Claim,
    ClaimSentenceType Type,
    int? SourceIndex,
    string EvidenceGrade,
    string? QuoteSpan = null,
    double MatchConfidence = 0,
    bool ContradictionDetected = false,
    string? EvidencePassageId = null,
    int? EvidencePassageOrdinal = null,
    string? EvidenceCitationLabel = null,
    string? EvidenceKind = null);

public sealed class CitationGroundingService : ICitationGroundingService
{
    private readonly IClaimExtractionService _claimExtraction;
    private readonly IClaimSourceMatcher _claimSourceMatcher;
    private readonly IEvidenceGradingService _evidenceGrading;
    private readonly ResearchGroundedSynthesisFormatter _researchFormatter;
    private readonly ICitationProjectionService _citationProjection;
    private readonly IResearchEvidenceTierPolicy _evidenceTierPolicy;

    public CitationGroundingService()
        : this(
            new ClaimExtractionService(),
            new ClaimSourceMatcher(),
            new EvidenceGradingService(),
            new ResearchGroundedSynthesisFormatter(),
            new CitationProjectionService(),
            new ResearchEvidenceTierPolicy())
    {
    }

    public CitationGroundingService(
        IClaimExtractionService claimExtraction,
        IClaimSourceMatcher claimSourceMatcher,
        IEvidenceGradingService evidenceGrading)
        : this(
            claimExtraction,
            claimSourceMatcher,
            evidenceGrading,
            new CitationProjectionService(),
            researchFormatter: null,
            evidenceTierPolicy: new ResearchEvidenceTierPolicy())
    {
    }

    internal CitationGroundingService(
        IClaimExtractionService claimExtraction,
        IClaimSourceMatcher claimSourceMatcher,
        IEvidenceGradingService evidenceGrading,
        ICitationProjectionService citationProjection,
        ResearchGroundedSynthesisFormatter? researchFormatter = null,
        IResearchEvidenceTierPolicy? evidenceTierPolicy = null)
        : this(
            claimExtraction,
            claimSourceMatcher,
            evidenceGrading,
            researchFormatter ?? new ResearchGroundedSynthesisFormatter(),
            citationProjection,
            evidenceTierPolicy ?? new ResearchEvidenceTierPolicy())
    {
    }

    private CitationGroundingService(
        IClaimExtractionService claimExtraction,
        IClaimSourceMatcher claimSourceMatcher,
        IEvidenceGradingService evidenceGrading,
        ResearchGroundedSynthesisFormatter researchFormatter,
        ICitationProjectionService citationProjection,
        IResearchEvidenceTierPolicy evidenceTierPolicy)
    {
        _claimExtraction = claimExtraction;
        _claimSourceMatcher = claimSourceMatcher;
        _evidenceGrading = evidenceGrading;
        _researchFormatter = researchFormatter;
        _citationProjection = citationProjection;
        _evidenceTierPolicy = evidenceTierPolicy;
    }

    public GroundingResult Apply(
        string output,
        IReadOnlyList<string> sources,
        bool factualPrompt,
        IntentType intent = IntentType.Unknown,
        string? language = null,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems = null,
        string? requestPrompt = null)
    {
        var safeOutput = output ?? string.Empty;
        var uncertainty = new List<string>();
        if (!factualPrompt)
        {
            return new GroundingResult(safeOutput, "not_required", 1.0, uncertainty, Array.Empty<ClaimGrounding>());
        }

        var extracted = _claimExtraction.Extract(safeOutput);
        var factualClaims = extracted
            .Where(x => x.Type == ClaimSentenceType.Fact)
            .Take(16)
            .ToList();
        if (factualClaims.Count == 0)
        {
            return new GroundingResult(
                safeOutput,
                "insufficient_claims",
                0.0,
                new[] { "no_factual_claims_detected" },
                Array.Empty<ClaimGrounding>());
        }

        var liveEvidenceItems = evidenceItems?
            .Where(static item => !item.IsFallback)
            .OrderBy(static item => item.Ordinal)
            .ToArray();
        var projection = _citationProjection.Build(sources, liveEvidenceItems);
        var matcherSources = projection.MatcherSources;
        var evidenceTier = _evidenceTierPolicy.Evaluate(sources, liveEvidenceItems ?? evidenceItems);

        if (matcherSources.Count == 0)
        {
            uncertainty.Add("missing_sources_for_factual_claims");
            var noSourceClaims = factualClaims.Select(x => new ClaimGrounding(x.Text, x.Type, null, "none")).ToList();
            uncertainty.AddRange(_evidenceGrading.BuildUncertaintyFlags(noSourceClaims, factualClaims.Count, verifiedClaims: 0));
            return new GroundingResult(
                $"{safeOutput}\n\nUncertainty: no verifiable source anchors were found for factual claims.",
                "unverified",
                0.0,
                uncertainty,
                noSourceClaims);
        }

        var groundedClaims = new List<ClaimGrounding>(factualClaims.Count);
        var contradictionCount = 0;
        for (var i = 0; i < factualClaims.Count; i++)
        {
            var match = _claimSourceMatcher.Match(factualClaims[i].Text, matcherSources, i);
            var projectionReference = _citationProjection.Resolve(projection, match.SourceIndex);
            var resolvedSourceIndex = projectionReference?.SourceOrdinal;
            var hasSource = projectionReference is not null;
            var contradictionDetected = evidenceTier.AllowsHardContradiction && match.ContradictionDetected;
            var grade = _evidenceGrading.Grade(match.Score, hasSource, contradictionDetected, match.Confidence);
            if (contradictionDetected)
            {
                contradictionCount++;
            }
            groundedClaims.Add(new ClaimGrounding(
                factualClaims[i].Text,
                factualClaims[i].Type,
                resolvedSourceIndex,
                grade,
                match.QuoteSpan,
                match.Confidence,
                contradictionDetected,
                projectionReference?.PassageId,
                projectionReference?.PassageOrdinal,
                projectionReference?.CitationLabel,
                projectionReference?.EvidenceKind));
        }

        var verifiedClaims = groundedClaims.Count(x => x.SourceIndex.HasValue);
        var coverage = factualClaims.Count == 0 ? 0 : (double)verifiedClaims / factualClaims.Count;

        var withCitations = AnnotateContent(extracted, groundedClaims, _citationProjection);
        var explicitResearchDisagreement = false;
        if (intent == IntentType.Research)
        {
            var formatted = _researchFormatter.TryFormat(groundedClaims, liveEvidenceItems ?? evidenceItems, language, requestPrompt);
            if (!string.IsNullOrWhiteSpace(formatted?.Content))
            {
                withCitations = formatted!.Content;
                explicitResearchDisagreement = evidenceTier.AllowsHardContradiction && formatted.HasExplicitDisagreement;
            }
        }

        uncertainty.AddRange(_evidenceGrading.BuildUncertaintyFlags(groundedClaims, factualClaims.Count, verifiedClaims));
        foreach (var flag in evidenceTier.UncertaintyFlags)
        {
            if (!uncertainty.Contains(flag, StringComparer.OrdinalIgnoreCase))
            {
                uncertainty.Add(flag);
            }
        }
        if (explicitResearchDisagreement)
        {
            if (!uncertainty.Contains("uncertainty.contradiction_detected", StringComparer.OrdinalIgnoreCase))
            {
                uncertainty.Add("uncertainty.contradiction_detected");
            }

            if (!uncertainty.Contains("uncertainty.source_disagreement", StringComparer.OrdinalIgnoreCase))
            {
                uncertainty.Add("uncertainty.source_disagreement");
            }
        }

        var status = contradictionCount > 0 || explicitResearchDisagreement
            ? "grounded_with_contradictions"
            : evidenceTier.RequiresGroundingCaution && verifiedClaims > 0
                ? "grounded_with_limits"
                : "grounded";
        if (contradictionCount > 0 && !explicitResearchDisagreement)
        {
            withCitations = $"{withCitations}\n\nUncertainty: potential contradictions were detected between claims and retrieved sources.";
        }
        else if (string.Equals(status, "grounded_with_limits", StringComparison.OrdinalIgnoreCase))
        {
            withCitations = AppendGroundingLimitsNote(withCitations, language, evidenceTier.StrongestTier);
        }

        return new GroundingResult(withCitations, status, coverage, uncertainty, groundedClaims);
    }

    private static string AppendGroundingLimitsNote(string content, string? language, string strongestTier)
    {
        var isRussian = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase);
        var note = isRussian
            ? strongestTier.Equals("search_hit", StringComparison.OrdinalIgnoreCase)
                ? "Неопределённость: опора здесь ограничена сниппетами поисковой выдачи, а не полноценными извлечёнными фрагментами страницы."
                : "Неопределённость: опора здесь ограничена ссылками на источники, а не полноценными извлечёнными фрагментами."
            : strongestTier.Equals("search_hit", StringComparison.OrdinalIgnoreCase)
                ? "Uncertainty: grounding here is limited to search-result snippets rather than fully extracted page evidence."
                : "Uncertainty: grounding here is limited to source links rather than fully extracted page evidence.";

        if (content.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        return $"{content}\n\n{note}";
    }

    private static string AnnotateContent(
        IReadOnlyList<ExtractedClaim> extracted,
        IReadOnlyList<ClaimGrounding> groundedClaims,
        ICitationProjectionService citationProjection)
    {
        if (extracted.Count == 0)
        {
            return string.Empty;
        }

        var factQueue = new Queue<ClaimGrounding>(groundedClaims.Where(x => x.Type == ClaimSentenceType.Fact));
        var lines = new List<string>(extracted.Count);
        foreach (var claim in extracted)
        {
            if (claim.Type == ClaimSentenceType.Fact && factQueue.Count > 0)
            {
                var grounding = factQueue.Dequeue();
                if (grounding.SourceIndex.HasValue)
                {
                    lines.Add($"{claim.Text} [{citationProjection.FormatCitationLabel(grounding)}]");
                    continue;
                }
            }

            lines.Add(claim.Text);
        }

        return string.Join(" ", lines);
    }
}

