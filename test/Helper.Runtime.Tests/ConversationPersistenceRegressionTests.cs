using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ConversationPersistenceRegressionTests
{
    [Fact]
    public void FlushDirty_CompactsSnapshot_EvenWhenLegacyFixedTempFileIsLocked()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-write-behind-{Guid.NewGuid():N}.json");
        var legacyTempPath = tempPath + ".tmp";
        var originalThreshold = Environment.GetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD");

        try
        {
            Environment.SetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD", "5");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            File.WriteAllText(legacyTempPath, "stale");

            using var lockedLegacyTemp = new FileStream(legacyTempPath, FileMode.Open, FileAccess.Read, FileShare.None);
            using var engine = new FileConversationPersistence(tempPath);
            var states = Enumerable.Range(1, 5)
                .Select(index =>
                {
                    var state = new ConversationState($"locked-temp-conv-{index}");
                    state.Messages.Add(new ChatMessageDto("user", $"persist me {index}", DateTimeOffset.UtcNow, $"turn-{index}"));
                    return state;
                })
                .ToArray();

            engine.FlushDirty(states, states);

            Assert.True(File.Exists(tempPath));
            var health = engine.GetSnapshot();
            Assert.True(health.LastFlushSucceeded);
            Assert.DoesNotContain(health.Alerts, alert => alert.Contains("flush failed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD", originalThreshold);

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var journalPath = Path.Combine(
                Path.GetDirectoryName(tempPath)!,
                Path.GetFileNameWithoutExtension(tempPath) + ".journal.jsonl");
            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }

            if (File.Exists(legacyTempPath))
            {
                File.Delete(legacyTempPath);
            }
        }
    }

    [Fact]
    public void FlushDirty_StaysHealthy_WhenSnapshotCompactionIsDeferredAfterJournalWrite()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-write-behind-{Guid.NewGuid():N}.json");
        var originalThreshold = Environment.GetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD");

        try
        {
            Environment.SetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD", "5");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            File.WriteAllText(tempPath, "{\"schemaVersion\":6,\"savedAt\":\"2026-04-14T00:00:00Z\",\"conversations\":[]}");

            using var lockedSnapshot = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);
            using var engine = new FileConversationPersistence(tempPath);
            _ = engine.Load();
            var states = Enumerable.Range(1, 5)
                .Select(index =>
                {
                    var state = new ConversationState($"deferred-compaction-conv-{index}");
                    state.Messages.Add(new ChatMessageDto("user", $"persist me {index}", DateTimeOffset.UtcNow, $"turn-{index}"));
                    return state;
                })
                .ToArray();

            engine.FlushDirty(states, states);

            var health = engine.GetSnapshot();
            Assert.True(health.LastFlushSucceeded);
            Assert.Contains(health.Alerts, alert => alert.Contains("snapshot compaction deferred", StringComparison.OrdinalIgnoreCase));

            var journalPath = Path.Combine(
                Path.GetDirectoryName(tempPath)!,
                Path.GetFileNameWithoutExtension(tempPath) + ".journal.jsonl");
            Assert.True(File.Exists(journalPath));
            Assert.Equal(5, File.ReadLines(journalPath).Count());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD", originalThreshold);

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var journalPath = Path.Combine(
                Path.GetDirectoryName(tempPath)!,
                Path.GetFileNameWithoutExtension(tempPath) + ".journal.jsonl");
            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }
        }
    }
}
