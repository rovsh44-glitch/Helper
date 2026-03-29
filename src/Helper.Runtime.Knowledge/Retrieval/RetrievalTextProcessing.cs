using System.Text;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RetrievalTextProcessing
{
    private static readonly string[] EnglishStopWords =
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "how", "in", "into", "is", "it", "of", "on", "or", "the", "to", "via", "with"
    };

    private static readonly string[] RussianStopWords =
    {
        "и", "в", "во", "на", "по", "с", "со", "к", "ко", "от", "до", "для", "из", "у", "о", "об", "обо", "при", "как", "что", "это", "через", "под", "над", "же", "ли"
    };

    private static readonly string[] RussianSuffixes =
    {
        "иями", "ями", "ами", "ого", "ему", "ыми", "ими", "иях", "ией", "ости", "ость", "ение", "ения", "ировать",
        "ому", "его", "ия", "ие", "ий", "ый", "ой", "ая", "яя", "ое", "ее", "ам", "ям", "ах", "ях", "ов", "ев", "ом", "ем",
        "ию", "а", "я", "у", "ю", "ы", "и", "е", "ть", "ти"
    };

    private static readonly string[] EnglishSuffixes =
    {
        "ation", "itions", "ition", "ments", "ment", "ings", "ing", "ized", "izes", "ions", "ion", "ers", "ies", "er", "ed", "es", "s"
    };

    internal static HashSet<string> CreateStopWordSet(IEnumerable<string>? extraTokens = null)
    {
        var set = new HashSet<string>(EnglishStopWords.Concat(RussianStopWords), StringComparer.OrdinalIgnoreCase);
        if (extraTokens is not null)
        {
            foreach (var token in extraTokens)
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    set.Add(token);
                }
            }
        }

        return set;
    }

    internal static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            var normalized = char.ToLowerInvariant(character) switch
            {
                'ё' => 'е',
                '_' or '-' or '/' or '\\' or '.' or ',' or ':' or ';' or '|' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '\'' or '`' => ' ',
                var value when char.IsLetterOrDigit(value) || char.IsWhiteSpace(value) => value,
                _ => ' '
            };

            builder.Append(normalized);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal static IEnumerable<string> Tokenize(string normalizedText, IReadOnlySet<string> stopWords)
    {
        return normalizedText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => IsInformativeToken(token, stopWords));
    }

    internal static string BuildRoot(string token)
    {
        if (token.Length < 4)
        {
            return token;
        }

        foreach (var suffix in RussianSuffixes)
        {
            if (token.Length - suffix.Length >= 4 && token.EndsWith(suffix, StringComparison.Ordinal))
            {
                return token[..^suffix.Length];
            }
        }

        foreach (var suffix in EnglishSuffixes)
        {
            if (token.Length - suffix.Length >= 4 && token.EndsWith(suffix, StringComparison.Ordinal))
            {
                return token[..^suffix.Length];
            }
        }

        return token.Length >= 8 ? token[..6] : token;
    }

    internal static string BuildStaticRoot(string token)
    {
        var normalized = NormalizeText(token);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var firstToken = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return string.Empty;
        }

        return firstToken.Length >= 8 ? firstToken[..6] : firstToken;
    }

    internal static HashSet<string> BuildIntentRootSet(IEnumerable<string> keywords)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stopWords = CreateStopWordSet();
        foreach (var keyword in keywords)
        {
            var normalized = NormalizeText(keyword);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            foreach (var token in Tokenize(normalized, stopWords))
            {
                roots.Add(token);
                roots.Add(BuildRoot(token));
                if (token.Length >= 6)
                {
                    roots.Add(token[..Math.Min(token.Length, 5)]);
                }
            }
        }

        roots.RemoveWhere(static root => string.IsNullOrWhiteSpace(root) || root.Length < 3);
        return roots;
    }

    private static bool IsInformativeToken(string token, IReadOnlySet<string> stopWords)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (stopWords.Contains(token))
        {
            return false;
        }

        if (token.Length >= 3)
        {
            return true;
        }

        return token.Any(char.IsDigit);
    }
}

