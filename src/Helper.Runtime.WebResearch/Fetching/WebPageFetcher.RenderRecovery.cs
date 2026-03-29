using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.WebResearch.Fetching;

public sealed partial class WebPageFetcher
{
    private async Task<WebPageFetchResult?> TryRecoverFromTransportFailureWithBrowserRenderAsync(
        Uri requestedUri,
        Uri currentUri,
        WebPageFetchContext context,
        WebPageFetchDiagnostics diagnostics,
        List<string> trace,
        CancellationToken ct)
    {
        if (!context.AllowBrowserRenderFallback || context.RenderBudgetRemaining <= 0)
        {
            trace.Add("web_page_fetch.render_recovery skipped=budget_or_context_disabled");
            return null;
        }

        var sourceType = WebSourceTypeClassifier.Classify(requestedUri, currentUri, "text/html");
        if (!TransportExceptionClassifier.ShouldAttemptBrowserRenderRecovery(currentUri, diagnostics, sourceType))
        {
            trace.Add($"web_page_fetch.render_recovery skipped=not_applicable source_type={sourceType.Kind} category={diagnostics.FinalFailureCategory ?? "none"}");
            return null;
        }

        var budget = _renderedPageBudgetPolicy.Evaluate(
            context,
            new HardPageDetectionDecision(
                IsHardPage: true,
                Reason: "transport_failure_recovery",
                Signals: new[]
                {
                    $"transport_failure:{diagnostics.FinalFailureCategory ?? "unknown"}",
                    $"source_type:{sourceType.Kind}"
                },
                Trace: new[]
                {
                    $"web_page_render.detected=yes reason=transport_failure_recovery signals=transport_failure:{diagnostics.FinalFailureCategory ?? "unknown"},source_type:{sourceType.Kind} target={currentUri}"
                }));
        trace.AddRange(budget.Trace);
        if (!budget.Allowed)
        {
            trace.Add($"web_page_fetch.render_recovery skipped=budget_denied reason={budget.Reason}");
            return null;
        }

        trace.Add($"web_page_fetch.render_recovery_attempt target={currentUri} source_type={sourceType.Kind} category={diagnostics.FinalFailureCategory ?? "unknown"}");
        try
        {
            var renderResult = await _browserRenderFallbackService.TryRenderAsync(currentUri, budget, ct).ConfigureAwait(false);
            trace.AddRange(renderResult.Trace);
            if (!renderResult.Success || renderResult.Page is null)
            {
                trace.Add($"web_page_fetch.render_recovery_failed target={currentUri} outcome={renderResult.Outcome}");
                return null;
            }

            trace.Add($"web_page_fetch.render_recovery_succeeded target={currentUri} outcome={renderResult.Outcome} passages={renderResult.Page.Passages.Count}");
            return new WebPageFetchResult(
                RequestedUrl: requestedUri.AbsoluteUri,
                ResolvedUrl: renderResult.Page.ResolvedUrl,
                Success: true,
                Outcome: renderResult.Outcome,
                ExtractedPage: renderResult.Page,
                Trace: trace,
                UsedBrowserRenderFallback: true,
                Diagnostics: diagnostics);
        }
        catch (Exception renderEx) when (renderEx is not OperationCanceledException)
        {
            trace.Add($"web_page_fetch.render_recovery_failed target={currentUri} outcome=error type={renderEx.GetType().Name} message={SanitizeTraceValue(renderEx.Message)}");
            return null;
        }
    }
}
