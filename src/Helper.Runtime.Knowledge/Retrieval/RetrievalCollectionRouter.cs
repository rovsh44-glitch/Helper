using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal sealed class RetrievalCollectionRouter
{
    private readonly IStructuredVectorStore _structuredStore;
    private readonly RetrievalCollectionProfileStore _profileStore;

    public RetrievalCollectionRouter(IStructuredVectorStore structuredStore, RetrievalCollectionProfileStore profileStore)
    {
        _structuredStore = structuredStore;
        _profileStore = profileStore;
    }

    public async Task<IReadOnlyList<CollectionRoute>> ResolveAsync(string query, string? domain, string pipelineVersion, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var preferred = KnowledgeCollectionNaming.BuildCollectionName(domain, pipelineVersion);
            var available = await _structuredStore.ListCollectionsAsync(null, ct).ConfigureAwait(false);
            if (available.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return new[] { new CollectionRoute(preferred, 10d, 10d, 1, 1, 1) };
            }

            if (pipelineVersion != "v2" || !ReadLegacyFallbackFlag())
            {
                return Array.Empty<CollectionRoute>();
            }

            var fallback = KnowledgeCollectionNaming.BuildCollectionName(domain, "v1");
            if (available.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            {
                return string.Equals(preferred, fallback, StringComparison.OrdinalIgnoreCase)
                    ? new[] { new CollectionRoute(preferred, 10d, 10d, 1, 1, 1) }
                    : new[] { new CollectionRoute(fallback, 10d, 10d, 1, 1, 1) };
            }

            return Array.Empty<CollectionRoute>();
        }

        var collections = await _structuredStore.ListCollectionsAsync("knowledge_", ct).ConfigureAwait(false);
        var routeCandidateCollections = collections
            .Where(name => pipelineVersion == "v2"
                ? name.EndsWith("_v2", StringComparison.OrdinalIgnoreCase)
                : !name.EndsWith("_v2", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pipelineVersion == "v2" && ReadLegacyFallbackFlag())
        {
            var legacyCollections = collections
                .Where(name => !name.EndsWith("_v2", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var collection in legacyCollections)
            {
                if (!routeCandidateCollections.Contains(collection, StringComparer.OrdinalIgnoreCase))
                {
                    routeCandidateCollections.Add(collection);
                }
            }
        }

        return await ResolveGlobalAsync(query, routeCandidateCollections, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CollectionRoute>> ResolveGlobalAsync(
        string query,
        IReadOnlyList<string> collections,
        CancellationToken ct)
    {
        if (collections.Count == 0)
        {
            return Array.Empty<CollectionRoute>();
        }

        var prepared = PreparedRoutingQuery.Create(query);
        if (prepared.Terms.Count == 0)
        {
            return collections
                .Select((collection, index) => new CollectionRoute(collection, 0d, 0d, 0, 0, index + 1))
                .ToList();
        }

        var scored = new List<CollectionRoute>(collections.Count);
        foreach (var collection in collections)
        {
            var profile = await _profileStore.GetAsync(collection, ct).ConfigureAwait(false);
            var score = RetrievalCollectionRoutingPolicy.ScoreCollection(prepared, profile);
            scored.Add(new CollectionRoute(collection, score.Score, score.AnchorScore, score.AnchorMatches, score.HintMatches, 0));
        }

        var included = scored
            .Where(route => !KnowledgeCollectionNaming.IsDefaultRetrievalExcludedCollection(route.Collection))
            .OrderByDescending(static route => route.Score)
            .ThenBy(static route => route.Collection, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var excluded = scored
            .Where(route => KnowledgeCollectionNaming.IsDefaultRetrievalExcludedCollection(route.Collection))
            .OrderByDescending(static route => route.Score)
            .ThenBy(static route => route.Collection, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topIncludedScore = included.Count == 0 ? 0d : included[0].Score;
        var topExcludedScore = excluded.Count == 0 ? 0d : excluded[0].Score;

        if (topIncludedScore < 1.25 && topExcludedScore < 1.5)
        {
            var fallback = included
                .Select((route, index) => route with { Rank = index + 1 })
                .ToList();

            foreach (var route in excluded.Where(route => RetrievalCollectionRoutingPolicy.HasStrongArchiveSignal(route, prepared)).Take(1))
            {
                fallback.Add(route with { Rank = fallback.Count + 1 });
            }

            return fallback;
        }

        var threshold = topIncludedScore switch
        {
            >= 8d => Math.Max(2.0, topIncludedScore * 0.38),
            >= 4d => Math.Max(1.1, topIncludedScore * 0.30),
            _ => 0.8
        };
        var maxCollections = topIncludedScore switch
        {
            >= 8d => 4,
            >= 4d => 6,
            _ => 8
        };

        var selected = included
            .Where(route => route.Score >= threshold)
            .Take(maxCollections)
            .ToList();

        var minimumCoverage = Math.Min(4, included.Count);
        foreach (var route in included)
        {
            if (selected.Count >= minimumCoverage)
            {
                break;
            }

            if (!selected.Contains(route))
            {
                selected.Add(route);
            }
        }

        if (excluded.Count > 0)
        {
            var includeExcludedThreshold = topIncludedScore switch
            {
                >= 8d => Math.Max(5.2, topIncludedScore * 0.95),
                >= 4d => Math.Max(3.4, topIncludedScore * 0.93),
                _ => 2.4
            };
            foreach (var route in excluded
                         .Where(route => route.Score >= includeExcludedThreshold || RetrievalCollectionRoutingPolicy.HasStrongArchiveSignal(route, prepared))
                         .Where(route => RetrievalCollectionRoutingPolicy.HasStrongArchiveSignal(route, prepared))
                         .Take(1))
            {
                selected.Add(route);
            }
        }

        return selected
            .DistinctBy(static route => route.Collection, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static route => route.Score)
            .ThenBy(static route => route.Collection, StringComparer.OrdinalIgnoreCase)
            .Select((route, index) => route with { Rank = index + 1 })
            .ToList();
    }

    private static bool ReadLegacyFallbackFlag()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_RAG_ALLOW_V1_FALLBACK");
        return bool.TryParse(raw, out var parsed) && parsed;
    }
}

