using System.Text.RegularExpressions;

namespace Helper.RuntimeSlice.Api.Services;

internal static partial class FixtureSecurityGuard
{
    [GeneratedRegex(@"(?i)\b[a-z]:\\(?!redacted_runtime\\)")]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex(@"(?i)\b(?:api[_ -]?key|bearer|token)\s*[:=]\s*[a-z0-9._-]{8,}")]
    private static partial Regex SecretPatternRegex();

    [GeneratedRegex(@"(?i)https?://(?!localhost\b|127\.0\.0\.1\b)[^\s""']+")]
    private static partial Regex ExternalUrlRegex();

    public static void ValidateText(string fullPath, string content)
    {
        if (WindowsAbsolutePathRegex().IsMatch(content))
        {
            throw new InvalidOperationException($"Fixture contains a non-redacted Windows path: {fullPath}");
        }

        if (SecretPatternRegex().IsMatch(content))
        {
            throw new InvalidOperationException($"Fixture contains token-like material: {fullPath}");
        }

        if (ExternalUrlRegex().IsMatch(content))
        {
            throw new InvalidOperationException($"Fixture contains a non-local URL: {fullPath}");
        }
    }
}
