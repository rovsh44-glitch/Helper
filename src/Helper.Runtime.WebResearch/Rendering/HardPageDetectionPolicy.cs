using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Rendering;

public interface IHardPageDetectionPolicy
{
    HardPageDetectionDecision Evaluate(
        Uri requestedUri,
        Uri resolvedUri,
        string? contentType,
        string content,
        ExtractedWebPage? extractedPage);
}

public sealed record HardPageDetectionDecision(
    bool IsHardPage,
    string Reason,
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> Trace);

public sealed class HardPageDetectionPolicy : IHardPageDetectionPolicy
{
    private static readonly Regex ScriptTagRegex = new(
        "<script\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsShellRegex = new(
        "(id\\s*=\\s*[\"']__(next|nuxt)[\"']|data-reactroot|window\\.__INITIAL_STATE__|window\\.__NUXT__|/_next/static/|webpack-runtime|__APOLLO_STATE__|ng-version=)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JavaScriptRequiredRegex = new(
        "(enable javascript|javascript is required|please turn on javascript|this app works best with javascript|loading\\.\\.\\.)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public HardPageDetectionDecision Evaluate(
        Uri requestedUri,
        Uri resolvedUri,
        string? contentType,
        string content,
        ExtractedWebPage? extractedPage)
    {
        var normalizedContentType = NormalizeContentType(contentType);
        if (!normalizedContentType.Equals("text/html", StringComparison.OrdinalIgnoreCase) &&
            !normalizedContentType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            return new HardPageDetectionDecision(
                false,
                "non_html",
                Array.Empty<string>(),
                new[] { $"web_page_render.detected=no reason=non_html target={resolvedUri}" });
        }

        var signals = new List<string>();
        if (extractedPage is null)
        {
            signals.Add("extraction_failed");
        }
        else
        {
            if (extractedPage.Body.Length < 220)
            {
                signals.Add("thin_body");
            }

            if (extractedPage.Passages.Count <= 1)
            {
                signals.Add("low_passage_count");
            }
        }

        var scriptCount = ScriptTagRegex.Matches(content).Count;
        if (scriptCount >= 5)
        {
            signals.Add($"script_heavy:{scriptCount}");
        }

        if (JsShellRegex.IsMatch(content))
        {
            signals.Add("js_shell_marker");
        }

        if (JavaScriptRequiredRegex.IsMatch(content))
        {
            signals.Add("javascript_required_text");
        }

        var shouldRender =
            signals.Contains("js_shell_marker", StringComparer.Ordinal) ||
            signals.Contains("javascript_required_text", StringComparer.Ordinal) ||
            (signals.Contains("extraction_failed", StringComparer.Ordinal) &&
             signals.Any(static signal => signal.StartsWith("script_heavy:", StringComparison.Ordinal))) ||
            (signals.Contains("thin_body", StringComparer.Ordinal) &&
             signals.Any(static signal => signal.StartsWith("script_heavy:", StringComparison.Ordinal)));

        var reason = shouldRender ? signals[0] : "sufficient_http_content";
        return new HardPageDetectionDecision(
            shouldRender,
            reason,
            signals.ToArray(),
            new[]
            {
                $"web_page_render.detected={(shouldRender ? "yes" : "no")} reason={reason} signals={(signals.Count == 0 ? "none" : string.Join(",", signals))} target={requestedUri}"
            });
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "text/html";
        }

        var separatorIndex = contentType.IndexOf(';');
        var mediaType = separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
        return mediaType.Trim().ToLowerInvariant();
    }
}

