using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class HelperWorkspacePathResolverTests
{
    [Fact]
    public void ResolveWritableProjectsRoot_DoesNotUseLegacyProjectsRoot_WhenFallbackIsDisabled()
    {
        using var temp = new TempDirectoryScope();
        var configuredRootAsFile = Path.Combine(temp.Path, "configured-projects-root.txt");
        File.WriteAllText(configuredRootAsFile, "not a directory");

        var resolved = HelperWorkspacePathResolver.ResolveWritableProjectsRoot(
            configuredRoot: configuredRootAsFile,
            helperRoot: temp.Path);

        Assert.Equal(configuredRootAsFile, resolved, StringComparer.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "PROJECTS")));
    }

    [Fact]
    public void ResolveWritableProjectsRoot_FallsBackToLegacyProjectsRoot_WhenConfiguredRootIsNotWritable()
    {
        using var temp = new TempDirectoryScope();
        var configuredRootAsFile = Path.Combine(temp.Path, "configured-projects-root.txt");
        File.WriteAllText(configuredRootAsFile, "not a directory");

        var resolved = HelperWorkspacePathResolver.ResolveWritableProjectsRoot(
            configuredRoot: configuredRootAsFile,
            helperRoot: temp.Path,
            allowLegacyFallback: true);

        Assert.Equal(Path.Combine(temp.Path, "PROJECTS"), resolved, StringComparer.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void ResolveProjectRunLogPath_UsesAncestorProjectsRoot_ForLegacyFallbackProject()
    {
        using var temp = new TempDirectoryScope();
        var projectRoot = Path.Combine(temp.Path, "PROJECTS", "FORGE_OUTPUT", "Template_PdfEpubConverter_demo");
        Directory.CreateDirectory(projectRoot);

        var runsPath = HelperWorkspacePathResolver.ResolveProjectRunLogPath(projectRoot);

        Assert.Equal(
            Path.Combine(temp.Path, "PROJECTS", "generation_runs.jsonl"),
            runsPath,
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_path_resolver_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}

