namespace Helper.Runtime.WebResearch.Rendering;

public sealed class DisabledBrowserRenderFallbackService : IBrowserRenderFallbackService
{
    public Task<BrowserRenderFallbackResult> TryRenderAsync(
        Uri requestedUri,
        RenderFallbackBudgetDecision budget,
        CancellationToken ct = default)
    {
        var trace = new List<string>(budget.Trace)
        {
            $"browser_render.disabled target={requestedUri}"
        };

        return Task.FromResult(new BrowserRenderFallbackResult(
            false,
            "browser_render_disabled",
            null,
            trace));
    }
}
