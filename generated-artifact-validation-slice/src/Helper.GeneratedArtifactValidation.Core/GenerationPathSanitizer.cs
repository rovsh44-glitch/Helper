using System.Text;
using System.Text.RegularExpressions;
using Helper.GeneratedArtifactValidation.Contracts;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class GenerationPathSanitizer
{
    private static readonly Regex MultiUnderscore = new("_+", RegexOptions.Compiled);

    public PathSanitizationResult SanitizeRelativePath(string? rawPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            errors.Add("Path is empty.");
            return new PathSanitizationResult(false, null, errors, warnings);
        }

        var normalized = rawPath.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            errors.Add("Absolute paths are not allowed.");
            return new PathSanitizationResult(false, null, errors, warnings);
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            errors.Add("Path has no valid segments.");
            return new PathSanitizationResult(false, null, errors, warnings);
        }

        var cleanSegments = new List<string>(segments.Length);
        foreach (var segmentRaw in segments)
        {
            var segment = segmentRaw.Trim();
            if (segment is "." or "..")
            {
                errors.Add("Path traversal segments are not allowed.");
                return new PathSanitizationResult(false, null, errors, warnings);
            }

            if (segment.Contains(':', StringComparison.Ordinal))
            {
                errors.Add("Colon is not allowed in relative paths.");
                return new PathSanitizationResult(false, null, errors, warnings);
            }

            var sanitized = SanitizeSegment(segment);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                errors.Add($"Path segment '{segment}' becomes empty after sanitization.");
                return new PathSanitizationResult(false, null, errors, warnings);
            }

            if (!string.Equals(segment, sanitized, StringComparison.Ordinal))
            {
                warnings.Add($"Path segment '{segment}' sanitized to '{sanitized}'.");
            }

            cleanSegments.Add(sanitized);
        }

        var output = string.Join('/', cleanSegments);
        var fileName = cleanSegments[^1];
        if (!fileName.Contains('.', StringComparison.Ordinal))
        {
            warnings.Add("Path does not contain a file extension.");
        }

        return new PathSanitizationResult(true, output, errors, warnings);
    }

    private static string SanitizeSegment(string segment)
    {
        var builder = new StringBuilder(segment.Length + 2);
        foreach (var ch in segment)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                builder.Append(ch);
                continue;
            }

            builder.Append('_');
        }

        var normalized = MultiUnderscore.Replace(builder.ToString(), "_");
        return normalized.Trim('_');
    }
}

