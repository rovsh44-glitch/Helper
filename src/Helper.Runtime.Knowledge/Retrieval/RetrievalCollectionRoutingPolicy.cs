namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RetrievalCollectionRoutingPolicy
{
    private const int MinimumCandidateWindow = 24;
    private const int MediumCandidateWindow = 64;
    private const int MaximumCandidateWindow = 128;

    private static readonly IReadOnlyDictionary<string, string[]> DomainRoutingHints =
        RetrievalDomainProfileCatalog.RoutingHintsByDomain;

    public static int ResolveCandidateWindow(int limit, int totalCollections, int? pointCount)
    {
        var normalizedLimit = Math.Max(limit, 1);
        var collectionCount = Math.Max(totalCollections, 1);

        var baseWindow = collectionCount switch
        {
            <= 1 => Math.Max(normalizedLimit * 8, MinimumCandidateWindow),
            <= 4 => Math.Max(normalizedLimit * 5, 16),
            _ => Math.Max(normalizedLimit * 3, 12)
        };

        var collectionCap = collectionCount switch
        {
            <= 1 => MaximumCandidateWindow,
            <= 4 => MediumCandidateWindow,
            _ => 32
        };

        var growthWindow = 0;
        if (pointCount.GetValueOrDefault() > 0)
        {
            growthWindow = (int)Math.Ceiling(Math.Sqrt(pointCount.GetValueOrDefault()));
        }

        var resolved = Math.Max(baseWindow, Math.Min(growthWindow, collectionCap));
        return Math.Clamp(resolved, Math.Min(baseWindow, collectionCap), collectionCap);
    }

    public static int ApplyRoutingWindow(int candidateWindow, CollectionRoute route)
    {
        var adjusted = candidateWindow;
        if (route.Rank == 1)
        {
            adjusted = (int)Math.Ceiling(candidateWindow * 1.75);
        }
        else if (route.Rank <= 3)
        {
            adjusted = (int)Math.Ceiling(candidateWindow * 1.35);
        }
        else if (route.Rank <= 6)
        {
            adjusted = (int)Math.Ceiling(candidateWindow * 1.15);
        }

        return Math.Clamp(adjusted, candidateWindow, MaximumCandidateWindow);
    }

    public static CollectionRoutingScore ScoreCollection(PreparedRoutingQuery query, CollectionProfile profile)
    {
        var score = 0d;
        var anchorScore = 0d;
        var matchedTerms = 0;
        var anchorMatches = 0;
        var hintMatches = CountHintMatches(query, profile.Domain);
        foreach (var term in query.Terms)
        {
            var lexicalMatched = false;
            if (profile.TokenWeights.TryGetValue(term.Token, out var tokenWeight))
            {
                score += 1.1 + tokenWeight;
                matchedTerms++;
                lexicalMatched = true;
            }

            if (!lexicalMatched)
            {
                var bestRootWeight = term.Roots
                    .Select(root => profile.RootWeights.GetValueOrDefault(root))
                    .DefaultIfEmpty(0d)
                    .Max();
                if (bestRootWeight > 0d)
                {
                    score += 0.45 + bestRootWeight;
                    matchedTerms++;
                }
            }

            var anchorMatched = false;
            if (profile.AnchorTokenWeights.TryGetValue(term.Token, out var anchorTokenWeight))
            {
                anchorScore += 1.0 + anchorTokenWeight;
                anchorMatches++;
                anchorMatched = true;
            }

            if (!anchorMatched)
            {
                var bestAnchorRootWeight = term.Roots
                    .Select(root => profile.AnchorRootWeights.GetValueOrDefault(root))
                    .DefaultIfEmpty(0d)
                    .Max();
                if (bestAnchorRootWeight > 0d)
                {
                    anchorScore += 0.4 + bestAnchorRootWeight;
                    anchorMatches++;
                }
            }
        }

        if (matchedTerms > 0)
        {
            score += matchedTerms / (double)Math.Max(query.Terms.Count, 1);
        }

        if (anchorMatches > 0)
        {
            anchorScore += anchorMatches / (double)Math.Max(query.Terms.Count, 1);
        }

        if (hintMatches > 0)
        {
            score += hintMatches * hintMatches * 1.1;
            anchorScore += Math.Min(hintMatches * 0.9, 3.0);
        }

        score = ApplyRouteBiasAdjustments(profile, query, score, anchorScore, anchorMatches, hintMatches);
        return new CollectionRoutingScore(score, anchorScore, anchorMatches, hintMatches);
    }

    public static bool HasStrongArchiveSignal(CollectionRoute route, PreparedRoutingQuery query)
    {
        if (!KnowledgeCollectionNaming.IsHistoricalArchiveCollection(route.Collection))
        {
            return true;
        }

        if (route.HintMatches > 0 && route.AnchorScore >= 2.0d)
        {
            return true;
        }

        if (route.HintMatches == 0 &&
            ResolveStrongestCompetingHintMatches(ExtractDomain(route.Collection), query) >= 2 &&
            route.AnchorScore < 6.8d)
        {
            return false;
        }

        if (route.AnchorMatches >= 2)
        {
            return true;
        }

        var anchorCoverage = route.AnchorMatches / (double)Math.Max(query.Terms.Count, 1);
        return route.AnchorScore >= 4.5d || anchorCoverage >= 0.5;
    }

    public static string ExtractDomain(string collection)
    {
        if (collection.StartsWith("knowledge_", StringComparison.OrdinalIgnoreCase))
        {
            collection = collection["knowledge_".Length..];
        }

        if (collection.EndsWith("_v2", StringComparison.OrdinalIgnoreCase))
        {
            collection = collection[..^3];
        }

        return collection.Trim('_');
    }

    private static double ApplyRouteBiasAdjustments(
        CollectionProfile profile,
        PreparedRoutingQuery query,
        double score,
        double anchorScore,
        int anchorMatches,
        int hintMatches)
    {
        if (score <= 0d)
        {
            return 0d;
        }

        var anchorCoverage = anchorMatches / (double)Math.Max(query.Terms.Count, 1);
        var strongestCompetingHintMatches = ResolveStrongestCompetingHintMatches(profile.Domain, query);
        if (KnowledgeCollectionNaming.IsHistoricalArchiveCollection(profile.Collection))
        {
            if (hintMatches == 0 && strongestCompetingHintMatches >= 2 && anchorScore < 6.8d)
            {
                return score * 0.08;
            }

            if (hintMatches == 0 && anchorMatches == 0)
            {
                return score * 0.14;
            }

            if (hintMatches == 0 && anchorCoverage < 0.34 && anchorScore < 4.5d)
            {
                return score * 0.36;
            }

            if (hintMatches == 0 && anchorCoverage < 0.5 && anchorScore < 6.2d)
            {
                return score * 0.62;
            }

            return score;
        }

        if (string.Equals(profile.Domain, "analysis_strategy", StringComparison.OrdinalIgnoreCase))
        {
            if (hintMatches == 0)
            {
                return score * 0.18;
            }

            if (anchorCoverage < 0.34 && anchorScore < 4.5d)
            {
                return score * 0.42;
            }

            if (anchorCoverage < 0.5 && anchorScore < 6.5d)
            {
                return score * 0.72;
            }
        }

        if (string.Equals(profile.Domain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
            && hintMatches <= 1
            && strongestCompetingHintMatches >= 2
            && anchorScore < 6.2d)
        {
            return score * 0.58;
        }

        if (string.Equals(profile.Domain, "philosophy", StringComparison.OrdinalIgnoreCase)
            && hintMatches <= 1
            && strongestCompetingHintMatches >= 2
            && anchorScore < 6.2d)
        {
            return score * 0.46;
        }

        if (string.Equals(profile.Domain, "history", StringComparison.OrdinalIgnoreCase)
            && hintMatches <= 1
            && strongestCompetingHintMatches >= 2
            && anchorScore < 6.0d)
        {
            return score * 0.62;
        }

        if (string.Equals(profile.Domain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase)
            && hintMatches <= 1
            && strongestCompetingHintMatches >= 2
            && anchorScore < 6.0d)
        {
            return score * 0.58;
        }

        if (string.Equals(profile.Domain, "neuro", StringComparison.OrdinalIgnoreCase)
            && hintMatches <= 1
            && strongestCompetingHintMatches >= 2
            && anchorScore < 6.2d)
        {
            return score * 0.56;
        }

        if (string.Equals(profile.Domain, "computer_science", StringComparison.OrdinalIgnoreCase) && hintMatches == 0 && strongestCompetingHintMatches >= 2)
        {
            return score * 0.46;
        }

        if (string.Equals(profile.Domain, "physics", StringComparison.OrdinalIgnoreCase) && hintMatches == 0 && strongestCompetingHintMatches >= 2)
        {
            return score * 0.5;
        }

        if (string.Equals(profile.Domain, "chemistry", StringComparison.OrdinalIgnoreCase) && hintMatches == 0 && strongestCompetingHintMatches >= 2)
        {
            return score * 0.54;
        }

        return score;
    }

    private static int CountHintMatches(PreparedRoutingQuery query, string domain)
    {
        var hints = DomainRoutingHints.GetValueOrDefault(domain);
        if (hints is null || hints.Length == 0 || query.Terms.Count == 0)
        {
            return 0;
        }

        var matches = 0;
        foreach (var hint in hints)
        {
            var normalizedHint = RetrievalRoutingText.NormalizeText(hint);
            if (string.IsNullOrWhiteSpace(normalizedHint))
            {
                continue;
            }

            var hintTokens = RetrievalRoutingText.Tokenize(normalizedHint)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (hintTokens.Length == 0)
            {
                continue;
            }

            if (hintTokens.Any(token =>
            {
                var hintRoot = RetrievalRoutingText.BuildRoot(token);
                return query.Terms.Any(term =>
                    string.Equals(term.Token, token, StringComparison.OrdinalIgnoreCase) ||
                    term.Roots.Contains(hintRoot, StringComparer.OrdinalIgnoreCase));
            }))
            {
                matches++;
            }
        }

        return matches;
    }

    private static int ResolveStrongestCompetingHintMatches(string domain, PreparedRoutingQuery query)
    {
        var strongest = 0;
        foreach (var candidateDomain in RetrievalDomainProfileCatalog.Domains)
        {
            if (string.Equals(candidateDomain, domain, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (KnowledgeCollectionNaming.IsHistoricalArchiveCollection(KnowledgeCollectionNaming.BuildCollectionName(candidateDomain, "v2")))
            {
                continue;
            }

            strongest = Math.Max(strongest, CountHintMatches(query, candidateDomain));
        }

        return strongest;
    }
}

