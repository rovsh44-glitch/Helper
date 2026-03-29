using System.Text.RegularExpressions;
using System.Text;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Infrastructure;

internal sealed record ResearchAnswerQualityDecision(
    bool Accepted,
    string Reason,
    IReadOnlyList<string> Signals);

internal sealed class ResearchAnswerQualityGate
{
    private static readonly string[] MetaFallbackSignals =
    {
        "could not verify enough grounded sources",
        "no verifiable sources were retrieved",
        "current runtime",
        "source path works",
        "working web/document path",
        "restore the search backend",
        "candidate source urls were preserved",
        "проверяемые источники не были получены",
        "в текущем runtime",
        "рабочему web/document path",
        "источниковый путь"
    };

    private static readonly HashSet<string> TopicStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "when", "where", "which", "with", "without", "about", "then", "than", "from", "this", "that", "these", "those",
        "как", "что", "чем", "затем", "потом", "после", "если", "или", "для", "при", "про", "это", "эта", "этот", "эти",
        "обычно", "простыми", "словами", "сейчас", "последних", "текущей", "сегодня", "нужны", "нужен", "нужно"
    };

    private static readonly Regex SiteChromeRegex = new(
        "(github advanced security|enterprise platform|ai-powered developer platform|skip to content|saved searches|search code|pull requests|issues|actions|marketplace|pricing|contact sales|cookie policy|privacy policy)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        "\\s+",
        RegexOptions.Compiled);

    private static readonly string[] OpinionSignals =
    {
        "my view", "my take", "i think", "i would", "looks promising", "looks useful", "should be treated cautiously",
        "на мой взгляд", "моё мнение", "мое мнение", "я бы", "выглядит сильной", "выглядит интересной", "стоит осторожно"
    };

    private static readonly string[] LimitationSignals =
    {
        "limitation", "limitations", "weakness", "trade-off", "tradeoff", "unclear", "needs", "would want",
        "ограничение", "ограничения", "слабая сторона", "слабые стороны", "неясно", "нужны", "я бы хотел"
    };

    private static readonly string[] FabricatedSourceSignals =
    {
        "https://example.com",
        "[link 1]",
        "[link 2]",
        "link 1",
        "link 2",
        "local library resources",
        "academic articles",
        "author a",
        "author b"
    };

    public ResearchAnswerQualityDecision Evaluate(
        string topic,
        IReadOnlyList<ResearchEvidenceItem> evidenceItems,
        string candidate)
    {
        var signals = new List<string>();
        var normalized = Normalize(candidate);
        var profile = ResearchRequestProfileResolver.From(topic);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            signals.Add("empty_output");
        }

        if (SiteChromeRegex.IsMatch(normalized))
        {
            signals.Add("site_chrome_markers");
        }

        if (HasDuplicatedHalf(normalized))
        {
            signals.Add("duplicated_half");
        }

        if (profile.WantsOpinion && !ContainsOpinion(normalized))
        {
            signals.Add("missing_explicit_opinion");
        }

        if (profile.IsDocumentAnalysis && !ContainsLimitation(normalized))
        {
            signals.Add("missing_limitations");
        }

        if (profile.IsDocumentAnalysis && evidenceItems.Count > 0 && !LooksGroundedInEvidence(normalized, evidenceItems))
        {
            signals.Add("missing_source_specific_grounding");
        }

        if (!profile.IsDocumentAnalysis && evidenceItems.Count == 0 && LooksLikeMetaFallback(normalized))
        {
            signals.Add("meta_runtime_fallback");
        }

        if (!profile.IsDocumentAnalysis && evidenceItems.Count == 0 && !HasTopicAnchor(topic, normalized))
        {
            signals.Add("missing_topic_anchor");
        }

        if (evidenceItems.Count == 0 && ContainsFabricatedSourceMarkers(normalized))
        {
            signals.Add("fabricated_source_placeholders");
        }

        var accepted = signals.Count == 0;
        return new ResearchAnswerQualityDecision(
            accepted,
            accepted ? "acceptable" : signals[0],
            signals);
    }

    private static bool LooksLikeMetaFallback(string candidate)
    {
        foreach (var signal in MetaFallbackSignals)
        {
            if (candidate.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTopicAnchor(string topic, string candidate)
    {
        var anchors = ExtractTopicAnchors(topic);
        if (anchors.Count == 0)
        {
            return true;
        }

        foreach (var anchor in anchors)
        {
            if (candidate.Contains(anchor, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksGroundedInEvidence(string candidate, IReadOnlyList<ResearchEvidenceItem> evidenceItems)
    {
        if (candidate.Contains("[1]", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var item in evidenceItems.Take(3))
        {
            if (!string.IsNullOrWhiteSpace(item.Title) &&
                candidate.Contains(item.Title, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsOpinion(string candidate)
    {
        foreach (var signal in OpinionSignals)
        {
            if (candidate.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLimitation(string candidate)
    {
        foreach (var signal in LimitationSignals)
        {
            if (candidate.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsFabricatedSourceMarkers(string candidate)
    {
        foreach (var signal in FabricatedSourceSignals)
        {
            if (candidate.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDuplicatedHalf(string candidate)
    {
        var sentences = candidate
            .Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static sentence => sentence.Length > 0)
            .ToArray();
        if (sentences.Length < 4 || sentences.Length % 2 != 0)
        {
            return false;
        }

        var half = sentences.Length / 2;
        for (var index = 0; index < half; index++)
        {
            if (!string.Equals(sentences[index], sentences[index + half], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> ExtractTopicAnchors(string topic)
    {
        var anchors = new List<string>();
        foreach (var token in topic.Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeToken(token);
            if (normalized.Length < 4 || TopicStopWords.Contains(normalized))
            {
                continue;
            }

            if (!anchors.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                anchors.Add(normalized);
            }

            if (anchors.Count >= 6)
            {
                break;
            }
        }

        return anchors;
    }

    private static string NormalizeToken(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static string Normalize(string candidate)
        => WhitespaceRegex.Replace(candidate ?? string.Empty, " ").Trim();
}

