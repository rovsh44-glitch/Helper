using System.Diagnostics;
using Helper.Api.Backend.Configuration;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed class PostTurnAuditWorker : BackgroundService
{
    private readonly IPostTurnAuditQueue _queue;
    private readonly IPostTurnAuditDeadLetterStore _deadLetters;
    private readonly IPostTurnAuditTraceStore _traceStore;
    private readonly ICriticService _critic;
    private readonly IChatResiliencePolicy _resilience;
    private readonly Helper.Api.Hosting.IConversationStageMetricsService? _stageMetrics;
    private readonly IBackendOptionsCatalog? _options;
    private readonly ILogger<PostTurnAuditWorker> _logger;
    private readonly TimeSpan _auditTimeout;
    private readonly int _maxAttempts;

    public PostTurnAuditWorker(
        IPostTurnAuditQueue queue,
        IPostTurnAuditDeadLetterStore deadLetters,
        IPostTurnAuditTraceStore traceStore,
        ICriticService critic,
        IChatResiliencePolicy resilience,
        Helper.Api.Hosting.IConversationStageMetricsService? stageMetrics,
        IBackendOptionsCatalog? options,
        ILogger<PostTurnAuditWorker> logger)
    {
        _queue = queue;
        _deadLetters = deadLetters;
        _traceStore = traceStore;
        _critic = critic;
        _resilience = resilience;
        _stageMetrics = stageMetrics;
        _options = options;
        _logger = logger;
        _auditTimeout = TimeSpan.FromSeconds(options?.Audit.TimeoutSeconds ?? ReadAuditTimeoutSec());
        _maxAttempts = options?.Audit.MaxAttempts ?? 2;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            PostTurnAuditItem item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var timer = Stopwatch.StartNew();
            var success = true;

            try
            {
                using var auditCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                auditCts.CancelAfter(_auditTimeout);

                var critique = await _resilience.ExecuteAsync(
                    "post_turn_audit.critic",
                    ct => _critic.CritiqueAsync(item.UserMessage, item.AssistantResponse, "Post-turn async audit", ct),
                    auditCts.Token);

                if (!critique.IsApproved)
                {
                    success = false;
                    _traceStore.Write(item, "flagged", critique.Feedback, item.CorrelationId);
                    _deadLetters.Write(item, "critic_rejected", critique.Feedback);
                    _logger.LogWarning(
                        "Post-turn audit flagged response. ConversationId={ConversationId} TurnId={TurnId} Feedback={Feedback}",
                        item.ConversationId,
                        item.TurnId,
                        critique.Feedback);
                }
                else
                {
                    _traceStore.Write(item, "approved", correlationId: item.CorrelationId);
                }
            }
            catch (Exception ex)
            {
                success = false;
                if (item.Attempt < _maxAttempts && _queue.Enqueue(item with { Attempt = item.Attempt + 1 }))
                {
                    _traceStore.Write(item, "failed_retry", ex.Message, item.CorrelationId);
                    _logger.LogWarning(
                        ex,
                        "Post-turn audit failed and will be retried. ConversationId={ConversationId} TurnId={TurnId} Attempt={Attempt}",
                        item.ConversationId,
                        item.TurnId,
                        item.Attempt);
                }
                else
                {
                    _traceStore.Write(item, "failed", ex.Message, item.CorrelationId);
                    _deadLetters.Write(item, "audit_failed", ex.Message);
                    _logger.LogWarning(
                        ex,
                        "Post-turn audit failed. ConversationId={ConversationId} TurnId={TurnId}",
                        item.ConversationId,
                        item.TurnId);
                }
            }
            finally
            {
                _queue.RecordProcessed(timer.ElapsedMilliseconds, success);
                _stageMetrics?.Record("audit_process", timer.ElapsedMilliseconds, success);
            }
        }
    }

    private static int ReadAuditTimeoutSec()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_POST_TURN_AUDIT_TIMEOUT_SEC");
        return int.TryParse(raw, out var parsed) ? Math.Clamp(parsed, 2, 60) : 8;
    }
}

