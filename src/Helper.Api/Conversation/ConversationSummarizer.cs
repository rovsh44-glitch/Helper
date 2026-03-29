using System.Text;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed class ConversationSummarizer : IConversationSummarizer
{
    private const int MinMessagesForSummary = 12;
    private const int SummaryWindow = 20;
    private const int MaxSummaryLength = 420;
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "about", "have", "what", "when",
        "where", "which", "your", "you", "are", "not", "but", "или", "что", "это", "для", "как", "при",
        "если", "чтобы", "также", "только", "нужно", "сделай", "пожалуйста"
    };

    public ConversationBranchSummary? TryBuild(
        string branchId,
        IReadOnlyList<ChatMessageDto> branchMessages,
        ConversationBranchSummary? previous,
        DateTimeOffset now)
    {
        if (branchMessages.Count < MinMessagesForSummary)
        {
            return previous;
        }

        var window = branchMessages.TakeLast(SummaryWindow).ToList();
        var users = window.Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase)).ToList();
        var assistants = window.Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)).ToList();
        if (users.Count == 0 || assistants.Count == 0)
        {
            return previous;
        }

        var summary = BuildSummaryText(users, assistants);
        var quality = EvaluateQuality(summary, window);
        if (quality < 0.32)
        {
            return previous;
        }

        if (previous != null &&
            quality + 0.03 < previous.QualityScore &&
            branchMessages.Count <= previous.SourceMessageCount + 4)
        {
            return previous;
        }

        return new ConversationBranchSummary(
            branchId,
            summary,
            branchMessages.Count,
            Math.Round(quality, 3),
            now);
    }

    private static string BuildSummaryText(IReadOnlyList<ChatMessageDto> userMessages, IReadOnlyList<ChatMessageDto> assistantMessages)
    {
        var goal = TakeSentence(userMessages.Last().Content, 120);
        var userContext = userMessages
            .TakeLast(5)
            .Select(m => TakeSentence(m.Content, 80))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        var assistantProgress = assistantMessages
            .TakeLast(4)
            .Select(m => TakeSentence(m.Content, 90))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        var pending = userMessages
            .TakeLast(6)
            .Select(m => m.Content)
            .FirstOrDefault(x => x.Contains('?', StringComparison.Ordinal));

        var builder = new StringBuilder();
        builder.Append("Goal: ");
        builder.Append(goal);

        if (userContext.Count > 0)
        {
            builder.Append(" | Context: ");
            builder.Append(string.Join("; ", userContext));
        }

        if (assistantProgress.Count > 0)
        {
            builder.Append(" | Progress: ");
            builder.Append(string.Join("; ", assistantProgress));
        }

        if (!string.IsNullOrWhiteSpace(pending))
        {
            builder.Append(" | Pending: ");
            builder.Append(TakeSentence(pending, 90));
        }

        var text = builder.ToString();
        if (text.Length > MaxSummaryLength)
        {
            text = text.Substring(0, MaxSummaryLength - 3) + "...";
        }

        return text;
    }

    private static double EvaluateQuality(string summary, IReadOnlyList<ChatMessageDto> messages)
    {
        if (summary.Length < 80)
        {
            return 0;
        }

        var summaryTokens = Tokenize(summary);
        if (summaryTokens.Count == 0)
        {
            return 0;
        }

        var contextTokens = Tokenize(string.Join(" ", messages.TakeLast(8).Select(m => m.Content)));
        if (contextTokens.Count == 0)
        {
            return 0;
        }

        var overlap = contextTokens.Intersect(summaryTokens, StringComparer.OrdinalIgnoreCase).Count();
        var coverage = (double)overlap / contextTokens.Count;
        var density = summary.Length switch
        {
            < 100 => 0.2,
            <= 420 => 1.0,
            _ => 0.4
        };
        var hasStructuredSections = summary.Contains("Goal:", StringComparison.OrdinalIgnoreCase) &&
                                    summary.Contains("Context:", StringComparison.OrdinalIgnoreCase) &&
                                    summary.Contains("Progress:", StringComparison.OrdinalIgnoreCase);
        var structure = hasStructuredSections ? 1.0 : 0.5;

        return (coverage * 0.55) + (density * 0.2) + (structure * 0.25);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var separators = new[]
        {
            ' ', '\n', '\r', '\t', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '|', '-'
        };

        foreach (var raw in text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw.Trim().ToLowerInvariant();
            if (token.Length < 4 || StopWords.Contains(token))
            {
                continue;
            }

            result.Add(token);
        }

        return result;
    }

    private static string TakeSentence(string text, int maxLength)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var punctuationIndex = trimmed.IndexOfAny(new[] { '.', ';', '\n' });
        var sentence = punctuationIndex > 0 ? trimmed.Substring(0, punctuationIndex) : trimmed;
        if (sentence.Length <= maxLength)
        {
            return sentence;
        }

        return sentence.Substring(0, maxLength - 3) + "...";
    }
}

