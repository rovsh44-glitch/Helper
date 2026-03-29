using System.Runtime.CompilerServices;
using System.Text;
using Helper.Api.Backend.ModelGateway;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed class ChatTurnAnswerService
{
    private readonly ChatTurnExecutorDependencies _deps;

    public ChatTurnAnswerService(ChatTurnExecutorDependencies deps)
    {
        _deps = deps;
    }

    public async Task ExecuteAsync(ChatTurnContext context, CancellationToken ct)
    {
        var prepared = await PrepareInvocationAsync(context, ct).ConfigureAwait(false);
        await WarmModelAsync(context, ct).ConfigureAwait(false);

        var llmCallAt = DateTimeOffset.UtcNow;
        using var stageCts = ChatTurnExecutionSupport.CreateStageCancellation("llm", context, ct);
        try
        {
            if (_deps.ReasoningBranchExecutor is not null &&
                _deps.ReasoningBranchExecutor.ShouldUseBranching(context))
            {
                context.ExecutionOutput = await _deps.ReasoningBranchExecutor.ExecuteAsync(prepared, context, stageCts.Token).ConfigureAwait(false);
            }
            else
            {
                context.ExecutionOutput = await _deps.Resilience.ExecuteAsync(
                        "llm.ask",
                        async retryCt => await _deps.ModelGateway.AskAsync(
                                new ModelGatewayRequest(
                                    prepared.Prompt,
                                    ChatTurnExecutionSupport.ResolveModelClass(context),
                                    ModelExecutionPool.Interactive,
                                    PreferredModel: prepared.PreferredModel,
                                    SystemInstruction: prepared.SystemInstruction),
                                retryCt)
                            .ConfigureAwait(false),
                        stageCts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stageCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            ChatTurnExecutionSupport.ApplyStageTimeoutFallback(context, "llm");
            _deps.ToolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                context,
                llmCallAt,
                "llm.ask",
                "CHAT_EXECUTE",
                success: false,
                error: "Stage timeout",
                details: ChatTurnExecutionSupport.BuildStageTimeoutDetails(context, "llm")));
            return;
        }

        context.EstimatedTokensGenerated = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
        if (context.EstimatedTokensGenerated > context.TokenBudget)
        {
            context.BudgetExceeded = true;
            context.UncertaintyFlags.Add("token_budget_exceeded");
            context.ExecutionOutput = TokenBudgetEstimator.TruncateToBudget(context.ExecutionOutput, context.TokenBudget);
        }
    }

    public async IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(
        ChatTurnContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var prepared = await PrepareInvocationAsync(context, ct).ConfigureAwait(false);
        await WarmModelAsync(context, ct).ConfigureAwait(false);

        var offset = 0;
        var modelStreamStartedAt = DateTimeOffset.UtcNow;
        var outputBuilder = new StringBuilder();
        var generatedTokens = 0;
        var llmCallAt = DateTimeOffset.UtcNow;
        var llmTimedOut = false;

        using (var stageCts = ChatTurnExecutionSupport.CreateStageCancellation("llm", context, ct))
        {
            var stream = _deps.Resilience.ExecuteStreamingAsync(
                "llm.ask.stream",
                retryCt => StreamThroughGatewayAsync(prepared, context, retryCt),
                stageCts.Token);

            await using var enumerator = stream.GetAsyncEnumerator(stageCts.Token);
            while (true)
            {
                var hasNext = false;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stageCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    llmTimedOut = true;
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                var token = enumerator.Current;
                var estimatedChunkTokens = TokenBudgetEstimator.Estimate(token);
                if (generatedTokens + estimatedChunkTokens > context.TokenBudget)
                {
                    context.BudgetExceeded = true;
                    context.UncertaintyFlags.Add("token_budget_exceeded");
                    var remainingBudget = Math.Max(0, context.TokenBudget - generatedTokens);
                    var truncated = TokenBudgetEstimator.TruncateToBudget(token, remainingBudget);
                    if (!string.IsNullOrEmpty(truncated))
                    {
                        outputBuilder.Append(truncated);
                        generatedTokens += TokenBudgetEstimator.Estimate(truncated);
                        offset++;
                        yield return new TokenChunk(
                            ChatStreamChunkType.Token,
                            truncated,
                            offset,
                            DateTimeOffset.UtcNow,
                            ModelStreamStartedAtUtc: modelStreamStartedAt);
                    }

                    const string budgetNotice = "\n\n[Output truncated by latency budget]";
                    outputBuilder.Append(budgetNotice);
                    generatedTokens += TokenBudgetEstimator.Estimate(budgetNotice);
                    offset++;
                    yield return new TokenChunk(
                        ChatStreamChunkType.Token,
                        budgetNotice,
                        offset,
                        DateTimeOffset.UtcNow,
                        ModelStreamStartedAtUtc: modelStreamStartedAt);
                    break;
                }

                outputBuilder.Append(token);
                generatedTokens += estimatedChunkTokens;
                offset++;
                yield return new TokenChunk(
                    ChatStreamChunkType.Token,
                    token,
                    offset,
                    DateTimeOffset.UtcNow,
                    ModelStreamStartedAtUtc: modelStreamStartedAt);
            }
        }

        if (llmTimedOut)
        {
            ChatTurnExecutionSupport.ApplyStageTimeoutFallback(context, "llm");
            _deps.ToolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                context,
                llmCallAt,
                "llm.ask.stream",
                "CHAT_EXECUTE",
                success: false,
                error: "Stage timeout",
                details: ChatTurnExecutionSupport.BuildStageTimeoutDetails(context, "llm")));
            outputBuilder.Clear();
            outputBuilder.Append(context.ExecutionOutput);
            generatedTokens = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
            offset++;
            yield return new TokenChunk(
                ChatStreamChunkType.Token,
                context.ExecutionOutput,
                offset,
                DateTimeOffset.UtcNow,
                ModelStreamStartedAtUtc: modelStreamStartedAt);
        }

        context.ExecutionOutput = outputBuilder.ToString();
        context.EstimatedTokensGenerated = generatedTokens;
    }

    private async Task<ChatTurnPreparedInvocation> PrepareInvocationAsync(ChatTurnContext context, CancellationToken ct)
    {
        var model = context.Intent.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            if (_deps.ModelOrchestrator is IContextAwareModelOrchestrator contextAware)
            {
                var approximateTokens = TokenBudgetEstimator.Estimate(string.Join("\n", context.History.Select(message => message.Content)));
                var route = await contextAware.SelectRoutingDecisionAsync(
                        new ModelRoutingRequest(
                            context.Request.Message,
                            context.Intent.Intent,
                            context.ExecutionMode.ToString(),
                            context.History.Count,
                            approximateTokens,
                            RequiresVerification: context.BudgetProfile == TurnBudgetProfile.HighRisk || context.IsFactualPrompt,
                            HasAttachments: context.Request.Attachments is { Count: > 0 }),
                        ct)
                    .ConfigureAwait(false);
                model = route.PreferredModel;
                context.ModelRouteKey = route.RouteKey;
                context.ModelRouteReason = string.Join("; ", route.Reasons);
                context.ModelRouteSignals.Clear();
                context.ModelRouteSignals.AddRange(route.Reasons);
            }
            else
            {
                model = await _deps.ModelOrchestrator.SelectOptimalModelAsync(context.Request.Message, ct).ConfigureAwait(false);
                context.ModelRouteKey = "legacy";
                context.ModelRouteReason = "legacy_prompt_only";
                context.ModelRouteSignals.Clear();
                context.ModelRouteSignals.Add("legacy_prompt_only");
            }
        }

        var prompt = ChatPromptFormatter.BuildConversationPrompt(context.History);
        context.RequestedMemoryLayers.Clear();
        context.UsedMemoryLayers.Clear();
        context.MemoryHistoryBudget = 0;
        context.ProceduralLessonBudget = 0;
        context.RetrievalChunkBudget = 0;
        context.ProceduralLessonsUsed = 0;
        context.RetrievalChunksUsed = 0;
        context.SelectedRetrievalPurpose = null;
        context.RetrievalTrace.Clear();

        if (_deps.ContextAssembler is not null)
        {
            var selection = MemoryLayerSelection.Resolve(context);
            context.RequestedMemoryLayers.AddRange(selection.Layers);
            context.MemoryHistoryBudget = selection.HistoryMessageBudget;
            context.ProceduralLessonBudget = selection.ProceduralLessonBudget;
            context.RetrievalChunkBudget = selection.RetrievalChunkBudget;

            var assembly = await _deps.ContextAssembler.AssembleAsync(context, ct).ConfigureAwait(false);
            prompt = assembly.Prompt;
            context.UsedMemoryLayers.AddRange(assembly.UsedLayers);
            context.ProceduralLessonsUsed = assembly.ProceduralLessonCount;
            context.RetrievalChunksUsed = assembly.RetrievalChunkCount;
        }

        var profile = _deps.UserProfileService.Resolve(context.Conversation);
        var resolvedTurnLanguage = context.ResolvedTurnLanguage
            ?? _deps.TurnLanguageResolver.Resolve(profile, context.Request.Message, context.History);
        context.ResolvedTurnLanguage = resolvedTurnLanguage;
        var styleRoute = _deps.UserProfileService.ResolveStyleRoute(profile, context);
        context.ResolvedStyleMode = styleRoute.Mode;
        context.ResolvedTonePreset = styleRoute.TonePreset;
        var styleHint = styleRoute.BuildSystemHint(profile, resolvedTurnLanguage);
        var systemInstruction = string.IsNullOrWhiteSpace(context.Request.SystemInstruction)
            ? styleHint
            : $"{styleHint} {context.Request.SystemInstruction}";

        return new ChatTurnPreparedInvocation(
            prompt,
            string.IsNullOrWhiteSpace(model) ? null : model,
            systemInstruction);
    }

    private async Task WarmModelAsync(ChatTurnContext context, CancellationToken ct)
    {
        try
        {
            await _deps.ModelGateway.WarmAsync(ChatTurnExecutionSupport.ResolveModelClass(context), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChatExecutor] Model warmup skipped: {ex.Message}");
        }
    }

    private async IAsyncEnumerable<string> StreamThroughGatewayAsync(
        ChatTurnPreparedInvocation prepared,
        ChatTurnContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in _deps.ModelGateway.StreamAsync(
                           new ModelGatewayRequest(
                               prepared.Prompt,
                               ChatTurnExecutionSupport.ResolveModelClass(context),
                               ModelExecutionPool.Interactive,
                               PreferredModel: prepared.PreferredModel,
                               SystemInstruction: prepared.SystemInstruction),
                           ct)
                           .WithCancellation(ct))
        {
            yield return chunk.Content;
        }
    }
}

