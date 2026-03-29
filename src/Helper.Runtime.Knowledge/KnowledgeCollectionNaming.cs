namespace Helper.Runtime.Knowledge;

public static class KnowledgeCollectionNaming
{
    public const string HistoricalEncyclopediasDomain = "historical_encyclopedias";
    private const string HistoricalEncyclopediaMarker = "большая советская энциклопедия";
    private static readonly string[] ReferenceWorkMarkers =
    {
        "encyclopedia",
        "dictionary",
        "atlas",
        "handbook",
        "lexicon",
        "энциклопед",
        "словар",
        "атлас"
    };

    public static string NormalizePipelineVersion(string? pipelineVersion)
        => string.Equals(pipelineVersion, "v2", StringComparison.OrdinalIgnoreCase) ? "v2" : "v1";

    public static string ResolveDomain(string? domain, string? sourcePath = null, string? title = null)
    {
        if (IsHistoricalEncyclopediaSource(sourcePath, title))
        {
            return HistoricalEncyclopediasDomain;
        }

        return KnowledgeDomainCatalog.Normalize(domain);
    }

    public static string BuildCollectionName(string domain, string? pipelineVersion)
    {
        var normalizedDomain = KnowledgeDomainCatalog.Normalize(domain);
        var normalizedVersion = NormalizePipelineVersion(pipelineVersion);
        return normalizedVersion == "v2"
            ? $"knowledge_{normalizedDomain}_v2"
            : $"knowledge_{normalizedDomain}";
    }

    public static string ResolveCollectionName(string? domain, string? pipelineVersion, string? sourcePath = null, string? title = null)
        => BuildCollectionName(ResolveDomain(domain, sourcePath, title), pipelineVersion);

    public static bool IsEncyclopediaLikeDomain(string? domain)
    {
        var normalizedDomain = KnowledgeDomainCatalog.Normalize(domain);
        return normalizedDomain is "encyclopedias" or HistoricalEncyclopediasDomain;
    }

    public static bool IsHistoricalArchiveCollection(string? collection)
        => !string.IsNullOrWhiteSpace(collection) &&
           collection.StartsWith($"knowledge_{HistoricalEncyclopediasDomain}", StringComparison.OrdinalIgnoreCase);

    public static bool IsDefaultRetrievalExcludedCollection(string? collection)
        => IsHistoricalArchiveCollection(collection);

    public static bool IsHistoricalEncyclopediaSource(string? sourcePath, string? title)
    {
        return ContainsHistoricalEncyclopediaMarker(sourcePath) || ContainsHistoricalEncyclopediaMarker(title);
    }

    public static bool IsReferenceLikeSource(string? sourcePath, string? title)
    {
        return ContainsReferenceWorkMarker(sourcePath) || ContainsReferenceWorkMarker(title);
    }

    private static bool ContainsHistoricalEncyclopediaMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(HistoricalEncyclopediaMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReferenceWorkMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ReferenceWorkMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

