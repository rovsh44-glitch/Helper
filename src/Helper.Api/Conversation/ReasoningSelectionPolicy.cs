namespace Helper.Api.Conversation;

public sealed class ReasoningSelectionPolicy
{
    private readonly bool _enabled;
    private readonly int _configuredMaxCandidates;

    public ReasoningSelectionPolicy()
    {
        _enabled = ReadFlag("HELPER_REASONING_BRANCH_VERIFY_ENABLED", false);
        _configuredMaxCandidates = ReadInt("HELPER_REASONING_BRANCH_MAX_CANDIDATES", 2, 2, 4);
    }

    public bool ShouldUseBranching(ChatTurnContext context, IReasoningVerifier verifier)
    {
        if (!_enabled || context.RequiresClarification || context.ForceBestEffort)
        {
            return false;
        }

        if (context.Request.Attachments is { Count: > 0 })
        {
            return false;
        }

        if (context.ExecutionMode == TurnExecutionMode.Fast)
        {
            return false;
        }

        var selectedIntent = context.Intent.Intent;
        var selectedWorkload = selectedIntent == Helper.Runtime.Core.IntentType.Research ||
                               context.ExecutionMode == TurnExecutionMode.Deep;
        return selectedWorkload && verifier.IsApplicable(context);
    }

    public int ResolveCandidateCount(ChatTurnContext context)
    {
        var modelBudget = Math.Max(2, context.ModelCallBudget);
        return Math.Min(_configuredMaxCandidates, modelBudget);
    }

    public IReadOnlyList<ReasoningCandidatePlan> BuildCandidatePlans(ChatTurnPreparedInvocation prepared, ChatTurnContext context)
    {
        var plans = new List<ReasoningCandidatePlan>
        {
            new("baseline", string.Empty),
            new("format_guard", "Return only the final answer and obey every explicit output-format constraint exactly."),
            new("verify_then_answer", "Check your draft against every explicit constraint before replying. If you cannot satisfy the format exactly, return a short refusal.")
        };

        return plans.Take(ResolveCandidateCount(context)).ToArray();
    }

    public ReasoningCandidate SelectCandidate(IReadOnlyList<ReasoningCandidate> candidates)
    {
        var approved = candidates.FirstOrDefault(candidate => candidate.Verification.Approved);
        if (approved is not null)
        {
            return approved with { Selected = true };
        }

        var undecided = candidates.FirstOrDefault(candidate => !candidate.Verification.Rejected);
        if (undecided is not null)
        {
            return undecided with { Selected = true };
        }

        return candidates[0] with { Selected = true };
    }

    public static bool HasVerifiablePromptSignal(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        return prompt.Contains("json", StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("pattern", StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("uppercase", StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("exactly one lowercase word", StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("только числом", StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("какой день будет через", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}

