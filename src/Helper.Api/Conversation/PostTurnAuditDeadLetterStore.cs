using System.Text.Json;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed record PostTurnAuditDeadLetterSnapshot(
    int TotalItems,
    DateTimeOffset? LastRecordedAtUtc,
    string Path);

public interface IPostTurnAuditDeadLetterStore
{
    void Write(PostTurnAuditItem item, string reason, string? details = null);
    PostTurnAuditDeadLetterSnapshot GetSnapshot();
}

public sealed class PostTurnAuditDeadLetterStore : IPostTurnAuditDeadLetterStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private int _totalItems;
    private DateTimeOffset? _lastRecordedAtUtc;

    public PostTurnAuditDeadLetterStore(ApiRuntimeConfig runtimeConfig)
    {
        _path = Path.Combine(runtimeConfig.LogsRoot, "post_turn_audit.dlq.jsonl");
    }

    public void Write(PostTurnAuditItem item, string reason, string? details = null)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(new
            {
                item.ConversationId,
                item.TurnId,
                item.UserMessage,
                item.AssistantResponse,
                item.IsFactualPrompt,
                item.Sources,
                item.CreatedAt,
                Reason = reason,
                Details = details,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
            File.AppendAllText(_path, line + Environment.NewLine);
            _totalItems++;
            _lastRecordedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public PostTurnAuditDeadLetterSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new PostTurnAuditDeadLetterSnapshot(_totalItems, _lastRecordedAtUtc, _path);
        }
    }
}

