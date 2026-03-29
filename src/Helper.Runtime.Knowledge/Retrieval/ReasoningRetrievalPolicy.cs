using System.Globalization;
using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Knowledge.Retrieval;

internal readonly record struct RetrievalPurposeAdjustment(double Bonus, double Penalty);

internal static class ReasoningRetrievalPolicy
{
    private static readonly HashSet<string> GenericReferenceDomains = new(
        new[] { "historical_encyclopedias", "encyclopedias", "analysis_strategy" },
        StringComparer.OrdinalIgnoreCase);

    public static RetrievalPurposeAdjustment ComputeAdjustment(
        RerankingPolicy.PreparedQuery query,
        KnowledgeChunk candidate,
        RetrievalRequestOptions? options)
    {
        if (options is null || options.Purpose == RetrievalPurpose.Standard)
        {
            return default;
        }

        var domain = candidate.Metadata.GetValueOrDefault("domain");
        var bonus = 0d;
        var penalty = 0d;
        var traceable = HasTraceability(candidate);
        var strongRouting = HasStrongRouting(candidate);
        var contentLength = (candidate.Content ?? string.Empty).Trim().Length;

        if (options.PreferTraceableChunks)
        {
            bonus += traceable ? 0.85 : 0d;
            penalty += traceable ? 0d : 0.45;
        }

        if (MatchesDomain(options.PreferredDomains, domain))
        {
            bonus += 0.8;
        }

        if (MatchesDomain(options.DisallowedDomains, domain))
        {
            penalty += 1.35;
        }

        if (options.Purpose == RetrievalPurpose.ReasoningSupport)
        {
            if (IsGenericReferenceDomain(domain) && !strongRouting)
            {
                penalty += 1.2;
            }

            if (contentLength is > 80 and < 1400)
            {
                bonus += 0.2;
            }
            else if (contentLength > 0 && contentLength < 50)
            {
                penalty += 0.35;
            }
        }
        else if (options.Purpose == RetrievalPurpose.FactualLookup)
        {
            if (IsGenericReferenceDomain(domain) && !strongRouting)
            {
                penalty += 0.45;
            }

            if (traceable)
            {
                bonus += 0.35;
            }
        }

        if (query.Phrases.Count > 0 && traceable)
        {
            bonus += 0.1;
        }

        return new RetrievalPurposeAdjustment(bonus, penalty);
    }

    internal static bool HasTraceability(KnowledgeChunk candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate.Metadata.GetValueOrDefault("document_id")) &&
               (!string.IsNullOrWhiteSpace(candidate.Metadata.GetValueOrDefault("section_path")) ||
                !string.IsNullOrWhiteSpace(candidate.Metadata.GetValueOrDefault("page_start")));
    }

    internal static bool HasStrongRouting(KnowledgeChunk candidate)
    {
        var hintMatches = ParseInt(candidate.Metadata.GetValueOrDefault("routing_hint_matches"));
        var anchorScore = ParseDouble(candidate.Metadata.GetValueOrDefault("routing_anchor_score"));
        return hintMatches > 0 || anchorScore >= 4.5d;
    }

    internal static bool IsGenericReferenceDomain(string? domain)
        => !string.IsNullOrWhiteSpace(domain) && GenericReferenceDomains.Contains(domain);

    internal static bool MatchesDomain(IReadOnlyList<string>? domains, string? value)
    {
        if (domains is null || domains.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return domains.Any(domain => string.Equals(domain, value, StringComparison.OrdinalIgnoreCase));
    }

    private static double ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;

    private static int ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}

