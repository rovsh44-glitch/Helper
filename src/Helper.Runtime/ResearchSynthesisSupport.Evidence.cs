using System.Security.Cryptography;
using System.Text;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Infrastructure;

internal static partial class ResearchSynthesisSupport
{
    public static IReadOnlyList<ResearchEvidenceItem> BuildEvidenceItems(IReadOnlyList<WebSearchResult> webResults)
    {
        return webResults
            .Where(static result => !string.IsNullOrWhiteSpace(result.Url))
            .GroupBy(static result => result.Url.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(4)
            .Select(static (result, index) => new ResearchEvidenceItem(
                Ordinal: index + 1,
                Url: result.Url.Trim(),
                Title: string.IsNullOrWhiteSpace(result.Title) ? $"Source {index + 1}" : result.Title.Trim(),
                Snippet: NormalizeSnippet(result.Content),
                IsFallback: result.IsDeepScan,
                TrustLevel: UntrustedWebContentTrustLevel,
                EvidenceKind: "search_hit",
                SourceLayer: "web",
                SourceFormat: ResolveWebSourceFormat(result.Url, contentType: null),
                SourceId: BuildStableWebSourceId(result.Url),
                DisplayTitle: string.IsNullOrWhiteSpace(result.Title) ? $"Source {index + 1}" : result.Title.Trim(),
                FreshnessEligibility: "current_external",
                AllowedClaimRoles: new[] { "background", "current_external_fact", "regulatory_current_fact" }))
            .ToArray();
    }

    public static IReadOnlyList<ResearchEvidenceItem> BuildEvidenceItems(IReadOnlyList<WebSearchDocument> documents)
    {
        return documents
            .Where(static document => !string.IsNullOrWhiteSpace(document.Url))
            .GroupBy(static document => document.Url.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(4)
            .Select(static (document, index) => new ResearchEvidenceItem(
                Ordinal: index + 1,
                Url: document.ExtractedPage?.CanonicalUrl?.Trim() ?? document.Url.Trim(),
                Title: ResolveTitle(document, index + 1),
                Snippet: NormalizeSnippet(ResolveSnippet(document)),
                IsFallback: document.IsFallback,
                TrustLevel: document.ExtractedPage?.TrustLevel ?? UntrustedWebContentTrustLevel,
                WasSanitized: document.ExtractedPage?.WasSanitized ?? false,
                SafetyFlags: document.ExtractedPage?.SafetyFlags ?? Array.Empty<string>(),
                EvidenceKind: ResolveEvidenceKind(document),
                PublishedAt: document.ExtractedPage?.PublishedAt,
                Passages: BuildEvidencePassages(document, index + 1),
                SourceLayer: "web",
                SourceFormat: ResolveWebSourceFormat(document.ExtractedPage?.CanonicalUrl ?? document.Url, document.ExtractedPage?.ContentType),
                SourceId: BuildStableWebSourceId(document.ExtractedPage?.CanonicalUrl ?? document.Url),
                DisplayTitle: ResolveTitle(document, index + 1),
                FreshnessEligibility: "current_external",
                AllowedClaimRoles: new[] { "background", "current_external_fact", "regulatory_current_fact" }))
            .ToArray();
    }

    public static string BuildRawEvidence(IReadOnlyList<ResearchEvidenceItem> evidenceItems)
    {
        var builder = new StringBuilder();
        foreach (var item in evidenceItems)
        {
            builder.Append('[').Append(item.Ordinal).Append("] ")
                .Append(item.Title)
                .Append(" | ")
                .AppendLine(item.Url);
            builder.Append("Trust: ").Append(item.TrustLevel);
            builder.Append(" | kind=").Append(item.EvidenceKind);
            if (item.WasSanitized)
            {
                builder.Append(" | sanitized=yes");
            }
            if (!string.IsNullOrWhiteSpace(item.PublishedAt))
            {
                builder.Append(" | date=").Append(item.PublishedAt);
            }
            if (item.SafetyFlags is { Count: > 0 })
            {
                builder.Append(" | flags=").Append(string.Join(",", item.SafetyFlags));
            }
            builder.AppendLine();
            builder.AppendLine("BEGIN_UNTRUSTED_WEB_EVIDENCE");
            AppendEvidenceBody(builder, item);
            builder.AppendLine("END_UNTRUSTED_WEB_EVIDENCE");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string NormalizeSnippet(string? content)
    {
        var snippet = (content ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (snippet.Length > 260)
        {
            snippet = snippet[..260].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(snippet)
            ? "No excerpt was captured for this source."
            : snippet;
    }

    private static string ResolveTitle(WebSearchDocument document, int ordinal)
    {
        var title = document.ExtractedPage?.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return string.IsNullOrWhiteSpace(document.Title) ? $"Source {ordinal}" : document.Title.Trim();
    }

    private static string ResolveSnippet(WebSearchDocument document)
    {
        if (document.ExtractedPage is { Passages.Count: > 0 } extractedPage)
        {
            var passages = string.Join(" ", extractedPage.Passages.Take(2).Select(static passage => passage.Text.Trim()));
            if (!string.IsNullOrWhiteSpace(passages))
            {
                return passages;
            }
        }

        if (!string.IsNullOrWhiteSpace(document.ExtractedPage?.Body))
        {
            return document.ExtractedPage.Body;
        }

        return document.Snippet;
    }

    private static string ResolveEvidenceKind(WebSearchDocument document)
    {
        if (document.ExtractedPage is null)
        {
            return "search_hit";
        }

        return document.ExtractedPage.ContentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? "fetched_document_pdf"
            : "fetched_page";
    }

    private static IReadOnlyList<EvidencePassage>? BuildEvidencePassages(WebSearchDocument document, int evidenceOrdinal)
    {
        if (document.ExtractedPage is not { Passages.Count: > 0 } extractedPage)
        {
            return null;
        }

        return extractedPage.Passages
            .Select(passage => new EvidencePassage(
                PassageId: $"e{evidenceOrdinal}:p{passage.Ordinal}",
                EvidenceOrdinal: evidenceOrdinal,
                PassageOrdinal: passage.Ordinal,
                CitationLabel: $"{evidenceOrdinal}:p{passage.Ordinal}",
                Url: extractedPage.CanonicalUrl,
                Title: extractedPage.Title,
                PublishedAt: extractedPage.PublishedAt,
                Text: passage.Text,
                EvidenceKind: "verified_passage",
                TrustLevel: passage.TrustLevel,
                WasSanitized: passage.WasSanitized,
                SafetyFlags: passage.SafetyFlags ?? Array.Empty<string>()))
            .ToArray();
    }

    private static string ResolveWebSourceFormat(string? url, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf";
        }

        if (!string.IsNullOrWhiteSpace(url) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension is "htm" ? "html" : extension;
            }
        }

        return "html";
    }

    private static string BuildStableWebSourceId(string? url)
    {
        var value = string.IsNullOrWhiteSpace(url) ? Guid.Empty.ToString("N") : url.Trim().ToLowerInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static void AppendEvidenceBody(StringBuilder builder, ResearchEvidenceItem item)
    {
        if (item.Passages is { Count: > 0 })
        {
            foreach (var passage in item.Passages.Take(3))
            {
                builder.Append('[')
                    .Append('p')
                    .Append(passage.PassageOrdinal)
                    .Append("] ")
                    .AppendLine(passage.Text);
            }

            return;
        }

        builder.AppendLine(item.Snippet);
    }
}
