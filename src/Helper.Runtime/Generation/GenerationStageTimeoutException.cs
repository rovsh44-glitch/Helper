namespace Helper.Runtime.Generation;

public sealed class GenerationStageTimeoutException : TimeoutException
{
    public GenerationStageTimeoutException(GenerationTimeoutStage timeoutStage, TimeSpan stageTimeout)
        : base($"Generation stage '{timeoutStage}' exceeded timeout of {stageTimeout.TotalSeconds:0} seconds.")
    {
        TimeoutStage = timeoutStage;
        StageTimeout = stageTimeout;
    }

    public GenerationTimeoutStage TimeoutStage { get; }

    public TimeSpan StageTimeout { get; }
}

