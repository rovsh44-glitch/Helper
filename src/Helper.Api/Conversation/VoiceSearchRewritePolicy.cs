using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IVoiceSearchRewritePolicy
{
    VoiceSearchRewriteDecision Rewrite(string query, string? language = null);
}

internal sealed record VoiceSearchRewriteDecision(
    string Query,
    bool Applied,
    IReadOnlyList<string> Trace);

internal sealed partial class VoiceSearchRewritePolicy : IVoiceSearchRewritePolicy
{
    public VoiceSearchRewriteDecision Rewrite(string query, string? language = null)
    {
        var normalized = NormalizeWhitespace(query);
        if (normalized.Length == 0)
        {
            return new VoiceSearchRewriteDecision(
                string.Empty,
                false,
                new[] { "web_query.voice applied=no reason=empty_query" });
        }

        var resolvedLanguage = ResolveLanguage(language, normalized);
        var rewritten = normalized;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var previous = rewritten;
            rewritten = StripWakePhrase(rewritten, resolvedLanguage);
            rewritten = StripLeadingFraming(rewritten, resolvedLanguage);
            rewritten = StripTrailingPoliteness(rewritten, resolvedLanguage);
            rewritten = NormalizeWhitespace(rewritten.Trim(' ', ',', '.', ':', ';', '!', '?', '-', '—'));
            if (string.Equals(previous, rewritten, StringComparison.Ordinal))
            {
                break;
            }
        }

        if (rewritten.Length < 8 || CountTokens(rewritten) < 2)
        {
            return new VoiceSearchRewriteDecision(
                normalized,
                false,
                new[] { "web_query.voice applied=no reason=insufficient_core_query" });
        }

        if (string.Equals(rewritten, normalized, StringComparison.Ordinal))
        {
            return new VoiceSearchRewriteDecision(
                normalized,
                false,
                new[] { "web_query.voice applied=no reason=no_voice_framing_detected" });
        }

        return new VoiceSearchRewriteDecision(
            rewritten,
            true,
            new[]
            {
                $"web_query.voice applied=yes language={resolvedLanguage}",
                $"web_query.voice cleaned={Summarize(rewritten)}"
            });
    }

    private static string StripWakePhrase(string value, string language)
    {
        var regex = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? RussianWakePhraseRegex()
            : EnglishWakePhraseRegex();
        return regex.Replace(value, string.Empty, 1);
    }

    private static string StripLeadingFraming(string value, string language)
    {
        var regex = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? RussianLeadingFramingRegex()
            : EnglishLeadingFramingRegex();
        return regex.Replace(value, string.Empty, 1);
    }

    private static string StripTrailingPoliteness(string value, string language)
    {
        var regex = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? RussianTrailingPolitenessRegex()
            : EnglishTrailingPolitenessRegex();
        return regex.Replace(value, string.Empty, 1);
    }

    private static string ResolveLanguage(string? language, string query)
    {
        if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return language!;
        }

        return query.Any(ch => ch is >= '\u0400' and <= '\u04FF') ? "ru" : "en";
    }

    private static int CountTokens(string value)
    {
        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SpaceRegex().Replace(value.Trim(), " ");
    }

    private static string Summarize(string value)
    {
        return value.Length <= 160 ? value : value[..160];
    }

    [GeneratedRegex(@"^(?:hey|hi|hello|ok|okay)\s+helper[\s,:\-]*|^helper[\s,:\-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishWakePhraseRegex();

    [GeneratedRegex(@"^(?:эй|хей|привет)\s+хелпер[\s,:\-]*|^хелпер[\s,:\-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianWakePhraseRegex();

    [GeneratedRegex(@"^(?:(?:um|uh|please)\s+)*(?:(?:can|could|would)\s+you\s+)?(?:(?:look\s+up|search\s+for|find|check|tell\s+me|show\s+me|give\s+me)\s+)+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishLeadingFramingRegex();

    [GeneratedRegex(@"^(?:(?:ну|ээ|эм|пожалуйста)\s+)*(?:(?:можешь|можно|подскажи|скажи|посмотри|поищи|найди|проверь|покажи|дай)\s+)+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianLeadingFramingRegex();

    [GeneratedRegex(@"(?:\s|,)+(?:please|pls|thanks|thank you)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishTrailingPolitenessRegex();

    [GeneratedRegex(@"(?:\s|,)+(?:пожалуйста|спасибо)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianTrailingPolitenessRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

