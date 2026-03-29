using Helper.Runtime.Core;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm;

internal static class TumenCriticPolicy
{
    public static bool ShouldRunCriticFor(FileRole role, IReadOnlyList<ArbanMethodTask> methods)
    {
        if (methods.Count == 0)
        {
            return false;
        }

        return role is not FileRole.Interface
            and not FileRole.View
            and not FileRole.Resource
            and not FileRole.Script;
    }

    public static bool IsNonActionableCritique(string feedback, FileRole role)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return true;
        }

        var normalized = feedback.ToLowerInvariant();
        if (normalized.Contains("parsing failed", StringComparison.Ordinal) ||
            normalized.Contains("source does not provide", StringComparison.Ordinal) ||
            normalized.Contains("does not provide any details", StringComparison.Ordinal) ||
            normalized.Contains("lacks any implementation details", StringComparison.Ordinal) ||
            normalized.Contains("does not reflect this directory structure", StringComparison.Ordinal))
        {
            return true;
        }

        if (role == FileRole.Interface &&
            (normalized.Contains("only defines an interface", StringComparison.Ordinal) ||
             normalized.Contains("without any implementation", StringComparison.Ordinal)))
        {
            return true;
        }

        if (normalized.Contains("windows platform", StringComparison.Ordinal) &&
            normalized.Contains("not reflected", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static string NormalizeFeedbackKey(string feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return string.Empty;
        }

        var chars = feedback
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    public static string TrimForPrompt(string feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return string.Empty;
        }

        var cleaned = feedback.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const int maxLength = 360;
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    public static string TrimForLog(string feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return string.Empty;
        }

        var cleaned = feedback.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const int maxLength = 180;
        return cleaned.Length <= maxLength ? cleaned : $"{cleaned[..maxLength]}...";
    }
}

