namespace Helper.Runtime.Generation;

public sealed class GenerationMetricsService : IGenerationMetricsService
{
    private long _generationRunsTotal;
    private long _generationValidationFailTotal;
    private long _generationCompileFailTotal;
    private long _generationPromotedTotal;
    private long _generationGoldenTemplateHitTotal;
    private long _generationGoldenTemplateMissTotal;
    private long _generationTimeoutRoutingTotal;
    private long _generationTimeoutForgeTotal;
    private long _generationTimeoutSynthesisTotal;
    private long _generationTimeoutAutofixTotal;
    private long _generationTimeoutUnknownTotal;
    private long _generationAutofixAttemptsTotal;
    private long _generationAutofixSuccessTotal;
    private long _generationAutofixFailTotal;
    private long _templatePromotionAttemptTotal;
    private long _templatePromotionSuccessTotal;
    private long _templatePromotionFailTotal;
    private long _templatePromotionFormatFixAppliedTotal;
    private long _templatePromotionFormatStillFailingTotal;
    private readonly object _templatePromotionFailReasonSync = new();
    private readonly Dictionary<string, long> _templatePromotionFailReasonTotals = new(StringComparer.OrdinalIgnoreCase);

    public void RecordRun() => Interlocked.Increment(ref _generationRunsTotal);

    public void RecordValidationFail() => Interlocked.Increment(ref _generationValidationFailTotal);

    public void RecordCompileFail() => Interlocked.Increment(ref _generationCompileFailTotal);

    public void RecordPromoted() => Interlocked.Increment(ref _generationPromotedTotal);

    public void RecordGoldenTemplateRoute(bool hit)
    {
        if (hit)
        {
            Interlocked.Increment(ref _generationGoldenTemplateHitTotal);
            return;
        }

        Interlocked.Increment(ref _generationGoldenTemplateMissTotal);
    }

    public void RecordTimeout(GenerationTimeoutStage stage)
    {
        switch (stage)
        {
            case GenerationTimeoutStage.Routing:
                Interlocked.Increment(ref _generationTimeoutRoutingTotal);
                break;
            case GenerationTimeoutStage.Forge:
                Interlocked.Increment(ref _generationTimeoutForgeTotal);
                break;
            case GenerationTimeoutStage.Synthesis:
                Interlocked.Increment(ref _generationTimeoutSynthesisTotal);
                break;
            case GenerationTimeoutStage.Autofix:
                Interlocked.Increment(ref _generationTimeoutAutofixTotal);
                break;
            default:
                Interlocked.Increment(ref _generationTimeoutUnknownTotal);
                break;
        }
    }

    public void RecordAutofixAttempt(bool success)
    {
        Interlocked.Increment(ref _generationAutofixAttemptsTotal);
        if (success)
        {
            Interlocked.Increment(ref _generationAutofixSuccessTotal);
            return;
        }

        Interlocked.Increment(ref _generationAutofixFailTotal);
    }

    public void RecordTemplatePromotionAttempt(bool success, string? reasonCode = null)
    {
        Interlocked.Increment(ref _templatePromotionAttemptTotal);
        if (success)
        {
            Interlocked.Increment(ref _templatePromotionSuccessTotal);
            return;
        }

        Interlocked.Increment(ref _templatePromotionFailTotal);
        var reason = string.IsNullOrWhiteSpace(reasonCode) ? "unknown" : reasonCode.Trim();
        lock (_templatePromotionFailReasonSync)
        {
            if (_templatePromotionFailReasonTotals.TryGetValue(reason, out var current))
            {
                _templatePromotionFailReasonTotals[reason] = current + 1;
            }
            else
            {
                _templatePromotionFailReasonTotals[reason] = 1;
            }
        }
    }

    public void RecordTemplatePromotionFormatFixResult(bool success)
    {
        if (success)
        {
            Interlocked.Increment(ref _templatePromotionFormatFixAppliedTotal);
            return;
        }

        Interlocked.Increment(ref _templatePromotionFormatStillFailingTotal);
    }

    public GenerationMetricsSnapshot GetSnapshot()
    {
        IReadOnlyDictionary<string, long> failReasons;
        lock (_templatePromotionFailReasonSync)
        {
            failReasons = new Dictionary<string, long>(_templatePromotionFailReasonTotals, StringComparer.OrdinalIgnoreCase);
        }

        return new GenerationMetricsSnapshot(
            Volatile.Read(ref _generationRunsTotal),
            Volatile.Read(ref _generationValidationFailTotal),
            Volatile.Read(ref _generationCompileFailTotal),
            Volatile.Read(ref _generationPromotedTotal),
            Volatile.Read(ref _generationGoldenTemplateHitTotal),
            Volatile.Read(ref _generationGoldenTemplateMissTotal),
            Volatile.Read(ref _generationTimeoutRoutingTotal),
            Volatile.Read(ref _generationTimeoutForgeTotal),
            Volatile.Read(ref _generationTimeoutSynthesisTotal),
            Volatile.Read(ref _generationTimeoutAutofixTotal),
            Volatile.Read(ref _generationTimeoutUnknownTotal),
            Volatile.Read(ref _generationAutofixAttemptsTotal),
            Volatile.Read(ref _generationAutofixSuccessTotal),
            Volatile.Read(ref _generationAutofixFailTotal),
            Volatile.Read(ref _templatePromotionAttemptTotal),
            Volatile.Read(ref _templatePromotionSuccessTotal),
            Volatile.Read(ref _templatePromotionFailTotal),
            Volatile.Read(ref _templatePromotionFormatFixAppliedTotal),
            Volatile.Read(ref _templatePromotionFormatStillFailingTotal),
            failReasons);
    }
}

