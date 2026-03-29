using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record ResearchEvidenceTierAssessment(
    string StrongestTier,
    bool AllowsFullGrounding,
    bool AllowsHardContradiction,
    bool RequiresGroundingCaution,
    IReadOnlyList<string> UncertaintyFlags);

internal interface IResearchEvidenceTierPolicy
{
    ResearchEvidenceTierAssessment Evaluate(IReadOnlyList<string> sources, IReadOnlyList<ResearchEvidenceItem>? evidenceItems);
}

internal sealed class ResearchEvidenceTierPolicy : IResearchEvidenceTierPolicy
{
    public ResearchEvidenceTierAssessment Evaluate(IReadOnlyList<string> sources, IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        if (evidenceItems is { Count: > 0 })
        {
            var strongestTier = ResolveStrongestTier(evidenceItems);
            return strongestTier switch
            {
                "verified_passage" => new ResearchEvidenceTierAssessment(
                    strongestTier,
                    AllowsFullGrounding: true,
                    AllowsHardContradiction: true,
                    RequiresGroundingCaution: false,
                    UncertaintyFlags: Array.Empty<string>()),
                "fetched_document_pdf" or "fetched_page" => new ResearchEvidenceTierAssessment(
                    strongestTier,
                    AllowsFullGrounding: true,
                    AllowsHardContradiction: true,
                    RequiresGroundingCaution: false,
                    UncertaintyFlags: Array.Empty<string>()),
                "search_hit" => new ResearchEvidenceTierAssessment(
                    strongestTier,
                    AllowsFullGrounding: false,
                    AllowsHardContradiction: false,
                    RequiresGroundingCaution: true,
                    UncertaintyFlags: new[] { "uncertainty.search_hit_only_evidence" }),
                _ => new ResearchEvidenceTierAssessment(
                    strongestTier,
                    AllowsFullGrounding: false,
                    AllowsHardContradiction: false,
                    RequiresGroundingCaution: true,
                    UncertaintyFlags: new[] { "uncertainty.source_url_only_evidence" })
            };
        }

        if (sources.Count > 0)
        {
            if (sources.All(LooksLikeBareUrl))
            {
                return new ResearchEvidenceTierAssessment(
                    "source_url",
                    AllowsFullGrounding: false,
                    AllowsHardContradiction: false,
                    RequiresGroundingCaution: true,
                    UncertaintyFlags: new[] { "uncertainty.source_url_only_evidence" });
            }

            return new ResearchEvidenceTierAssessment(
                "inline_source_text",
                AllowsFullGrounding: true,
                AllowsHardContradiction: true,
                RequiresGroundingCaution: false,
                UncertaintyFlags: Array.Empty<string>());
        }

        return new ResearchEvidenceTierAssessment(
            "none",
            AllowsFullGrounding: false,
            AllowsHardContradiction: false,
            RequiresGroundingCaution: false,
            UncertaintyFlags: Array.Empty<string>());
    }

    private static string ResolveStrongestTier(IReadOnlyList<ResearchEvidenceItem> evidenceItems)
    {
        if (evidenceItems.Any(static item => item.Passages is { Count: > 0 }))
        {
            return "verified_passage";
        }

        if (evidenceItems.Any(static item => string.Equals(item.EvidenceKind, "fetched_document_pdf", StringComparison.OrdinalIgnoreCase)))
        {
            return "fetched_document_pdf";
        }

        if (evidenceItems.Any(static item => string.Equals(item.EvidenceKind, "fetched_page", StringComparison.OrdinalIgnoreCase)))
        {
            return "fetched_page";
        }

        if (evidenceItems.Any(static item => string.Equals(item.EvidenceKind, "search_hit", StringComparison.OrdinalIgnoreCase)))
        {
            return "search_hit";
        }

        return "source_url";
    }

    private static bool LooksLikeBareUrl(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
               !source.Contains(' ');
    }
}

