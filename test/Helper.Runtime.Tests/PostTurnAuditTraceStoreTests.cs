using System.Text.Json;
using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class PostTurnAuditTraceStoreTests
{
    [Fact]
    public void Write_RecordsStructuredTraceEntries_AndUpdatesSnapshot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"helper-audit-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var config = new ApiRuntimeConfig("root", "data", "projects", "library", tempRoot, "templates", "dev-key");
            var store = new PostTurnAuditTraceStore(config);
            var item = new PostTurnAuditItem(
                "conv-1",
                "turn-1",
                "research .net observability",
                "answer",
                true,
                new List<string> { "https://example.org/a", "https://example.org/b" },
                DateTimeOffset.UtcNow);

            store.Write(item, "approved");
            store.Write(item with { TurnId = "turn-2" }, "flagged", "source mismatch");

            var snapshot = store.GetSnapshot();

            Assert.Equal(2, snapshot.TotalItems);
            Assert.Equal(1, snapshot.Approved);
            Assert.Equal(1, snapshot.Flagged);
            Assert.Equal(0, snapshot.Failed);
            Assert.True(File.Exists(snapshot.Path));

            var lines = File.ReadAllLines(snapshot.Path);
            Assert.Equal(2, lines.Length);

            using var doc = JsonDocument.Parse(lines[1]);
            Assert.Equal("conv-1", doc.RootElement.GetProperty("ConversationId").GetString());
            Assert.Equal("turn-2", doc.RootElement.GetProperty("TurnId").GetString());
            Assert.Equal("flagged", doc.RootElement.GetProperty("Outcome").GetString());
            Assert.Equal("source mismatch", doc.RootElement.GetProperty("Feedback").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("SourceCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

