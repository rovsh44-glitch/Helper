using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed record LocalBaselineAnswerResult(
    string Answer,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Trace,
    IReadOnlyList<ResearchEvidenceItem> EvidenceItems);

public interface ILocalBaselineAnswerDiagnostics
{
    Task<LocalBaselineAnswerResult> GenerateDetailedAsync(string topic, CancellationToken ct = default);
}

public static class LocalBaselineAnswerServiceSupport
{
    public static async Task<LocalBaselineAnswerResult> GenerateDetailedAsync(
        ILocalBaselineAnswerService service,
        string topic,
        CancellationToken ct = default)
    {
        if (service is ILocalBaselineAnswerDiagnostics diagnostics)
        {
            return await diagnostics.GenerateDetailedAsync(topic, ct).ConfigureAwait(false);
        }

        var answer = await service.GenerateAsync(topic, ct).ConfigureAwait(false);
        return new LocalBaselineAnswerResult(
            answer,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<ResearchEvidenceItem>());
    }
}

