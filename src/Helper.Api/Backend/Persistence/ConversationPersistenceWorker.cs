using System.Diagnostics;
using Helper.Api.Backend.Configuration;
using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Api.Backend.Persistence;

public sealed class ConversationPersistenceWorker : BackgroundService
{
    private readonly IConversationWriteBehindQueue _queue;
    private readonly IConversationWriteBehindStore _store;
    private readonly IConversationPersistenceEngine _engine;
    private readonly IBackendOptionsCatalog _options;
    private readonly IConversationStageMetricsService? _stageMetrics;
    private readonly ILogger<ConversationPersistenceWorker> _logger;

    public ConversationPersistenceWorker(
        IConversationWriteBehindQueue queue,
        IConversationWriteBehindStore store,
        IConversationPersistenceEngine engine,
        IBackendOptionsCatalog options,
        IConversationStageMetricsService? stageMetrics,
        ILogger<ConversationPersistenceWorker> logger)
    {
        _queue = queue;
        _store = store;
        _engine = engine;
        _options = options;
        _stageMetrics = stageMetrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<string> batch;
            try
            {
                batch = await _queue.DequeueBatchAsync(
                    _options.Persistence.MaxBatchSize,
                    TimeSpan.FromMilliseconds(_options.Persistence.FlushDelayMs),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushAsync(batch, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _store.FlushDirtyConversationsNow();
        await base.StopAsync(cancellationToken);
    }

    private Task FlushAsync(IReadOnlyList<string> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        var timer = Stopwatch.StartNew();
        var ids = batch
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            var dirtyStates = _store.DrainDirtyConversations(ids);
            if (dirtyStates.Count == 0)
            {
                _queue.RecordFlushed(0, timer.ElapsedMilliseconds, success: true);
                return Task.CompletedTask;
            }

            var allStates = _store.SnapshotAllConversations();
            _engine.FlushDirty(dirtyStates, allStates);
            _queue.RecordFlushed(dirtyStates.Count, timer.ElapsedMilliseconds, success: true);
            _stageMetrics?.Record("persistence_queue_flush", timer.ElapsedMilliseconds, success: true);
        }
        catch (Exception ex)
        {
            _queue.RecordFlushed(0, timer.ElapsedMilliseconds, success: false);
            _stageMetrics?.Record("persistence_queue_flush", timer.ElapsedMilliseconds, success: false);
            _logger.LogWarning(ex, "Conversation persistence worker flush failed. BatchSize={BatchSize}", ids.Length);
        }

        return Task.CompletedTask;
    }
}

