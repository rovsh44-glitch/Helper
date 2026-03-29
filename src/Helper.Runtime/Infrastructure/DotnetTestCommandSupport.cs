using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Helper.Runtime.Infrastructure;

internal static class DotnetTestCommandSupport
{
    internal static readonly string[] DefaultSingleRunArguments = { "--no-build" };
    internal static readonly string[] DefaultBatchedArguments = { "--no-build", "--blame-hang", "--blame-hang-timeout", "2m" };

    private static readonly Regex FullyQualifiedNamePrefixRegex = new(
        "FullyQualifiedName\\s*[~=]?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TestClassRegex = new(
        @"(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*Tests\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FilterOperatorRegex = new(
        @"\b[A-Za-z][A-Za-z0-9_.]*\s*(=|~)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsDotnetTestCommand(string? command)
    {
        var tokens = CommandLineTokenizer.Split(command);
        return tokens.Count >= 2 &&
               string.Equals(tokens[0], "dotnet", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(tokens[1], "test", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseShellCommand(string command, out DotnetTestInvocation invocation, out string error)
    {
        invocation = null!;
        error = string.Empty;

        var tokens = CommandLineTokenizer.Split(command);
        if (tokens.Count < 3 ||
            !string.Equals(tokens[0], "dotnet", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tokens[1], "test", StringComparison.OrdinalIgnoreCase))
        {
            error = "Command is not a supported 'dotnet test' invocation.";
            return false;
        }

        var target = tokens[2].Trim();
        if (string.IsNullOrWhiteSpace(target) || target.StartsWith("-", StringComparison.Ordinal))
        {
            error = "dotnet test target is missing.";
            return false;
        }

        var baseArguments = new List<string>();
        string? rawFilter = null;

        for (var index = 3; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (string.Equals(token, "--filter", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= tokens.Count)
                {
                    error = "dotnet test filter value is missing.";
                    return false;
                }

                rawFilter = tokens[++index];
                continue;
            }

            if (token.StartsWith("--filter:", StringComparison.OrdinalIgnoreCase))
            {
                rawFilter = token["--filter:".Length..];
                continue;
            }

            baseArguments.Add(token);
        }

        var classNames = ExtractCandidateTestClasses(rawFilter);
        var normalizedFilter = NormalizeFilter(rawFilter, classNames);
        invocation = new DotnetTestInvocation(
            Target: target,
            BaseArguments: baseArguments,
            FilterExpression: normalizedFilter,
            ClassNames: classNames,
            UseBatchedRunner: false,
            ShowProcessMonitor: false,
            ApplyDefaultArgumentsWhenEmpty: false,
            MaxDurationSec: 900,
            LogPath: null,
            ErrorLogPath: null,
            StatusPath: null,
            ResultsRoot: null);
        return true;
    }

    public static bool TryCreateInvocationFromToolArguments(Dictionary<string, object> arguments, out DotnetTestInvocation invocation, out string error)
    {
        invocation = null!;
        error = string.Empty;

        var target = FirstNonEmptyString(arguments, "target", "project", "solution", "path");
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "dotnet_test requires 'target'.";
            return false;
        }

        var baseArguments = ReadArgumentList(arguments, "baseArguments", "arguments", "args");
        var rawFilter = FirstNonEmptyString(arguments, "filter");
        var classNames = ReadClassNames(arguments);
        var normalizedFilter = NormalizeFilter(rawFilter, classNames);
        var useBatchedRunner = ReadBool(arguments, false, "batched", "useBatchedRunner");
        if (useBatchedRunner && classNames.Count == 0)
        {
            classNames = ExtractCandidateTestClasses(normalizedFilter);
            if (classNames.Count == 0)
            {
                useBatchedRunner = false;
            }
        }

        invocation = new DotnetTestInvocation(
            Target: target,
            BaseArguments: baseArguments,
            FilterExpression: normalizedFilter,
            ClassNames: classNames,
            UseBatchedRunner: useBatchedRunner,
            ShowProcessMonitor: ReadBool(arguments, false, "showProcessMonitor", "showMonitor"),
            ApplyDefaultArgumentsWhenEmpty: true,
            MaxDurationSec: ReadInt(arguments, 900, "maxDurationSec", "timeoutSec"),
            LogPath: FirstNonEmptyString(arguments, "logPath"),
            ErrorLogPath: FirstNonEmptyString(arguments, "errorLogPath", "stderrPath"),
            StatusPath: FirstNonEmptyString(arguments, "statusPath"),
            ResultsRoot: FirstNonEmptyString(arguments, "resultsRoot"));
        return true;
    }

    public static string? NormalizeFilter(string? rawFilter, IReadOnlyList<string> classNames)
    {
        if (classNames.Count > 0)
        {
            return BuildClassFilter(classNames);
        }

        if (string.IsNullOrWhiteSpace(rawFilter))
        {
            return null;
        }

        var trimmed = rawFilter.Trim();
        if (LooksLikeStructuredFilter(trimmed))
        {
            return trimmed;
        }

        var extracted = ExtractCandidateTestClasses(trimmed);
        return extracted.Count > 0
            ? BuildClassFilter(extracted)
            : trimmed;
    }

    public static string BuildClassFilter(IEnumerable<string> classNames)
    {
        var normalized = classNames
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Select(static value =>
            {
                if (value.StartsWith("FullyQualifiedName~", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("FullyQualifiedName=", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }

                if (value.Contains('=') || value.Contains('~'))
                {
                    return value;
                }

                return "FullyQualifiedName~" + value;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join("|", normalized);
    }

    public static IReadOnlyList<string> ExtractCandidateTestClasses(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Array.Empty<string>();
        }

        var scrubbed = FullyQualifiedNamePrefixRegex.Replace(rawValue, " ");
        scrubbed = scrubbed
            .Replace("|", " ", StringComparison.Ordinal)
            .Replace("&", " ", StringComparison.Ordinal)
            .Replace("(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal)
            .Replace("\"", " ", StringComparison.Ordinal)
            .Replace("'", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(";", " ", StringComparison.Ordinal);

        return TestClassRegex.Matches(scrubbed)
            .Select(static match => match.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadClassNames(IReadOnlyDictionary<string, object> arguments)
    {
        var values = ReadStringList(arguments, "classNames", "testClasses", "classes");
        if (values.Count > 0)
        {
            return values;
        }

        var rawFilter = FirstNonEmptyString(arguments, "filter");
        return ExtractCandidateTestClasses(rawFilter);
    }

    private static List<string> ReadArgumentList(IReadOnlyDictionary<string, object> arguments, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            if (rawValue is string stringValue)
            {
                return CommandLineTokenizer.Split(stringValue).ToList();
            }

            if (rawValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                return CommandLineTokenizer.Split(jsonElement.GetString()).ToList();
            }

            return ReadStringList(rawValue);
        }

        return new List<string>();
    }

    private static bool LooksLikeStructuredFilter(string value)
        => FilterOperatorRegex.IsMatch(value);

    private static string? FirstNonEmptyString(IReadOnlyDictionary<string, object> arguments, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            var value = ReadString(rawValue);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static List<string> ReadStringList(IReadOnlyDictionary<string, object> arguments, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            return ReadStringList(rawValue);
        }

        return new List<string>();
    }

    private static List<string> ReadStringList(object? rawValue)
    {
        if (rawValue is null)
        {
            return new List<string>();
        }

        if (rawValue is string stringValue)
        {
            return ParseStringList(stringValue);
        }

        if (rawValue is JsonElement jsonElement)
        {
            return ReadStringList(jsonElement);
        }

        if (rawValue is IEnumerable<string> stringEnumerable)
        {
            return stringEnumerable
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToList();
        }

        if (rawValue is IEnumerable<object> objectEnumerable)
        {
            return objectEnumerable
                .Select(ReadString)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .ToList();
        }

        return ParseStringList(ReadString(rawValue) ?? string.Empty);
    }

    private static List<string> ReadStringList(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => ReadString(item))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .ToList(),
            JsonValueKind.String => ParseStringList(element.GetString() ?? string.Empty),
            _ => new List<string>()
        };
    }

    private static List<string> ParseStringList(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new List<string>();
        }

        var trimmed = rawValue.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is { Count: > 0 })
                {
                    return parsed
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value.Trim())
                        .ToList();
                }
            }
            catch (JsonException)
            {
            }
        }

        var extractedClasses = ExtractCandidateTestClasses(trimmed);
        if (extractedClasses.Count > 1)
        {
            return extractedClasses.ToList();
        }

        var split = trimmed
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (split.Count > 0)
        {
            return split;
        }

        return new List<string> { trimmed };
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object> arguments, bool fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            switch (rawValue)
            {
                case bool boolValue:
                    return boolValue;
                case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    return element.GetBoolean();
            }

            if (bool.TryParse(ReadString(rawValue), out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object> arguments, int fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            switch (rawValue)
            {
                case int intValue:
                    return intValue;
                case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                    return (int)longValue;
                case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt):
                    return jsonInt;
            }

            if (int.TryParse(ReadString(rawValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static string? ReadString(object? rawValue)
    {
        return rawValue switch
        {
            null => null,
            string stringValue => stringValue,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            _ => Convert.ToString(rawValue, CultureInfo.InvariantCulture)
        };
    }
}
