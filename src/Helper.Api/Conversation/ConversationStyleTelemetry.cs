using System.Text.RegularExpressions;
using Helper.Api.Backend.Application;

namespace Helper.Api.Conversation;

public sealed record ConversationStyleTelemetry(
    string? LeadPhraseFingerprint,
    bool MixedLanguageDetected,
    bool GenericClarificationDetected,
    bool GenericNextStepDetected,
    bool MemoryAckTemplateDetected,
    string? SourceFingerprint);

public interface IConversationStyleTelemetryAnalyzer
{
    ConversationStyleTelemetry Analyze(ChatTurnContext context);
}

public sealed class ConversationStyleTelemetryAnalyzer : IConversationStyleTelemetryAnalyzer
{
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CitationRegex = new(@"\[\d+\]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> GenericClarificationTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "please provide more details so i can help accurately.",
        "please clarify your request.",
        "what should i remember for this conversation?",
        "что именно запомнить для этого диалога?",
        "provide required clarification.",
        "пожалуйста, дайте чуть больше контекста, чтобы я помог точно.",
        "пришлите нужное уточнение, и я продолжу."
    };

    private readonly ISourceNormalizationService _sourceNormalization;

    public ConversationStyleTelemetryAnalyzer(ISourceNormalizationService? sourceNormalization = null)
    {
        _sourceNormalization = sourceNormalization ?? new SourceNormalizationService();
    }

    public ConversationStyleTelemetry Analyze(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var responseText = !string.IsNullOrWhiteSpace(context.FinalResponse)
            ? context.FinalResponse
            : context.ExecutionOutput;

        return new ConversationStyleTelemetry(
            LeadPhraseFingerprint: BuildLeadPhraseFingerprint(responseText),
            MixedLanguageDetected: DetectMixedLanguage(context.ResolvedTurnLanguage, context.Request.Message, responseText, context.NextStep),
            GenericClarificationDetected: DetectGenericClarification(context, responseText),
            GenericNextStepDetected: DetectGenericNextStep(context.NextStep),
            MemoryAckTemplateDetected: DetectMemoryAckTemplate(context, responseText),
            SourceFingerprint: BuildSourceFingerprint(context.Sources));
    }

    private bool DetectMemoryAckTemplate(ChatTurnContext context, string responseText)
    {
        if (!RememberDirectiveParser.TryExtractFact(context.Request.Message, out _))
        {
            return false;
        }

        return MemoryAcknowledgementCatalog.MatchesLeadTemplate(responseText) ||
               MemoryAcknowledgementCatalog.MatchesNextStepTemplate(context.NextStep);
    }

    private static bool DetectGenericClarification(ChatTurnContext context, string responseText)
    {
        if (!context.RequiresClarification &&
            !string.Equals(context.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedResponse = NormalizeText(responseText);
        if (GenericClarificationTemplates.Contains(normalizedResponse))
        {
            return true;
        }

        return normalizedResponse.StartsWith("please provide more details", StringComparison.Ordinal) ||
               normalizedResponse.StartsWith("please clarify", StringComparison.Ordinal) ||
               normalizedResponse.StartsWith("could you clarify", StringComparison.Ordinal) ||
               normalizedResponse.StartsWith("уточните", StringComparison.Ordinal) ||
               normalizedResponse.StartsWith("что именно", StringComparison.Ordinal);
    }

    private static bool DetectGenericNextStep(string? nextStep)
    {
        return IntentAwareNextStepPolicy.IsGenericTemplate(nextStep);
    }

    private string? BuildSourceFingerprint(IReadOnlyList<string> sources)
    {
        var normalized = _sourceNormalization
            .Normalize(sources)
            .Sources
            .Select(source => source.CanonicalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return normalized.Length == 0
            ? null
            : string.Join("|", normalized);
    }

    private static string? BuildLeadPhraseFingerprint(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var lines = responseText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var normalized = NormalizeText(rawLine);
            if (normalized.Length < 4)
            {
                continue;
            }

            return normalized.Length <= 96 ? normalized : normalized[..96];
        }

        return null;
    }

    private static bool DetectMixedLanguage(string? resolvedTurnLanguage, string? requestText, string? responseText, string? nextStep)
    {
        var requestProfile = AnalyzeLanguage(requestText);
        var responseProfile = AnalyzeLanguage($"{responseText}\n{nextStep}");

        if (responseProfile.HasCyrillic && responseProfile.HasLatin)
        {
            return true;
        }

        if (string.Equals(resolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase) &&
            responseProfile.DominantLanguage == LanguageSignal.English)
        {
            return true;
        }

        if (string.Equals(resolvedTurnLanguage, "en", StringComparison.OrdinalIgnoreCase) &&
            responseProfile.DominantLanguage == LanguageSignal.Russian)
        {
            return true;
        }

        return requestProfile.DominantLanguage != LanguageSignal.Unknown &&
               responseProfile.DominantLanguage != LanguageSignal.Unknown &&
               requestProfile.DominantLanguage != responseProfile.DominantLanguage;
    }

    private static LanguageProfile AnalyzeLanguage(string? text)
    {
        var normalized = NormalizeLanguageProbe(text);
        var cyrillicCount = 0;
        var latinCount = 0;

        foreach (var ch in normalized)
        {
            if ((ch >= '\u0400' && ch <= '\u04FF') || ch == '\u0401' || ch == '\u0451')
            {
                cyrillicCount++;
                continue;
            }

            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            {
                latinCount++;
            }
        }

        var dominant = LanguageSignal.Unknown;
        if (cyrillicCount >= 3 && cyrillicCount >= latinCount)
        {
            dominant = LanguageSignal.Russian;
        }
        else if (latinCount >= 3 && latinCount > cyrillicCount)
        {
            dominant = LanguageSignal.English;
        }

        return new LanguageProfile(
            DominantLanguage: dominant,
            HasCyrillic: cyrillicCount >= 3,
            HasLatin: latinCount >= 6);
    }

    private static string NormalizeLanguageProbe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var withoutUrls = UrlRegex.Replace(text, " ");
        return CitationRegex.Replace(withoutUrls, " ");
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var withoutUrls = UrlRegex.Replace(text, " ");
        var withoutCitations = CitationRegex.Replace(withoutUrls, " ");
        var collapsed = WhitespaceRegex.Replace(withoutCitations, " ").Trim().Trim('"', '\'', '`');
        return collapsed.ToLowerInvariant();
    }

    private enum LanguageSignal
    {
        Unknown,
        Russian,
        English
    }

    private sealed record LanguageProfile(LanguageSignal DominantLanguage, bool HasCyrillic, bool HasLatin);
}

