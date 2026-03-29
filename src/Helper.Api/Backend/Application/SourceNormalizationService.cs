namespace Helper.Api.Backend.Application;

public sealed record NormalizedSource(
    string CanonicalId,
    string Source,
    double Score,
    bool HasPotentialContradiction);

public sealed record SourceNormalizationResult(
    IReadOnlyList<NormalizedSource> Sources,
    IReadOnlyList<string> Alerts);

public interface ISourceNormalizationService
{
    SourceNormalizationResult Normalize(IReadOnlyList<string> sources);
}

public sealed class SourceNormalizationService : ISourceNormalizationService
{
    public SourceNormalizationResult Normalize(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0)
        {
            return new SourceNormalizationResult(Array.Empty<NormalizedSource>(), Array.Empty<string>());
        }

        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var canonical = BuildCanonicalId(source);
            if (!normalized.TryGetValue(canonical, out var items))
            {
                items = new List<string>();
                normalized[canonical] = items;
            }

            items.Add(source.Trim());
        }

        var results = normalized
            .Select(pair => new NormalizedSource(
                CanonicalId: pair.Key,
                Source: pair.Value[0],
                Score: Score(pair.Value[0]),
                HasPotentialContradiction: pair.Value.Count > 1))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var alerts = results.Where(x => x.HasPotentialContradiction)
            .Select(x => $"source_contradiction:{x.CanonicalId}")
            .ToList();
        return new SourceNormalizationResult(results, alerts);
    }

    private static string BuildCanonicalId(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            return $"{uri.Host.ToLowerInvariant()}{path.ToLowerInvariant()}";
        }

        return source.Trim().ToLowerInvariant();
    }

    private static double Score(string source)
    {
        var score = 0.5;
        if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.2;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".org", StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.2;
        }

        if (!source.Contains("utm_", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        return Math.Min(1.0, score);
    }
}

