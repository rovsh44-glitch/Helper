using System.Text.RegularExpressions;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public enum VariationSlot
{
    LeadLine,
    NextStepHeader,
    NextStepBridge,
    ContextualNextStep,
    MemoryAckLead,
    MemoryAckNextStep,
    ClarifyPrompt,
    OperatorSummaryHeader
}

internal sealed record VariationCandidate(
    string Text,
    IReadOnlyList<string>? DetailLevels = null,
    IReadOnlyList<string>? Formalities = null,
    bool PreferShort = false,
    bool PreferExpanded = false);

public interface IConversationVariationPolicy
{
    string Select(DialogAct dialogAct, VariationSlot slot, ChatTurnContext context, IReadOnlyList<string> candidates);
}

public sealed class ConversationVariationPolicy : IConversationVariationPolicy
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public string Select(DialogAct dialogAct, VariationSlot slot, ChatTurnContext context, IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return Select(
            dialogAct,
            slot,
            context,
            candidates.Select(candidate => new VariationCandidate(candidate)).ToArray());
    }

    internal string Select(DialogAct dialogAct, VariationSlot slot, ChatTurnContext context, IReadOnlyList<VariationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(candidates);

        var options = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
            .GroupBy(candidate => candidate.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        if (options.Length == 0)
        {
            return string.Empty;
        }

        if (options.Length == 1)
        {
            return options[0].Text;
        }

        var recentFingerprints = BuildRecentFingerprints(context.History);
        var detailLevel = NormalizeDetailLevel(context.Conversation.DetailLevel);
        var formality = NormalizeFormality(context.Conversation.Formality);
        var scoredOptions = options
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreCandidate(candidate, detailLevel, formality)
            })
            .ToArray();
        var bestScore = scoredOptions.Max(option => option.Score);
        var shortlist = scoredOptions
            .Where(option => option.Score == bestScore)
            .Select(option => option.Candidate)
            .ToArray();
        var startIndex = PositiveModulo(
            StableHash($"{context.Conversation.Id}|{context.TurnId}|{dialogAct}|{slot}|{context.ResolvedTurnLanguage}|{context.ResolvedStyleMode}|{context.ExecutionMode}|{detailLevel}|{formality}"),
            shortlist.Length);

        for (var offset = 0; offset < shortlist.Length; offset++)
        {
            var index = (startIndex + offset) % shortlist.Length;
            var fingerprint = BuildFingerprint(shortlist[index].Text);
            if (!recentFingerprints.Contains(fingerprint))
            {
                return shortlist[index].Text;
            }
        }

        return shortlist[startIndex].Text;
    }

    internal static string BuildFingerprint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in normalized)
        {
            var collapsed = WhitespaceRegex.Replace(rawLine, " ").Trim().Trim('"', '\'', '`', ':', '.', '!', '?').ToLowerInvariant();
            if (collapsed.Length == 0)
            {
                continue;
            }

            return collapsed.Length <= 96 ? collapsed : collapsed[..96];
        }

        return string.Empty;
    }

    private static HashSet<string> BuildRecentFingerprints(IReadOnlyList<ChatMessageDto> history)
    {
        var fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = history.Count - 1; i >= 0 && fingerprints.Count < 8; i--)
        {
            var message = history[i];
            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fingerprint = BuildFingerprint(message.Content);
            if (fingerprint.Length > 0)
            {
                fingerprints.Add(fingerprint);
            }
        }

        return fingerprints;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static int PositiveModulo(int value, int modulo)
    {
        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private static int ScoreCandidate(VariationCandidate candidate, string detailLevel, string formality)
    {
        var score = 0;
        score += ScorePreference(detailLevel, candidate.DetailLevels);
        score += ScorePreference(formality, candidate.Formalities);

        if (string.Equals(detailLevel, "concise", StringComparison.Ordinal))
        {
            if (candidate.PreferShort)
            {
                score += 3;
            }

            if (candidate.PreferExpanded)
            {
                score -= 2;
            }

            if (candidate.Text.Length <= 56)
            {
                score += 1;
            }
        }
        else if (string.Equals(detailLevel, "deep", StringComparison.Ordinal))
        {
            if (candidate.PreferExpanded)
            {
                score += 3;
            }

            if (candidate.PreferShort)
            {
                score -= 1;
            }

            if (candidate.Text.Length >= 64)
            {
                score += 1;
            }
        }

        return score;
    }

    private static int ScorePreference(string value, IReadOnlyList<string>? preferences)
    {
        if (preferences is null || preferences.Count == 0)
        {
            return 1;
        }

        foreach (var preference in preferences)
        {
            if (string.Equals(value, preference, StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }
        }

        return 0;
    }

    private static string NormalizeDetailLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "balanced";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "concise" or "short" => "concise",
            "deep" or "detailed" or "long" => "deep",
            _ => "balanced"
        };
    }

    private static string NormalizeFormality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "neutral";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "formal" => "formal",
            "casual" or "informal" => "casual",
            _ => "neutral"
        };
    }
}

