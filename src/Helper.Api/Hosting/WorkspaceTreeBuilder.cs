namespace Helper.Api.Hosting;

internal static class WorkspaceTreeBuilder
{
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        "node_modules",
        "__pycache__",
        ".compile_gate"
    };

    internal static WorkspaceProjectDto BuildProject(string projectRoot)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        return new WorkspaceProjectDto(
            Name: Path.GetFileName(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FullPath: fullRoot,
            Root: BuildFolder(fullRoot, fullRoot));
    }

    private static WorkspaceFolderDto BuildFolder(string projectRoot, string currentFolder)
    {
        var files = Directory.EnumerateFiles(currentFolder, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new WorkspaceFileDto(
                Name: Path.GetFileName(path),
                Path: WorkspacePathAccess.GetRelativePath(projectRoot, path),
                Language: DetectLanguage(path)))
            .ToArray();

        var folders = Directory.EnumerateDirectories(currentFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !SkippedDirectoryNames.Contains(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildFolder(projectRoot, path))
            .ToArray();

        return new WorkspaceFolderDto(
            Name: Path.GetFileName(currentFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Path: WorkspacePathAccess.GetRelativePath(projectRoot, currentFolder),
            Files: files,
            Folders: folders);
    }

    private static string DetectLanguage(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ts" or ".tsx" => "typescript",
            ".py" => "python",
            ".swift" => "swift",
            ".kt" => "kotlin",
            ".json" => "json",
            ".xml" => "xml",
            ".xaml" => "xaml",
            ".cs" => "csharp",
            ".md" => "markdown",
            _ => "text"
        };
    }
}

