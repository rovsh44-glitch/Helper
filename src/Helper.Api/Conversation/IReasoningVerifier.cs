namespace Helper.Api.Conversation;

public interface IReasoningVerifier
{
    bool IsApplicable(ChatTurnContext context);
    Task<ReasoningVerificationReport> VerifyAsync(ChatTurnContext context, string candidateOutput, CancellationToken ct);
}

