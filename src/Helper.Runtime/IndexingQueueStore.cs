using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Infrastructure
{
    public interface IIndexingQueueStore
    {
        Task SyncWithLibraryAsync(CancellationToken ct = default);
        Task<Dictionary<string, string>> LoadAsync(CancellationToken ct = default);
        Task SaveAsync(Dictionary<string, string> queue, CancellationToken ct = default);
        Task UpdateStatusAsync(string filePath, string status, CancellationToken ct = default);
        Task DeleteAsync(CancellationToken ct = default);
        Task RecoverStuckFilesAsync(CancellationToken ct = default);
    }

    public sealed class IndexingQueueStore : IIndexingQueueStore
    {
        private readonly ILearningPathPolicy _pathPolicy;
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        public IndexingQueueStore(ILearningPathPolicy pathPolicy)
        {
            _pathPolicy = pathPolicy;
        }

        public async Task SyncWithLibraryAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(_pathPolicy.LibraryDocsRoot))
            {
                Console.WriteLine($"[Learning] Library docs root not found: {_pathPolicy.LibraryDocsRoot}");
                return;
            }

            var files = Directory.GetFiles(_pathPolicy.LibraryDocsRoot, "*.*", SearchOption.AllDirectories)
                .Select(file => HelperWorkspacePathResolver.CanonicalizeLibraryPath(file, _pathPolicy.LibraryRoot, _pathPolicy.HelperRoot))
                .Where(file => _pathPolicy.IndexedDocumentExtensions.Contains(Path.GetExtension(file)))
                .Where(file => !_pathPolicy.ExcludedDocumentPaths.Contains(file))
                .ToList();
            var knownFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

            await _fileLock.WaitAsync(ct);
            try
            {
                var queue = await ReadQueueUnsafeAsync();
                queue = _pathPolicy.NormalizeQueueEntries(queue, out var changed);

                foreach (var file in files)
                {
                    if (!queue.ContainsKey(file))
                    {
                        queue[file] = LearningQueueStatus.Pending;
                        changed = true;
                    }
                }

                foreach (var staleFile in queue.Keys
                    .Where(path => HelperWorkspacePathResolver.IsPathUnderRoot(path, _pathPolicy.LibraryDocsRoot) && !knownFiles.Contains(path))
                    .ToList())
                {
                    queue.Remove(staleFile);
                    changed = true;
                }

                if (changed)
                {
                    await WriteQueueUnsafeAsync(queue);
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct = default)
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                var queue = await ReadQueueUnsafeAsync();
                queue = _pathPolicy.NormalizeQueueEntries(queue, out var changed);
                if (changed)
                {
                    await WriteQueueUnsafeAsync(queue);
                }

                return queue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Learning] LoadQueueAsync failed: {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveAsync(Dictionary<string, string> queue, CancellationToken ct = default)
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                var normalized = _pathPolicy.NormalizeQueueEntries(queue, out _);
                await WriteQueueUnsafeAsync(normalized);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task UpdateStatusAsync(string filePath, string status, CancellationToken ct = default)
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                var queue = await ReadQueueUnsafeAsync();
                queue = _pathPolicy.NormalizeQueueEntries(queue, out _);

                var canonicalPath = _pathPolicy.CanonicalizeTargetFile(filePath) ?? filePath;
                queue[canonicalPath] = status;
                await WriteQueueUnsafeAsync(queue);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Learning] Error updating queue status: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task DeleteAsync(CancellationToken ct = default)
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                if (File.Exists(_pathPolicy.QueuePath))
                {
                    File.Delete(_pathPolicy.QueuePath);
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task RecoverStuckFilesAsync(CancellationToken ct = default)
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                if (!File.Exists(_pathPolicy.QueuePath))
                {
                    return;
                }

                var queue = await ReadQueueUnsafeAsync();
                queue = _pathPolicy.NormalizeQueueEntries(queue, out var changed);

                foreach (var file in queue.Keys.ToList())
                {
                    if (string.Equals(queue[file], LearningQueueStatus.Processing, StringComparison.OrdinalIgnoreCase))
                    {
                        queue[file] = LearningQueueStatus.Pending;
                        changed = true;
                    }
                }

                if (changed)
                {
                    await WriteQueueUnsafeAsync(queue);
                    Console.WriteLine("[System] 🛠️ Auto-recovered stuck indexing files on startup.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[System] Failed to cleanup stuck files: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task<Dictionary<string, string>> ReadQueueUnsafeAsync()
        {
            if (!File.Exists(_pathPolicy.QueuePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            using var fs = new FileStream(_pathPolicy.QueuePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var json = await sr.ReadToEndAsync();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task WriteQueueUnsafeAsync(Dictionary<string, string> queue)
        {
            var directory = Path.GetDirectoryName(_pathPolicy.QueuePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(queue, new JsonSerializerOptions { WriteIndented = true });
            using var fs = new FileStream(_pathPolicy.QueuePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs);
            await sw.WriteAsync(json);
        }
    }
}

