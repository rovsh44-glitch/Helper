using System.Text;
using System.Text.Json;

namespace Helper.Runtime.Generation;

public sealed record TemplateCertificationStatus(
    DateTimeOffset EvaluatedAtUtc,
    bool Passed,
    bool HasCriticalAlerts,
    IReadOnlyList<string> CriticalAlerts,
    string? ReportPath);

public static class TemplateCertificationStatusStore
{
    public const string StatusFileName = "certification_status.json";
    private static readonly HashSet<string> IgnoredSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        "node_modules",
        ".compile_gate",
        "__pycache__"
    };

    public static string GetStatusPath(string templateVersionRoot)
    {
        return Path.Combine(templateVersionRoot, StatusFileName);
    }

    public static async Task WriteAsync(string templateVersionRoot, TemplateCertificationStatus status, CancellationToken ct = default)
    {
        Directory.CreateDirectory(templateVersionRoot);
        var path = GetStatusPath(templateVersionRoot);
        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
    }

    public static TemplateCertificationStatus? TryRead(string templateVersionRoot)
    {
        var path = GetStatusPath(templateVersionRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TemplateCertificationStatus>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public static bool IsStale(string templateVersionRoot, TemplateCertificationStatus? status)
    {
        if (status is null || string.IsNullOrWhiteSpace(templateVersionRoot) || !Directory.Exists(templateVersionRoot))
        {
            return false;
        }

        var latestContentWriteUtc = GetLatestRelevantContentWriteUtc(templateVersionRoot);
        if (!latestContentWriteUtc.HasValue)
        {
            return false;
        }

        return latestContentWriteUtc.Value > status.EvaluatedAtUtc.UtcDateTime.AddSeconds(1);
    }

    private static DateTime? GetLatestRelevantContentWriteUtc(string templateVersionRoot)
    {
        DateTime? latest = null;
        foreach (var file in Directory.EnumerateFiles(templateVersionRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldIgnore(file, templateVersionRoot))
            {
                continue;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(file);
            if (!latest.HasValue || lastWriteUtc > latest.Value)
            {
                latest = lastWriteUtc;
            }
        }

        return latest;
    }

    private static bool ShouldIgnore(string filePath, string templateVersionRoot)
    {
        var relativePath = Path.GetRelativePath(templateVersionRoot, filePath);
        if (string.Equals(Path.GetFileName(relativePath), StatusFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (IgnoredSegments.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}

