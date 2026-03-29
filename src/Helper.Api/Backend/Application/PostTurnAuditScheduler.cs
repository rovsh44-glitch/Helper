using System.Collections.Concurrent;
using Helper.Api.Backend.Configuration;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Backend.Application;

public interface IPostTurnAuditScheduler
{
    bool TrySchedule(ChatTurnContext context, ChatResponseDto response);
}

public sealed class PostTurnAuditScheduler : IPostTurnAuditScheduler
{
    private readonly ITurnStagePolicy _stagePolicy;
    private readonly IPostTurnAuditQueue _queue;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly int _backlogThreshold;
    private readonly int _maxTrackedTurns;
    private readonly bool _strictAuditMode;
    private readonly int _maxOutstandingAudits;
    private readonly ConcurrentDictionary<string, byte> _scheduledTurns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _scheduledOrder = new();

    public PostTurnAuditScheduler(
        ITurnStagePolicy stagePolicy,
        IPostTurnAuditQueue queue,
        IHttpContextAccessor httpContextAccessor,
        IBackendOptionsCatalog options)
    {
        _stagePolicy = stagePolicy;
        _queue = queue;
        _httpContextAccessor = httpContextAccessor;
        _backlogThreshold = Math.Min(options.Audit.BacklogAlertThreshold, options.Audit.QueueCapacity);
        _maxTrackedTurns = Math.Max(256, options.Audit.QueueCapacity * 4);
        _strictAuditMode = ReadStrictAuditMode();
        _maxOutstandingAudits = ResolveMaxOutstandingAudits(_strictAuditMode, _backlogThreshold);
    }

    public bool TrySchedule(ChatTurnContext context, ChatResponseDto response)
    {
        if (!_stagePolicy.AllowsAsyncAudit(context))
        {
            ApplyAuditDecision(context, eligible: false, expectedTrace: false, decision: "skipped_stage_policy", outstanding: 0, pending: 0);
            return false;
        }

        context.AuditEligible = true;

        var snapshot = _queue.GetSnapshot();
        var outstanding = Math.Max(0, snapshot.Enqueued - snapshot.Processed - snapshot.Dropped);
        if (outstanding >= _maxOutstandingAudits)
        {
            ApplyAuditDecision(context, eligible: true, expectedTrace: false, decision: "skipped_outstanding_limit", outstanding: outstanding, pending: snapshot.Pending);
            return false;
        }

        if (string.IsNullOrWhiteSpace(response.TurnId) ||
            string.Equals(response.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAuditDecision(
                context,
                eligible: false,
                expectedTrace: false,
                decision: string.IsNullOrWhiteSpace(response.TurnId) ? "skipped_missing_turn_id" : "skipped_clarification_grounding",
                outstanding: outstanding,
                pending: snapshot.Pending);
            return false;
        }

        if (snapshot.Pending >= _backlogThreshold)
        {
            ApplyAuditDecision(context, eligible: true, expectedTrace: false, decision: "skipped_backlog_threshold", outstanding: outstanding, pending: snapshot.Pending);
            return false;
        }

        var userMessage = ResolveUserMessage(context, response);
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            ApplyAuditDecision(context, eligible: false, expectedTrace: false, decision: "skipped_missing_user_message", outstanding: outstanding, pending: snapshot.Pending);
            return false;
        }

        var dedupeKey = $"{response.ConversationId}:{response.TurnId}";
        if (!_scheduledTurns.TryAdd(dedupeKey, 0))
        {
            ApplyAuditDecision(context, eligible: true, expectedTrace: false, decision: "skipped_duplicate_turn", outstanding: outstanding, pending: snapshot.Pending);
            return false;
        }

        var correlationId = _httpContextAccessor.HttpContext?.Items.TryGetValue("CorrelationId", out var correlationValue) == true
            ? correlationValue?.ToString()
            : null;

        var enqueued = _queue.Enqueue(new PostTurnAuditItem(
            response.ConversationId,
            response.TurnId!,
            userMessage,
            response.Response,
            IsFactualPrompt(userMessage),
            response.Sources?.ToList() ?? new List<string>(),
            DateTimeOffset.UtcNow,
            correlationId));

        if (!enqueued)
        {
            _scheduledTurns.TryRemove(dedupeKey, out _);
            ApplyAuditDecision(context, eligible: true, expectedTrace: false, decision: "skipped_queue_rejected", outstanding: outstanding, pending: snapshot.Pending);
            return false;
        }

        _scheduledOrder.Enqueue(dedupeKey);
        TrimTrackedTurns();
        ApplyAuditDecision(context, eligible: true, expectedTrace: true, decision: "scheduled", outstanding: outstanding, pending: snapshot.Pending);
        return true;
    }

    private void ApplyAuditDecision(ChatTurnContext context, bool eligible, bool expectedTrace, string decision, int outstanding, int pending)
    {
        context.AuditEligible = eligible;
        context.AuditExpectedTrace = expectedTrace;
        context.AuditStrictMode = _strictAuditMode;
        context.AuditDecision = decision;
        context.AuditOutstandingAtDecision = Math.Max(0, outstanding);
        context.AuditPendingAtDecision = Math.Max(0, pending);
        context.AuditMaxOutstandingAudits = Math.Max(1, _maxOutstandingAudits);
    }

    private void TrimTrackedTurns()
    {
        while (_scheduledTurns.Count > _maxTrackedTurns && _scheduledOrder.TryDequeue(out var key))
        {
            _scheduledTurns.TryRemove(key, out _);
        }
    }

    private static string? ResolveUserMessage(ChatTurnContext context, ChatResponseDto response)
    {
        if (!string.IsNullOrWhiteSpace(context.Request.Message))
        {
            return context.Request.Message.Trim();
        }

        return response.Messages
            .LastOrDefault(message =>
                message.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(message.TurnId, response.TurnId, StringComparison.OrdinalIgnoreCase))
            ?.Content
            ?.Trim();
    }

    private static bool IsFactualPrompt(string prompt)
    {
        var factualTokens = new[] { "what", "when", "where", "who", "когда", "где", "кто", "сколько", "факт" };
        return factualTokens.Any(token => prompt.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ReadStrictAuditMode()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_POST_TURN_AUDIT_STRICT");
        return bool.TryParse(raw, out var parsed) && parsed;
    }

    private static int ResolveMaxOutstandingAudits(bool strictMode, int backlogThreshold)
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_POST_TURN_AUDIT_MAX_OUTSTANDING");
        if (int.TryParse(raw, out var parsed))
        {
            return Math.Clamp(parsed, 1, Math.Max(1, backlogThreshold));
        }

        return strictMode
            ? Math.Clamp(4, 1, Math.Max(1, backlogThreshold))
            : 1;
    }
}

