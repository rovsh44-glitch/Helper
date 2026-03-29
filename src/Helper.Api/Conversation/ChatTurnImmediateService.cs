using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed class ChatTurnImmediateService
{
    private readonly ChatTurnExecutorDependencies _deps;

    public ChatTurnImmediateService(ChatTurnExecutorDependencies deps)
    {
        _deps = deps;
    }

    public async Task<ChatTurnImmediateOutcome?> TryHandleAsync(ChatTurnContext context, ChatTurnHandlingMode mode, CancellationToken ct)
    {
        if (context.RequiresClarification)
        {
            context.ExecutionOutput = context.ClarifyingQuestion ?? ChatTurnExecutionSupport.BuildClarificationFallbackPrompt(context, _deps.VariationPolicy);
            return SingleMessage(context.ExecutionOutput);
        }

        if (ChatTurnExecutionSupport.TryApplyDeterministicMemoryCapture(context, _deps.VariationPolicy))
        {
            context.EstimatedTokensGenerated = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
            return SingleMessage(context.ExecutionOutput);
        }

        return context.Intent.Intent switch
        {
            IntentType.Generate => await HandleGenerateAsync(context, mode, ct).ConfigureAwait(false),
            IntentType.Research => await HandleResearchAsync(context, mode, ct).ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<ChatTurnImmediateOutcome> HandleGenerateAsync(ChatTurnContext context, ChatTurnHandlingMode mode, CancellationToken ct)
    {
        if (!_deps.ProjectGenerationEnabled)
        {
            ChatTurnExecutionSupport.ApplyGenerationDisabledFallback(context);
            return SingleMessage(context.ExecutionOutput);
        }

        var generationAdmitted = GenerationAdmissionPolicy.IsGenerateAdmitted(context.Request.Message, context.IntentConfidence, _deps.GenerateMinConfidence);
        var bypassForGoldenTemplate = ChatTurnExecutionSupport.ShouldBypassGenerationAdmissionForGoldenTemplate(context);
        if (!generationAdmitted && !bypassForGoldenTemplate)
        {
            ChatTurnExecutionSupport.ApplyGenerationAdmissionFallback(context);
            return SingleMessage(context.ExecutionOutput);
        }

        if (!generationAdmitted && bypassForGoldenTemplate)
        {
            context.UncertaintyFlags.Add("generation_admission_bypassed_for_golden_template");
        }

        if (context.ToolCallsUsed >= context.ToolCallBudget)
        {
            context.BudgetExceeded = true;
            context.ExecutionOutput = ChatTurnExecutionSupport.BuildToolBudgetExceededMessage(context);
            return SingleMessage(context.ExecutionOutput);
        }

        context.ToolCallsUsed++;
        context.ToolCalls.Add("helper.generate");
        var generateCallAt = DateTimeOffset.UtcNow;
        var outputDir = Path.Combine(_deps.Config.ProjectsRoot, Guid.NewGuid().ToString("N")[..8]);
        var request = new GenerationRequest(context.Request.Message, outputDir);
        var messages = new List<ChatTurnMessage>();
        if (mode == ChatTurnHandlingMode.Streaming)
        {
            messages.Add(new ChatTurnMessage(ChatStreamChunkType.Token, ChatTurnExecutionSupport.BuildGenerationStartMessage(context)));
        }

        GenerationResult result;
        using (var stageCts = ChatTurnExecutionSupport.CreateStageCancellation("generate", context, ct))
        {
            try
            {
                result = await _deps.Orchestrator.GenerateProjectAsync(request, true, null, stageCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stageCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                ChatTurnExecutionSupport.ApplyStageTimeoutFallback(context, "generate");
                _deps.ToolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                    context,
                    generateCallAt,
                    "helper.generate",
                    "CHAT_EXECUTE",
                    success: false,
                    error: "Stage timeout",
                    details: ChatTurnExecutionSupport.BuildStageTimeoutDetails(context, "generate")));
                messages.Add(new ChatTurnMessage(ChatStreamChunkType.Token, context.ExecutionOutput));
                return new ChatTurnImmediateOutcome(messages);
            }
        }

        if (result.Success)
        {
            context.ExecutionOutput = ChatTurnExecutionSupport.BuildGenerationSuccessMessage(context, result.ProjectPath, result.Files.Count);
            context.Sources.Add($"Project Path: {result.ProjectPath}");
            _deps.ToolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                context,
                generateCallAt,
                "helper.generate",
                "CHAT_EXECUTE",
                success: true,
                details: result.ProjectPath));
        }
        else
        {
            var envelopes = result.FailureEnvelopes is { Count: > 0 }
                ? result.FailureEnvelopes
                : _deps.FailureEnvelopeFactory.FromBuildErrors(FailureStage.Synthesis, "Helper", result.Errors);
            context.ExecutionOutput = ChatTurnExecutionSupport.FormatGenerationFailure(envelopes);
            _deps.ToolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                context,
                generateCallAt,
                "helper.generate",
                "CHAT_EXECUTE",
                success: false,
                error: envelopes.FirstOrDefault()?.Evidence ?? "Generation failed",
                details: envelopes.FirstOrDefault()?.ErrorCode));
        }

        messages.Add(new ChatTurnMessage(ChatStreamChunkType.Token, context.ExecutionOutput));
        return new ChatTurnImmediateOutcome(messages);
    }

    private async Task<ChatTurnImmediateOutcome> HandleResearchAsync(ChatTurnContext context, ChatTurnHandlingMode mode, CancellationToken ct)
    {
        if (!_deps.PolicyProvider.GetPolicies().ResearchEnabled)
        {
            ChatTurnExecutionSupport.ApplyResearchDisabledFallback(context);
            return mode == ChatTurnHandlingMode.Streaming
                ? new ChatTurnImmediateOutcome(new[]
                {
                    new ChatTurnMessage(ChatStreamChunkType.Warning, context.ExecutionOutput, "research_disabled")
                })
                : SingleMessage(context.ExecutionOutput);
        }

        return await (_deps.WebSearchOrchestrator ?? new WebSearchOrchestrator(
                _deps.ResearchService,
                _deps.ResearchCache,
                _deps.SourceNormalizer,
                _deps.ToolAudit,
                localBaselineAnswerService: _deps.LocalBaselineAnswerService))
            .ExecuteAsync(context, ct)
            .ConfigureAwait(false);
    }

    private static ChatTurnImmediateOutcome SingleMessage(string content)
    {
        return new ChatTurnImmediateOutcome(new[] { new ChatTurnMessage(ChatStreamChunkType.Token, content) });
    }
}

