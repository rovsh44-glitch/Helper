using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public enum ChatStreamChunkType
{
    Token,
    Stage,
    Warning,
    Done
}

public sealed record TokenChunk(
    ChatStreamChunkType Type,
    string? Content,
    int Offset,
    DateTimeOffset TimestampUtc,
    ChatResponseDto? FinalResponse = null,
    string? ConversationId = null,
    string? TurnId = null,
    DateTimeOffset? ModelStreamStartedAtUtc = null,
    string? Stage = null,
    string? WarningCode = null,
    int? ResumeCursor = null);

