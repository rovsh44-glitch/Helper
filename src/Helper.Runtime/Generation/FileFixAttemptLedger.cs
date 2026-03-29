using System.Text.Json;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class FileFixAttemptLedger : IFixAttemptLedger
{
    private readonly string _root;

    public FileFixAttemptLedger(string? root = null)
    {
        _root = string.IsNullOrWhiteSpace(root)
            ? HelperWorkspacePathResolver.ResolveLogsPath(Path.Combine("fix_attempts"))
            : Path.GetFullPath(root);
    }

    public async Task<string?> RecordAsync(
        string runId,
        FixAttemptRecord attempt,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var safeRunId = SanitizeFileName(runId);
        var runDir = Path.Combine(_root, safeRunId);
        Directory.CreateDirectory(runDir);

        var strategy = attempt.Strategy.ToString().ToLowerInvariant();
        var fileName = $"attempt_{attempt.Attempt:00}_{strategy}.json";
        var path = Path.Combine(runDir, fileName);
        var json = JsonSerializer.Serialize(attempt, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}

