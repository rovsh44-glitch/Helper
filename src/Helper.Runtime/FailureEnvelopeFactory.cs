using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed class FailureEnvelopeFactory : IFailureEnvelopeFactory
{
    public IReadOnlyList<FailureEnvelope> FromBuildErrors(
        FailureStage stage,
        string subsystem,
        IReadOnlyList<BuildError> errors,
        string? correlationId = null)
    {
        if (errors.Count == 0)
        {
            return new[]
            {
                new FailureEnvelope(
                    stage,
                    subsystem,
                    "UNKNOWN",
                    RootCauseClass.Unknown,
                    Retryable: true,
                    UserAction: "Retry request with the same prompt. If the issue repeats, provide the full error payload.",
                    Evidence: "Build failed without explicit errors.",
                    CorrelationId: NormalizeCorrelation(correlationId))
            };
        }

        return errors
            .Select(error => new FailureEnvelope(
                stage,
                subsystem,
                string.IsNullOrWhiteSpace(error.Code) ? "UNKNOWN" : error.Code,
                MapRootCause(error.Code, error.Message),
                Retryable: IsRetryable(error.Code, error.Message),
                UserAction: BuildUserAction(error.Code, error.Message),
                Evidence: NormalizeMessage(error.Message),
                CorrelationId: NormalizeCorrelation(correlationId)))
            .ToList();
    }

    public FailureEnvelope FromException(FailureStage stage, string subsystem, Exception exception, string? correlationId = null)
    {
        var code = exception is OperationCanceledException ? "OPERATION_CANCELED" : "UNHANDLED_EXCEPTION";
        return new FailureEnvelope(
            Stage: stage,
            Subsystem: subsystem,
            ErrorCode: code,
            RootCauseClass: exception is OperationCanceledException ? RootCauseClass.Timeout : RootCauseClass.Runtime,
            Retryable: exception is OperationCanceledException,
            UserAction: exception is OperationCanceledException
                ? "Retry with a smaller scope or increase timeout budget."
                : "Retry request. If it fails again, attach logs with correlation id.",
            Evidence: NormalizeMessage(exception.Message),
            CorrelationId: NormalizeCorrelation(correlationId));
    }

    private static RootCauseClass MapRootCause(string? code, string? message)
    {
        if (ContainsAny(code, "TIMEOUT", "GENERATION_TIMEOUT", "GENERATION_STAGE_TIMEOUT") ||
            ContainsAny(message, "timeout", "timed out", "task was canceled"))
        {
            return RootCauseClass.Timeout;
        }

        if (ContainsAny(code, "CS0246", "CS0234", "CS0012", "NU1101", "NU1102", "NU1605") ||
            ContainsAny(message, "nuget", "package", "dependency", "restore", "assembly reference"))
        {
            return RootCauseClass.Dependency;
        }

        if (ContainsAny(code, "CS", "DUPLICATE_SIGNATURE") ||
            ContainsAny(message, "duplicate method signatures"))
        {
            return RootCauseClass.Compilation;
        }

        if (ContainsAny(code, "TEMPLATE_NOT_FOUND", "TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS", "VALIDATION", "FORMAT"))
        {
            return RootCauseClass.Validation;
        }

        if (ContainsAny(message, "access denied", "unauthorized", "forbidden"))
        {
            return RootCauseClass.Permission;
        }

        if (ContainsAny(message, "qdrant", "network", "connection refused", "service unavailable", "dns", "socket", "http request"))
        {
            return RootCauseClass.ExternalService;
        }

        if (ContainsAny(code, "UNHANDLED_EXCEPTION", "RUNTIME") ||
            ContainsAny(message, "object reference", "nullreference", "invalid operation", "runtime"))
        {
            return RootCauseClass.Runtime;
        }

        return RootCauseClass.Unknown;
    }

    private static bool IsRetryable(string? code, string? message)
    {
        if (ContainsAny(message, "access denied", "unauthorized", "forbidden"))
        {
            return false;
        }

        return ContainsAny(code, "TIMEOUT", "OPERATION_CANCELED", "GENERATION_TIMEOUT", "GENERATION_STAGE_TIMEOUT") ||
               ContainsAny(message, "timeout", "temporar", "rate limit", "task was canceled", "network", "connection refused", "service unavailable");
    }

    private static string BuildUserAction(string? code, string? message)
    {
        if (ContainsAny(code, "TIMEOUT") || ContainsAny(message, "timeout", "task was canceled"))
        {
            return "Retry with reduced scope or increase generation timeout budget.";
        }

        if (ContainsAny(code, "CS0246", "CS0234", "CS0012", "NU1101", "NU1102", "NU1605") ||
            ContainsAny(message, "package", "dependency", "assembly reference"))
        {
            return "Check package/reference graph and rerun generation or autofix.";
        }

        if (ContainsAny(code, "CS"))
        {
            return "Inspect compile diagnostics in generated project and rerun generation.";
        }

        if (ContainsAny(message, "access denied", "unauthorized", "forbidden"))
        {
            return "Check path and permission settings, then rerun generation.";
        }

        if (ContainsAny(message, "qdrant", "network", "connection refused", "service unavailable", "dns", "socket"))
        {
            return "Verify external service/network availability, then retry.";
        }

        if (ContainsAny(code, "TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS"))
        {
            return "Refresh template certification status or adjust template routing certification filters, then retry.";
        }

        if (ContainsAny(code, "TEMPLATE_NOT_FOUND"))
        {
            return "Install or restore the missing template, then retry.";
        }

        return "Retry request. If it repeats, provide logs and correlation id.";
    }

    private static string NormalizeCorrelation(string? correlationId)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")[..12]
            : correlationId.Trim();
    }

    private static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "No detailed error message.";
        }

        var normalized = message.Trim();
        if (string.Equals(normalized, "A task was canceled.", StringComparison.OrdinalIgnoreCase))
        {
            return "Operation was canceled due to timeout or external cancellation.";
        }

        return normalized;
    }

    private static bool ContainsAny(string? source, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        foreach (var needle in needles)
        {
            if (source.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

