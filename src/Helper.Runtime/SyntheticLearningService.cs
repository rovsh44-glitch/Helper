using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class SyntheticLearningService : ISyntheticLearningService
    {
        private readonly ILibrarianAgent _librarian;
        private readonly IIndexingTelemetrySink _telemetry;
        private readonly ILearningPathPolicy _pathPolicy;
        private readonly IIndexingQueueStore _queueStore;
        private readonly ILearningLifecycleController _lifecycle;
        private readonly ISyntheticTaskRunner _taskRunner;

        public SyntheticLearningService(
            ILibrarianAgent librarian,
            IIndexingTelemetrySink telemetry,
            ILearningPathPolicy pathPolicy,
            IIndexingQueueStore queueStore,
            ILearningLifecycleController lifecycle,
            ISyntheticTaskRunner taskRunner)
        {
            _librarian = librarian;
            _telemetry = telemetry;
            _pathPolicy = pathPolicy;
            _queueStore = queueStore;
            _lifecycle = lifecycle;
            _taskRunner = taskRunner;
        }

        public void SetTargetDomain(string? domain) => _lifecycle.SetTargetDomain(domain);

        public void SetTargetFile(string? filePath, bool singleFileOnly = false)
            => _lifecycle.SetTargetFile(_pathPolicy.CanonicalizeTargetFile(filePath), singleFileOnly);

        public async Task StartIndexingAsync() { _lifecycle.SetIndexingStatus(LearningStatus.Running); await StartLearningAsync(); }

        public Task PauseIndexingAsync() { _lifecycle.SetIndexingStatus(LearningStatus.Paused); return Task.CompletedTask; }

        public async Task StartEvolutionAsync() { _lifecycle.SetEvolutionStatus(LearningStatus.Running); await StartLearningAsync(); }

        public Task PauseEvolutionAsync() { _lifecycle.SetEvolutionStatus(LearningStatus.Paused); return Task.CompletedTask; }

        public async Task StopLearningAsync()
        {
            var stopHandle = _lifecycle.Stop();
            _telemetry.Reset();

            if (stopHandle.RunningTask != null)
            {
                try
                {
                    await stopHandle.RunningTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Learning] StopLearningAsync wait failed: {ex.Message}");
                }
            }

            stopHandle.CancellationSource?.Dispose();
        }

        public async Task<IndexingProgress> GetProgressAsync()
        {
            var queue = await _queueStore.LoadAsync();
            var snapshot = _lifecycle.Snapshot();
            var telemetry = _telemetry.Snapshot();
            var total = CountVisibleFiles(queue, snapshot.TargetDomain);
            var processed = CountMatchingFiles(queue, snapshot.TargetDomain, LearningQueueStatus.Done);
            var current = queue.FirstOrDefault(entry => string.Equals(entry.Value, LearningQueueStatus.Processing, StringComparison.OrdinalIgnoreCase)).Key
                ?? "None";

            return new IndexingProgress(
                total,
                processed,
                current,
                snapshot.IndexingStatus,
                snapshot.EvolutionStatus,
                snapshot.CurrentFileProgress,
                telemetry.PipelineVersion,
                telemetry.ChunkingStrategy,
                telemetry.CurrentSection,
                telemetry.CurrentPageStart,
                telemetry.CurrentPageEnd,
                telemetry.ParserVersion);
        }

        public async Task RunAdvancedLearningCycleAsync(CancellationToken ct = default, Func<string, Task>? onThought = null)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var snapshot = _lifecycle.Snapshot();
                    if (!snapshot.IsActive)
                    {
                        await Task.Delay(2000, ct);
                        continue;
                    }

                    if (snapshot.IndexingStatus != LearningStatus.Running && snapshot.EvolutionStatus != LearningStatus.Running)
                    {
                        await Task.Delay(2000, ct);
                        continue;
                    }

                    if (snapshot.IndexingStatus == LearningStatus.Running)
                    {
                        var indexedFile = await TryProcessNextIndexingFileAsync(snapshot, ct, onThought);
                        if (indexedFile)
                        {
                            continue;
                        }
                    }

                    snapshot = _lifecycle.Snapshot();
                    if (snapshot.EvolutionStatus == LearningStatus.Running)
                    {
                        if (onThought != null)
                        {
                            await onThought("🌙 Running synthetic innovation cycle...");
                        }

                        await _taskRunner.RunAsync(ct, onThought);
                        await Task.Delay(5000, ct);
                    }
                    else
                    {
                        await Task.Delay(2000, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on stop/shutdown.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Learning] Advanced cycle stopped due to error: {ex.Message}");
            }
        }

        public Task ToggleAsync(bool active) { _lifecycle.SetActive(active); return Task.CompletedTask; }

        public async Task ResetAsync()
        {
            await StopLearningAsync();
            await _queueStore.DeleteAsync();
        }

        public Task CleanupStuckFilesAsync() => _queueStore.RecoverStuckFilesAsync();

        public Task<EvolutionState> GetStateAsync()
        {
            var snapshot = _lifecycle.Snapshot();
            return Task.FromResult(new EvolutionState(string.Empty, snapshot.EvolutionStatus.ToString(), new List<string>(), snapshot.IsActive));
        }

        private Task StartLearningAsync() { _lifecycle.TryStartCycle(ct => RunAdvancedLearningCycleAsync(ct, LogThoughtAsync)); return Task.CompletedTask; }

        private static Task LogThoughtAsync(string message) { Console.WriteLine($"[Learning] {message}"); return Task.CompletedTask; }

        private async Task<bool> TryProcessNextIndexingFileAsync(LearningRuntimeSnapshot snapshot, CancellationToken ct, Func<string, Task>? onThought)
        {
            await _queueStore.SyncWithLibraryAsync(ct);
            var queue = await _queueStore.LoadAsync(ct);

            var nextFile = await ResolveRequestedTargetFileAsync(snapshot, queue, onThought);
            if (nextFile == null && !snapshot.SingleFileOnly)
            {
                nextFile = SelectNextPendingFile(queue, snapshot.TargetDomain);
            }

            if (string.IsNullOrWhiteSpace(nextFile)) return false;

            if (onThought != null)
            {
                await onThought($"📚 Indexing ({snapshot.TargetDomain ?? "All"}): {Path.GetFileName(nextFile)}");
            }

            await _queueStore.UpdateStatusAsync(nextFile, LearningQueueStatus.Processing, ct);
            try
            {
                _lifecycle.SetCurrentFileProgress(0);
                await _librarian.IndexFileAsync(nextFile, progress =>
                {
                    _lifecycle.SetCurrentFileProgress(progress);
                    return Task.CompletedTask;
                }, ct);
                await _queueStore.UpdateStatusAsync(nextFile, LearningQueueStatus.Done, ct);
                _lifecycle.SetCurrentFileProgress(100);
                _telemetry.Reset();

                if (snapshot.SingleFileOnly) _lifecycle.CompleteSingleFileRun();
            }
            catch (OperationCanceledException ex) when (IsControlledIndexCancellation(ct))
            {
                await RecoverCanceledFileAsync(nextFile, ex, ct);
                _lifecycle.SetCurrentFileProgress(0);
                _telemetry.Reset();
                if (snapshot.SingleFileOnly) _lifecycle.CompleteSingleFileRun();
            }
            catch (Exception ex)
            {
                await _queueStore.UpdateStatusAsync(nextFile, "Error: " + ex.Message, ct);
                _lifecycle.SetCurrentFileProgress(0);
                _telemetry.Reset();
                if (snapshot.SingleFileOnly) _lifecycle.CompleteSingleFileRun();
            }

            return true;
        }

        private async Task<string?> ResolveRequestedTargetFileAsync(
            LearningRuntimeSnapshot snapshot,
            IReadOnlyDictionary<string, string> queue,
            Func<string, Task>? onThought)
        {
            if (string.IsNullOrWhiteSpace(snapshot.TargetFile)) return null;

            var requestedTarget = snapshot.TargetFile;
            if (queue.TryGetValue(requestedTarget, out var status) &&
                !string.Equals(status, LearningQueueStatus.Done, StringComparison.OrdinalIgnoreCase))
            {
                _lifecycle.ClearTargetFileSelection();
                return requestedTarget;
            }

            if (snapshot.SingleFileOnly)
            {
                _lifecycle.MarkSingleFileTargetUnavailable();
                _telemetry.Reset();
                if (onThought != null) await onThought($"⏹️ Single-file target unavailable: {Path.GetFileName(requestedTarget)}");
                return null;
            }

            _lifecycle.ClearTargetFileSelection();
            return null;
        }

        private bool IsControlledIndexCancellation(CancellationToken ct)
            => ct.IsCancellationRequested || _lifecycle.Snapshot().IndexingStatus != LearningStatus.Running;

        private async Task RecoverCanceledFileAsync(string filePath, OperationCanceledException ex, CancellationToken ct)
        {
            try
            {
                await _librarian.CleanupFileArtifactsAsync(filePath, CancellationToken.None);
            }
            catch (Exception cleanupEx)
            {
                Console.WriteLine($"[Learning] Cleanup for canceled file failed: {cleanupEx.Message}");
            }

            await _queueStore.UpdateStatusAsync(filePath, LearningQueueStatus.Pending, ct);
            Console.WriteLine($"[Learning] Re-queued canceled indexing file: {Path.GetFileName(filePath)} ({ex.Message})");
        }

        private static string? SelectNextPendingFile(IReadOnlyDictionary<string, string> queue, string? targetDomain)
        {
            return string.IsNullOrWhiteSpace(targetDomain)
                ? queue.FirstOrDefault(entry => string.Equals(entry.Value, LearningQueueStatus.Pending, StringComparison.OrdinalIgnoreCase)).Key
                : queue.FirstOrDefault(entry =>
                    string.Equals(entry.Value, LearningQueueStatus.Pending, StringComparison.OrdinalIgnoreCase) &&
                    MatchesTargetDomain(entry.Key, targetDomain)).Key;
        }

        private static int CountVisibleFiles(IReadOnlyDictionary<string, string> queue, string? targetDomain)
            => queue.Count(entry => MatchesTargetDomain(entry.Key, targetDomain));

        private static int CountMatchingFiles(IReadOnlyDictionary<string, string> queue, string? targetDomain, params string[] statuses)
        {
            return queue.Count(entry =>
                statuses.Any(status => string.Equals(entry.Value, status, StringComparison.OrdinalIgnoreCase)) &&
                MatchesTargetDomain(entry.Key, targetDomain));
        }

        private static bool MatchesTargetDomain(string path, string? targetDomain)
            => string.IsNullOrWhiteSpace(targetDomain) || path.Contains($"\\{targetDomain}\\", StringComparison.OrdinalIgnoreCase);
    }
}

