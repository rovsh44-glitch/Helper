using Helper.Runtime.Infrastructure;

namespace Helper.Api.Hosting;

internal static class WorkspacePathAccess
{
    internal static string ResolveProjectRoot(string projectPath, Helper.Runtime.Core.IFileSystemGuard guard)
    {
        var normalized = Path.GetFullPath(projectPath);
        guard.EnsureSafePath(normalized);

        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException($"Workspace root not found: {normalized}");
        }

        return normalized;
    }

    internal static string ResolveUnderProject(string projectRoot, string? relativePath)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot);
        var trimmedRelative = string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim();

        if (trimmedRelative.Contains("..", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Workspace path traversal is not allowed.");
        }

        var combined = string.IsNullOrWhiteSpace(trimmedRelative)
            ? normalizedRoot
            : Path.GetFullPath(Path.Combine(normalizedRoot, trimmedRelative));

        if (!HelperWorkspacePathResolver.IsPathUnderRoot(combined, normalizedRoot))
        {
            throw new UnauthorizedAccessException("Workspace path escapes the selected project root.");
        }

        return combined;
    }

    internal static string GetRelativePath(string projectRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(projectRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? string.Empty : relative;
    }
}

