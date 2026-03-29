namespace Helper.Api.Conversation;

public sealed class ClaimSourceMatcher : IClaimSourceMatcher
{
    public ClaimSourceMatch Match(string claim, IReadOnlyList<string> sources, int fallbackSeed = 0)
    {
        if (sources.Count == 0)
        {
            return new ClaimSourceMatch(-1, 0, "none", null, 0, false, Array.Empty<string>());
        }

        var claimTokens = Tokenize(claim);
        if (claimTokens.Count == 0)
        {
            return new ClaimSourceMatch(fallbackSeed % sources.Count, 0, "fallback", null, 0, false, new[] { "fallback:no_tokens" });
        }

        var claimNumbers = ExtractNumbers(claim);
        var claimEntities = ExtractEntities(claim);
        var bestIndex = -1;
        var bestScore = 0.0;
        var bestQuote = string.Empty;
        var bestContradiction = false;
        IReadOnlyList<string> bestSignals = Array.Empty<string>();
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i] ?? string.Empty;
            var sourceTokens = Tokenize(source);
            if (sourceTokens.Count == 0)
            {
                continue;
            }

            var lexicalOverlap = (double)claimTokens.Intersect(sourceTokens, StringComparer.OrdinalIgnoreCase).Count() / claimTokens.Count;
            var sourceNumbers = ExtractNumbers(source);
            var sourceCategory = ResearchSourceCategoryClassifier.ResolveFromMatcherText(source);
            var numericOverlap = claimNumbers.Count == 0
                ? 0
                : (double)claimNumbers.Intersect(sourceNumbers, StringComparer.OrdinalIgnoreCase).Count() / claimNumbers.Count;
            var claimYears = ExtractYears(claimNumbers);
            var sourceYears = ExtractYears(sourceNumbers);
            var sourceEntities = ExtractEntities(source);
            var entityOverlap = claimEntities.Count == 0
                ? 0
                : (double)claimEntities.Intersect(sourceEntities, StringComparer.OrdinalIgnoreCase).Count() / claimEntities.Count;
            var quoteSpan = FindQuoteSpan(claim, source);
            var quoteBoost = string.IsNullOrWhiteSpace(quoteSpan) ? 0.0 : 0.08;
            var comparableClaimNumbers = claimNumbers.Except(claimYears, StringComparer.OrdinalIgnoreCase).ToArray();
            var comparableSourceNumbers = sourceNumbers.Except(sourceYears, StringComparer.OrdinalIgnoreCase).ToArray();
            var sharedComparableNumbers = comparableClaimNumbers.Intersect(comparableSourceNumbers, StringComparer.OrdinalIgnoreCase).Count();
            var yearConflict = claimYears.Count > 0 &&
                               sourceYears.Count > 0 &&
                               claimYears.Intersect(sourceYears, StringComparer.OrdinalIgnoreCase).Count() == 0 &&
                               lexicalOverlap >= 0.55 &&
                               (entityOverlap >= 0.35 || sharedComparableNumbers > 0) &&
                               ResearchSourceCategoryClassifier.SupportsYearConflict(sourceCategory);
            var numericConflict =
                comparableClaimNumbers.Length > 0 &&
                comparableSourceNumbers.Length > 0 &&
                comparableClaimNumbers.Intersect(comparableSourceNumbers, StringComparer.OrdinalIgnoreCase).Count() == 0 &&
                lexicalOverlap >= 0.35 &&
                ResearchSourceCategoryClassifier.SupportsNumericConflict(sourceCategory);
            var contradictionDetected = yearConflict || numericConflict;
            var metadataBoost = LooksLikeAuthoritativeSource(source) ? 0.1 : 0;
            var score = (lexicalOverlap * 0.55) + (numericOverlap * 0.2) + (entityOverlap * 0.15) + quoteBoost + metadataBoost;
            if (contradictionDetected)
            {
                score -= 0.2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
                bestQuote = quoteSpan;
                bestContradiction = contradictionDetected;
                bestSignals = new[]
                {
                    $"lexical={lexicalOverlap:0.000}",
                    $"numeric={numericOverlap:0.000}",
                    $"entity={entityOverlap:0.000}",
                    $"quote={(string.IsNullOrWhiteSpace(quoteSpan) ? "none" : "present")}",
                    $"authoritative={(metadataBoost > 0 ? "yes" : "no")}",
                    $"contradiction={(contradictionDetected ? "yes" : "no")}"
                };
            }
        }

        if (bestIndex < 0)
        {
            return new ClaimSourceMatch(fallbackSeed % sources.Count, 0, "fallback", null, 0, false, new[] { "fallback:no_match" });
        }

        var confidence = Math.Clamp(bestScore - (bestContradiction ? 0.15 : 0), 0, 1);
        var mode = bestScore >= 0.20 ? "semantic_lexical_quote" : "fallback";
        return new ClaimSourceMatch(bestIndex, bestScore, mode, bestQuote, confidence, bestContradiction, bestSignals);
    }

    private static bool LooksLikeAuthoritativeSource(string source)
    {
        if (source.Contains(".gov", StringComparison.OrdinalIgnoreCase) ||
            source.Contains(".edu", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("docs.", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("wikipedia.org", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', ':', ';', '-', '_', '/', '\\', '?', '!', '\n', '\r', '\t', '[', ']', '(', ')', '"' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Where(x => x.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string token)
    {
        if (token.Length > 5 && token.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^3];
        }

        if (token.Length > 4 && token.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^2];
        }

        if (token.Length > 4 && token.EndsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^2];
        }

        if (token.Length > 3 && token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^1];
        }

        return token;
    }

    private static HashSet<string> ExtractNumbers(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new List<char>(8);
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                current.Add(ch);
                continue;
            }

            if (current.Count > 0)
            {
                result.Add(new string(current.ToArray()));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            result.Add(new string(current.ToArray()));
        }

        return result;
    }

    private static HashSet<string> ExtractEntities(string text)
    {
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(text))
        {
            if (token.Length >= 4 && char.IsLetter(token[0]))
            {
                entities.Add(token);
            }
        }

        foreach (var number in ExtractNumbers(text))
        {
            entities.Add(number);
        }

        return entities;
    }

    private static HashSet<string> ExtractYears(IReadOnlySet<string> numbers)
    {
        var years = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var number in numbers)
        {
            if (!int.TryParse(number, out var numeric))
            {
                continue;
            }

            if (numeric is >= 1900 and <= 2100)
            {
                years.Add(number);
            }
        }

        return years;
    }

    private static string FindQuoteSpan(string claim, string source)
    {
        var claimWords = claim
            .Split(new[] { ' ', ',', '.', ':', ';', '-', '_', '/', '\\', '?', '!', '\n', '\r', '\t', '[', ']', '(', ')', '"' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 4)
            .ToArray();
        if (claimWords.Length < 3)
        {
            return string.Empty;
        }

        for (var window = Math.Min(6, claimWords.Length); window >= 3; window--)
        {
            for (var i = 0; i + window <= claimWords.Length; i++)
            {
                var span = string.Join(' ', claimWords.Skip(i).Take(window));
                if (source.Contains(span, StringComparison.OrdinalIgnoreCase))
                {
                    return span;
                }
            }
        }

        return string.Empty;
    }
}

