namespace Helper.Api.Conversation;

internal enum CitationRenderSurface
{
    Answer,
    Trace
}

internal sealed record PublisherComplianceDecision(
    string Tier,
    bool AllowUserFacingExcerpt,
    bool AllowDirectQuote,
    int MaxWords,
    string Reason);

internal interface IPublisherCompliancePolicy
{
    PublisherComplianceDecision Evaluate(
        string? url,
        string? title,
        string? evidenceKind,
        CitationRenderSurface surface);
}

internal sealed class PublisherCompliancePolicy : IPublisherCompliancePolicy
{
    private static readonly string[] OfficialHostTokens =
    {
        "docs.", "doc.", "learn.", "developer.", "developers.", "help.", "support.", "reference.", "manual.", "spec."
    };

    private static readonly string[] OfficialTitleTokens =
    {
        "docs", "documentation", "reference", "manual", "specification",
        "документация", "справочник", "спецификация"
    };

    private static readonly string[] EditorialHostTokens =
    {
        "reuters", "apnews", "bbc", "nytimes", "wsj", "bloomberg", "cnn", "theguardian"
    };

    public PublisherComplianceDecision Evaluate(
        string? url,
        string? title,
        string? evidenceKind,
        CitationRenderSurface surface)
    {
        var host = TryResolveHost(url);
        var tier = ResolveTier(host, title);
        return surface switch
        {
            CitationRenderSurface.Answer => ResolveForAnswer(tier, evidenceKind),
            _ => ResolveForTrace(tier, evidenceKind)
        };
    }

    private static PublisherComplianceDecision ResolveForAnswer(string tier, string? evidenceKind)
    {
        return tier switch
        {
            "official_reference" => new PublisherComplianceDecision(tier, true, true, 10, "official_reference_short_quote"),
            "public_interest" => new PublisherComplianceDecision(tier, true, true, 10, "public_interest_short_quote"),
            _ => new PublisherComplianceDecision(tier, false, false, 0, $"surface_answer_restricts_{NormalizeEvidenceKind(evidenceKind)}")
        };
    }

    private static PublisherComplianceDecision ResolveForTrace(string tier, string? evidenceKind)
    {
        return tier switch
        {
            "official_reference" => new PublisherComplianceDecision(tier, true, true, 16, "official_reference_trace_excerpt"),
            "public_interest" => new PublisherComplianceDecision(tier, true, true, 16, "public_interest_trace_excerpt"),
            _ => new PublisherComplianceDecision(tier, false, false, 0, $"trace_excerpt_restricted_for_{NormalizeEvidenceKind(evidenceKind)}")
        };
    }

    private static string ResolveTier(string? host, string? title)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "general_web";
        }

        if (host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase))
        {
            return "public_interest";
        }

        if (OfficialHostTokens.Any(token => host.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            OfficialTitleTokens.Any(token => (title ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return "official_reference";
        }

        if (EditorialHostTokens.Any(token => host.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return "editorial";
        }

        return "general_web";
    }

    private static string? TryResolveHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return uri.Host.Trim().ToLowerInvariant();
    }

    private static string NormalizeEvidenceKind(string? evidenceKind)
    {
        return string.IsNullOrWhiteSpace(evidenceKind)
            ? "source"
            : evidenceKind.Trim().ToLowerInvariant();
    }
}

