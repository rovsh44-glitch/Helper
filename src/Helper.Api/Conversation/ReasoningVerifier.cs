using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed class ReasoningVerifier : IReasoningVerifier
{
    private readonly StructuredOutputVerifier _structuredVerifier;

    public ReasoningVerifier(StructuredOutputVerifier structuredVerifier)
    {
        _structuredVerifier = structuredVerifier;
    }

    public bool IsApplicable(ChatTurnContext context)
    {
        return ReasoningSelectionPolicy.HasVerifiablePromptSignal(context.Request.Message);
    }

    public Task<ReasoningVerificationReport> VerifyAsync(ChatTurnContext context, string candidateOutput, CancellationToken ct)
    {
        var candidateContext = new ChatTurnContext
        {
            TurnId = context.TurnId,
            Request = context.Request,
            Conversation = context.Conversation,
            History = context.History,
            Intent = context.Intent,
            IntentConfidence = context.IntentConfidence,
            IntentSource = context.IntentSource,
            ExecutionMode = context.ExecutionMode,
            BudgetProfile = context.BudgetProfile,
            IsFactualPrompt = context.IsFactualPrompt,
            ExecutionOutput = candidateOutput,
            TokenBudget = context.TokenBudget
        };

        foreach (var signal in context.IntentSignals)
        {
            candidateContext.IntentSignals.Add(signal);
        }

        return _structuredVerifier.VerifyAsync(candidateContext, ct);
    }
}

