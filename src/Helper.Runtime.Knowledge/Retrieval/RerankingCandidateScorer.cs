using System.Globalization;
using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RerankingCandidateScorer
{
    public static double Score(RerankingPolicy.PreparedQuery query, KnowledgeChunk candidate, RetrievalRequestOptions? options = null)
    {
        var title = RerankingPolicy.FieldFeatures.Create(candidate.Metadata.GetValueOrDefault("title"));
        var chunkTitle = RerankingPolicy.FieldFeatures.Create(candidate.Metadata.GetValueOrDefault("chunk_title"));
        var chunkSummary = RerankingPolicy.FieldFeatures.Create(candidate.Metadata.GetValueOrDefault("chunk_summary"));
        var semanticTerms = RerankingPolicy.FieldFeatures.Create(candidate.Metadata.GetValueOrDefault("semantic_terms"));
        var sourcePath = RerankingPolicy.FieldFeatures.Create(candidate.Metadata.GetValueOrDefault("source_path"));
        var sectionPath = RerankingPolicy.FieldFeatures.Create(candidate.Metadata.GetValueOrDefault("section_path"));
        var content = RerankingPolicy.FieldFeatures.Create(candidate.Content);

        var titleScore = RerankingQueryModel.MatchField(query, title);
        var chunkTitleScore = RerankingQueryModel.MatchField(query, chunkTitle);
        var chunkSummaryScore = RerankingQueryModel.MatchField(query, chunkSummary);
        var semanticTermsScore = RerankingQueryModel.MatchField(query, semanticTerms);
        var sourceScore = RerankingQueryModel.MatchField(query, sourcePath);
        var sectionScore = RerankingQueryModel.MatchField(query, sectionPath);
        var contentScore = RerankingQueryModel.MatchField(query, content);

        var vectorScore = ParseDouble(candidate.Metadata.GetValueOrDefault("vector_score"));
        var routingScore = ParseDouble(candidate.Metadata.GetValueOrDefault("routing_score"));
        var routingAnchorScore = ParseDouble(candidate.Metadata.GetValueOrDefault("routing_anchor_score"));
        var routingHintMatches = ParseInt(candidate.Metadata.GetValueOrDefault("routing_hint_matches"));

        var titleLexical = (titleScore.ExactScore * 3.0) + (titleScore.FuzzyScore * 1.8) + (titleScore.Coverage * 2.4) + (titleScore.PhraseHits * 1.1);
        var chunkTitleLexical = (chunkTitleScore.ExactScore * 2.4) + (chunkTitleScore.FuzzyScore * 1.5) + (chunkTitleScore.Coverage * 1.9) + (chunkTitleScore.PhraseHits * 0.9);
        var chunkSummaryLexical = (chunkSummaryScore.ExactScore * 1.2) + (chunkSummaryScore.FuzzyScore * 0.7) + (chunkSummaryScore.Coverage * 1.1) + (chunkSummaryScore.PhraseHits * 0.45);
        var semanticTermsLexical = (semanticTermsScore.ExactScore * 1.4) + (semanticTermsScore.FuzzyScore * 0.8) + (semanticTermsScore.Coverage * 1.1) + (semanticTermsScore.PhraseHits * 0.55);
        var sourceLexical = (sourceScore.ExactScore * 2.2) + (sourceScore.FuzzyScore * 1.3) + (sourceScore.Coverage * 1.6) + (sourceScore.PhraseHits * 0.8);
        var sectionLexical = (sectionScore.ExactScore * 1.6) + (sectionScore.FuzzyScore * 0.9) + (sectionScore.Coverage * 1.2) + (sectionScore.PhraseHits * 0.6);
        var contentLexical = (contentScore.ExactScore * 0.85) + (contentScore.FuzzyScore * 0.45) + (contentScore.Coverage * 0.9) + (contentScore.PhraseHits * 0.35);
        var lexicalScore = titleLexical + chunkTitleLexical + chunkSummaryLexical + semanticTermsLexical + sourceLexical + sectionLexical + contentLexical;
        var routingBoost = routingScore switch
        {
            >= 8d => 2.1,
            >= 5d => 1.55,
            >= 3d => 1.1,
            >= 1.5d => 0.65,
            > 0d => 0.25,
            _ => 0d
        };
        var routingHintBoost = routingHintMatches switch
        {
            >= 3 => 2.8,
            2 => 1.6,
            1 => 0.5,
            _ => 0d
        };

        var routedCollection = candidate.Metadata.GetValueOrDefault("collection", candidate.Collection);
        var routedDomain = candidate.Metadata.GetValueOrDefault("domain");
        if (KnowledgeCollectionNaming.IsHistoricalArchiveCollection(routedCollection) && routingAnchorScore < 4.5d)
        {
            routingBoost *= 0.35;
        }
        else if (string.Equals(routedDomain, "analysis_strategy", StringComparison.OrdinalIgnoreCase) && routingAnchorScore < 4.5d)
        {
            routingBoost *= 0.55;
        }

        var adjustment = RerankingDomainIntentPolicy.ComputeAdjustment(
            query,
            new RerankingDomainScoreContext(
                routedCollection,
                routedDomain,
                routingHintMatches,
                routingAnchorScore,
                titleLexical + chunkTitleLexical,
                sourceLexical,
                contentLexical + chunkSummaryLexical + semanticTermsLexical,
                Math.Max(contentScore.Coverage, Math.Max(chunkSummaryScore.Coverage, semanticTermsScore.Coverage))));
        var purposeAdjustment = ReasoningRetrievalPolicy.ComputeAdjustment(query, candidate, options);
        var topicalFit = RetrievalTopicalFitPolicy.Evaluate(
            query,
            candidate,
            options,
            titleScore,
            sourceScore,
            sectionScore,
            contentScore,
            routingHintMatches,
            routingAnchorScore);
        RetrievalTopicalFitPolicy.Annotate(candidate, options, topicalFit);

        var roleBoost = candidate.Metadata.GetValueOrDefault("chunk_role") switch
        {
            "parent" => 0.15,
            "child" => 0.05,
            _ => 0
        };
        var traceabilityBoost = 0d;
        if (!string.IsNullOrWhiteSpace(candidate.Metadata.GetValueOrDefault("section_path")))
        {
            traceabilityBoost += 0.05;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Metadata.GetValueOrDefault("page_start")))
        {
            traceabilityBoost += 0.05;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Metadata.GetValueOrDefault("document_id")))
        {
            traceabilityBoost += 0.05;
        }

        return (vectorScore * 2.0) + lexicalScore + routingBoost + routingHintBoost + roleBoost + traceabilityBoost +
               adjustment.Bonus - adjustment.Penalty +
               purposeAdjustment.Bonus - purposeAdjustment.Penalty +
               topicalFit.Bonus - topicalFit.Penalty;
    }

    private static double ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
    }

    private static int ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}

