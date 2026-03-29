using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal readonly record struct RetrievalTopicalFitAssessment(
    double Score,
    string Label,
    bool NarrowTopic,
    bool GenericDomain,
    bool StrongMatch,
    bool SuggestDeeperRetrieval,
    double Bonus,
    double Penalty);

internal static class RetrievalTopicalFitPolicy
{
    public static RetrievalTopicalFitAssessment Evaluate(
        RerankingPolicy.PreparedQuery query,
        KnowledgeChunk candidate,
        RetrievalRequestOptions? options,
        RerankingPolicy.MatchMetrics title,
        RerankingPolicy.MatchMetrics source,
        RerankingPolicy.MatchMetrics section,
        RerankingPolicy.MatchMetrics content,
        int routingHintMatches,
        double routingAnchorScore)
    {
        var domain = candidate.Metadata.GetValueOrDefault("domain");
        var genericDomain = ReasoningRetrievalPolicy.IsGenericReferenceDomain(domain);
        var traceable = ReasoningRetrievalPolicy.HasTraceability(candidate);
        var narrowTopic = IsNarrowTopic(query);
        var strongRouting = routingHintMatches >= 2 || routingAnchorScore >= 5.5d;
        var anchorCoverage = Math.Max(title.Coverage, Math.Max(source.Coverage, section.Coverage));
        var anchorPhraseHits = title.PhraseHits + source.PhraseHits + section.PhraseHits;
        var anchorExact = title.ExactScore + source.ExactScore + section.ExactScore;

        var anchorSignal = (anchorCoverage * 0.55d) +
                           (Math.Min(anchorPhraseHits, 2) * 0.10d) +
                           (Math.Min(anchorExact / 3.5d, 1d) * 0.12d);
        var contentSignal = (content.Coverage * 0.45d) + (Math.Min(content.PhraseHits, 2) * 0.12d);
        var routingSignal = strongRouting ? 0.08d : routingHintMatches == 1 ? 0.03d : 0d;
        var traceSignal = traceable ? 0.05d : 0d;

        var score = Math.Clamp(anchorSignal + contentSignal + routingSignal + traceSignal, 0d, 1d);
        if (narrowTopic && anchorCoverage < 0.12d && content.Coverage < 0.20d)
        {
            score *= 0.45d;
        }

        var strongMatch = anchorCoverage >= 0.22d ||
                          anchorPhraseHits > 0 ||
                          content.PhraseHits > 0 ||
                          (content.Coverage >= 0.30d && traceable) ||
                          (strongRouting && content.Coverage >= 0.20d);
        if (genericDomain && narrowTopic && !strongMatch)
        {
            score *= 0.55d;
        }

        var bonus = 0d;
        if (strongMatch)
        {
            bonus += narrowTopic ? 0.85d : 0.45d;
        }
        else if (score >= 0.58d)
        {
            bonus += 0.30d;
        }

        var penalty = 0d;
        if (score < 0.42d)
        {
            penalty += 0.65d;
        }

        if (genericDomain && narrowTopic && !strongMatch)
        {
            penalty += 1.15d;
        }

        if (narrowTopic && score < 0.35d)
        {
            penalty += 0.35d;
        }

        var purposeMultiplier = options?.Purpose switch
        {
            RetrievalPurpose.ReasoningSupport => 1.15d,
            RetrievalPurpose.FactualLookup => 1.05d,
            _ => 0.90d
        };

        bonus *= purposeMultiplier;
        penalty *= purposeMultiplier;

        score = Math.Clamp(score, 0d, 1d);
        var label = score >= 0.72d
            ? "high"
            : score >= 0.48d
                ? "medium"
                : "low";
        var suggestDeeperRetrieval = narrowTopic && (label == "low" || (genericDomain && !strongMatch));

        return new RetrievalTopicalFitAssessment(
            score,
            label,
            narrowTopic,
            genericDomain,
            strongMatch,
            suggestDeeperRetrieval,
            bonus,
            penalty);
    }

    public static void Annotate(KnowledgeChunk candidate, RetrievalRequestOptions? options, RetrievalTopicalFitAssessment assessment)
    {
        candidate.Metadata["retrieval_purpose"] = (options?.Purpose ?? RetrievalPurpose.Standard).ToString();
        candidate.Metadata["topical_fit_score"] = assessment.Score.ToString("0.000", CultureInfo.InvariantCulture);
        candidate.Metadata["topical_fit_label"] = assessment.Label;
        candidate.Metadata["topical_fit_narrow_topic"] = assessment.NarrowTopic ? "true" : "false";
        candidate.Metadata["topical_fit_generic_domain"] = assessment.GenericDomain ? "true" : "false";
        candidate.Metadata["topical_fit_strong_match"] = assessment.StrongMatch ? "true" : "false";
        candidate.Metadata["topical_fit_suggest_deeper_retrieval"] = assessment.SuggestDeeperRetrieval ? "true" : "false";
        candidate.Metadata["domain_fit"] = assessment.Label;
    }

    private static bool IsNarrowTopic(RerankingPolicy.PreparedQuery query)
    {
        var weightedTerms = query.Terms.Count(term => term.Weight >= 1.05d);
        return query.Phrases.Count > 0 ||
               weightedTerms >= 3 ||
               (weightedTerms >= 2 && query.Terms.Count >= 4);
    }
}

