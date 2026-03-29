namespace Helper.Runtime.Generation;

internal static class GenerationArtifactLocator
{
    internal static IReadOnlyList<string> EnumerateRunHistoryFiles(
        string workspaceRoot,
        GenerationArtifactDiscoveryMode discoveryMode = GenerationArtifactDiscoveryMode.Mixed)
        => EnumerateRunHistoryFiles(GenerationArtifactDiscoveryOptions.Resolve(workspaceRoot, discoveryMode));

    internal static IReadOnlyList<string> EnumerateRunHistoryFiles(GenerationArtifactDiscoveryOptions options)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in options.DirectRoots)
        {
            var rootRuns = Path.Combine(root, "generation_runs.jsonl");
            if (File.Exists(rootRuns))
            {
                files.Add(Path.GetFullPath(rootRuns));
            }
        }

        foreach (var root in options.RecursiveRoots)
        {
            var rootRuns = Path.Combine(root, "generation_runs.jsonl");
            if (File.Exists(rootRuns))
            {
                files.Add(Path.GetFullPath(rootRuns));
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "generation_runs.jsonl", SearchOption.AllDirectories))
            {
                files.Add(Path.GetFullPath(file));
            }
        }

        return files
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> EnumerateValidationReportFiles(
        string workspaceRoot,
        GenerationArtifactDiscoveryMode discoveryMode = GenerationArtifactDiscoveryMode.Mixed)
        => EnumerateValidationReportFiles(GenerationArtifactDiscoveryOptions.Resolve(workspaceRoot, discoveryMode));

    internal static IReadOnlyList<string> EnumerateValidationReportFiles(GenerationArtifactDiscoveryOptions options)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in options.RecursiveRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "validation_report.json", SearchOption.AllDirectories))
            {
                files.Add(Path.GetFullPath(file));
            }
        }

        return files
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

