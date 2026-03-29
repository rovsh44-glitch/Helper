using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed class WebEvidenceCache
{
    private readonly Dictionary<string, CacheItem> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public bool TryGet(string query, out CachedWebEvidenceSnapshot snapshot)
    {
        var key = NormalizeKey(query);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var item))
            {
                snapshot = new CachedWebEvidenceSnapshot(key, item.Result, item.StoredAtUtc, item.CategoryHint);
                return true;
            }
        }

        snapshot = default!;
        return false;
    }

    public void Set(string query, ResearchResult result, string? categoryHint = null)
    {
        Set(query, result, DateTimeOffset.UtcNow, categoryHint);
    }

    internal void Set(string query, ResearchResult result, DateTimeOffset storedAtUtc, string? categoryHint = null)
    {
        var key = NormalizeKey(query);
        lock (_lock)
        {
            _entries[key] = new CacheItem(result, storedAtUtc, NormalizeCategory(categoryHint));
            TrimIfNeeded();
        }
    }

    private void TrimIfNeeded()
    {
        if (_entries.Count <= 128)
        {
            return;
        }

        var oldest = _entries
            .OrderBy(static pair => pair.Value.StoredAtUtc)
            .Take(16)
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (var key in oldest)
        {
            _entries.Remove(key);
        }
    }

    private static string NormalizeKey(string query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(trimmed.Length);
        var previousWhitespace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (previousWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWhitespace = true;
                continue;
            }

            builder.Append(ch);
            previousWhitespace = false;
        }

        return builder.ToString();
    }

    private static string? NormalizeCategory(string? categoryHint)
    {
        return string.IsNullOrWhiteSpace(categoryHint)
            ? null
            : categoryHint.Trim().ToLowerInvariant();
    }

    private sealed record CacheItem(ResearchResult Result, DateTimeOffset StoredAtUtc, string? CategoryHint);
}

public sealed record CachedWebEvidenceSnapshot(
    string QueryKey,
    ResearchResult Result,
    DateTimeOffset StoredAtUtc,
    string? CategoryHint);

