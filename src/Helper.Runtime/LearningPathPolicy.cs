using System;
using System.Collections.Generic;
using System.IO;

namespace Helper.Runtime.Infrastructure
{
    public interface ILearningPathPolicy
    {
        string HelperRoot { get; }
        string QueuePath { get; }
        string LibraryRoot { get; }
        string LibraryDocsRoot { get; }
        string ActiveLearningOutputPath { get; }
        bool ActiveLearningGenerationEnabled { get; }
        IReadOnlySet<string> IndexedDocumentExtensions { get; }
        IReadOnlySet<string> ExcludedDocumentPaths { get; }

        string? CanonicalizeTargetFile(string? filePath);
        Dictionary<string, string> NormalizeQueueEntries(IReadOnlyDictionary<string, string> queue, out bool changed);
    }

    public sealed class LearningPathPolicy : ILearningPathPolicy
    {
        private static readonly HashSet<string> DefaultIndexedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".epub",
            ".html",
            ".htm",
            ".docx",
            ".fb2",
            ".md",
            ".markdown",
            ".djvu",
            ".chm",
            ".zim"
        };

        public LearningPathPolicy()
        {
            HelperRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
            QueuePath = HelperWorkspacePathResolver.ResolveIndexingQueuePath(helperRoot: HelperRoot);
            LibraryRoot = HelperWorkspacePathResolver.ResolveLibraryRoot(helperRoot: HelperRoot);
            LibraryDocsRoot = HelperWorkspacePathResolver.ResolveLibraryDocsRoot(helperRoot: HelperRoot);
            ActiveLearningGenerationEnabled = ReadFlag("HELPER_ENABLE_ACTIVE_LEARNING_GENERATION", false);
            ActiveLearningOutputPath = ResolveDefaultActiveLearningOutputPath(HelperRoot);
            IndexedDocumentExtensions = ResolveIndexedDocumentExtensions();
            ExcludedDocumentPaths = ResolveExcludedDocumentPaths(LibraryRoot, HelperRoot);
        }

        public LearningPathPolicy(
            string helperRoot,
            string queuePath,
            string libraryRoot,
            string libraryDocsRoot,
            string activeLearningOutputPath,
            bool activeLearningGenerationEnabled,
            IEnumerable<string>? indexedDocumentExtensions = null,
            IEnumerable<string>? excludedDocumentPaths = null)
        {
            HelperRoot = Path.GetFullPath(helperRoot);
            QueuePath = Path.GetFullPath(queuePath);
            LibraryRoot = Path.GetFullPath(libraryRoot);
            LibraryDocsRoot = Path.GetFullPath(libraryDocsRoot);
            ActiveLearningOutputPath = Path.GetFullPath(activeLearningOutputPath);
            ActiveLearningGenerationEnabled = activeLearningGenerationEnabled;
            IndexedDocumentExtensions = new HashSet<string>(
                indexedDocumentExtensions ?? DefaultIndexedDocumentExtensions,
                StringComparer.OrdinalIgnoreCase);
            ExcludedDocumentPaths = new HashSet<string>(
                excludedDocumentPaths ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public string HelperRoot { get; }
        public string QueuePath { get; }
        public string LibraryRoot { get; }
        public string LibraryDocsRoot { get; }
        public string ActiveLearningOutputPath { get; }
        public bool ActiveLearningGenerationEnabled { get; }
        public IReadOnlySet<string> IndexedDocumentExtensions { get; }
        public IReadOnlySet<string> ExcludedDocumentPaths { get; }

        public string? CanonicalizeTargetFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            return HelperWorkspacePathResolver.CanonicalizeLibraryPath(filePath, LibraryRoot, HelperRoot);
        }

        public Dictionary<string, string> NormalizeQueueEntries(IReadOnlyDictionary<string, string> queue, out bool changed)
            => HelperWorkspacePathResolver.NormalizeLibraryQueue(queue, out changed, LibraryRoot, HelperRoot);

        public static string ResolveDefaultActiveLearningOutputPath(string? helperRoot = null)
        {
            var configured = Environment.GetEnvironmentVariable("HELPER_ACTIVE_LEARNING_OUTPUT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            var root = HelperWorkspacePathResolver.ResolveHelperRoot(helperRoot);
            return Path.Combine(root, "runtime", "active_learning");
        }

        private static bool ReadFlag(string envName, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(envName);
            return bool.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        private static IReadOnlySet<string> ResolveIndexedDocumentExtensions()
        {
            var extensions = new HashSet<string>(DefaultIndexedDocumentExtensions, StringComparer.OrdinalIgnoreCase);
            var rawExcluded = Environment.GetEnvironmentVariable("HELPER_INDEX_EXCLUDED_EXTENSIONS");
            if (string.IsNullOrWhiteSpace(rawExcluded))
            {
                return extensions;
            }

            foreach (var token in rawExcluded.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = token.StartsWith(".", StringComparison.Ordinal) ? token : "." + token;
                extensions.Remove(normalized);
            }

            return extensions;
        }

        private static IReadOnlySet<string> ResolveExcludedDocumentPaths(string libraryRoot, string helperRoot)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawExcluded = Environment.GetEnvironmentVariable("HELPER_INDEX_EXCLUDED_FILES");
            if (string.IsNullOrWhiteSpace(rawExcluded))
            {
                return excluded;
            }

            foreach (var token in rawExcluded.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = HelperWorkspacePathResolver.CanonicalizeLibraryPath(token, libraryRoot, helperRoot);
                excluded.Add(normalized);
            }

            return excluded;
        }
    }
}

