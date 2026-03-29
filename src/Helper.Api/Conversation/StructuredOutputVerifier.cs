using System.Text;

namespace Helper.Api.Conversation;

public enum ReasoningVerificationStatus
{
    NotApplicable,
    Approved,
    Rejected
}

public sealed record ReasoningVerifierResult(
    string VerifierName,
    ReasoningVerificationStatus Status,
    string Summary,
    string? CorrectedContent = null,
    IReadOnlyList<string>? Flags = null);

public sealed record ReasoningVerificationReport(
    bool Applied,
    bool Approved,
    bool Rejected,
    string Summary,
    string? CorrectedContent,
    IReadOnlyList<string> Trace,
    IReadOnlyList<string> Flags);

public interface IReasoningOutputVerifier
{
    int Priority { get; }
    ValueTask<ReasoningVerifierResult> VerifyAsync(ChatTurnContext context, CancellationToken ct);
}

public sealed class StructuredOutputVerifier
{
    private readonly IReadOnlyList<IReasoningOutputVerifier> _verifiers;

    public StructuredOutputVerifier(IEnumerable<IReasoningOutputVerifier> verifiers)
    {
        _verifiers = verifiers
            .OrderBy(verifier => verifier.Priority)
            .ToArray();
    }

    public async Task<ReasoningVerificationReport> VerifyAsync(ChatTurnContext context, CancellationToken ct)
    {
        var trace = new List<string>();
        var flags = new List<string>();
        var applied = false;

        foreach (var verifier in _verifiers)
        {
            var result = await verifier.VerifyAsync(context, ct).ConfigureAwait(false);
            if (result.Status == ReasoningVerificationStatus.NotApplicable)
            {
                continue;
            }

            applied = true;
            trace.Add($"{result.VerifierName}:{result.Status}:{result.Summary}");
            if (result.Flags is { Count: > 0 })
            {
                flags.AddRange(result.Flags);
            }

            if (result.Status == ReasoningVerificationStatus.Rejected)
            {
                return new ReasoningVerificationReport(
                    Applied: true,
                    Approved: false,
                    Rejected: true,
                    Summary: result.Summary,
                    CorrectedContent: result.CorrectedContent,
                    Trace: trace,
                    Flags: flags);
            }

            if (result.Status == ReasoningVerificationStatus.Approved)
            {
                return new ReasoningVerificationReport(
                    Applied: true,
                    Approved: true,
                    Rejected: false,
                    Summary: result.Summary,
                    CorrectedContent: result.CorrectedContent,
                    Trace: trace,
                    Flags: flags);
            }
        }

        return new ReasoningVerificationReport(
            Applied: applied,
            Approved: false,
            Rejected: false,
            Summary: applied ? "Verifier chain completed without decisive verdict." : "No local verifier matched this turn.",
            CorrectedContent: null,
            Trace: trace,
            Flags: flags);
    }

    public static string BuildRejectedResponse(string rawOutput, string summary)
    {
        var preview = (rawOutput ?? string.Empty).Trim();
        if (preview.Length > 220)
        {
            preview = preview[..220].TrimEnd() + "...";
        }

        var builder = new StringBuilder();
        builder.Append("Local verification rejected the generated output: ")
            .Append(summary)
            .AppendLine();
        builder.Append("Please request a retry or provide a narrower constraint.");

        if (!string.IsNullOrWhiteSpace(preview))
        {
            builder.AppendLine()
                .AppendLine()
                .Append("Rejected draft preview: ")
                .Append(preview);
        }

        return builder.ToString();
    }
}

