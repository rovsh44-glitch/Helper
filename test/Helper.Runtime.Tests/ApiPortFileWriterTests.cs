using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ApiPortFileWriterTests
{
    [Fact]
    public void BuildCandidatePaths_PrefersLogsRoot_ThenDataRoot_ThenRootFallbacks()
    {
        var runtimeConfig = new ApiRuntimeConfig(
            RootPath: @"C:\helper-root",
            DataRoot: @"D:\helper-data",
            ProjectsRoot: @"D:\helper-data\PROJECTS",
            LibraryRoot: @"D:\helper-data\library",
            LogsRoot: @"D:\helper-data\LOG",
            TemplatesRoot: @"D:\helper-data\library\forge_templates",
            ApiKey: "test-key");

        var candidates = ApiPortFileWriter.BuildCandidatePaths(runtimeConfig);

        Assert.Equal(Path.GetFullPath(@"D:\helper-data\LOG\API_PORT.txt"), candidates[0]);
        Assert.Equal(Path.GetFullPath(@"D:\helper-data\runtime\API_PORT.txt"), candidates[1]);
        Assert.Equal(Path.GetFullPath(@"C:\helper-root\temp\runtime\API_PORT.txt"), candidates[2]);
        Assert.Contains(candidates, path => path.EndsWith(Path.Combine("HELPER", "runtime", "API_PORT.txt"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildCandidatePaths_DeduplicatesEquivalentFallbacks()
    {
        var runtimeConfig = new ApiRuntimeConfig(
            RootPath: @"C:\helper-root",
            DataRoot: @"C:\helper-root",
            ProjectsRoot: @"C:\helper-root\PROJECTS",
            LibraryRoot: @"C:\helper-root\library",
            LogsRoot: @"C:\helper-root\LOG",
            TemplatesRoot: @"C:\helper-root\library\forge_templates",
            ApiKey: "test-key");

        var candidates = ApiPortFileWriter.BuildCandidatePaths(runtimeConfig);

        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TryWrite_UsesFallbackPath_WhenPrimaryLocationFails()
    {
        var runtimeConfig = new ApiRuntimeConfig(
            RootPath: @"C:\helper-root",
            DataRoot: @"D:\helper-data",
            ProjectsRoot: @"D:\helper-data\PROJECTS",
            LibraryRoot: @"D:\helper-data\library",
            LogsRoot: @"D:\helper-data\LOG",
            TemplatesRoot: @"D:\helper-data\library\forge_templates",
            ApiKey: "test-key");

        var attempts = new List<string>();
        var result = ApiPortFileWriter.TryWrite(
            runtimeConfig,
            5239,
            (path, _) =>
            {
                attempts.Add(path);
                return attempts.Count == 1
                    ? new UnauthorizedAccessException("Access denied.")
                    : null;
            });

        Assert.True(result.Succeeded);
        Assert.True(result.UsedFallback);
        Assert.Equal(attempts[1], result.WrittenPath);
        Assert.Contains("fallback path", result.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryWrite_ReturnsAggregatedDiagnostic_WhenAllCandidatesFail()
    {
        var runtimeConfig = new ApiRuntimeConfig(
            RootPath: @"C:\helper-root",
            DataRoot: @"D:\helper-data",
            ProjectsRoot: @"D:\helper-data\PROJECTS",
            LibraryRoot: @"D:\helper-data\library",
            LogsRoot: @"D:\helper-data\LOG",
            TemplatesRoot: @"D:\helper-data\library\forge_templates",
            ApiKey: "test-key");

        var result = ApiPortFileWriter.TryWrite(
            runtimeConfig,
            5239,
            static (path, _) => new UnauthorizedAccessException($"Denied: {path}"));

        Assert.False(result.Succeeded);
        Assert.False(result.UsedFallback);
        Assert.Null(result.WrittenPath);
        Assert.Contains("Denied:", result.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API_PORT.txt", result.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
    }
}

