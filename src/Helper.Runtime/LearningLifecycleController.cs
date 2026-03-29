using System;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface ILearningLifecycleController
    {
        LearningRuntimeSnapshot Snapshot();
        void SetTargetDomain(string? domain);
        void SetTargetFile(string? filePath, bool singleFileOnly);
        void SetIndexingStatus(LearningStatus status);
        void SetEvolutionStatus(LearningStatus status);
        void SetCurrentFileProgress(double progress);
        void ClearTargetFileSelection();
        void MarkSingleFileTargetUnavailable();
        void CompleteSingleFileRun();
        void SetActive(bool active);
        bool TryStartCycle(Func<CancellationToken, Task> runLoop);
        LearningStopHandle Stop();
    }

    public sealed class LearningLifecycleController : ILearningLifecycleController
    {
        private readonly object _gate = new();

        private LearningStatus _indexingStatus = LearningStatus.Idle;
        private LearningStatus _evolutionStatus = LearningStatus.Idle;
        private bool _isActive = true;
        private CancellationTokenSource? _cycleCts;
        private Task? _cycleTask;
        private string? _targetDomain;
        private string? _targetFile;
        private bool _singleFileOnly;
        private double _currentFileProgress;

        public LearningRuntimeSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new LearningRuntimeSnapshot(
                    _indexingStatus,
                    _evolutionStatus,
                    _isActive,
                    _targetDomain,
                    _targetFile,
                    _singleFileOnly,
                    _currentFileProgress);
            }
        }

        public void SetTargetDomain(string? domain)
        {
            lock (_gate)
            {
                _targetDomain = domain;
            }
        }

        public void SetTargetFile(string? filePath, bool singleFileOnly)
        {
            lock (_gate)
            {
                _targetFile = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
                _singleFileOnly = !string.IsNullOrWhiteSpace(_targetFile) && singleFileOnly;
            }
        }

        public void SetIndexingStatus(LearningStatus status)
        {
            lock (_gate)
            {
                _indexingStatus = status;
            }
        }

        public void SetEvolutionStatus(LearningStatus status)
        {
            lock (_gate)
            {
                _evolutionStatus = status;
            }
        }

        public void SetCurrentFileProgress(double progress)
        {
            lock (_gate)
            {
                _currentFileProgress = progress;
            }
        }

        public void ClearTargetFileSelection()
        {
            lock (_gate)
            {
                _targetFile = null;
            }
        }

        public void MarkSingleFileTargetUnavailable()
        {
            lock (_gate)
            {
                _targetFile = null;
                _singleFileOnly = false;
                _indexingStatus = LearningStatus.Idle;
                _currentFileProgress = 0;
            }
        }

        public void CompleteSingleFileRun()
        {
            lock (_gate)
            {
                _indexingStatus = LearningStatus.Idle;
                _singleFileOnly = false;
                _currentFileProgress = 0;
            }
        }

        public void SetActive(bool active)
        {
            lock (_gate)
            {
                _isActive = active;
            }
        }

        public bool TryStartCycle(Func<CancellationToken, Task> runLoop)
        {
            ArgumentNullException.ThrowIfNull(runLoop);

            lock (_gate)
            {
                if (_cycleTask != null && !_cycleTask.IsCompleted)
                {
                    return false;
                }

                _cycleCts?.Dispose();
                _cycleCts = new CancellationTokenSource();
                _cycleTask = runLoop(_cycleCts.Token);
                return true;
            }
        }

        public LearningStopHandle Stop()
        {
            lock (_gate)
            {
                _indexingStatus = LearningStatus.Idle;
                _evolutionStatus = LearningStatus.Idle;
                _currentFileProgress = 0;
                _targetDomain = null;
                _targetFile = null;
                _singleFileOnly = false;

                if (_cycleCts != null && !_cycleCts.IsCancellationRequested)
                {
                    _cycleCts.Cancel();
                }

                var handle = new LearningStopHandle(_cycleTask, _cycleCts);
                _cycleTask = null;
                _cycleCts = null;
                return handle;
            }
        }
    }
}

