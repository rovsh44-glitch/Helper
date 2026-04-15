using System.Text.RegularExpressions;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

internal sealed record SearchQueryIntentProfile(
    string Language,
    bool FreshnessSensitive,
    bool ComparisonSensitive,
    bool ContradictionSensitive,
    bool EvidenceSensitive,
    bool MedicalEvidenceSensitive,
    bool OfficialBias,
    bool HumanPromptLike,
    bool UrlAnchored,
    bool PaperAnalysisLike,
    bool BroadPromptLike,
    bool AmbiguousPromptLike,
    bool SparseRecallRisk);

internal static partial class SearchQueryIntentProfileClassifier
{
    public static SearchQueryIntentProfile Classify(string? query, SearchRankingQueryProfile queryProfile)
    {
        var text = NormalizeWhitespace(query);
        var language = LooksRussian(text) ? "ru" : "en";
        var humanPromptLike = HumanPromptRegex().IsMatch(text) ||
                              LooksLikeQuestionPrompt(text, language) ||
                              text.Contains('?');
        var urlAnchored = UrlRegex().IsMatch(text);
        var paperAnalysisLike = urlAnchored ||
                                (SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.PaperTokens) &&
                                 !queryProfile.MedicalEvidenceHeavy);
        var broadPromptLike = !queryProfile.DocumentationHeavy &&
                              (SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.BreadthTokens) ||
                               (humanPromptLike && CountTokens(text) >= 9 && !queryProfile.CurrentnessHeavy));
        var ambiguousPromptLike = !queryProfile.DocumentationHeavy &&
                                  SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.AmbiguityTokens) &&
                                  !SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.CurrentnessTokens);
        var sparseRecallRisk = paperAnalysisLike ||
                               broadPromptLike ||
                               ambiguousPromptLike ||
                               queryProfile.EvidenceHeavy ||
                               queryProfile.MedicalEvidenceHeavy ||
                               SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.ContradictionTokens);

        return new SearchQueryIntentProfile(
            Language: language,
            FreshnessSensitive: queryProfile.CurrentnessHeavy ||
                                SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.CurrentnessTokens),
            ComparisonSensitive: queryProfile.ComparisonHeavy ||
                                 SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.ComparisonTokens),
            ContradictionSensitive: SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.ContradictionTokens),
            EvidenceSensitive: queryProfile.EvidenceHeavy,
            MedicalEvidenceSensitive: queryProfile.MedicalEvidenceHeavy,
            OfficialBias: queryProfile.OfficialBias,
            HumanPromptLike: humanPromptLike,
            UrlAnchored: urlAnchored,
            PaperAnalysisLike: paperAnalysisLike,
            BroadPromptLike: broadPromptLike,
            AmbiguousPromptLike: ambiguousPromptLike,
            SparseRecallRisk: sparseRecallRisk);
    }

    internal static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SpaceRegex().Replace(value.Trim(), " ");
    }

    internal static bool LooksRussian(string value)
    {
        return value.Any(ch => ch is >= '\u0400' and <= '\u04FF');
    }

    private static bool LooksLikeQuestionPrompt(string text, string language)
    {
        return string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? RussianQuestionPromptRegex().IsMatch(text)
            : EnglishQuestionPromptRegex().IsMatch(text);
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountTokens(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    [GeneratedRegex(@"^(?:please\s+)?(?:explain|describe|analyze|review|compare|check|verify|summarize|evaluate|assess|critique|tell\s+me|show\s+me|prepare|draft|make)\b|^(?:пожалуйста\s+)?(?:объясни|расскажи|опиши|разбери|проанализируй|сравни|проверь|уточни|покажи|оцени|составь|подготовь|сделай|критически\s+разбери)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HumanPromptRegex();

    [GeneratedRegex(@"^(?:what|how|why|when|where|which|does|do|is|are|can|should)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishQuestionPromptRegex();

    [GeneratedRegex(@"^(?:что|как|почему|когда|где|какие|какой|помогает\s+ли|насколько|можно\s+ли|нужно\s+ли)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianQuestionPromptRegex();

    [GeneratedRegex(@"https?://[^\s\)\]\}>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

