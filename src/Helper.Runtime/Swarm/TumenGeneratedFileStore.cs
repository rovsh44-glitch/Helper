using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Swarm;

internal sealed class TumenGeneratedFileStore
{
    private static readonly HashSet<string> SourceFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".js",
        ".cjs",
        ".mjs",
        ".ts",
        ".tsx",
        ".py",
        ".json"
    };

    private readonly IIdentifierSanitizer _identifierSanitizer;
    private readonly IUsingInferenceService _usingInference;

    public TumenGeneratedFileStore(
        IIdentifierSanitizer identifierSanitizer,
        IUsingInferenceService usingInference)
    {
        _identifierSanitizer = identifierSanitizer;
        _usingInference = usingInference;
    }

    public List<string> BuildUsings(
        string rootNamespace,
        string relativePath,
        FileRole role,
        IReadOnlyList<ArbanMethodTask> methods)
    {
        return _usingInference.InferUsings(rootNamespace, relativePath, role, methods).ToList();
    }

    public string ResolveClassName(string safeRelativePath)
    {
        if (safeRelativePath.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
        {
            var xamlCodeBehind = Path.GetFileName(safeRelativePath);
            var stem = xamlCodeBehind[..^".xaml.cs".Length];
            return _identifierSanitizer.SanitizeTypeName(stem);
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(safeRelativePath);
        return _identifierSanitizer.SanitizeTypeName(fileNameWithoutExtension);
    }

    public void CopyValidatedProject(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (directory.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, targetFile, overwrite: true);
        }
    }

    public void SaveGeneratedFile(string baseDir, string relPath, string content)
    {
        var root = Path.GetFullPath(baseDir);
        var path = Path.GetFullPath(Path.Combine(root, relPath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsafe generation path blocked: {relPath}");
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (IsSourceFile(path) && string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Zero-byte source invariant violated for '{relPath}'.");
        }

        File.WriteAllText(path, content);
    }

    private static bool IsSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return SourceFileExtensions.Contains(extension);
    }
}

