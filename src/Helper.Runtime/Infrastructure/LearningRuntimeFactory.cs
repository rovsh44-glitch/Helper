using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public static class LearningRuntimeFactory
{
    public static ISyntheticTaskRunner CreateSyntheticTaskRunner(
        AILink ai,
        IGraphOrchestrator graphOrchestrator,
        ILearningPathPolicy learningPathPolicy)
    {
        return new SyntheticTaskRunner(ai, graphOrchestrator, learningPathPolicy);
    }

    public static ISyntheticLearningService CreateSyntheticLearningService(
        ILibrarianAgent librarianAgent,
        IIndexingTelemetrySink indexingTelemetrySink,
        ILearningPathPolicy learningPathPolicy,
        IIndexingQueueStore indexingQueueStore,
        ILearningLifecycleController learningLifecycleController,
        ISyntheticTaskRunner syntheticTaskRunner)
    {
        return new SyntheticLearningService(
            librarianAgent,
            indexingTelemetrySink,
            learningPathPolicy,
            indexingQueueStore,
            learningLifecycleController,
            syntheticTaskRunner);
    }
}
