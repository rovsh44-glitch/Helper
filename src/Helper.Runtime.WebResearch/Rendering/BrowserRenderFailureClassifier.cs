using System.ComponentModel;

namespace Helper.Runtime.WebResearch.Rendering;

internal sealed record BrowserRenderFailure(
    string Outcome,
    string Category,
    string Reason);

internal static class BrowserRenderFailureClassifier
{
    public static BrowserRenderFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var current in Enumerate(exception))
        {
            var message = current.Message ?? string.Empty;
            if (message.Contains("spawn eperm", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("access is denied", StringComparison.OrdinalIgnoreCase) ||
                current is UnauthorizedAccessException ||
                current is Win32Exception)
            {
                return new BrowserRenderFailure(
                    Outcome: "browser_spawn_blocked",
                    Category: "browser_spawn_blocked",
                    Reason: Summarize(message));
            }

            if (message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Please run the following command to download", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("playwright install", StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserRenderFailure(
                    Outcome: "browser_render_unavailable",
                    Category: "browser_render_unavailable",
                    Reason: Summarize(message));
            }

            if (message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Navigation timeout", StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserRenderFailure(
                    Outcome: "browser_navigation_failed",
                    Category: "browser_navigation_failed",
                    Reason: Summarize(message));
            }

            if (message.Contains("ERR_", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("net::", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Navigation failed", StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserRenderFailure(
                    Outcome: "browser_navigation_failed",
                    Category: "browser_navigation_failed",
                    Reason: Summarize(message));
            }

            if (message.Contains("Failed to launch", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("browserType.launch", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserRenderFailure(
                    Outcome: "browser_launch_failed",
                    Category: "browser_launch_failed",
                    Reason: Summarize(message));
            }
        }

        return new BrowserRenderFailure(
            Outcome: "browser_render_unavailable",
            Category: "browser_render_unavailable",
            Reason: Summarize(exception.Message));
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            yield return current;
        }
    }

    private static string Summarize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        return message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

