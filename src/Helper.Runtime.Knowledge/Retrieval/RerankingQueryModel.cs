using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RerankingQueryModel
{
    private static readonly HashSet<string> StopWords = RetrievalTextProcessing.CreateStopWordSet();

    public static RerankingPolicy.PreparedQuery CreatePreparedQuery(string query)
    {
        var normalizedText = NormalizeText(query);
        var tokens = Tokenize(normalizedText)
            .Select(token => new RerankingPolicy.QueryTerm(token, ComputeWeight(token), BuildTermRoots(token)))
            .ToList();

        return new RerankingPolicy.PreparedQuery(normalizedText, tokens, BuildPhrases(tokens));
    }

    public static RerankingPolicy.MatchMetrics MatchField(RerankingPolicy.PreparedQuery query, RerankingPolicy.FieldFeatures field)
    {
        if (query.Terms.Count == 0 || field.IsEmpty)
        {
            return RerankingPolicy.MatchMetrics.Empty;
        }

        double exactScore = 0d;
        double fuzzyScore = 0d;
        var matchedTerms = 0;
        foreach (var term in query.Terms)
        {
            if (field.Tokens.Contains(term.Token))
            {
                exactScore += term.Weight;
                matchedTerms++;
                continue;
            }

            if (term.Roots.Any(root => field.Roots.Contains(root)))
            {
                fuzzyScore += term.Weight * 0.45;
                matchedTerms++;
            }
        }

        var phraseHits = query.Phrases.Count(phrase => field.NormalizedText.Contains(phrase, StringComparison.Ordinal));
        var coverage = query.Terms.Count == 0 ? 0d : matchedTerms / (double)query.Terms.Count;
        return new RerankingPolicy.MatchMetrics(exactScore, fuzzyScore, coverage, phraseHits);
    }

    public static string NormalizeText(string? text)
    {
        return RetrievalTextProcessing.NormalizeText(text);
    }

    public static List<string> Tokenize(string text)
    {
        return RetrievalTextProcessing.Tokenize(text, StopWords)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildRoot(string token)
    {
        return RetrievalTextProcessing.BuildRoot(token);
    }

    public static int CountIntentMatches(RerankingPolicy.PreparedQuery query, HashSet<string> intentRoots)
    {
        var matches = 0;
        foreach (var term in query.Terms)
        {
            if (intentRoots.Contains(term.Token) || term.Roots.Any(intentRoots.Contains))
            {
                matches++;
            }
        }

        return matches;
    }

    public static IReadOnlyList<string> BuildPhrases(IReadOnlyList<RerankingPolicy.QueryTerm> terms)
    {
        var phrases = new List<string>();
        for (var index = 0; index < terms.Count - 1; index++)
        {
            phrases.Add(string.Concat(terms[index].Token, " ", terms[index + 1].Token));
        }

        return phrases;
    }

    public static RerankingPolicy.FieldFeatures CreateFieldFeatures(string? text)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return RerankingPolicy.FieldFeatures.Empty;
        }

        var tokens = Tokenize(normalized);
        var roots = tokens
            .Select(BuildRoot)
            .Where(static root => root.Length >= 4)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new RerankingPolicy.FieldFeatures(
            normalized,
            tokens.ToHashSet(StringComparer.OrdinalIgnoreCase),
            roots);
    }

    private static double ComputeWeight(string token)
    {
        var weight = 1d;
        if (token.Length >= 8)
        {
            weight += 0.25;
        }

        if (token.Any(char.IsDigit))
        {
            weight += 0.2;
        }

        return weight;
    }

    private static IReadOnlyList<string> BuildTermRoots(string token)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildRoot(token)
        };

        if (token.Length >= 6)
        {
            roots.Add(token[..Math.Min(token.Length, 5)]);
        }

        return roots
            .Where(static root => root.Length >= 4)
            .ToList();
    }
}

