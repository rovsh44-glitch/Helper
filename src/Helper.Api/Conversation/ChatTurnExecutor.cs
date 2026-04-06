using System.Diagnostics;
using System.Runtime.CompilerServices;
using Helper.Api.Backend.Application;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Conversation;

public sealed class ChatTurnExecutor : IChatTurnExecutor
{
    private readonly ChatTurnImmediateService _immediate;
    private readonly ChatTurnAnswerService _answer;

    public ChatTurnExecutor(
        AILink ai,
        IModelOrchestrator modelOrchestrator,
        IResearchService researchService,
        IShortHorizonResearchCache researchCache,
        IChatResiliencePolicy resilience,
        IHelperOrchestrator orchestrator,
        Hosting.ApiRuntimeConfig config,
        IUserProfileService? userProfileService = null,
        ITurnLanguageResolver? turnLanguageResolver = null,
        IFailureEnvelopeFactory? failureEnvelopeFactory = null,
        IToolAuditService? toolAudit = null,
        IModelGateway? modelGateway = null,
        IBackendRuntimePolicyProvider? policyProvider = null,
        ISourceNormalizationService? sourceNormalizer = null,
        IConversationContextAssembler? contextAssembler = null,
        IReasoningBranchExecutor? reasoningBranchExecutor = null,
        IConversationVariationPolicy? variationPolicy = null,
        IProviderProfileResolver? providerProfileResolver = null)
    {
        var resolvedModelGateway = modelGateway ?? new HelperModelGateway(ai, new BackendOptionsCatalog(config), new ModelGatewayTelemetry(), providerProfileResolver);
        var resolvedSourceNormalizer = sourceNormalizer ?? new SourceNormalizationService();
        var localBaselineAnswerService = new LocalBaselineAnswerService(ai);
        var deps = new ChatTurnExecutorDependencies(
            ai,
            resolvedModelGateway,
            modelOrchestrator,
            researchService,
            researchCache,
            resilience,
            userProfileService ?? new UserProfileService(),
            turnLanguageResolver ?? new TurnLanguageResolver(),
            orchestrator,
            config,
            failureEnvelopeFactory ?? new FailureEnvelopeFactory(),
            toolAudit,
            resolvedSourceNormalizer,
            variationPolicy ?? new ConversationVariationPolicy(),
            policyProvider ?? new BackendOptionsCatalog(config),
            new ConversationPromptPolicy(),
            new ConversationModelSelectionPolicy(providerProfileResolver: providerProfileResolver),
            ChatTurnExecutionSupport.ReadProjectGenerationFlag(),
            ChatTurnExecutionSupport.ReadDouble("HELPER_CHAT_GENERATE_MIN_CONFIDENCE", 0.70, 0.0, 1.0),
            localBaselineAnswerService,
            new WebSearchOrchestrator(
                researchService,
                researchCache,
                resolvedSourceNormalizer,
                toolAudit,
                localBaselineAnswerService: localBaselineAnswerService),
            contextAssembler,
            reasoningBranchExecutor);
        _immediate = new ChatTurnImmediateService(deps);
        _answer = new ChatTurnAnswerService(deps);
    }

    public async Task ExecuteAsync(ChatTurnContext context, CancellationToken ct)
    {
        var turnTimer = Stopwatch.StartNew();
        var immediate = await _immediate.TryHandleAsync(context, ChatTurnHandlingMode.Standard, ct).ConfigureAwait(false);
        if (immediate is not null)
        {
            context.EstimatedTokensGenerated = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
            ApplyTurnBudget(context, turnTimer);
            return;
        }

        await _answer.ExecuteAsync(context, ct).ConfigureAwait(false);
        ApplyTurnBudget(context, turnTimer);
    }

    public async IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(ChatTurnContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var turnTimer = Stopwatch.StartNew();
        var immediate = await _immediate.TryHandleAsync(context, ChatTurnHandlingMode.Streaming, ct).ConfigureAwait(false);
        if (immediate is not null)
        {
            context.EstimatedTokensGenerated = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
            var offset = 0;
            foreach (var message in immediate.Messages)
            {
                offset++;
                yield return new TokenChunk(
                    message.ChunkType,
                    message.Content,
                    offset,
                    DateTimeOffset.UtcNow,
                    WarningCode: message.WarningCode,
                    ResumeCursor: message.ChunkType == ChatStreamChunkType.Warning ? offset : null);
            }

            ApplyTurnBudget(context, turnTimer);
            yield break;
        }

        await foreach (var chunk in _answer.ExecuteStreamAsync(context, ct).WithCancellation(ct))
        {
            yield return chunk;
        }

        ApplyTurnBudget(context, turnTimer);
    }

    private static void ApplyTurnBudget(ChatTurnContext context, Stopwatch turnTimer)
    {
        if (turnTimer.Elapsed > context.TimeBudget)
        {
            context.BudgetExceeded = true;
        }
    }
}

