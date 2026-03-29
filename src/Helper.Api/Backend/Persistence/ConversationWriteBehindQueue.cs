using System.Threading.Channels;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Backend.Persistence;

public sealed record ConversationPersistenceQueueSnapshot(
    int Pending,
    int Enqueued,
    int Dropped,
    int Flushed,
    double AvgFlushMs,
    DateTimeOffset? LastFlushedAtUtc,
    IReadOnlyList<string> Alerts);

public interface IConversationWriteBehindQueue
{
    bool Enqueue(string conversationId);
    ValueTask<IReadOnlyList<string>> DequeueBatchAsync(int maxBatchSize, TimeSpan maxWait, CancellationToken ct);
    void RecordFlushed(int count, long elapsedMs, bool success);
    ConversationPersistenceQueueSnapshot GetSnapshot();
}

public sealed class ConversationWriteBehindQueue : IConversationWriteBehindQueue
{
    private readonly Channel<string> _channel;
    private readonly IBackendOptionsCatalog _options;
    private long _pending;
    private long _enqueued;
    private long _dropped;
    private long _flushed;
    private long _flushElapsedMs;
    private long _lastFlushedUnixMs;

    public ConversationWriteBehindQueue(IBackendOptionsCatalog options)
    {
        _options = options;
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.Persistence.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool Enqueue(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(conversationId.Trim()))
        {
            Interlocked.Increment(ref _pending);
            Interlocked.Increment(ref _enqueued);
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public async ValueTask<IReadOnlyList<string>> DequeueBatchAsync(int maxBatchSize, TimeSpan maxWait, CancellationToken ct)
    {
        var batch = new List<string>(Math.Max(1, maxBatchSize));
        var first = await _channel.Reader.ReadAsync(ct);
        batch.Add(first);
        Interlocked.Decrement(ref _pending);

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(maxWait);
        try
        {
            while (batch.Count < maxBatchSize)
            {
                var next = await _channel.Reader.ReadAsync(waitCts.Token);
                batch.Add(next);
                Interlocked.Decrement(ref _pending);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }

        return batch;
    }

    public void RecordFlushed(int count, long elapsedMs, bool success)
    {
        if (success)
        {
            Interlocked.Add(ref _flushed, Math.Max(0, count));
        }

        Interlocked.Add(ref _flushElapsedMs, Math.Max(0, elapsedMs));
        Interlocked.Exchange(ref _lastFlushedUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public ConversationPersistenceQueueSnapshot GetSnapshot()
    {
        var enqueued = (int)Volatile.Read(ref _enqueued);
        var flushed = (int)Volatile.Read(ref _flushed);
        var dropped = (int)Volatile.Read(ref _dropped);
        var pending = (int)Math.Max(0, Volatile.Read(ref _pending));
        var avgFlushMs = flushed == 0 ? 0 : (double)Volatile.Read(ref _flushElapsedMs) / flushed;
        var lastFlushedUnix = Volatile.Read(ref _lastFlushedUnixMs);
        var alerts = new List<string>();

        if (pending >= _options.Persistence.BacklogAlertThreshold)
        {
            alerts.Add("Persistence queue backlog exceeded threshold.");
        }

        if (dropped > 0)
        {
            alerts.Add("Persistence queue dropped dirty conversation notifications.");
        }

        return new ConversationPersistenceQueueSnapshot(
            Pending: pending,
            Enqueued: enqueued,
            Dropped: dropped,
            Flushed: flushed,
            AvgFlushMs: avgFlushMs,
            LastFlushedAtUtc: lastFlushedUnix <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastFlushedUnix),
            Alerts: alerts);
    }
}

