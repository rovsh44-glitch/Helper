using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal static class CompileWorkspaceSynchronizer
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".compile_gate"
    };

    public static async Task<IReadOnlyList<string>> SyncPatchedCodeFilesAsync(
        string compileWorkspace,
        string projectRoot,
        CancellationToken ct)
    {
        var changed = new List<string>();
        if (!Directory.Exists(compileWorkspace) || !Directory.Exists(projectRoot))
        {
            return changed;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(compileWorkspace, "*.cs", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (ShouldSkip(sourceFile, compileWorkspace))
            {
                continue;
            }

            var relative = Path.GetRelativePath(compileWorkspace, sourceFile);
            var targetFile = Path.Combine(projectRoot, relative);
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var sourceContent = await File.ReadAllTextAsync(sourceFile, ct);
            var targetContent = File.Exists(targetFile)
                ? await File.ReadAllTextAsync(targetFile, ct)
                : null;

            if (string.Equals(sourceContent, targetContent, StringComparison.Ordinal))
            {
                continue;
            }

            await File.WriteAllTextAsync(targetFile, sourceContent, ct);
            changed.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }

        return changed;
    }

    public static IReadOnlyList<GeneratedFile> SnapshotGeneratedFiles(string projectRoot)
    {
        var files = new List<GeneratedFile>();
        if (!Directory.Exists(projectRoot))
        {
            return files;
        }

        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(file, projectRoot))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (!IsTextual(extension))
            {
                continue;
            }

            var relative = Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            var content = File.ReadAllText(file);
            files.Add(new GeneratedFile(relative, content, ResolveLanguage(extension)));
        }

        return files;
    }

    private static bool ShouldSkip(string fullPath, string root)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        var normalizedRelative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        if (fullPath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = normalizedRelative.Split(
            new[] { Path.DirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (ExcludedDirectories.Contains(segment))
            {
                return true;
            }
        }

        return normalizedRelative.StartsWith(".compile_gate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextual(string extension)
    {
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".config", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".xaml" => "xaml",
            ".csproj" => "xml",
            ".xml" => "xml",
            ".json" => "json",
            ".md" => "markdown",
            _ => "text"
        };
    }
}

