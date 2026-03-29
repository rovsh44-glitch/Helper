using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Normalization;

internal sealed record EventCluster(
    string ClusterKey,
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> Hosts);

internal sealed record EventClusterSet(
    IReadOnlyList<EventCluster> Clusters,
    IReadOnlyList<string> Trace);

internal interface IEventClusterBuilder
{
    EventClusterSet Build(IReadOnlyList<WebSearchDocument> documents);
}

internal sealed partial class EventClusterBuilder : IEventClusterBuilder
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "that", "this", "into", "about", "after", "before",
        "official", "report", "reports", "news", "update", "updates", "latest", "today", "current",
        "what", "when", "where", "how", "why", "guide", "guidance", "analysis",
        "это", "как", "что", "когда", "где", "после", "перед", "для", "при", "или", "новость",
        "новости", "обновление", "обновления", "сегодня", "текущий", "официальный", "отчет", "отчёт"
    };

    public EventClusterSet Build(IReadOnlyList<WebSearchDocument> documents)
    {
        if (documents.Count < 2)
        {
            return new EventClusterSet(Array.Empty<EventCluster>(), Array.Empty<string>());
        }

        var grouped = documents
            .Select(document => new
            {
                Document = document,
                Signature = BuildSignature(document)
            })
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Signature))
            .GroupBy(static entry => entry.Signature!, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(group =>
            {
                var docs = group.Select(static entry => entry.Document).ToArray();
                var hosts = docs
                    .Select(static document => TryGetHost(document.Url))
                    .Where(static host => !string.IsNullOrWhiteSpace(host))
                    .Select(static host => host!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new EventCluster(group.Key, docs, hosts);
            })
            .OrderByDescending(static cluster => cluster.Documents.Count)
            .ThenBy(static cluster => cluster.ClusterKey, StringComparer.Ordinal)
            .ToArray();

        if (grouped.Length == 0)
        {
            return new EventClusterSet(Array.Empty<EventCluster>(), Array.Empty<string>());
        }

        var trace = new List<string>
        {
            $"web_normalization.event_clusters={grouped.Length}"
        };

        for (var index = 0; index < grouped.Length; index++)
        {
            var cluster = grouped[index];
            trace.Add(
                $"web_normalization.event_cluster[{index + 1}] key={cluster.ClusterKey} size={cluster.Documents.Count} hosts={string.Join(",", cluster.Hosts)}");
        }

        return new EventClusterSet(grouped, trace);
    }

    private static string? BuildSignature(WebSearchDocument document)
    {
        var titleTokens = Tokenize(StripSourceSuffix(document.ExtractedPage?.Title ?? document.Title))
            .Where(static token => token.Length >= 4 && !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        if (titleTokens.Length < 3)
        {
            var snippetTokens = Tokenize(document.Snippet)
                .Where(static token => token.Length >= 4 && !StopWords.Contains(token))
                .Distinct(StringComparer.Ordinal)
                .Take(5 - titleTokens.Length);
            titleTokens = titleTokens.Concat(snippetTokens).Distinct(StringComparer.Ordinal).Take(5).ToArray();
        }

        if (titleTokens.Length < 3)
        {
            return null;
        }

        var datePrefix = document.ExtractedPage?.PublishedAt?.Trim();
        return string.IsNullOrWhiteSpace(datePrefix)
            ? string.Join("-", titleTokens)
            : $"{datePrefix}:{string.Join("-", titleTokens)}";
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return SpaceRegex()
            .Replace(NonWordRegex().Replace(value.ToLowerInvariant(), " "), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string StripSourceSuffix(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var separators = new[] { " | ", " - ", " — ", " – ", " :: " };
        foreach (var separator in separators)
        {
            var index = title.LastIndexOf(separator, StringComparison.Ordinal);
            if (index > 20)
            {
                return title[..index];
            }
        }

        return title;
    }

    private static string? TryGetHost(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

