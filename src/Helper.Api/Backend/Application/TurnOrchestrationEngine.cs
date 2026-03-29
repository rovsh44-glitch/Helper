using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Api.Backend.Application;

public interface ITurnOrchestrationEngine
{
    Task<ChatResponseDto> StartTurnAsync(ChatRequestDto request, CancellationToken ct);
    IAsyncEnumerable<TokenChunk> StartTurnStreamAsync(ChatRequestDto request, CancellationToken ct);
    Task<ChatResponseDto> ResumeTurnAsync(string conversationId, ChatResumeRequestDto request, CancellationToken ct);
    Task<ChatResponseDto> RegenerateTurnAsync(string conversationId, string turnId, TurnRegenerateRequestDto request, CancellationToken ct);
    Task<ChatResponseDto> RepairConversationAsync(string conversationId, ConversationRepairRequestDto request, CancellationToken ct);
}

public sealed class TurnOrchestrationEngine : ITurnOrchestrationEngine
{
    public TurnOrchestrationEngine(
        IConversationStore store,
        IChatTurnPlanner planner,
        IChatTurnExecutor executor,
        IChatTurnCritic critic,
        IChatTurnFinalizer finalizer,
        IInputRiskScanner inputRiskScanner,
        IOutputExfiltrationGuard outputGuard,
        ITurnLifecycleStateMachine legacyLifecycle,
        ITurnExecutionStateMachine executionStateMachine,
        ITurnCheckpointManager checkpointManager,
        ITurnStagePolicy stagePolicy,
        IPostTurnAuditScheduler auditScheduler,
        IConversationStageMetricsService? stageMetrics = null,
        ILogger<TurnOrchestrationEngine>? logger = null,
        ITurnExecutionStageRunner? stageRunner = null,
        ITurnRouteTelemetryRecorder? telemetryRecorder = null,
        ITurnResponseWriter? responseWriter = null)
    {
        _store = store;
        _inputRiskScanner = inputRiskScanner;
        _executionStateMachine = executionStateMachine;
        _checkpointManager = checkpointManager;
        var recorder = telemetryRecorder ?? new TurnRouteTelemetryRecorder();
        _stageRunner = stageRunner ?? new TurnExecutionStageRunner(
            store,
            planner,
            executor,
            critic,
            finalizer,
            outputGuard,
            legacyLifecycle,
            executionStateMachine,
            checkpointManager,
            stagePolicy,
            stageMetrics,
            null);
        _responseWriter = responseWriter ?? new TurnResponseWriter(
            store,
            checkpointManager,
            auditScheduler,
            legacyLifecycle,
            executionStateMachine,
            recorder);
    }

    private readonly IConversationStore _store;
    private readonly IInputRiskScanner _inputRiskScanner;
    private readonly ITurnExecutionStateMachine _executionStateMachine;
    private readonly ITurnCheckpointManager _checkpointManager;
    private readonly ITurnExecutionStageRunner _stageRunner;
    private readonly ITurnResponseWriter _responseWriter;

    public async Task<ChatResponseDto> StartTurnAsync(ChatRequestDto request, CancellationToken ct)
    {
        ValidateMessage(request.Message, nameof(request));

        var state = _store.GetOrCreate(request.ConversationId);
        EnsureActiveBranch(state, request.BranchId);

        var risk = await _inputRiskScanner.ScanAsync(request.Message, request.Attachments, ct);
        if (risk.IsBlocked)
        {
            return _responseWriter.BuildBlockedResponse(state, request, risk);
        }

        var turnId = Guid.NewGuid().ToString("N");
        var branchId = _store.GetActiveBranchId(state);
        var userText = request.Message.Trim();
        _store.AddMessage(state, new ChatMessageDto(
            "user",
            userText,
            DateTimeOffset.UtcNow,
            turnId,
            TurnVersion: 1,
            BranchId: branchId,
            Attachments: request.Attachments,
            InputMode: ConversationInputMode.Normalize(request.InputMode)));

        var context = BuildContext(state, request, turnId, branchId);
        _executionStateMachine.Transition(context, TurnExecutionState.Validated);
        _checkpointManager.SavePlannedCheckpoint(state, context, branchId);
        _store.MarkUpdated(state);

        return await RunTurnAsync(state, context, branchId, turnVersion: 1, ct);
    }

    public async IAsyncEnumerable<TokenChunk> StartTurnStreamAsync(
        ChatRequestDto request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ValidateMessage(request.Message, nameof(request));

        var state = _store.GetOrCreate(request.ConversationId);
        EnsureActiveBranch(state, request.BranchId);

        var risk = await _inputRiskScanner.ScanAsync(request.Message, request.Attachments, ct);
        if (risk.IsBlocked)
        {
            var blocked = _responseWriter.BuildBlockedResponse(state, request, risk);
            yield return new TokenChunk(
                ChatStreamChunkType.Warning,
                blocked.Response,
                0,
                DateTimeOffset.UtcNow,
                blocked,
                blocked.ConversationId,
                blocked.TurnId,
                WarningCode: "input_blocked",
                ResumeCursor: 0);
            yield break;
        }

        var turnId = Guid.NewGuid().ToString("N");
        var branchId = _store.GetActiveBranchId(state);
        var userText = request.Message.Trim();
        _store.AddMessage(state, new ChatMessageDto(
            "user",
            userText,
            DateTimeOffset.UtcNow,
            turnId,
            TurnVersion: 1,
            BranchId: branchId,
            Attachments: request.Attachments,
            InputMode: ConversationInputMode.Normalize(request.InputMode)));

        var context = BuildContext(state, request, turnId, branchId);
        _executionStateMachine.Transition(context, TurnExecutionState.Validated);
        _checkpointManager.SavePlannedCheckpoint(state, context, branchId);
        _store.MarkUpdated(state);

        await foreach (var chunk in RunTurnStreamAsync(state, context, branchId, turnVersion: 1, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    public async Task<ChatResponseDto> ResumeTurnAsync(string conversationId, ChatResumeRequestDto request, CancellationToken ct)
    {
        if (!_store.TryGet(conversationId, out var state))
        {
            throw new KeyNotFoundException("Conversation not found.");
        }

        var branchId = _store.GetActiveBranchId(state);
        var requestEnvelope = new ChatRequestDto(
            state.ActiveTurnUserMessage ?? string.Empty,
            state.Id,
            request.MaxHistory,
            request.SystemInstruction,
            branchId,
            null,
            request.IdempotencyKey,
            request.LiveWebMode,
            ResolveTurnInputMode(state, branchId, state.ActiveTurnId));

        if (_checkpointManager.TryHydrate(state, requestEnvelope, out var checkpointContext))
        {
            checkpointContext.History = _store.GetRecentMessages(state, branchId, request.MaxHistory ?? 12);
            return await ResumeFromCheckpointAsync(state, checkpointContext, ct);
        }

        string? pendingMessage;
        string? pendingTurnId;
        lock (state.SyncRoot)
        {
            pendingMessage = state.ActiveTurnUserMessage;
            pendingTurnId = state.ActiveTurnId;
            if (string.IsNullOrWhiteSpace(pendingMessage))
            {
                var lastUser = state.Messages.LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
                pendingMessage = lastUser?.Content;
                pendingTurnId = lastUser?.TurnId;
            }
        }

        if (string.IsNullOrWhiteSpace(pendingMessage))
        {
            throw new InvalidOperationException("No active turn to resume.");
        }

        var resumedTurnId = string.IsNullOrWhiteSpace(pendingTurnId)
            ? Guid.NewGuid().ToString("N")
            : pendingTurnId;
        var context = PrepareExistingUserTurnContext(
            state,
            requestEnvelope with
            {
                Message = pendingMessage,
                InputMode = ResolveTurnInputMode(state, branchId, pendingTurnId)
            },
            resumedTurnId,
            branchId);
        return await RunTurnAsync(state, context, branchId, turnVersion: 1, ct);
    }

    public async Task<ChatResponseDto> RegenerateTurnAsync(string conversationId, string turnId, TurnRegenerateRequestDto request, CancellationToken ct)
    {
        if (!_store.TryGet(conversationId, out var state))
        {
            throw new KeyNotFoundException("Conversation not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.BranchId))
        {
            _store.SetActiveBranch(state, request.BranchId!);
        }

        var branchId = _store.GetActiveBranchId(state);
        ChatMessageDto? sourceUser;
        int nextVersion;
        lock (state.SyncRoot)
        {
            sourceUser = state.Messages.LastOrDefault(m =>
                m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.TurnId, turnId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.BranchId ?? "main", branchId, StringComparison.OrdinalIgnoreCase));

            nextVersion = state.Messages
                .Where(m =>
                    m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.TurnId, turnId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.BranchId ?? "main", branchId, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.TurnVersion)
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        if (sourceUser == null)
        {
            throw new KeyNotFoundException("Turn not found for regeneration.");
        }

        var context = PrepareExistingUserTurnContext(
            state,
            new ChatRequestDto(
                sourceUser.Content,
                state.Id,
                request.MaxHistory,
                request.SystemInstruction,
                branchId,
                sourceUser.Attachments,
                request.IdempotencyKey,
                request.LiveWebMode,
                ConversationInputMode.Normalize(sourceUser.InputMode)),
            turnId,
            branchId);
        return await RunTurnAsync(state, context, branchId, nextVersion, ct);
    }

    public async Task<ChatResponseDto> RepairConversationAsync(string conversationId, ConversationRepairRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CorrectedIntent))
        {
            throw new ArgumentException("CorrectedIntent must not be empty.", nameof(request));
        }

        if (!_store.TryGet(conversationId, out var state))
        {
            throw new KeyNotFoundException("Conversation not found.");
        }

        var branchId = string.IsNullOrWhiteSpace(request.BranchId)
            ? _store.GetActiveBranchId(state)
            : request.BranchId!.Trim();
        if (!string.IsNullOrWhiteSpace(request.BranchId))
        {
            _store.SetActiveBranch(state, branchId);
        }

        _store.AddMessage(state, new ChatMessageDto(
            "system",
            BuildRepairSummary(request),
            DateTimeOffset.UtcNow,
            request.TurnId,
            BranchId: branchId));

        var repairedMessage = string.IsNullOrWhiteSpace(request.RepairNote)
            ? request.CorrectedIntent.Trim()
            : $"{request.CorrectedIntent.Trim()}\n\nRepair note: {request.RepairNote.Trim()}";

        return await StartTurnAsync(
            new ChatRequestDto(
                repairedMessage,
                conversationId,
                request.MaxHistory,
                request.SystemInstruction,
                branchId,
                null,
                request.IdempotencyKey,
                request.LiveWebMode),
            ct);
    }

    private static ChatTurnContext BuildContext(ConversationState state, ChatRequestDto request, string turnId, string branchId)
    {
        return new ChatTurnContext
        {
            TurnId = turnId,
            Request = request with { BranchId = branchId, ConversationId = state.Id },
            Conversation = state,
            History = Array.Empty<ChatMessageDto>()
        };
    }

    private ChatTurnContext PrepareExistingUserTurnContext(
        ConversationState state,
        ChatRequestDto request,
        string turnId,
        string branchId)
    {
        var context = BuildContext(state, request, turnId, branchId);
        _executionStateMachine.Transition(context, TurnExecutionState.Validated);
        _checkpointManager.SavePlannedCheckpoint(state, context, branchId);
        _store.MarkUpdated(state);
        return context;
    }

    private static void ValidateMessage(string? message, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message must not be empty.", argumentName);
        }
    }

    private void EnsureActiveBranch(ConversationState state, string? requestedBranchId)
    {
        if (!string.IsNullOrWhiteSpace(requestedBranchId))
        {
            _store.SetActiveBranch(state, requestedBranchId);
        }
    }

    private static string ResolveTurnInputMode(ConversationState state, string branchId, string? turnId)
    {
        lock (state.SyncRoot)
        {
            var sourceUser = state.Messages.LastOrDefault(m =>
                m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.BranchId ?? "main", branchId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(turnId) || string.Equals(m.TurnId, turnId, StringComparison.OrdinalIgnoreCase)));

            return ConversationInputMode.Normalize(sourceUser?.InputMode);
        }
    }

    private async Task<ChatResponseDto> RunTurnAsync(
        ConversationState state,
        ChatTurnContext context,
        string branchId,
        int turnVersion,
        CancellationToken ct)
    {
        try
        {
            await _stageRunner.PlanAsync(state, context, branchId, ct);
            await _stageRunner.ExecuteAsync(state, context, branchId, ct);
            await _stageRunner.ValidateByPolicyAsync(context, ct);
            await _stageRunner.FinalizeAsync(context, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _stageRunner.RecoverFromTurnFailure(context, ex);
        }

        await _stageRunner.ApplyOutputSafetyAsync(context, ct);
        return _responseWriter.PersistCompletedTurn(state, context, branchId, turnVersion);
    }

    private async Task<ChatResponseDto> ResumeFromCheckpointAsync(
        ConversationState state,
        ChatTurnContext context,
        CancellationToken ct)
    {
        var branchId = _store.GetActiveBranchId(state);
        if (context.ExecutionState is TurnExecutionState.Executed or TurnExecutionState.ValidatedByPolicy)
        {
            await _stageRunner.ValidateByPolicyAsync(context, ct);
        }

        if (context.ExecutionState is TurnExecutionState.Executed or TurnExecutionState.ValidatedByPolicy or TurnExecutionState.Finalized)
        {
            await _stageRunner.FinalizeAsync(context, ct);
        }

        await _stageRunner.ApplyOutputSafetyAsync(context, ct);
        return _responseWriter.PersistCompletedTurn(state, context, branchId, turnVersion: 1);
    }

    private async IAsyncEnumerable<TokenChunk> RunTurnStreamAsync(
        ConversationState state,
        ChatTurnContext context,
        string branchId,
        int turnVersion,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var offset = 0;
        await _stageRunner.PlanAsync(state, context, branchId, ct);
        yield return BuildStageChunk("planned", offset, state.Id, context.TurnId);

        if (context.RequiresClarification)
        {
            await _stageRunner.FinalizeAsync(context, ct);
            await _stageRunner.ApplyOutputSafetyAsync(context, ct);
            var clarified = _responseWriter.PersistCompletedTurn(state, context, branchId, turnVersion);
            offset = clarified.Response?.Length ?? 0;
            yield return new TokenChunk(
                ChatStreamChunkType.Token,
                clarified.Response,
                offset,
                DateTimeOffset.UtcNow,
                ConversationId: clarified.ConversationId,
                TurnId: clarified.TurnId,
                ResumeCursor: offset);
            yield return new TokenChunk(
                ChatStreamChunkType.Done,
                null,
                offset,
                DateTimeOffset.UtcNow,
                clarified,
                clarified.ConversationId,
                clarified.TurnId,
                ResumeCursor: offset);
            yield break;
        }

        await foreach (var modelChunk in _stageRunner.ExecuteStreamAsync(state, context, branchId, ct).WithCancellation(ct))
        {
            offset = modelChunk.Offset > offset ? modelChunk.Offset : offset + (modelChunk.Content?.Length ?? 0);
            yield return modelChunk;
        }
        yield return BuildStageChunk("executed", offset, state.Id, context.TurnId);

        await _stageRunner.ValidateByPolicyAsync(context, ct);
        yield return BuildStageChunk("validated_by_policy", offset, state.Id, context.TurnId);

        await _stageRunner.FinalizeAsync(context, ct);
        await _stageRunner.ApplyOutputSafetyAsync(context, ct);
        if (context.BudgetExceeded)
        {
            yield return new TokenChunk(
                ChatStreamChunkType.Warning,
                "Turn budget was exceeded; response finalized in degraded mode.",
                offset,
                DateTimeOffset.UtcNow,
                ConversationId: state.Id,
                TurnId: context.TurnId,
                WarningCode: "budget_exceeded",
                ResumeCursor: offset);
        }

        var response = _responseWriter.PersistCompletedTurn(state, context, branchId, turnVersion);
        offset = Math.Max(offset, response.Response?.Length ?? 0);
        yield return BuildStageChunk("persisted", offset, response.ConversationId, response.TurnId);
        yield return new TokenChunk(
            ChatStreamChunkType.Done,
            null,
            offset,
            DateTimeOffset.UtcNow,
            response,
            response.ConversationId,
            response.TurnId,
            ResumeCursor: offset);
    }

    private static string BuildRepairSummary(ConversationRepairRequestDto request)
    {
        var turnMarker = string.IsNullOrWhiteSpace(request.TurnId)
            ? "latest assistant turn"
            : request.TurnId.Trim();
        var note = string.IsNullOrWhiteSpace(request.RepairNote)
            ? string.Empty
            : $" Note: {request.RepairNote.Trim()}";
        return $"Conversation repair requested for {turnMarker}. Updated intent: {request.CorrectedIntent.Trim()}.{note}";
    }

    private static TokenChunk BuildStageChunk(string stage, int offset, string conversationId, string? turnId)
    {
        return new TokenChunk(
            ChatStreamChunkType.Stage,
            stage,
            offset,
            DateTimeOffset.UtcNow,
            ConversationId: conversationId,
            TurnId: turnId,
            Stage: stage,
            ResumeCursor: offset);
    }
}

