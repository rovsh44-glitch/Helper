namespace Helper.Runtime.WebResearch.Ranking;

internal static class WebDocumentRerankingSelectionPolicy
{
    public static IReadOnlyList<WebRerankedDocumentCandidate> SelectTop(
        IReadOnlyList<WebRerankedDocumentCandidate> scored,
        int limit,
        WebDocumentRerankerProfile profile)
    {
        if (scored.Count == 0 || limit <= 0)
        {
            return Array.Empty<WebRerankedDocumentCandidate>();
        }

        if (!profile.PreferSourceDiversity)
        {
            return scored.Take(limit).ToArray();
        }

        var selected = new List<WebRerankedDocumentCandidate>(Math.Min(limit, scored.Count));
        var selectedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perHostCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hostGroups = scored
            .GroupBy(static candidate => GetHost(candidate.Candidate.Document.Url), StringComparer.OrdinalIgnoreCase)
            .Select(group => new HostBucket(
                group.Key,
                group.OrderByDescending(static candidate => candidate.Score)
                    .ThenByDescending(static candidate => candidate.Candidate.FinalScore)
                    .ThenBy(static candidate => candidate.OriginalOrder)
                    .ToList(),
                group.Max(static candidate => candidate.Score),
                group.Min(static candidate => candidate.OriginalOrder)))
            .OrderByDescending(static bucket => bucket.Score)
            .ThenBy(static bucket => bucket.OriginalOrder)
            .ToList();

        var targetDistinctHosts = Math.Min(
            Math.Min(limit, profile.TargetDistinctHosts),
            hostGroups.Count);

        for (var index = 0; index < targetDistinctHosts; index++)
        {
            Add(hostGroups[index].Items[0]);
        }

        foreach (var candidate in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            var host = GetHost(candidate.Candidate.Document.Url);
            if (selectedUrls.Contains(candidate.Candidate.Document.Url) ||
                perHostCounts.GetValueOrDefault(host) >= profile.MaxPerHost)
            {
                continue;
            }

            Add(candidate);
        }

        return selected;

        void Add(WebRerankedDocumentCandidate candidate)
        {
            var host = GetHost(candidate.Candidate.Document.Url);
            selected.Add(candidate);
            selectedUrls.Add(candidate.Candidate.Document.Url);
            perHostCounts[host] = perHostCounts.TryGetValue(host, out var current) ? current + 1 : 1;
        }
    }

    private static string GetHost(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : "unknown";
    }

    private sealed record HostBucket(
        string Host,
        IReadOnlyList<WebRerankedDocumentCandidate> Items,
        double Score,
        int OriginalOrder);
}

