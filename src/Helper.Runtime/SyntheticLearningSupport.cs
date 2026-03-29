using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public static class LearningQueueStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Done = "Done";
    }

    public sealed record LearningRuntimeSnapshot(
        LearningStatus IndexingStatus,
        LearningStatus EvolutionStatus,
        bool IsActive,
        string? TargetDomain,
        string? TargetFile,
        bool SingleFileOnly,
        double CurrentFileProgress);

    public sealed record LearningStopHandle(Task? RunningTask, CancellationTokenSource? CancellationSource);
}

