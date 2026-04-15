using Helper.Api.Backend.Application;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.Persistence;
using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public class BackendRebuildRuntimeTests
{
    [Fact]
    public void StartupReadinessService_TracksLifecycleStates()
    {
        var config = new ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "key");
        var options = new BackendOptionsCatalog(config);
        var readiness = new StartupReadinessService(options);

        readiness.MarkListening();
        readiness.MarkDependenciesReady();
        readiness.MarkMinimalReady("minimal_ready");
        readiness.MarkWarmReady();

        var snapshot = readiness.GetSnapshot();

        Assert.Equal("warmready", snapshot.LifecycleState.Replace("_", string.Empty).ToLowerInvariant());
        Assert.True(snapshot.ReadyForChat);
        Assert.True(snapshot.Listening);
        Assert.Equal("warm_ready", snapshot.Phase);
        Assert.NotNull(snapshot.TimeToListeningMs);
        Assert.NotNull(snapshot.TimeToReadyMs);
        Assert.NotNull(snapshot.TimeToWarmReadyMs);
    }

    [Fact]
    public void TurnExecutionStateMachine_TracksNominalTrace()
    {
        var machine = new TurnExecutionStateMachine();
        var context = new ChatTurnContext
        {
            TurnId = "exec-trace",
            Request = new ChatRequestDto("hello", "conv", 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>()
        };

        machine.Transition(context, TurnExecutionState.Validated);
        machine.Transition(context, TurnExecutionState.Planned);
        machine.Transition(context, TurnExecutionState.Executed);
        machine.Transition(context, TurnExecutionState.ValidatedByPolicy);
        machine.Transition(context, TurnExecutionState.Finalized);
        machine.Transition(context, TurnExecutionState.Persisted);
        machine.Transition(context, TurnExecutionState.AuditedAsync);
        machine.Transition(context, TurnExecutionState.Completed);

        Assert.Equal(
            new[]
            {
                TurnExecutionState.Received,
                TurnExecutionState.Validated,
                TurnExecutionState.Planned,
                TurnExecutionState.Executed,
                TurnExecutionState.ValidatedByPolicy,
                TurnExecutionState.Finalized,
                TurnExecutionState.Persisted,
                TurnExecutionState.AuditedAsync,
                TurnExecutionState.Completed
            },
            context.ExecutionTrace);
    }

    [Fact]
    public void TurnExecutionStateMachine_RecoversToFinalized()
    {
        var machine = new TurnExecutionStateMachine();
        var context = new ChatTurnContext
        {
            TurnId = "exec-recover",
            Request = new ChatRequestDto("hello", "conv", 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>()
        };
        machine.Transition(context, TurnExecutionState.Validated);
        machine.Transition(context, TurnExecutionState.Planned);
        machine.Transition(context, TurnExecutionState.Executed);

        var recovered = machine.TryRecoverToFinalize(context, out var reason);

        Assert.True(recovered);
        Assert.Equal(TurnExecutionState.Finalized, context.ExecutionState);
        Assert.Contains("recovered_from_executed", reason);
    }

    [Fact]
    public async Task WriteBehindPersistenceQueue_FlushesDirtyConversation()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-write-behind-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ApiRuntimeConfig("root", Path.GetTempPath(), "projects", "library", Path.GetDirectoryName(tempPath)!, "templates", "key");
            var options = new BackendOptionsCatalog(config);
            var queue = new ConversationWriteBehindQueue(options);
            var engine = new FileConversationPersistence(tempPath);
            var store = new InMemoryConversationStore(new MemoryPolicyService(), new ConversationSummarizer(), engine, queue);

            var state = store.GetOrCreate("write-behind-conv");
            store.AddMessage(state, new ChatMessageDto("user", "persist me", DateTimeOffset.UtcNow, "turn-1"));

            var batch = await queue.DequeueBatchAsync(16, TimeSpan.FromMilliseconds(25), CancellationToken.None);
            var dirty = store.DrainDirtyConversations(batch);
            engine.FlushDirty(dirty, store.SnapshotAllConversations());

            var restored = new InMemoryConversationStore(new MemoryPolicyService(), new ConversationSummarizer(), new FileConversationPersistence(tempPath), null);

            Assert.True(restored.TryGet("write-behind-conv", out var restoredState));
            Assert.NotNull(restoredState);
            Assert.Contains(restoredState.Messages, message => message.Content.Contains("persist me", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var journalPath = Path.ChangeExtension(tempPath, ".journal.jsonl");
            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }
        }
    }

}

