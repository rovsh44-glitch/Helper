using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Rendering;

public interface IRenderedPageBudgetPolicy
{
    int ResolvePerSearchBudget(int fetchBudget);
    RenderFallbackBudgetDecision Evaluate(WebPageFetchContext context, HardPageDetectionDecision detection);
}

public sealed record RenderFallbackBudgetDecision(
    bool Allowed,
    string Reason,
    TimeSpan Timeout,
    int MaxHtmlChars,
    IReadOnlyList<string> Trace);

public sealed class RenderedPageBudgetPolicy : IRenderedPageBudgetPolicy
{
    private const int DefaultMaxRenderedPagesPerSearch = 1;
    private const int DefaultRenderTimeoutSeconds = 8;
    private const int DefaultMaxRenderedHtmlChars = 300_000;

    public int ResolvePerSearchBudget(int fetchBudget)
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_RENDER_MAX_PAGES_PER_SEARCH");
        var configured = int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 0, 3)
            : DefaultMaxRenderedPagesPerSearch;
        return Math.Min(fetchBudget, configured);
    }

    public RenderFallbackBudgetDecision Evaluate(WebPageFetchContext context, HardPageDetectionDecision detection)
    {
        if (!ReadEnabled())
        {
            return Denied("disabled", detection);
        }

        if (!detection.IsHardPage)
        {
            return Denied("not_hard_page", detection);
        }

        if (!context.AllowBrowserRenderFallback)
        {
            return Denied("context_disabled", detection);
        }

        if (context.RenderBudgetRemaining <= 0)
        {
            return Denied("budget_exhausted", detection);
        }

        var timeout = ReadTimeout();
        var maxHtmlChars = ReadMaxRenderedHtmlChars();
        return new RenderFallbackBudgetDecision(
            true,
            "allowed",
            timeout,
            maxHtmlChars,
            new[]
            {
                $"browser_render.budget allowed=yes remaining={context.RenderBudgetRemaining} timeout_sec={timeout.TotalSeconds:0} max_html_chars={maxHtmlChars} detection={detection.Reason}"
            });
    }

    private static RenderFallbackBudgetDecision Denied(string reason, HardPageDetectionDecision detection)
    {
        return new RenderFallbackBudgetDecision(
            false,
            reason,
            TimeSpan.Zero,
            0,
            new[]
            {
                $"browser_render.budget allowed=no reason={reason} detection={detection.Reason}"
            });
    }

    private static bool ReadEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_RENDER_ENABLED");
        return !string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan ReadTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_RENDER_TIMEOUT_SEC");
        return int.TryParse(raw, out var parsed)
            ? TimeSpan.FromSeconds(Math.Clamp(parsed, 2, 20))
            : TimeSpan.FromSeconds(DefaultRenderTimeoutSeconds);
    }

    private static int ReadMaxRenderedHtmlChars()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_RENDER_MAX_HTML_CHARS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 16_384, 1_000_000)
            : DefaultMaxRenderedHtmlChars;
    }
}

