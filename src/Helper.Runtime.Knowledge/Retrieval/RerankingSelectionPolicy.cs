using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RerankingSelectionPolicy
{
    public static IReadOnlyList<KnowledgeChunk> SelectBalancedTop(IReadOnlyList<RerankingPolicy.ScoredCandidate> scored, int limit)
    {
        if (scored.Count == 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var selected = new List<KnowledgeChunk>(Math.Min(limit, scored.Count));
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perDocumentCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var documentGroups = scored
            .GroupBy(item => GetDocumentKey(item.Chunk), StringComparer.OrdinalIgnoreCase)
            .Select(group => new RerankingPolicy.DocumentBucket(
                group.Key,
                group.OrderByDescending(static item => item.Score).ThenBy(static item => item.OriginalOrder).ToList(),
                ComputeDocumentScore(group),
                group.Min(static item => item.OriginalOrder)))
            .OrderByDescending(static bucket => bucket.Score)
            .ThenBy(static bucket => bucket.OriginalOrder)
            .ToList();

        var targetDistinctDocuments = Math.Min(documentGroups.Count, Math.Max(1, Math.Min(limit, (limit + 1) / 2)));
        for (var index = 0; index < targetDistinctDocuments; index++)
        {
            AddCandidate(documentGroups[index].Items[0]);
        }

        if (selected.Count >= limit)
        {
            return selected;
        }

        var maxChunksPerDocument = Math.Max(1, Math.Min(3, limit <= 4 ? 2 : 3));
        foreach (var item in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            var documentKey = GetDocumentKey(item.Chunk);
            if (selectedIds.Contains(item.Chunk.Id) || perDocumentCounts.GetValueOrDefault(documentKey) >= maxChunksPerDocument)
            {
                continue;
            }

            AddCandidate(item);
        }

        if (selected.Count >= limit)
        {
            return selected;
        }

        foreach (var item in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (selectedIds.Contains(item.Chunk.Id))
            {
                continue;
            }

            AddCandidate(item);
        }

        return selected;

        void AddCandidate(RerankingPolicy.ScoredCandidate item)
        {
            var documentKey = GetDocumentKey(item.Chunk);
            selected.Add(item.Chunk);
            selectedIds.Add(item.Chunk.Id);
            perDocumentCounts[documentKey] = perDocumentCounts.TryGetValue(documentKey, out var current) ? current + 1 : 1;
        }
    }

    public static double ComputeDocumentScore(IEnumerable<RerankingPolicy.ScoredCandidate> group)
    {
        var ordered = group
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.OriginalOrder)
            .Take(3)
            .Select(static item => item.Score)
            .ToArray();

        if (ordered.Length == 0)
        {
            return 0d;
        }

        var aggregate = ordered[0];
        if (ordered.Length > 1)
        {
            aggregate += ordered[1] * 0.15;
        }

        if (ordered.Length > 2)
        {
            aggregate += ordered[2] * 0.05;
        }

        return aggregate;
    }

    public static string GetDocumentKey(KnowledgeChunk chunk)
    {
        return chunk.Metadata.GetValueOrDefault("document_id")
            ?? chunk.Metadata.GetValueOrDefault("source_path")
            ?? chunk.Metadata.GetValueOrDefault("title")
            ?? chunk.Id;
    }
}

