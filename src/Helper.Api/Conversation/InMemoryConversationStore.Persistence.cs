using Helper.Api.Backend.Persistence;
using Helper.Api.Hosting;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Conversation;

public sealed partial class InMemoryConversationStore
{
    private static string? ResolvePersistencePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("HELPER_CONVERSATION_STORE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.IsPathRooted(explicitPath)
                ? Path.GetFullPath(explicitPath)
                : HelperWorkspacePathResolver.ResolveLogsPath(explicitPath);
        }

        var enabledRaw = Environment.GetEnvironmentVariable("HELPER_CONVERSATION_PERSIST");
        var enabled = bool.TryParse(enabledRaw, out var parsed) && parsed;
        if (!enabled)
        {
            return null;
        }

        return HelperWorkspacePathResolver.ResolveLogsPath("conversation_store.json");
    }

    private static IConversationPersistenceEngine? BuildPersistenceEngine(string? persistencePath)
    {
        if (string.IsNullOrWhiteSpace(persistencePath))
        {
            return null;
        }

        return new FileConversationPersistence(persistencePath);
    }

    private static int ReadPersistenceFlushDelayMs()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_CONVERSATION_PERSIST_FLUSH_MS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 0, 30000)
            : 1500;
    }

    private void RequestPersist(string conversationId)
    {
        if (_persistenceEngine == null || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _dirtyConversationIds[conversationId.Trim()] = 0;
        if (_writeBehindQueue != null)
        {
            _writeBehindQueue.Enqueue(conversationId);
            return;
        }

        _persistenceScheduler?.Request();
    }

    private void FlushDirtyConversations()
    {
        if (_persistenceEngine == null || _dirtyConversationIds.IsEmpty)
        {
            return;
        }

        try
        {
            var dirtyIds = _dirtyConversationIds.Keys.ToArray();
            var dirtyStates = new List<ConversationState>(dirtyIds.Length);
            foreach (var conversationId in dirtyIds)
            {
                if (_dirtyConversationIds.TryRemove(conversationId, out _) &&
                    _conversations.TryGetValue(conversationId, out var state))
                {
                    dirtyStates.Add(state);
                }
            }

            if (dirtyStates.Count == 0)
            {
                return;
            }

            _persistenceEngine.FlushDirty(dirtyStates, _conversations.Values.ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationStore] Persist failed: {ex.Message}");
        }
    }
}

