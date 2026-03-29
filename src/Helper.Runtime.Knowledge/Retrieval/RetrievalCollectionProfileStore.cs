using System.Collections.Concurrent;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal sealed class RetrievalCollectionProfileStore
{
    private const int CollectionProfileSampleSize = 48;
    private const int CollectionProfileTokenLimit = 96;

    private static readonly ConcurrentDictionary<string, CollectionProfile> CollectionProfiles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IVectorStore _store;
    private readonly IStructuredVectorStore _structuredStore;

    public RetrievalCollectionProfileStore(IVectorStore store, IStructuredVectorStore structuredStore)
    {
        _store = store;
        _structuredStore = structuredStore;
    }

    internal static void ResetForTesting() => CollectionProfiles.Clear();

    public async Task<CollectionProfile> GetAsync(string collection, CancellationToken ct)
    {
        var pointCount = await _structuredStore.GetCollectionPointCountAsync(collection, ct).ConfigureAwait(false) ?? 0;
        if (CollectionProfiles.TryGetValue(collection, out var cached) && cached.PointCount == pointCount)
        {
            return cached;
        }

        var tokenWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var rootWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var anchorTokenWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var anchorRootWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var domain = RetrievalCollectionRoutingPolicy.ExtractDomain(collection);

        AddProfileText(tokenWeights, rootWeights, domain.Replace('_', ' '), 3.2, 12);
        AddProfileText(anchorTokenWeights, anchorRootWeights, domain.Replace('_', ' '), 3.2, 12);
        AddProfileText(tokenWeights, rootWeights, collection, 2.8, 12);
        AddProfileText(anchorTokenWeights, anchorRootWeights, collection, 2.8, 12);
        foreach (var hint in RetrievalDomainProfileCatalog.GetRoutingHints(domain))
        {
            AddProfileText(tokenWeights, rootWeights, hint, 2.4, 4);
            AddProfileText(anchorTokenWeights, anchorRootWeights, hint, 2.4, 4);
        }

        var sampledChunks = await _store.ScrollMetadataAsync(collection, CollectionProfileSampleSize, null, ct).ConfigureAwait(false);
        foreach (var group in sampledChunks
                     .GroupBy(chunk => chunk.Metadata.GetValueOrDefault("document_id")
                                   ?? chunk.Metadata.GetValueOrDefault("source_path")
                                   ?? chunk.Metadata.GetValueOrDefault("title")
                                   ?? chunk.Id,
                         StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            var first = group.First();
            AddProfileText(tokenWeights, rootWeights, first.Metadata.GetValueOrDefault("title"), 2.2, 12);
            AddProfileText(anchorTokenWeights, anchorRootWeights, first.Metadata.GetValueOrDefault("title"), 2.2, 12);
            AddProfileText(tokenWeights, rootWeights, first.Metadata.GetValueOrDefault("source_path"), 1.9, 14);
            AddProfileText(anchorTokenWeights, anchorRootWeights, first.Metadata.GetValueOrDefault("source_path"), 1.9, 14);
            AddProfileText(tokenWeights, rootWeights, first.Metadata.GetValueOrDefault("section_path"), 1.6, 16);
            AddProfileText(anchorTokenWeights, anchorRootWeights, first.Metadata.GetValueOrDefault("section_path"), 1.6, 16);
            AddProfileText(tokenWeights, rootWeights, first.Content, ResolveProfileContentWeight(domain, collection), 20);
        }

        var profile = new CollectionProfile(
            collection,
            domain,
            pointCount,
            tokenWeights
                .OrderByDescending(static pair => pair.Value)
                .Take(CollectionProfileTokenLimit)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            rootWeights
                .OrderByDescending(static pair => pair.Value)
                .Take(CollectionProfileTokenLimit)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            anchorTokenWeights
                .OrderByDescending(static pair => pair.Value)
                .Take(CollectionProfileTokenLimit)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            anchorRootWeights
                .OrderByDescending(static pair => pair.Value)
                .Take(CollectionProfileTokenLimit)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase));

        PruneProfileNoise(profile);
        CollectionProfiles[collection] = profile;
        return profile;
    }

    private static void PruneProfileNoise(CollectionProfile profile)
    {
        if (!KnowledgeCollectionNaming.IsHistoricalArchiveCollection(profile.Collection))
        {
            return;
        }

        foreach (var token in RetrievalRoutingText.HistoricalArchiveGenericTokenSet)
        {
            profile.TokenWeights.Remove(token);
            profile.AnchorTokenWeights.Remove(token);
        }

        foreach (var root in RetrievalRoutingText.HistoricalArchiveGenericRootSet)
        {
            profile.RootWeights.Remove(root);
            profile.AnchorRootWeights.Remove(root);
        }
    }

    private static void AddProfileText(
        IDictionary<string, double> tokenWeights,
        IDictionary<string, double> rootWeights,
        string? text,
        double weight,
        int maxTokens)
    {
        var normalized = RetrievalRoutingText.NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        foreach (var token in RetrievalRoutingText.Tokenize(normalized).Take(maxTokens).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            tokenWeights[token] = tokenWeights.TryGetValue(token, out var currentTokenWeight)
                ? currentTokenWeight + weight
                : weight;

            var root = RetrievalRoutingText.BuildRoot(token);
            if (root.Length < 4)
            {
                continue;
            }

            rootWeights[root] = rootWeights.TryGetValue(root, out var currentRootWeight)
                ? currentRootWeight + (weight * 0.8)
                : (weight * 0.8);
        }
    }

    private static double ResolveProfileContentWeight(string domain, string collection)
    {
        if (KnowledgeCollectionNaming.IsHistoricalArchiveCollection(collection))
        {
            return 0.08;
        }

        return string.Equals(domain, "analysis_strategy", StringComparison.OrdinalIgnoreCase)
            ? 0.22
            : 0.65;
    }
}

