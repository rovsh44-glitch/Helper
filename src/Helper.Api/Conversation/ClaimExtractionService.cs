using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

public sealed class ClaimExtractionService : IClaimExtractionService
{
    private static readonly Regex Splitter = new(@"(?<=[\.\!\?\n])\s+", RegexOptions.Compiled);
    private static readonly Regex EntityRegex = new(@"\b([A-Z][a-zA-Z0-9_-]{2,}|[А-ЯЁ][а-яёА-ЯЁ0-9_-]{2,}|[A-Z]{2,}[0-9]{0,3})\b", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d{2,4}\b", RegexOptions.Compiled);
    private static readonly string[] OpinionMarkers =
    {
        "i think", "i believe", "in my opinion", "probably", "likely", "may", "might", "seems",
        "думаю", "считаю", "мне кажется", "вероятно", "возможно", "полагаю"
    };
    private static readonly string[] InstructionMarkers =
    {
        "use ", "run ", "set ", "install ", "create ", "add ", "remove ", "check ", "ensure ", "avoid ",
        "используй", "запусти", "установи", "создай", "добавь", "удали", "проверь", "убедись", "избегай", "сделай"
    };
    private static readonly string[] FactualMarkers =
    {
        " is ", " are ", " was ", " were ", " has ", " have ", " includes ", " supports ", "contains ",
        " составляет ", " является ", " включает ", " содержит ", " поддерживает "
    };

    public IReadOnlyList<ExtractedClaim> Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ExtractedClaim>();
        }

        var cleaned = text.Replace("\r", " ").Trim();
        if (cleaned.Length == 0)
        {
            return Array.Empty<ExtractedClaim>();
        }

        var parts = Splitter.Split(cleaned)
            .Select(SanitizeSegment)
            .Where(x => x.Length >= 12)
            .Take(32)
            .ToList();
        if (parts.Count == 0)
        {
            return Array.Empty<ExtractedClaim>();
        }

        var result = new List<ExtractedClaim>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var sentence = parts[i];
            var type = Classify(sentence);
            var entities = ExtractEntities(sentence);
            result.Add(new ExtractedClaim(sentence, type, i, entities));
        }

        return result;
    }

    private static ClaimSentenceType Classify(string sentence)
    {
        var normalized = " " + sentence.Trim().ToLowerInvariant() + " ";

        if (ContainsAny(normalized, OpinionMarkers))
        {
            return ClaimSentenceType.Opinion;
        }

        if (StartsWithInstruction(sentence))
        {
            return ClaimSentenceType.Instruction;
        }

        if (ContainsAny(normalized, FactualMarkers) || ContainsFactualPattern(sentence))
        {
            return ClaimSentenceType.Fact;
        }

        // Treat unknown informative statements as factual by default for grounding.
        return ClaimSentenceType.Fact;
    }

    private static bool StartsWithInstruction(string sentence)
    {
        var normalized = sentence.Trim().ToLowerInvariant();
        return InstructionMarkers.Any(marker => normalized.StartsWith(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string sentence, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (sentence.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsFactualPattern(string sentence)
    {
        if (sentence.Any(char.IsDigit))
        {
            return true;
        }

        if (sentence.Contains('%') || sentence.Contains(':') || sentence.Contains('/'))
        {
            return true;
        }

        return false;
    }

    private static string SanitizeSegment(string segment)
    {
        var trimmed = segment.Trim();
        trimmed = trimmed.Trim('-', '*', ' ', '\t');
        trimmed = Regex.Replace(trimmed, @"\s+", " ");
        return trimmed;
    }

    private static IReadOnlyList<string> ExtractEntities(string sentence)
    {
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EntityRegex.Matches(sentence))
        {
            var value = match.Value.Trim();
            if (value.Length >= 3)
            {
                entities.Add(value);
            }
        }

        foreach (Match match in NumberRegex.Matches(sentence))
        {
            var value = match.Value.Trim();
            if (value.Length > 0)
            {
                entities.Add(value);
            }
        }

        return entities.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

