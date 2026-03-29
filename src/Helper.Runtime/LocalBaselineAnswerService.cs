using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public interface ILocalBaselineAnswerService
{
    Task<string> GenerateAsync(string topic, CancellationToken ct = default);
}

public sealed class LocalBaselineAnswerService : ILocalBaselineAnswerService, ILocalBaselineAnswerDiagnostics
{
    private static readonly TimeSpan LocalBaselineTimeout = TimeSpan.FromSeconds(6);

    private readonly AILink _ai;
    private readonly IRetrievalContextAssembler? _retrievalContextAssembler;
    private readonly ResearchAnswerQualityGate _answerQualityGate;

    public LocalBaselineAnswerService(AILink ai, IRetrievalContextAssembler? retrievalContextAssembler = null)
    {
        _ai = ai;
        _retrievalContextAssembler = retrievalContextAssembler;
        _answerQualityGate = new ResearchAnswerQualityGate();
    }

    public async Task<string> GenerateAsync(string topic, CancellationToken ct = default)
    {
        var detailed = await GenerateDetailedAsync(topic, ct).ConfigureAwait(false);
        return detailed.Answer;
    }

    public async Task<LocalBaselineAnswerResult> GenerateDetailedAsync(string topic, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(LocalBaselineTimeout);
        var supportingChunks = Array.Empty<KnowledgeChunk>();
        var trace = new List<string>();
        var evidenceItems = Array.Empty<ResearchEvidenceItem>();
        var sources = Array.Empty<string>();

        if (_retrievalContextAssembler is not null)
        {
            try
            {
                supportingChunks = (await _retrievalContextAssembler.AssembleAsync(
                    topic,
                    domain: null,
                    limit: 4,
                    pipelineVersion: "v2",
                    expandContext: true,
                    ct: timeoutCts.Token,
                    options: new RetrievalRequestOptions(
                        Purpose: RetrievalPurpose.ReasoningSupport,
                        PreferTraceableChunks: true)).ConfigureAwait(false))
                    .ToArray();

                if (supportingChunks.Length > 0)
                {
                    trace.AddRange(LocalLibraryGroundingSupport.BuildTrace(supportingChunks));
                    evidenceItems = LocalLibraryGroundingSupport.BuildEvidenceItems(supportingChunks).ToArray();
                    sources = LocalLibraryGroundingSupport.BuildSources(supportingChunks).ToArray();
                }
                else
                {
                    trace.Add("local_retrieval.mode=hybrid_rrf");
                    trace.Add("local_retrieval.chunk_count=0");
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                trace.Add("local_retrieval.timeout=yes");
            }
            catch (Exception ex)
            {
                trace.Add($"local_retrieval.failed={ex.GetType().Name}");
            }
        }

        try
        {
            if (supportingChunks.Length > 0)
            {
                var groundedBaseline = await _ai.AskAsync(
                    LocalLibraryGroundingSupport.BuildPrompt(topic, supportingChunks),
                    timeoutCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(groundedBaseline))
                {
                    var groundedQuality = _answerQualityGate.Evaluate(topic, evidenceItems, groundedBaseline);
                    if (groundedQuality.Accepted)
                    {
                        return new LocalBaselineAnswerResult(groundedBaseline, sources, trace, evidenceItems);
                    }

                    var groundedRepair = await TryGroundedRepairAsync(
                        topic,
                        supportingChunks,
                        groundedBaseline,
                        groundedQuality.Signals,
                        timeoutCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(groundedRepair))
                    {
                        var repairedQuality = _answerQualityGate.Evaluate(topic, evidenceItems, groundedRepair);
                        if (repairedQuality.Accepted)
                        {
                            return new LocalBaselineAnswerResult(groundedRepair, sources, trace, evidenceItems);
                        }
                    }
                }

                var deterministicGrounded = LocalLibraryGroundingSupport.BuildDeterministicFallback(topic, supportingChunks);
                var deterministicGroundedQuality = _answerQualityGate.Evaluate(topic, evidenceItems, deterministicGrounded);
                if (deterministicGroundedQuality.Accepted)
                {
                    return new LocalBaselineAnswerResult(deterministicGrounded, sources, trace, evidenceItems);
                }
            }

            var localBaseline = await _ai.AskAsync(
                ResearchSynthesisSupport.BuildLocalBaselinePrompt(topic),
                timeoutCts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(localBaseline))
            {
                var baselineQuality = _answerQualityGate.Evaluate(topic, evidenceItems, localBaseline);
                if (baselineQuality.Accepted)
                {
                    return new LocalBaselineAnswerResult(localBaseline, sources, trace, evidenceItems);
                }

                var repaired = await TryRepairAsync(topic, localBaseline, baselineQuality.Signals, timeoutCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(repaired))
                {
                    var repairedQuality = _answerQualityGate.Evaluate(topic, evidenceItems, repaired);
                    if (repairedQuality.Accepted)
                    {
                        return new LocalBaselineAnswerResult(repaired, sources, trace, evidenceItems);
                    }
                }
            }
            else
            {
                var repaired = await TryRepairAsync(topic, string.Empty, Array.Empty<string>(), timeoutCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(repaired))
                {
                    var repairedQuality = _answerQualityGate.Evaluate(topic, evidenceItems, repaired);
                    if (repairedQuality.Accepted)
                    {
                        return new LocalBaselineAnswerResult(repaired, sources, trace, evidenceItems);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Fall through to deterministic baseline.
        }
        catch
        {
            // Fall through to deterministic baseline.
        }

        var deterministic = ResearchSynthesisSupport.BuildDeterministicLocalBaselineFallback(topic);
        var deterministicQuality = _answerQualityGate.Evaluate(topic, evidenceItems, deterministic);
        if (deterministicQuality.Accepted)
        {
            return new LocalBaselineAnswerResult(deterministic, sources, trace, evidenceItems);
        }

        return new LocalBaselineAnswerResult(
            ResearchSynthesisSupport.BuildHonestFallbackResponse(topic, Array.Empty<string>()),
            sources,
            trace,
            evidenceItems);
    }

    private async Task<string?> TryRepairAsync(
        string topic,
        string rejectedDraft,
        IReadOnlyList<string> rejectionSignals,
        CancellationToken ct)
    {
        var repaired = await _ai.AskAsync(
            ResearchSynthesisSupport.BuildLocalBaselineRepairPrompt(topic, rejectedDraft, rejectionSignals),
            ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(repaired) ? null : repaired;
    }

    private async Task<string?> TryGroundedRepairAsync(
        string topic,
        IReadOnlyList<KnowledgeChunk> supportingChunks,
        string rejectedDraft,
        IReadOnlyList<string> rejectionSignals,
        CancellationToken ct)
    {
        var repaired = await _ai.AskAsync(
            LocalLibraryGroundingSupport.BuildRepairPrompt(topic, supportingChunks, rejectedDraft, rejectionSignals),
            ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(repaired) ? null : repaired;
    }
}

