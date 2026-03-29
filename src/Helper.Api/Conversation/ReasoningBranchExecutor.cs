using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Conversation;

public interface IReasoningBranchExecutor
{
    bool ShouldUseBranching(ChatTurnContext context);
    Task<string> ExecuteAsync(ChatTurnPreparedInvocation prepared, ChatTurnContext context, CancellationToken ct);
}

public sealed class ReasoningBranchExecutor : IReasoningBranchExecutor
{
    private readonly IModelGateway _modelGateway;
    private readonly IChatResiliencePolicy _resilience;
    private readonly IReasoningVerifier _verifier;
    private readonly ReasoningSelectionPolicy _selectionPolicy;

    public ReasoningBranchExecutor(
        IModelGateway modelGateway,
        IChatResiliencePolicy resilience,
        IReasoningVerifier verifier,
        ReasoningSelectionPolicy selectionPolicy)
    {
        _modelGateway = modelGateway;
        _resilience = resilience;
        _verifier = verifier;
        _selectionPolicy = selectionPolicy;
    }

    public bool ShouldUseBranching(ChatTurnContext context)
        => _selectionPolicy.ShouldUseBranching(context, _verifier);

    public async Task<string> ExecuteAsync(ChatTurnPreparedInvocation prepared, ChatTurnContext context, CancellationToken ct)
    {
        var plans = _selectionPolicy.BuildCandidatePlans(prepared, context);
        var candidates = new List<ReasoningCandidate>(plans.Count);
        context.ReasoningBranchingApplied = true;
        context.ReasoningCandidateTrace.Clear();
        context.ReasoningCandidatesGenerated = 0;
        context.ReasoningCandidatesRejected = 0;
        context.ReasoningModelCallsUsed = 0;
        context.ApproximateReasoningTokenCost = 0;

        foreach (var plan in plans)
        {
            var systemInstruction = string.IsNullOrWhiteSpace(plan.InstructionSuffix)
                ? prepared.SystemInstruction
                : $"{prepared.SystemInstruction} {plan.InstructionSuffix}".Trim();
            var output = await _resilience.ExecuteAsync(
                    $"llm.branch.{plan.StrategyId}",
                    retryCt => _modelGateway.AskAsync(
                        new ModelGatewayRequest(
                            prepared.Prompt,
                            ChatTurnExecutionSupport.ResolveModelClass(context),
                            ModelExecutionPool.Interactive,
                            PreferredModel: prepared.PreferredModel,
                            SystemInstruction: systemInstruction),
                        retryCt),
                    ct)
                .ConfigureAwait(false);
            context.ReasoningModelCallsUsed++;

            var verification = await _verifier.VerifyAsync(context, output, ct).ConfigureAwait(false);
            var candidate = new ReasoningCandidate(
                plan.StrategyId,
                output,
                verification,
                TokenBudgetEstimator.Estimate(output),
                Selected: false);
            candidates.Add(candidate);
            context.ReasoningCandidatesGenerated++;
            context.ApproximateReasoningTokenCost += candidate.EstimatedTokens;
            if (verification.Applied)
            {
                context.LocalVerificationAppliedCount++;
            }

            if (verification.Approved)
            {
                context.LocalVerificationPassCount++;
            }

            if (verification.Rejected)
            {
                context.ReasoningCandidatesRejected++;
                context.LocalVerificationRejectCount++;
            }

            context.ReasoningCandidateTrace.Add($"{plan.StrategyId}:{DescribeVerification(verification)}");
            if (verification.Approved)
            {
                break;
            }
        }

        var selected = _selectionPolicy.SelectCandidate(candidates);
        context.SelectedReasoningStrategy = selected.StrategyId;

        if (selected.Verification.Rejected)
        {
            context.UncertaintyFlags.Add("reasoning_branch_all_candidates_rejected");
            return StructuredOutputVerifier.BuildRejectedResponse(selected.Output, selected.Verification.Summary);
        }

        return selected.Output;
    }

    private static string DescribeVerification(ReasoningVerificationReport verification)
    {
        if (verification.Approved)
        {
            return $"approved:{verification.Summary}";
        }

        if (verification.Rejected)
        {
            return $"rejected:{verification.Summary}";
        }

        return $"undecided:{verification.Summary}";
    }
}

