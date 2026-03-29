namespace Helper.Runtime.WebResearch.Extraction;

public interface IRemoteDocumentExtractor
{
    Task<RemoteDocumentExtractionResult> ExtractAsync(
        Uri requestedUri,
        Uri resolvedUri,
        string? contentType,
        byte[] contentBytes,
        CancellationToken ct = default);
}

public sealed record RemoteDocumentExtractionResult(
    bool Handled,
    bool Success,
    string Outcome,
    ExtractedWebPage? ExtractedPage,
    IReadOnlyList<string> Trace);

internal sealed class NoopRemoteDocumentExtractor : IRemoteDocumentExtractor
{
    public static NoopRemoteDocumentExtractor Instance { get; } = new();

    private NoopRemoteDocumentExtractor()
    {
    }

    public Task<RemoteDocumentExtractionResult> ExtractAsync(
        Uri requestedUri,
        Uri resolvedUri,
        string? contentType,
        byte[] contentBytes,
        CancellationToken ct = default)
    {
        return Task.FromResult(new RemoteDocumentExtractionResult(
            Handled: false,
            Success: false,
            Outcome: "not_supported",
            ExtractedPage: null,
            Trace: new[]
            {
                $"web_document_extract.skipped reason=noop requested={requestedUri} resolved={resolvedUri} content_type={contentType ?? "unknown"}"
            }));
    }
}

