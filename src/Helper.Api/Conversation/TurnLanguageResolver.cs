using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

public interface ITurnLanguageResolver
{
    string Resolve(ConversationUserProfile profile, string? requestMessage, IReadOnlyList<Helper.Api.Hosting.ChatMessageDto>? history = null);
    string Resolve(string? preferredLanguage, string? requestMessage, IReadOnlyList<Helper.Api.Hosting.ChatMessageDto>? history = null);
}

public sealed class TurnLanguageResolver : ITurnLanguageResolver
{
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CitationRegex = new(@"\[\d+\]", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"\p{L}+", RegexOptions.Compiled);

    public string Resolve(ConversationUserProfile profile, string? requestMessage, IReadOnlyList<Helper.Api.Hosting.ChatMessageDto>? history = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Resolve(profile.Language, requestMessage, history);
    }

    public string Resolve(string? preferredLanguage, string? requestMessage, IReadOnlyList<Helper.Api.Hosting.ChatMessageDto>? history = null)
    {
        var normalizedPreferred = NormalizePreferredLanguage(preferredLanguage);
        if (normalizedPreferred is "ru" or "en")
        {
            return normalizedPreferred;
        }

        var requestProfile = Analyze(requestMessage);
        if (requestProfile.ResolvedLanguage is not null)
        {
            return requestProfile.ResolvedLanguage;
        }

        if (history is not null)
        {
            for (var i = history.Count - 1; i >= 0; i--)
            {
                var messageProfile = Analyze(history[i].Content);
                if (messageProfile.ResolvedLanguage is not null)
                {
                    return messageProfile.ResolvedLanguage;
                }
            }
        }

        return "en";
    }

    private static string NormalizePreferredLanguage(string? preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(preferredLanguage))
        {
            return "auto";
        }

        return preferredLanguage.Trim().ToLowerInvariant() switch
        {
            "ru" or "russian" => "ru",
            "en" or "english" => "en",
            _ => "auto"
        };
    }

    private static LanguageProfile Analyze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LanguageProfile(0, 0, 0, 0, 0, null);
        }

        var probe = NormalizeProbe(text);
        var cyrillicCount = 0;
        var latinCount = 0;
        var cyrillicWordCount = 0;
        var latinWordCount = 0;
        var technicalLatinTokenCount = 0;

        foreach (var ch in probe)
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

        foreach (Match match in WordRegex.Matches(probe))
        {
            var token = match.Value;
            if (ContainsCyrillic(token))
            {
                cyrillicWordCount++;
                continue;
            }

            if (!ContainsLatin(token))
            {
                continue;
            }

            if (IsTechnicalLatinToken(token))
            {
                technicalLatinTokenCount++;
                continue;
            }

            latinWordCount++;
        }

        return new LanguageProfile(
            CyrillicCount: cyrillicCount,
            LatinCount: latinCount,
            CyrillicWordCount: cyrillicWordCount,
            LatinWordCount: latinWordCount,
            TechnicalLatinTokenCount: technicalLatinTokenCount,
            ResolvedLanguage: ResolveBySignals(cyrillicCount, latinCount, cyrillicWordCount, latinWordCount, technicalLatinTokenCount));
    }

    private static string NormalizeProbe(string text)
    {
        return CitationRegex.Replace(UrlRegex.Replace(text, " "), " ");
    }

    private static string? ResolveBySignals(int cyrillicCount, int latinCount, int cyrillicWordCount, int latinWordCount, int technicalLatinTokenCount)
    {
        if (cyrillicWordCount >= 2 && cyrillicWordCount >= latinWordCount)
        {
            return "ru";
        }

        if (latinWordCount >= 2 && latinWordCount > cyrillicWordCount + 1)
        {
            return "en";
        }

        var effectiveLatinCount = Math.Max(0, latinCount - (technicalLatinTokenCount * 3));
        if (cyrillicCount >= 3 && cyrillicCount >= effectiveLatinCount)
        {
            return "ru";
        }

        if (latinCount >= 3 && latinWordCount >= cyrillicWordCount && latinCount > cyrillicCount * 1.2)
        {
            return "en";
        }

        if (cyrillicCount > 0 && latinCount == 0)
        {
            return "ru";
        }

        if (latinCount > 0)
        {
            return "en";
        }

        return null;
    }

    private static bool ContainsCyrillic(string token)
    {
        foreach (var ch in token)
        {
            if ((ch >= '\u0400' && ch <= '\u04FF') || ch == '\u0401' || ch == '\u0451')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLatin(string token)
    {
        foreach (var ch in token)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTechnicalLatinToken(string token)
    {
        var uppercaseCount = 0;
        var lowercaseCount = 0;
        foreach (var ch in token)
        {
            if (ch >= 'A' && ch <= 'Z')
            {
                uppercaseCount++;
            }
            else if (ch >= 'a' && ch <= 'z')
            {
                lowercaseCount++;
            }
        }

        if (token.Length <= 6 && uppercaseCount == token.Length)
        {
            return true;
        }

        return token.Length <= 6 && uppercaseCount >= 2 && lowercaseCount > 0;
    }

    private sealed record LanguageProfile(
        int CyrillicCount,
        int LatinCount,
        int CyrillicWordCount,
        int LatinWordCount,
        int TechnicalLatinTokenCount,
        string? ResolvedLanguage);
}

