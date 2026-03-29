using System.Threading.Channels;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Conversation;

public sealed record PostTurnAuditItem(
    string ConversationId,
    string TurnId,
    string UserMessage,
    string AssistantResponse,
    bool IsFactualPrompt,
    IReadOnlyList<string> Sources,
    DateTimeOffset CreatedAt,
    string? CorrelationId = null,
    int Attempt = 1);

public sealed record PostTurnAuditSnapshot(
    int Pending,
    int Enqueued,
    int Dropped,
    int Processed,
    int Failed,
    int DeadLettered,
    double AvgProcessingMs,
    DateTimeOffset? LastProcessedAt,
    IReadOnlyList<string> Alerts);

public interface IPostTurnAuditQueue
{
    bool Enqueue(PostTurnAuditItem item);
    ValueTask<PostTurnAuditItem> DequeueAsync(CancellationToken ct);
    void RecordProcessed(long elapsedMs, bool success);
    PostTurnAuditSnapshot GetSnapshot();
}

public sealed class PostTurnAuditQueue : IPostTurnAuditQueue
{
    private readonly Channel<PostTurnAuditItem> _channel;
    private readonly IPostTurnAuditDeadLetterStore? _deadLetters;
    private readonly Helper.Api.Hosting.IConversationStageMetricsService? _stageMetrics;
    private readonly IBackendOptionsCatalog? _options;
    private long _pending;
    private long _enqueued;
    private long _dropped;
    private long _processed;
    private long _failed;
    private long _deadLettered;
    private long _elapsedMsSum;
    private long _lastProcessedUnixMs;

    public PostTurnAuditQueue(
        IPostTurnAuditDeadLetterStore? deadLetters = null,
        Helper.Api.Hosting.IConversationStageMetricsService? stageMetrics = null,
        IBackendOptionsCatalog? options = null)
    {
        _deadLetters = deadLetters;
        _stageMetrics = stageMetrics;
        _options = options;
        var capacity = options?.Audit.QueueCapacity ?? ReadCapacity();
        _channel = Channel.CreateBounded<PostTurnAuditItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool Enqueue(PostTurnAuditItem item)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (_channel.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _enqueued);
            Interlocked.Increment(ref _pending);
            _stageMetrics?.Record("audit_enqueue", timer.ElapsedMilliseconds, success: true);
            return true;
        }

        Interlocked.Increment(ref _dropped);
        Interlocked.Increment(ref _deadLettered);
        _deadLetters?.Write(item, "queue_full");
        _stageMetrics?.Record("audit_enqueue", timer.ElapsedMilliseconds, success: false);
        return false;
    }

    public async ValueTask<PostTurnAuditItem> DequeueAsync(CancellationToken ct)
    {
        var item = await _channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return item;
    }

    public void RecordProcessed(long elapsedMs, bool success)
    {
        Interlocked.Increment(ref _processed);
        Interlocked.Add(ref _elapsedMsSum, Math.Max(0, elapsedMs));
        if (!success)
        {
            Interlocked.Increment(ref _failed);
        }

        Interlocked.Exchange(ref _lastProcessedUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public PostTurnAuditSnapshot GetSnapshot()
    {
        var pending = (int)Math.Max(0, Volatile.Read(ref _pending));
        var enqueued = (int)Volatile.Read(ref _enqueued);
        var dropped = (int)Volatile.Read(ref _dropped);
        var processed = (int)Volatile.Read(ref _processed);
        var failed = (int)Volatile.Read(ref _failed);
        var deadLettered = (int)Volatile.Read(ref _deadLettered);
        var elapsedSum = Volatile.Read(ref _elapsedMsSum);
        var lastProcessedUnix = Volatile.Read(ref _lastProcessedUnixMs);
        var avgProcessing = processed == 0 ? 0 : (double)elapsedSum / processed;
        DateTimeOffset? lastProcessedAt = lastProcessedUnix <= 0
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(lastProcessedUnix);

        var alerts = new List<string>();
        if (dropped > 0)
        {
            alerts.Add("Post-turn audit queue dropped items.");
        }
        var backlogThreshold = _options?.Audit.BacklogAlertThreshold ?? 96;
        if (pending >= backlogThreshold)
        {
            alerts.Add("Post-turn audit backlog exceeded threshold.");
        }
        if (processed >= 20 && failed > processed * 0.2)
        {
            alerts.Add("Post-turn audit failure rate above 20%.");
        }

        return new PostTurnAuditSnapshot(
            pending,
            enqueued,
            dropped,
            processed,
            failed,
            deadLettered,
            avgProcessing,
            lastProcessedAt,
            alerts);
    }

    private static int ReadCapacity()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_POST_TURN_AUDIT_QUEUE_CAPACITY");
        return int.TryParse(raw, out var capacity)
            ? Math.Clamp(capacity, 64, 8192)
            : 512;
    }
}

