using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IBenchmarkDraftQualityPolicy
{
    bool BenchmarkRequiresRussian(ChatTurnContext context);
    bool ContainsMetaUncertainty(string solution);
    bool LooksLikeResearchFallback(string solution);
    bool LooksLowQualityBenchmarkDraft(string solution);
    bool LooksLowQualityBenchmarkDraft(ChatTurnContext context, string solution);
    bool LooksPredominantlyLatin(string text);
    string StripMetaFallbackContent(string candidate);
}

internal sealed class BenchmarkDraftQualityPolicy : IBenchmarkDraftQualityPolicy
{
    private static readonly string[] BenchmarkPlaceholderSignals =
    {
        "https://example.com",
        "[link 1]",
        "[link 2]",
        "link 1",
        "link 2",
        "local library resources",
        "academic articles",
        "author a",
        "author b",
        "several sources provide clear explanations"
    };

    private readonly IBenchmarkResponseStructurePolicy _structurePolicy;

    public BenchmarkDraftQualityPolicy(IBenchmarkResponseStructurePolicy structurePolicy)
    {
        _structurePolicy = structurePolicy;
    }

    public bool LooksLikeResearchFallback(string solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        return solution.Contains("could not retrieve grounded live search results", StringComparison.OrdinalIgnoreCase) ||
               solution.Contains("No verifiable sources were retrieved", StringComparison.OrdinalIgnoreCase) ||
               solution.Contains("restore the search backend", StringComparison.OrdinalIgnoreCase) ||
               solution.Contains("Research request:", StringComparison.OrdinalIgnoreCase) ||
               solution.Contains("Неопределённость: для фактических утверждений не удалось найти проверяемые опорные источники.", StringComparison.OrdinalIgnoreCase) ||
               solution.Contains("Неопределённость: для этого фактического утверждения не удалось получить проверяемые источники.", StringComparison.OrdinalIgnoreCase);
    }

    public bool ContainsMetaUncertainty(string solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        return solution.Contains("Uncertainty:", StringComparison.OrdinalIgnoreCase) ||
               solution.Contains("Неопределённость:", StringComparison.OrdinalIgnoreCase);
    }

    public bool LooksLowQualityBenchmarkDraft(string solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        foreach (var signal in BenchmarkPlaceholderSignals)
        {
            if (solution.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (solution.Contains("type load", StringComparison.OrdinalIgnoreCase) ||
            solution.Contains("very strong bodies", StringComparison.OrdinalIgnoreCase) ||
            solution.Contains("simultaneously improve", StringComparison.OrdinalIgnoreCase) ||
            solution.Contains("misunderstanding", StringComparison.OrdinalIgnoreCase) ||
            solution.Contains("airport loads", StringComparison.OrdinalIgnoreCase) ||
            solution.Contains("Theoretical Physics V2", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(solution, @"[\u4E00-\u9FFF]"))
        {
            return true;
        }

        var latinWords = Regex.Matches(solution, @"\b[a-zA-Z]{3,}\b").Count;
        var cyrillicWords = Regex.Matches(solution, @"\b[\p{IsCyrillic}]{3,}\b").Count;
        return latinWords >= 4 && cyrillicWords >= 4;
    }

    public bool LooksLowQualityBenchmarkDraft(ChatTurnContext context, string solution)
    {
        if (LooksLowQualityBenchmarkDraft(solution))
        {
            return true;
        }

        if (!BenchmarkRequiresRussian(context))
        {
            return false;
        }

        if (solution.Contains("**Answer:**", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var heading in new[] { "Local Findings", "Analysis", "Conclusion", "Opinion" })
        {
            var section = _structurePolicy.TryExtractSection(solution, heading);
            if (string.IsNullOrWhiteSpace(section))
            {
                continue;
            }

            if (LooksPredominantlyLatin(section))
            {
                return true;
            }
        }

        return false;
    }

    public string StripMetaFallbackContent(string candidate)
    {
        var lines = candidate
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (LooksLikeResearchFallback(line))
            {
                continue;
            }

            if (line.StartsWith("Uncertainty:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Неопределённость:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join(" ", kept);
    }

    public bool BenchmarkRequiresRussian(ChatTurnContext context)
    {
        return string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.Conversation.PreferredLanguage, "ru", StringComparison.OrdinalIgnoreCase) ||
               (context.Request.SystemInstruction?.Contains("Answer in Russian.", StringComparison.OrdinalIgnoreCase) ?? false) ||
               ContainsCyrillic(context.Request.Message);
    }

    public bool LooksPredominantlyLatin(string text)
    {
        var latinWords = Regex.Matches(text, @"\b[a-zA-Z]{3,}\b").Count;
        var cyrillicWords = Regex.Matches(text, @"\b[\p{IsCyrillic}]{3,}\b").Count;
        return latinWords >= 6 && latinWords > cyrillicWords * 2;
    }

    private static bool ContainsCyrillic(string text)
        => Regex.IsMatch(text ?? string.Empty, @"\p{IsCyrillic}");
}

