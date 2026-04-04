namespace Helper.RuntimeLogSemantics;

public sealed record RuntimeLogSemanticSnapshot(
    string Scope,
    string Domain,
    string OperationKind,
    string Summary,
    string? Route = null,
    string? CorrelationId = null,
    int? LatencyMs = null,
    string? LatencyBucket = null,
    string? DegradationReason = null,
    IReadOnlyList<string>? Markers = null,
    bool Structured = true);
