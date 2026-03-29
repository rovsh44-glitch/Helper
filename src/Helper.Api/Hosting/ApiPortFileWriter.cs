namespace Helper.Api.Hosting;

internal sealed record ApiPortFileWriteResult(
    bool Succeeded,
    bool UsedFallback,
    string? WrittenPath,
    string DiagnosticMessage);

internal static class ApiPortFileWriter
{
    private const string PortFileName = "API_PORT.txt";

    public static ApiPortFileWriteResult TryWrite(ApiRuntimeConfig runtimeConfig, int port)
        => TryWrite(runtimeConfig, port, TryWritePortFile);

    internal static ApiPortFileWriteResult TryWrite(
        ApiRuntimeConfig runtimeConfig,
        int port,
        Func<string, int, Exception?> writeAttempt)
    {
        var candidatePaths = BuildCandidatePaths(runtimeConfig);
        var failures = new List<string>(candidatePaths.Count);
        for (var index = 0; index < candidatePaths.Count; index++)
        {
            var candidatePath = candidatePaths[index];
            var writeError = writeAttempt(candidatePath, port);
            if (writeError is null)
            {
                return new ApiPortFileWriteResult(
                    Succeeded: true,
                    UsedFallback: index > 0,
                    WrittenPath: candidatePath,
                    DiagnosticMessage: index > 0
                        ? $"Configured logs root unavailable; fallback path '{candidatePath}' used."
                        : $"API_PORT.txt written to '{candidatePath}'.");
            }

            failures.Add($"{candidatePath} => {writeError.Message}");
        }

        return new ApiPortFileWriteResult(
            Succeeded: false,
            UsedFallback: false,
            WrittenPath: null,
            DiagnosticMessage: string.Join(" | ", failures));
    }

    internal static IReadOnlyList<string> BuildCandidatePaths(ApiRuntimeConfig runtimeConfig)
    {
        var candidates = new List<string>(capacity: 4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(candidates, seen, Path.Combine(runtimeConfig.LogsRoot, PortFileName));
        AddCandidate(candidates, seen, Path.Combine(runtimeConfig.DataRoot, "runtime", PortFileName));
        AddCandidate(candidates, seen, Path.Combine(runtimeConfig.RootPath, "temp", "runtime", PortFileName));
        AddCandidate(candidates, seen, Path.Combine(Path.GetTempPath(), "HELPER", "runtime", PortFileName));

        return candidates;
    }

    private static Exception? TryWritePortFile(string filePath, int port)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, port.ToString());
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void AddCandidate(
        ICollection<string> candidates,
        ISet<string> seen,
        string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (seen.Add(normalizedPath))
        {
            candidates.Add(normalizedPath);
        }
    }
}

