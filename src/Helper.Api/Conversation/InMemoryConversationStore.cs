using System.Collections.Concurrent;
using Helper.Api.Backend.Persistence;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed partial class InMemoryConversationStore : IConversationStore, IDisposable, IConversationPersistenceHealth, IConversationWriteBehindStore
{
    private readonly ConcurrentDictionary<string, ConversationState> _conversations = new();
    private readonly ConcurrentDictionary<string, byte> _dirtyConversationIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly IMemoryPolicyService _memoryPolicy;
    private readonly IProjectMemoryBoundaryPolicy _projectMemoryBoundaryPolicy;
    private readonly IConversationSummarizer _summarizer;
    private readonly DebouncedPersistenceScheduler? _persistenceScheduler;
    private readonly IConversationPersistenceEngine? _persistenceEngine;
    private readonly IConversationWriteBehindQueue? _writeBehindQueue;

    public InMemoryConversationStore()
        : this(new MemoryPolicyService(), new ConversationSummarizer(), BuildPersistenceEngine(ResolvePersistencePath()), null, null)
    {
    }

    public InMemoryConversationStore(string? persistencePath)
        : this(new MemoryPolicyService(), new ConversationSummarizer(), BuildPersistenceEngine(persistencePath), null, null)
    {
    }

    public InMemoryConversationStore(IMemoryPolicyService memoryPolicy)
        : this(memoryPolicy, new ConversationSummarizer(), BuildPersistenceEngine(ResolvePersistencePath()), null, null)
    {
    }

    public InMemoryConversationStore(IMemoryPolicyService memoryPolicy, IConversationSummarizer summarizer)
        : this(memoryPolicy, summarizer, BuildPersistenceEngine(ResolvePersistencePath()), null, null)
    {
    }

    public InMemoryConversationStore(IMemoryPolicyService memoryPolicy, string? persistencePath)
        : this(memoryPolicy, new ConversationSummarizer(), BuildPersistenceEngine(persistencePath), null, null)
    {
    }

    public InMemoryConversationStore(IMemoryPolicyService memoryPolicy, IConversationSummarizer summarizer, string? persistencePath)
        : this(memoryPolicy, summarizer, BuildPersistenceEngine(persistencePath), null, null)
    {
    }

    public InMemoryConversationStore(
        IMemoryPolicyService memoryPolicy,
        IConversationSummarizer summarizer,
        IConversationPersistenceEngine? persistenceEngine,
        IConversationWriteBehindQueue? writeBehindQueue,
        IConversationStageMetricsService? stageMetrics = null,
        IProjectMemoryBoundaryPolicy? projectMemoryBoundaryPolicy = null)
    {
        _memoryPolicy = memoryPolicy ?? throw new ArgumentNullException(nameof(memoryPolicy));
        _projectMemoryBoundaryPolicy = projectMemoryBoundaryPolicy ?? new ProjectMemoryBoundaryPolicy();
        _summarizer = summarizer ?? throw new ArgumentNullException(nameof(summarizer));
        _persistenceEngine = persistenceEngine;
        _writeBehindQueue = writeBehindQueue;
        if (_persistenceEngine != null)
        {
            if (_writeBehindQueue == null)
            {
                _persistenceScheduler = new DebouncedPersistenceScheduler(
                    TimeSpan.FromMilliseconds(ReadPersistenceFlushDelayMs()),
                    FlushDirtyConversations);
            }

            foreach (var state in _persistenceEngine.Load())
            {
                _conversations[state.Id] = state;
            }
        }
    }

    public ConversationState GetOrCreate(string? conversationId)
    {
        var id = string.IsNullOrWhiteSpace(conversationId) ? Guid.NewGuid().ToString("N") : conversationId.Trim();
        return _conversations.GetOrAdd(id, key => new ConversationState(key));
    }

    public void AddMessage(ConversationState state, ChatMessageDto message)
    {
        var now = DateTimeOffset.UtcNow;
        lock (state.SyncRoot)
        {
            var resolvedBranch = string.IsNullOrWhiteSpace(message.BranchId) ? state.ActiveBranchId : message.BranchId!;
            var normalizedMessage = message with { BranchId = resolvedBranch };
            state.Messages.Add(normalizedMessage);
            _memoryPolicy.CaptureFromUserMessage(state, normalizedMessage, now);
            UpdateRollingSummary(state);
            UpdateBranchSummary(state, resolvedBranch, now);
            state.UpdatedAt = now;
        }
        RequestPersist(state.Id);
    }

    public bool TryGet(string conversationId, out ConversationState state)
    {
        return _conversations.TryGetValue(conversationId, out state!);
    }

    public void MarkUpdated(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        RequestPersist(state.Id);
    }

    public bool Remove(string conversationId)
    {
        var removed = _conversations.TryRemove(conversationId, out _);
        if (removed)
        {
            RequestPersist(conversationId);
        }

        return removed;
    }

    public void Dispose()
    {
        FlushDirtyConversations();
        _persistenceScheduler?.Dispose();
        _persistenceEngine?.Dispose();
    }

    public ConversationPersistenceHealthSnapshot GetSnapshot(int pendingDirtyConversations = 0)
    {
        return _persistenceEngine?.GetSnapshot(Math.Max(pendingDirtyConversations, PendingDirtyConversations))
            ?? new ConversationPersistenceHealthSnapshot(
                Enabled: false,
                Ready: true,
                Loaded: true,
                LastFlushSucceeded: true,
                PendingDirtyConversations: 0,
                LastJournalWriteAtUtc: null,
                LastSnapshotAtUtc: null,
                SnapshotPath: string.Empty,
                JournalPath: string.Empty,
                Alerts: Array.Empty<string>());
    }

    public int PendingDirtyConversations => _dirtyConversationIds.Count;

    public IReadOnlyList<ConversationState> DrainDirtyConversations(IReadOnlyCollection<string> conversationIds)
    {
        if (conversationIds.Count == 0)
        {
            return Array.Empty<ConversationState>();
        }

        var dirtyStates = new List<ConversationState>(conversationIds.Count);
        foreach (var conversationId in conversationIds)
        {
            if (_dirtyConversationIds.TryRemove(conversationId, out _) &&
                _conversations.TryGetValue(conversationId, out var state))
            {
                dirtyStates.Add(state);
            }
        }

        return dirtyStates;
    }

    public IReadOnlyList<ConversationState> SnapshotAllConversations()
    {
        return _conversations.Values.ToList();
    }

    public void FlushDirtyConversationsNow()
    {
        FlushDirtyConversations();
    }
}

