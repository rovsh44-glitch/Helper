using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Runtime.WebResearch.Rendering;

public interface IBrowserRenderFallbackService
{
    Task<BrowserRenderFallbackResult> TryRenderAsync(
        Uri requestedUri,
        RenderFallbackBudgetDecision budget,
        CancellationToken ct = default);
}

public sealed record BrowserRenderFallbackResult(
    bool Success,
    string Outcome,
    ExtractedWebPage? Page,
    IReadOnlyList<string> Trace);
