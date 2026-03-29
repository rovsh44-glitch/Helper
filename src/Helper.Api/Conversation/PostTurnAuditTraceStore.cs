using System.Text.Json;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed record PostTurnAuditTraceSnapshot(
    int TotalItems,
    int Approved,
    int Flagged,
    int Failed,
    DateTimeOffset? LastRecordedAtUtc,
    string Path);

public interface IPostTurnAuditTraceStore
{
    void Write(PostTurnAuditItem item, string outcome, string? feedback = null, string? correlationId = null);
    PostTurnAuditTraceSnapshot GetSnapshot();
}

public sealed class PostTurnAuditTraceStore : IPostTurnAuditTraceStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private int _totalItems;
    private int _approved;
    private int _flagged;
    private int _failed;
    private DateTimeOffset? _lastRecordedAtUtc;

    public PostTurnAuditTraceStore(ApiRuntimeConfig runtimeConfig)
    {
        _path = Path.Combine(runtimeConfig.LogsRoot, "post_turn_audit.trace.jsonl");
    }

    public void Write(PostTurnAuditItem item, string outcome, string? feedback = null, string? correlationId = null)
    {
        var normalizedOutcome = string.IsNullOrWhiteSpace(outcome) ? "unknown" : outcome.Trim().ToLowerInvariant();

        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(new
            {
                item.ConversationId,
                item.TurnId,
                CorrelationId = correlationId,
                Outcome = normalizedOutcome,
                Feedback = feedback,
                item.Attempt,
                item.IsFactualPrompt,
                Sources = item.Sources,
                SourceCount = item.Sources.Count,
                item.CreatedAt,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });

            File.AppendAllText(_path, line + Environment.NewLine);
            _totalItems++;
            switch (normalizedOutcome)
            {
                case "approved":
                    _approved++;
                    break;
                case "flagged":
                    _flagged++;
                    break;
                default:
                    _failed++;
                    break;
            }

            _lastRecordedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public PostTurnAuditTraceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new PostTurnAuditTraceSnapshot(
                _totalItems,
                _approved,
                _flagged,
                _failed,
                _lastRecordedAtUtc,
                _path);
        }
    }
}

