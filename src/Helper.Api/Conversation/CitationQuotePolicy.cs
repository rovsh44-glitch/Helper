namespace Helper.Api.Conversation;

internal sealed record CitationQuoteDecision(
    string? Text,
    bool Included,
    bool Truncated,
    bool DirectQuoteAllowed,
    string Tier,
    string Reason,
    IReadOnlyList<string> Flags);

internal interface ICitationQuotePolicy
{
    CitationQuoteDecision Build(
        string? excerpt,
        string? url,
        string? title,
        string? evidenceKind,
        CitationRenderSurface surface);
}

internal sealed class CitationQuotePolicy : ICitationQuotePolicy
{
    private readonly IPublisherCompliancePolicy _publisherCompliancePolicy;
    private readonly IExcerptBudgetPolicy _excerptBudgetPolicy;

    public CitationQuotePolicy(
        IPublisherCompliancePolicy? publisherCompliancePolicy = null,
        IExcerptBudgetPolicy? excerptBudgetPolicy = null)
    {
        _publisherCompliancePolicy = publisherCompliancePolicy ?? new PublisherCompliancePolicy();
        _excerptBudgetPolicy = excerptBudgetPolicy ?? new ExcerptBudgetPolicy();
    }

    public CitationQuoteDecision Build(
        string? excerpt,
        string? url,
        string? title,
        string? evidenceKind,
        CitationRenderSurface surface)
    {
        var compliance = _publisherCompliancePolicy.Evaluate(url, title, evidenceKind, surface);
        var flags = new List<string>
        {
            $"publisher_tier:{compliance.Tier}",
            $"compliance_reason:{NormalizeFlagToken(compliance.Reason)}",
            compliance.AllowDirectQuote ? "direct_quote_allowed" : "direct_quote_restricted"
        };
        if (!compliance.AllowUserFacingExcerpt)
        {
            flags.Add("excerpt_omitted_by_policy");
            return new CitationQuoteDecision(
                null,
                false,
                false,
                compliance.AllowDirectQuote,
                compliance.Tier,
                compliance.Reason,
                flags);
        }

        var budget = _excerptBudgetPolicy.Apply(excerpt, compliance.MaxWords);
        if (!budget.Included || string.IsNullOrWhiteSpace(budget.Text))
        {
            flags.Add("excerpt_empty_after_budget");
            return new CitationQuoteDecision(
                null,
                false,
                false,
                compliance.AllowDirectQuote,
                compliance.Tier,
                "excerpt_empty_after_budget",
                flags);
        }

        if (budget.Truncated)
        {
            flags.Add("excerpt_clipped_by_policy");
        }

        return new CitationQuoteDecision(
            budget.Text,
            true,
            budget.Truncated,
            compliance.AllowDirectQuote,
            compliance.Tier,
            compliance.Reason,
            flags);
    }

    private static string NormalizeFlagToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unspecified";
        }

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(' ', '_');
    }
}

