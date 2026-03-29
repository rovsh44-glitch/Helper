using Helper.Runtime.Core;
using Helper.Api.Hosting;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Conversation;

public sealed class ChatTurnCritic : IChatTurnCritic
{
    private readonly ICriticService _criticService;
    private readonly StructuredOutputVerifier _localVerifier;
    private readonly IChatResiliencePolicy _resilience;
    private readonly IChatResilienceTelemetryService _telemetry;
    private readonly ICriticRiskPolicy _riskPolicy;
    private readonly IBackendRuntimePolicyProvider _policyProvider;
    private readonly ILogger<ChatTurnCritic> _logger;

    public ChatTurnCritic(
        ICriticService criticService,
        StructuredOutputVerifier localVerifier,
        IChatResiliencePolicy resilience,
        IChatResilienceTelemetryService telemetry,
        ICriticRiskPolicy? riskPolicy,
        IBackendRuntimePolicyProvider? policyProvider,
        ILogger<ChatTurnCritic> logger)
    {
        _criticService = criticService;
        _localVerifier = localVerifier;
        _resilience = resilience;
        _telemetry = telemetry;
        _riskPolicy = riskPolicy ?? new CriticRiskPolicy();
        _policyProvider = policyProvider ?? new BackendOptionsCatalog(new ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "dev-key"));
        _logger = logger;
    }

    public ChatTurnCritic(
        ICriticService criticService,
        IChatResiliencePolicy resilience,
        IChatResilienceTelemetryService telemetry,
        ICriticRiskPolicy? riskPolicy,
        IBackendRuntimePolicyProvider? policyProvider,
        ILogger<ChatTurnCritic> logger)
        : this(
            criticService,
            new StructuredOutputVerifier(Array.Empty<IReasoningOutputVerifier>()),
            resilience,
            telemetry,
            riskPolicy,
            policyProvider,
            logger)
    {
    }

    public ChatTurnCritic(
        ICriticService criticService,
        StructuredOutputVerifier localVerifier,
        IChatResiliencePolicy resilience,
        IChatResilienceTelemetryService telemetry,
        ICriticRiskPolicy? riskPolicy,
        ILogger<ChatTurnCritic> logger)
        : this(criticService, localVerifier, resilience, telemetry, riskPolicy, null, logger)
    {
    }

    public ChatTurnCritic(
        ICriticService criticService,
        IChatResiliencePolicy resilience,
        IChatResilienceTelemetryService telemetry,
        ICriticRiskPolicy? riskPolicy,
        ILogger<ChatTurnCritic> logger)
        : this(
            criticService,
            new StructuredOutputVerifier(Array.Empty<IReasoningOutputVerifier>()),
            resilience,
            telemetry,
            riskPolicy,
            null,
            logger)
    {
    }

    public async Task CritiqueAsync(ChatTurnContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.ExecutionOutput))
        {
            context.IsCritiqueApproved = false;
            context.CritiqueFeedback = "Empty execution output.";
            context.CorrectedContent = "I could not generate a reliable answer for this turn. Please rephrase your request.";
            return;
        }

        if (!_policyProvider.GetPolicies().SynchronousCriticEnabled)
        {
            context.IsCritiqueApproved = true;
            context.CritiqueFeedback = "Synchronous critic disabled by runtime policy.";
            context.CorrectedContent = context.ExecutionOutput;
            context.UncertaintyFlags.Add("sync_critic_disabled");
            return;
        }

        var verification = await _localVerifier.VerifyAsync(context, ct).ConfigureAwait(false);
        context.LocalVerificationApplied = verification.Applied;
        context.LocalVerificationPassed = verification.Approved;
        context.LocalVerificationSummary = verification.Summary;
        context.LocalVerificationTrace.Clear();
        context.LocalVerificationTrace.AddRange(verification.Trace);
        if (verification.Flags.Count > 0)
        {
            context.UncertaintyFlags.AddRange(verification.Flags);
        }

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
            context.LocalVerificationRejectCount++;
        }

        if (verification.Rejected)
        {
            context.IsCritiqueApproved = false;
            context.CritiqueFeedback = $"Local verifier rejected the draft. {verification.Summary}";
            context.CorrectedContent = verification.CorrectedContent ?? StructuredOutputVerifier.BuildRejectedResponse(context.ExecutionOutput, verification.Summary);
            context.Confidence = Math.Min(context.Confidence, 0.34);
            context.NextStep = "Retry with a narrower prompt or ask for a reformatted answer.";
            return;
        }

        if (verification.Approved)
        {
            context.IsCritiqueApproved = true;
            context.CritiqueFeedback = $"Local verifier approved the draft. {verification.Summary}";
            context.CorrectedContent = context.ExecutionOutput;
            context.Confidence = Math.Max(context.Confidence, 0.86);
            return;
        }

        // Deterministic tool outcomes should not wait on an additional LLM critique call.
        if (context.ToolCalls.Any(x => string.Equals(x, "helper.generate", StringComparison.OrdinalIgnoreCase)))
        {
            context.IsCritiqueApproved = true;
            context.CritiqueFeedback = "Critic skipped for deterministic generation outcome.";
            context.CorrectedContent = context.ExecutionOutput;
            context.Confidence = Math.Max(context.Confidence, 0.82);
            return;
        }

        // Research turns are already grounded by source/citation pipeline.
        // Skipping synchronous critic here prevents deterministic timeout spikes under eval load.
        if (context.Intent.Intent == IntentType.Research)
        {
            context.IsCritiqueApproved = true;
            context.CritiqueFeedback = "Critic skipped for bounded-latency research execution.";
            context.CorrectedContent = context.ExecutionOutput;
            context.Confidence = Math.Max(context.Confidence, 0.74);
            return;
        }

        try
        {
            var critique = await _resilience.ExecuteAsync(
                "critic.evaluate",
                retryCt => _criticService.CritiqueAsync(
                    context.Request.Message,
                    context.ExecutionOutput,
                    "Chat Turn",
                    retryCt),
                ct);

            context.IsCritiqueApproved = critique.IsApproved;
            context.CritiqueFeedback = critique.Feedback;
            context.CorrectedContent = critique.CorrectedContent;
            context.Confidence = critique.IsApproved ? 0.82 : 0.56;
        }
        catch (Exception ex)
        {
            var riskTier = _riskPolicy.Evaluate(context);
            if (_riskPolicy.AllowFailOpen(riskTier))
            {
                // Fail-open is allowed only for low/medium risk tiers.
                _logger.LogWarning(ex, "Critic degraded for turn {TurnId}. RiskTier={RiskTier}. Returning execution output as-is.", context.TurnId, riskTier);
                _telemetry.RecordFallback("critic_unavailable");
                context.IsCritiqueApproved = true;
                context.CritiqueFeedback = "Critic unavailable. Returned response without critic validation.";
                context.CorrectedContent = context.ExecutionOutput;
                context.Confidence = Math.Min(context.Confidence, 0.58);
                context.UncertaintyFlags.Add("critic_unavailable");
                return;
            }

            _logger.LogWarning(ex, "Critic unavailable in high-risk mode for turn {TurnId}. Switching to fail-safe guard.", context.TurnId);
            _telemetry.RecordFallback("critic_unavailable_fail_safe");
            context.IsCritiqueApproved = false;
            context.CritiqueFeedback = "Critic unavailable for high-risk turn. Returned fail-safe guarded response.";
            context.CorrectedContent = BuildFailSafeGuardedResponse(context.ExecutionOutput);
            context.Confidence = Math.Min(context.Confidence, 0.32);
            context.UncertaintyFlags.Add("critic_unavailable_high_risk");
            context.UncertaintyFlags.Add("critic_fail_safe_guarded");
            context.NextStep = "Provide explicit scope/constraints to continue safely.";
        }
    }

    private static string BuildFailSafeGuardedResponse(string rawOutput)
    {
        var preview = string.IsNullOrWhiteSpace(rawOutput)
            ? string.Empty
            : rawOutput.Trim();
        if (preview.Length > 300)
        {
            preview = preview[..300].TrimEnd() + "...";
        }

        if (string.IsNullOrWhiteSpace(preview))
        {
            return "Guarded response: critic validation is unavailable for this high-risk request. I cannot provide unverified operational guidance.";
        }

        return "Guarded response: critic validation is unavailable for this high-risk request. " +
               "Treat the draft below as unverified and do not execute destructive/critical actions without independent checks.\n\n" +
               preview;
    }
}

