using System.Text.RegularExpressions;

namespace Helper.RuntimeLogSemantics;

public static partial class RuntimeLogSemanticDeriver
{
    [GeneratedRegex(@"\b(\d+(?:\.\d+)?)\s*ms\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MillisecondsRegex();

    [GeneratedRegex(@"\b(\d+(?:\.\d+)?)\s*s\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecondsRegex();

    [GeneratedRegex(@"\b(?:status(?:code)?|http)\D{0,10}([1-5]\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatusCodeRegex();

    [GeneratedRegex(@"\b(?:corr(?:elation)?[-_ ]?id|trace[-_ ]?id|request[-_ ]?id)\s*[:=]\s*([a-z0-9-]{6,})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedCorrelationRegex();

    [GeneratedRegex(@"\b([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuidCorrelationRegex();

    [GeneratedRegex(@"\b(?:GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+([\/a-z0-9._?=&%-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MethodRouteRegex();

    [GeneratedRegex(@"\b(?:route|path|endpoint)\s*[:=]\s*([\/a-z0-9._?=&%-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedRouteRegex();

    [GeneratedRegex(@"""([a-z0-9_.-]{2,24})""\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonKeyRegex();

    public static RuntimeLogSemanticSnapshot Derive(string line, string severity, string sourcePath, bool isContinuation)
    {
        var message = line ?? string.Empty;
        var lower = $"{sourcePath} {message}".ToLowerInvariant();
        var latencyMs = ExtractLatencyMs(message);
        var route = ExtractRoute(message);
        var correlationId = ExtractCorrelationId(message);
        var operationKind = InferOperationKind(lower, sourcePath, latencyMs, route);
        var scope = InferScope(lower, severity);
        var domain = InferDomain(lower, scope);
        var degradationReason = InferDegradationReason(lower, severity);
        var markers = BuildMarkers(message, latencyMs, route, correlationId, degradationReason, isContinuation);

        return new RuntimeLogSemanticSnapshot(
            Scope: scope,
            Domain: domain,
            OperationKind: operationKind,
            Summary: BuildSummary(message),
            Route: route,
            CorrelationId: correlationId,
            LatencyMs: latencyMs,
            LatencyBucket: ResolveLatencyBucket(latencyMs),
            DegradationReason: degradationReason,
            Markers: markers,
            Structured: true);
    }

    private static string InferScope(string lower, string severity)
    {
        if (string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase) &&
            (lower.Contains("exception", StringComparison.OrdinalIgnoreCase) || lower.Contains("traceback", StringComparison.OrdinalIgnoreCase)))
        {
            return "exception";
        }

        if (Regex.IsMatch(lower, @"\b(auth|token|api[_ -]?key|bearer|scope\b|401\b|403\b|unauthori|forbidden|denied|secret|credential)\b"))
        {
            return "security";
        }

        if (Regex.IsMatch(lower, @"\b(qdrant|sqlite|persist|snapshot|journal|database|vector|storage|disk)\b"))
        {
            return "storage";
        }

        if (Regex.IsMatch(lower, @"\b(signalr|hub|event bus|message bus|broadcast|stream\b|bus)\b"))
        {
            return "bus";
        }

        if (Regex.IsMatch(lower, @"\b(build|compile|test|lint|restore|publish|msbuild|dotnet)\b"))
        {
            return "build";
        }

        if (Regex.IsMatch(lower, @"\b(model|gateway|prompt|completion|embedding|warmup|inference|llm)\b"))
        {
            return "model";
        }

        if (Regex.IsMatch(lower, @"\b(get|post|put|patch|delete|endpoint|status=|http|request|response|latency|route)\b"))
        {
            return "http";
        }

        return "runtime";
    }

    private static string InferDomain(string lower, string scope)
    {
        if (Regex.IsMatch(lower, @"\b(chat|conversation|turn|clarification|intent|finalizer|response)\b"))
        {
            return "conversation";
        }

        if (Regex.IsMatch(lower, @"\b(template|forge|golden|compile gate|artifact|promotion|generation)\b"))
        {
            return "generation";
        }

        if (Regex.IsMatch(lower, @"\b(research|search|citation|source|evidence|grounding|web)\b"))
        {
            return "research";
        }

        if (Regex.IsMatch(lower, @"\b(runtime review|review slice|readiness|startup|warmup)\b"))
        {
            return "runtime_review";
        }

        return scope switch
        {
            "storage" => "storage",
            "bus" => "transport",
            "security" => "security",
            _ => "runtime"
        };
    }

    private static string InferOperationKind(string lower, string sourcePath, int? latencyMs, string? route)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            return "http_request";
        }

        if (Regex.IsMatch(lower, @"\b(compile gate|msbuild|dotnet build|restore)\b"))
        {
            return "build";
        }

        if (Regex.IsMatch(lower, @"\b(dotnet test|passed!|failed!|xunit|testhost)\b"))
        {
            return "test";
        }

        if (Regex.IsMatch(lower, @"\b(indexing|chunk|parse|librarian|knowledge)\b"))
        {
            return "indexing";
        }

        if (Regex.IsMatch(lower, @"\b(search|query|fetch|page|citation|grounding)\b"))
        {
            return "research";
        }

        if (Regex.IsMatch(lower, @"\b(generate|template|promotion|artifact|forge)\b"))
        {
            return "generation";
        }

        if (latencyMs.HasValue && latencyMs.Value > 0)
        {
            return "timed_operation";
        }

        return sourcePath.Contains("runtime", StringComparison.OrdinalIgnoreCase)
            ? "runtime"
            : "event";
    }

    private static string? InferDegradationReason(string lower, string severity)
    {
        if (Regex.IsMatch(lower, @"\b(timeout|timed out|deadline exceeded)\b"))
        {
            return "timeout";
        }

        if (Regex.IsMatch(lower, @"\b(compile gate failed|build failed|restore failed)\b"))
        {
            return "compile_failed";
        }

        if (Regex.IsMatch(lower, @"\b(fallback|degraded|orphan risk)\b"))
        {
            return "degraded";
        }

        if (string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        return null;
    }

    private static IReadOnlyList<string> BuildMarkers(
        string message,
        int? latencyMs,
        string? route,
        string? correlationId,
        string? degradationReason,
        bool isContinuation)
    {
        var markers = new List<string>();

        if (!string.IsNullOrWhiteSpace(route))
        {
            markers.Add($"route:{route}");
        }

        if (latencyMs.HasValue)
        {
            markers.Add($"latency:{ResolveLatencyBucket(latencyMs)}");
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            markers.Add($"correlation:{correlationId}");
        }

        if (!string.IsNullOrWhiteSpace(degradationReason))
        {
            markers.Add($"degradation:{degradationReason}");
        }

        if (isContinuation)
        {
            markers.Add("continuation");
        }

        foreach (Match match in JsonKeyRegex().Matches(message))
        {
            markers.Add($"json:{match.Groups[1].Value}");
        }

        return markers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildSummary(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(empty)";
        }

        var normalized = message.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 120 ? normalized : normalized[..120].TrimEnd() + " ...";
    }

    private static int? ExtractLatencyMs(string message)
    {
        var milliseconds = MillisecondsRegex().Match(message);
        if (milliseconds.Success &&
            double.TryParse(milliseconds.Groups[1].Value, out var parsedMs))
        {
            return (int)Math.Round(parsedMs);
        }

        var seconds = SecondsRegex().Match(message);
        if (seconds.Success &&
            double.TryParse(seconds.Groups[1].Value, out var parsedSeconds))
        {
            return (int)Math.Round(parsedSeconds * 1000);
        }

        return null;
    }

    private static string? ResolveLatencyBucket(int? latencyMs)
    {
        if (!latencyMs.HasValue)
        {
            return null;
        }

        return latencyMs.Value switch
        {
            < 100 => "sub_100ms",
            < 500 => "100_500ms",
            < 1000 => "500ms_1s",
            < 3000 => "1_3s",
            _ => "over_3s"
        };
    }

    private static string? ExtractRoute(string message)
    {
        var methodRoute = MethodRouteRegex().Match(message);
        if (methodRoute.Success)
        {
            return methodRoute.Groups[1].Value;
        }

        var namedRoute = NamedRouteRegex().Match(message);
        return namedRoute.Success ? namedRoute.Groups[1].Value : null;
    }

    private static string? ExtractCorrelationId(string message)
    {
        var named = NamedCorrelationRegex().Match(message);
        if (named.Success)
        {
            return named.Groups[1].Value;
        }

        var guid = GuidCorrelationRegex().Match(message);
        return guid.Success ? guid.Groups[1].Value : null;
    }
}
