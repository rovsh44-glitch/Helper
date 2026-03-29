using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class FileSystemGuard : IFileSystemGuard
    {
        private readonly string _root;
        private readonly HashSet<string> _allowedRoots;

        public FileSystemGuard(string? root = null, IEnumerable<string>? extraAllowedRoots = null)
        {
            _root = string.IsNullOrWhiteSpace(root)
                ? HelperWorkspacePathResolver.ResolveHelperRoot()
                : Path.GetFullPath(root);
            _allowedRoots = BuildAllowedRoots(_root, extraAllowedRoots);
        }

        public string GetFullPath(string relativePath)
        {
            // Normalize path
            var combined = Path.GetFullPath(Path.Combine(_root, relativePath));
            
            EnsureSafePath(combined);
            return combined;
        }

        public void EnsureSafePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            
            // 1. Path Traversal Protection
            if (path.Contains(".."))
            {
                throw new UnauthorizedAccessException("[Security Guard] Path traversal attempt detected.");
            }

            // 2. Strict Root Check
            if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                var insideAllowedRoot = _allowedRoots.Any(allowedRoot =>
                    HelperWorkspacePathResolver.IsPathUnderRoot(fullPath, allowedRoot));
                if (!insideAllowedRoot)
                {
                    throw new UnauthorizedAccessException($"[Security Guard] Access denied: Path '{fullPath}' is outside the system root.");
                }
            }

            // 3. Subfolder Whitelist Check
            bool isAllowed = _allowedRoots.Any(allowedRoot =>
                HelperWorkspacePathResolver.IsPathUnderRoot(fullPath, allowedRoot));

            // Also allow certain root files specifically if needed (e.g. .sln)
            var fileName = Path.GetFileName(fullPath);
            bool isRootFile = fullPath.Equals(Path.Combine(_root, fileName), StringComparison.OrdinalIgnoreCase);
            
            if (isRootFile && (fileName.EndsWith(".sln") || fileName.EndsWith(".txt") || fileName.EndsWith(".md")))
            {
                isAllowed = true;
            }

            if (!isAllowed)
            {
                throw new UnauthorizedAccessException($"[Security Guard] Access denied: Folder or File type not in allow-list.");
            }
        }

        private static HashSet<string> BuildAllowedRoots(string root, IEnumerable<string>? extraAllowedRoots)
        {
            var helperRoot = HelperWorkspacePathResolver.DiscoverHelperRoot(root);
            var allowedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(helperRoot, "PROJECTS"),
                Path.Combine(helperRoot, "doc"),
                Path.Combine(helperRoot, "LOG"),
                Path.Combine(helperRoot, "logs"),
                Path.Combine(helperRoot, "sandbox"),
                Path.Combine(helperRoot, "library"),
                HelperWorkspacePathResolver.ResolveDataRoot(Environment.GetEnvironmentVariable("HELPER_DATA_ROOT"), helperRoot),
                HelperWorkspacePathResolver.ResolveProjectsRoot(helperRoot: helperRoot),
                HelperWorkspacePathResolver.ResolveLibraryRoot(helperRoot: helperRoot),
                HelperWorkspacePathResolver.ResolveLogsRoot(helperRoot: helperRoot),
                HelperWorkspacePathResolver.ResolveTemplatesRoot(helperRoot: helperRoot)
            };

            if (extraAllowedRoots != null)
            {
                foreach (var extraRoot in extraAllowedRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
                {
                    allowedRoots.Add(Path.GetFullPath(extraRoot));
                }
            }

            return allowedRoots;
        }
    }
}

