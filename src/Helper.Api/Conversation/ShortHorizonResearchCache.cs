using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public interface IShortHorizonResearchCache
{
    bool TryGet(string query, out ResearchResult result);
    bool TryGetSnapshot(string query, out CachedWebEvidenceSnapshot snapshot);
    void Set(string query, ResearchResult result, string? categoryHint = null);
}

public sealed class ShortHorizonResearchCache : IShortHorizonResearchCache
{
    private readonly WebEvidenceCache _cache;

    public ShortHorizonResearchCache()
        : this(new WebEvidenceCache())
    {
    }

    internal ShortHorizonResearchCache(WebEvidenceCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(string query, out ResearchResult result)
    {
        if (TryGetSnapshot(query, out var snapshot))
        {
            result = snapshot.Result;
            return true;
        }

        result = default!;
        return false;
    }

    public bool TryGetSnapshot(string query, out CachedWebEvidenceSnapshot snapshot)
    {
        return _cache.TryGet(query, out snapshot);
    }

    public void Set(string query, ResearchResult result, string? categoryHint = null)
    {
        _cache.Set(query, result, categoryHint);
    }

    internal void Seed(string query, ResearchResult result, DateTimeOffset storedAtUtc, string? categoryHint = null)
    {
        _cache.Set(query, result, storedAtUtc, categoryHint);
    }
}

