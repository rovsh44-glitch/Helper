using System.Text.Json;

namespace Helper.RuntimeSlice.Api.Services;

internal sealed class FixtureFileStore
{
    private readonly RuntimeSliceOptions _options;

    public FixtureFileStore(RuntimeSliceOptions options)
    {
        _options = options;
    }

    public T ReadJson<T>(params string[] segments)
    {
        var fullPath = ResolvePath(segments);
        var content = File.ReadAllText(fullPath);
        FixtureSecurityGuard.ValidateText(fullPath, content);
        return JsonSerializer.Deserialize<T>(content, RuntimeSliceJson.Options)
            ?? throw new InvalidOperationException($"Fixture '{fullPath}' could not be deserialized.");
    }

    public IReadOnlyList<T> ReadJsonLines<T>(params string[] segments)
    {
        var fullPath = ResolvePath(segments);
        var lines = File.ReadAllLines(fullPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        foreach (var line in lines)
        {
            FixtureSecurityGuard.ValidateText(fullPath, line);
        }

        return lines
            .Select(line => JsonSerializer.Deserialize<T>(line, RuntimeSliceJson.Options)
                ?? throw new InvalidOperationException($"Fixture line in '{fullPath}' could not be deserialized."))
            .ToArray();
    }

    public string ResolvePath(params string[] segments)
    {
        var fullPath = Path.Combine(new[] { _options.FixtureRoot }.Concat(segments).ToArray());
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"Required fixture path is missing: {fullPath}");
        }

        return fullPath;
    }

    public string RelativeToRepo(string fullPath)
    {
        return Path.GetRelativePath(_options.RepoRoot, fullPath).Replace('\\', '/');
    }
}
