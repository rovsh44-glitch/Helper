using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record CitationProjectionEntry(
    int MatcherIndex,
    int SourceOrdinal,
    string CitationLabel,
    string EvidenceKind,
    string Url,
    string Title,
    string? PublishedAt,
    string? PassageId,
    int? PassageOrdinal,
    string MatcherText);

internal sealed record CitationProjection(
    IReadOnlyList<string> MatcherSources,
    IReadOnlyList<CitationProjectionEntry> Entries);

internal sealed record CitationProjectionReference(
    int SourceOrdinal,
    string CitationLabel,
    string EvidenceKind,
    string Url,
    string Title,
    string? PublishedAt,
    string? PassageId,
    int? PassageOrdinal);

internal interface ICitationProjectionService
{
    CitationProjection Build(IReadOnlyList<string> sources, IReadOnlyList<ResearchEvidenceItem>? evidenceItems);
    CitationProjectionReference? Resolve(CitationProjection projection, int matchSourceIndex);
    string FormatCitationLabel(ClaimGrounding claim);
}

internal sealed class CitationProjectionService : ICitationProjectionService
{
    public CitationProjection Build(IReadOnlyList<string> sources, IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        if (evidenceItems is { Count: > 0 })
        {
            var entries = new List<CitationProjectionEntry>();
            foreach (var item in evidenceItems.OrderBy(static item => item.Ordinal))
            {
                if (item.Passages is { Count: > 0 })
                {
                    foreach (var passage in item.Passages.OrderBy(static passage => passage.PassageOrdinal))
                    {
                        entries.Add(new CitationProjectionEntry(
                            MatcherIndex: entries.Count,
                            SourceOrdinal: item.Ordinal,
                            CitationLabel: passage.CitationLabel,
                            EvidenceKind: passage.EvidenceKind,
                            Url: passage.Url,
                            Title: passage.Title,
                            PublishedAt: passage.PublishedAt,
                            PassageId: passage.PassageId,
                            PassageOrdinal: passage.PassageOrdinal,
                            MatcherText: BuildMatcherText(item.Title, item.Url, item.PublishedAt, item.EvidenceKind, passage.Text)));
                    }

                    continue;
                }

                entries.Add(new CitationProjectionEntry(
                    MatcherIndex: entries.Count,
                    SourceOrdinal: item.Ordinal,
                    CitationLabel: item.Ordinal.ToString(),
                    EvidenceKind: item.EvidenceKind,
                    Url: item.Url,
                    Title: item.Title,
                    PublishedAt: item.PublishedAt,
                    PassageId: null,
                    PassageOrdinal: null,
                    MatcherText: BuildMatcherText(item.Title, item.Url, item.PublishedAt, item.EvidenceKind, item.Snippet)));
            }

            return new CitationProjection(entries.Select(static entry => entry.MatcherText).ToArray(), entries);
        }

        var fallbackEntries = sources
            .Select((source, index) => new CitationProjectionEntry(
                MatcherIndex: index,
                SourceOrdinal: index + 1,
                CitationLabel: (index + 1).ToString(),
                EvidenceKind: "source_url",
                Url: source,
                Title: source,
                PublishedAt: null,
                PassageId: null,
                PassageOrdinal: null,
                MatcherText: source))
            .ToArray();
        return new CitationProjection(fallbackEntries.Select(static entry => entry.MatcherText).ToArray(), fallbackEntries);
    }

    public CitationProjectionReference? Resolve(CitationProjection projection, int matchSourceIndex)
    {
        if (matchSourceIndex < 0 || matchSourceIndex >= projection.Entries.Count)
        {
            return null;
        }

        var entry = projection.Entries[matchSourceIndex];
        return new CitationProjectionReference(
            entry.SourceOrdinal,
            entry.CitationLabel,
            entry.EvidenceKind,
            entry.Url,
            entry.Title,
            entry.PublishedAt,
            entry.PassageId,
            entry.PassageOrdinal);
    }

    public string FormatCitationLabel(ClaimGrounding claim)
    {
        if (!string.IsNullOrWhiteSpace(claim.EvidenceCitationLabel))
        {
            return claim.EvidenceCitationLabel!;
        }

        return claim.SourceIndex?.ToString() ?? "?";
    }

    private static string BuildMatcherText(string title, string url, string? publishedAt, string evidenceKind, string text)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : url;
        return string.Join(
            " ",
            new[]
            {
                title,
                host,
                publishedAt ?? string.Empty,
                evidenceKind,
                text
            }.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }
}

