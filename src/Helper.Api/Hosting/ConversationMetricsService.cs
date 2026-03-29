using System.Collections.Concurrent;

namespace Helper.Api.Hosting;

public sealed partial class ConversationMetricsService : IConversationMetricsService
{
    private long _turns;
    private long _firstTokenLatencySum;
    private long _fullLatencySum;
    private long _toolCalls;
    private long _factualTurns;
    private long _factualWithCitations;
    private long _totalClaims;
    private long _verifiedClaims;
    private long _confidenceMilliSum;
    private long _successTurns;
    private long _modelTtftSum;
    private long _modelTtftSamples;
    private long _transportTtftSum;
    private long _transportTtftSamples;
    private long _endToEndTtftSum;
    private long _endToEndTtftSamples;
    private long _budgetExceededTurns;
    private long _fastModeTurns;
    private long _balancedModeTurns;
    private long _deepModeTurns;
    private long _unknownModeTurns;
    private long _researchRoutedTurns;
    private long _researchClarificationFallbackTurns;
    private long _styleTurns;
    private long _styleRepeatedPhraseTurns;
    private long _styleMixedLanguageTurns;
    private long _styleGenericClarificationTurns;
    private long _styleGenericNextStepTurns;
    private long _styleMemoryAckTemplateTurns;
    private long _styleSourceTurns;
    private long _reasoningTurns;
    private long _reasoningBranchingTurns;
    private long _reasoningBranchesExploredSum;
    private long _reasoningCandidatesRejectedSum;
    private long _reasoningLocalVerificationChecksSum;
    private long _reasoningLocalVerificationPassesSum;
    private long _reasoningLocalVerificationRejectsSum;
    private long _reasoningModelCallsUsedSum;
    private long _reasoningRetrievalChunksUsedSum;
    private long _reasoningProceduralLessonsUsedSum;
    private long _reasoningApproximateTokenCostSum;
    private readonly ConcurrentDictionary<string, long> _leadPhraseCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _sourceFingerprintCounts = new(StringComparer.OrdinalIgnoreCase);

    public void RecordTurn(ConversationTurnMetric metric)
    {
        Interlocked.Increment(ref _turns);
        Interlocked.Add(ref _firstTokenLatencySum, metric.FirstTokenLatencyMs);
        Interlocked.Add(ref _fullLatencySum, metric.FullResponseLatencyMs);
        Interlocked.Add(ref _toolCalls, metric.ToolCallsCount);
        Interlocked.Add(ref _confidenceMilliSum, (long)(metric.Confidence * 1000));

        if (metric.IsSuccessful)
        {
            Interlocked.Increment(ref _successTurns);
        }

        if (metric.IsFactualPrompt)
        {
            Interlocked.Increment(ref _factualTurns);
            if (metric.HasCitations)
            {
                Interlocked.Increment(ref _factualWithCitations);
            }
        }

        if (metric.TotalClaims > 0)
        {
            Interlocked.Add(ref _totalClaims, metric.TotalClaims);
            var verified = Math.Max(0, Math.Min(metric.VerifiedClaims, metric.TotalClaims));
            Interlocked.Add(ref _verifiedClaims, verified);
        }

        if (metric.ModelTtftMs is >= 0)
        {
            Interlocked.Add(ref _modelTtftSum, metric.ModelTtftMs.Value);
            Interlocked.Increment(ref _modelTtftSamples);
        }

        if (metric.TransportTtftMs is >= 0)
        {
            Interlocked.Add(ref _transportTtftSum, metric.TransportTtftMs.Value);
            Interlocked.Increment(ref _transportTtftSamples);
        }

        if (metric.EndToEndTtftMs is >= 0)
        {
            Interlocked.Add(ref _endToEndTtftSum, metric.EndToEndTtftMs.Value);
            Interlocked.Increment(ref _endToEndTtftSamples);
        }

        if (metric.BudgetExceeded)
        {
            Interlocked.Increment(ref _budgetExceededTurns);
        }

        if (string.Equals(metric.Intent, "research", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _researchRoutedTurns);
        }

        if (metric.ResearchClarificationFallback)
        {
            Interlocked.Increment(ref _researchClarificationFallbackTurns);
        }

        if (metric.Style is { } style)
        {
            Interlocked.Increment(ref _styleTurns);

            if (style.MixedLanguageDetected)
            {
                Interlocked.Increment(ref _styleMixedLanguageTurns);
            }

            if (style.GenericClarificationDetected)
            {
                Interlocked.Increment(ref _styleGenericClarificationTurns);
            }

            if (style.GenericNextStepDetected)
            {
                Interlocked.Increment(ref _styleGenericNextStepTurns);
            }

            if (style.MemoryAckTemplateDetected)
            {
                Interlocked.Increment(ref _styleMemoryAckTemplateTurns);
            }

            if (!string.IsNullOrWhiteSpace(style.LeadPhraseFingerprint))
            {
                var nextCount = _leadPhraseCounts.AddOrUpdate(style.LeadPhraseFingerprint, 1, static (_, current) => current + 1);
                if (nextCount > 1)
                {
                    Interlocked.Increment(ref _styleRepeatedPhraseTurns);
                }
            }

            if (!string.IsNullOrWhiteSpace(style.SourceFingerprint))
            {
                _sourceFingerprintCounts.AddOrUpdate(style.SourceFingerprint, 1, static (_, current) => current + 1);
                Interlocked.Increment(ref _styleSourceTurns);
            }
        }

        var mode = metric.ExecutionMode?.Trim().ToLowerInvariant();
        switch (mode)
        {
            case "fast":
                Interlocked.Increment(ref _fastModeTurns);
                break;
            case "deep":
                Interlocked.Increment(ref _deepModeTurns);
                break;
            case "balanced":
                Interlocked.Increment(ref _balancedModeTurns);
                break;
            default:
                Interlocked.Increment(ref _unknownModeTurns);
                break;
        }

        if (metric.Reasoning is { PathActive: true } reasoning)
        {
            Interlocked.Increment(ref _reasoningTurns);
            if (reasoning.BranchingApplied)
            {
                Interlocked.Increment(ref _reasoningBranchingTurns);
            }

            Interlocked.Add(ref _reasoningBranchesExploredSum, Math.Max(0, reasoning.BranchesExplored));
            Interlocked.Add(ref _reasoningCandidatesRejectedSum, Math.Max(0, reasoning.CandidatesRejected));
            Interlocked.Add(ref _reasoningLocalVerificationChecksSum, Math.Max(0, reasoning.LocalVerificationChecks));
            Interlocked.Add(ref _reasoningLocalVerificationPassesSum, Math.Max(0, reasoning.LocalVerificationPasses));
            Interlocked.Add(ref _reasoningLocalVerificationRejectsSum, Math.Max(0, reasoning.LocalVerificationRejects));
            Interlocked.Add(ref _reasoningModelCallsUsedSum, Math.Max(0, reasoning.ModelCallsUsed));
            Interlocked.Add(ref _reasoningRetrievalChunksUsedSum, Math.Max(0, reasoning.RetrievalChunksUsed));
            Interlocked.Add(ref _reasoningProceduralLessonsUsedSum, Math.Max(0, reasoning.ProceduralLessonsUsed));
            Interlocked.Add(ref _reasoningApproximateTokenCostSum, Math.Max(0, reasoning.ApproximateTokenCost));
        }
    }

}

