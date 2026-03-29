using System.Text.RegularExpressions;

namespace Helper.Api.Hosting;

internal static partial class RuntimeLogSemanticDeriver
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

    public static RuntimeLogSemanticsDto Derive(string line, string severity, string sourcePath, bool isContinuation)
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

        return new RuntimeLogSemanticsDto(
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

        if (Regex.IsMatch(lower, @"\b(model|gateway|prompt|completion|embedding|catalog|warmup|inference|llm)\b"))
        {
            return "model";
        }

        if (Regex.IsMatch(lower, @"\b(control plane|policy|audit|queue|bootstrap|lifecycle|config(?:uration)?)\b"))
        {
            return "control";
        }

        if (Regex.IsMatch(lower, @"\b(boot|starting|started|listening|ready|readiness|minimal ready|warm ready|application started)\b"))
        {
            return "boot";
        }

        if (Regex.IsMatch(lower, @"\b(http|request|response|endpoint|route|controller|api\b|status code)\b"))
        {
            return lower.Contains("api", StringComparison.OrdinalIgnoreCase) ? "api" : "network";
        }

        if (Regex.IsMatch(lower, @"\b(timeout|dns|socket|connect|network|tcp|tls|websocket|handshake)\b"))
        {
            return "network";
        }

        return "misc";
    }

    private static string InferDomain(string lower, string scope)
    {
        if (scope == "model" || Regex.IsMatch(lower, @"\b(model gateway|catalog refresh|embedding|completion|prompt|warmup)\b"))
        {
            return "gateway";
        }

        if (scope == "storage" || Regex.IsMatch(lower, @"\b(snapshot|journal|persist|qdrant|vector|queue flush|journal write)\b"))
        {
            return "persistence";
        }

        if (scope == "security" || Regex.IsMatch(lower, @"\b(auth|token|credential|apikey|scope\b|forbidden|unauthori)\b"))
        {
            return "auth";
        }

        if (Regex.IsMatch(lower, @"\b(generation|template|forge|compile|build gate|validator|certification)\b"))
        {
            return "generation";
        }

        if (Regex.IsMatch(lower, @"\b(metric|telemetry|trace\b|observability|counter|histogram|gauge)\b"))
        {
            return "telemetry";
        }

        if (scope is "api" or "bus" or "network" || Regex.IsMatch(lower, @"\b(signalr|http|transport|websocket|endpoint|request|response|socket)\b"))
        {
            return "transport";
        }

        if (scope is "boot" or "control" || Regex.IsMatch(lower, @"\b(readiness|lifecycle|minimal ready|warm ready|listening|started)\b"))
        {
            return "readiness";
        }

        return string.IsNullOrWhiteSpace(lower) ? "unknown" : "runtime";
    }

    private static string InferOperationKind(string lower, string sourcePath, int? latencyMs, string? route)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            return "http_request";
        }

        if (latencyMs.HasValue)
        {
            return "timing";
        }

        if (sourcePath.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            return "audit";
        }

        if (Regex.IsMatch(lower, @"\b(indexing|librarian|chunking|parser)\b"))
        {
            return "indexing";
        }

        if (Regex.IsMatch(lower, @"\b(template|forge|compile|promotion|validation_report)\b"))
        {
            return "generation";
        }

        if (Regex.IsMatch(lower, @"\b(model|gateway|warmup|catalog)\b"))
        {
            return "model_gateway";
        }

        if (Regex.IsMatch(lower, @"\b(readiness|listening|minimal ready|warm ready|startup)\b"))
        {
            return "startup";
        }

        if (Regex.IsMatch(lower, @"\b(auth|scope|token|apikey|credential)\b"))
        {
            return "auth";
        }

        if (Regex.IsMatch(lower, @"\b(snapshot|journal|flush|persist|database|qdrant)\b"))
        {
            return "persistence";
        }

        if (Regex.IsMatch(lower, @"\b(metric|telemetry|trace|counter|histogram|gauge)\b"))
        {
            return "telemetry";
        }

        if (Regex.IsMatch(lower, @"\b(exception|fatal|unhandled|traceback|crash)\b"))
        {
            return "exception";
        }

        return "runtime";
    }

    private static string? InferDegradationReason(string lower, string severity)
    {
        if (Regex.IsMatch(lower, @"\bforbidden|unauthori|denied\b"))
        {
            return "access_denied";
        }

        if (Regex.IsMatch(lower, @"\btimeout|timed out|deadline exceeded\b"))
        {
            return "timeout";
        }

        if (Regex.IsMatch(lower, @"\bdegraded|fallback|best-effort|best effort\b"))
        {
            return "degraded_mode";
        }

        if (Regex.IsMatch(lower, @"\bcompile gate failed|compile failed|build failed\b"))
        {
            return "compile_failed";
        }

        if (Regex.IsMatch(lower, @"\bnot found|missing\b"))
        {
            return "missing_dependency";
        }

        if (string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
        {
            return "runtime_error";
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
            markers.Add($"route:{TrimMarker(route, 24)}");
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            markers.Add($"corr:{TrimMarker(correlationId, 18)}");
        }

        if (latencyMs.HasValue)
        {
            markers.Add($"latency:{ResolveLatencyBucket(latencyMs)}");
        }

        if (!string.IsNullOrWhiteSpace(degradationReason))
        {
            markers.Add($"degraded:{degradationReason}");
        }

        var statusCode = ExtractStatusCode(message);
        if (!string.IsNullOrWhiteSpace(statusCode))
        {
            markers.Add($"http:{statusCode}");
        }

        var jsonKeys = JsonKeyRegex().Matches(message)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);
        markers.AddRange(jsonKeys.Select(static key => $"json:{key.ToLowerInvariant()}"));

        if (isContinuation)
        {
            markers.Add("continuation");
        }

        return markers.Count == 0
            ? Array.Empty<string>()
            : markers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildSummary(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "blank line";
        }

        var summary = message
            .Replace('\t', ' ')
            .Trim()
            .Replace("  ", " ");

        if (summary.Length <= 140)
        {
            return summary;
        }

        return summary[..140].TrimEnd() + " ...";
    }

    private static int? ExtractLatencyMs(string message)
    {
        var msMatch = MillisecondsRegex().Match(message);
        if (msMatch.Success && double.TryParse(msMatch.Groups[1].Value, out var ms))
        {
            return Math.Max(0, (int)Math.Round(ms));
        }

        var sMatch = SecondsRegex().Match(message);
        if (sMatch.Success && double.TryParse(sMatch.Groups[1].Value, out var seconds))
        {
            return Math.Max(0, (int)Math.Round(seconds * 1000));
        }

        return null;
    }

    private static string? ExtractStatusCode(string message)
    {
        var match = StatusCodeRegex().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractCorrelationId(string message)
    {
        var named = NamedCorrelationRegex().Match(message);
        if (named.Success)
        {
            return named.Groups[1].Value;
        }

        var fallback = GuidCorrelationRegex().Match(message);
        return fallback.Success ? fallback.Groups[1].Value : null;
    }

    private static string? ExtractRoute(string message)
    {
        var methodMatch = MethodRouteRegex().Match(message);
        if (methodMatch.Success)
        {
            return methodMatch.Groups[1].Value;
        }

        var namedMatch = NamedRouteRegex().Match(message);
        return namedMatch.Success ? namedMatch.Groups[1].Value : null;
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
            < 1500 => "500_1500ms",
            < 5000 => "1500_5000ms",
            _ => "5000_plus"
        };
    }

    private static string TrimMarker(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value.ToLowerInvariant();
        }

        var head = Math.Max(4, (maxLength - 3) / 2);
        var tail = Math.Max(3, maxLength - head - 3);
        return $"{value[..head].ToLowerInvariant()}...{value[^tail..].ToLowerInvariant()}";
    }
}

