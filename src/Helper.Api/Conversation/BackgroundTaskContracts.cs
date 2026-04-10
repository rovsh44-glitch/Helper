namespace Helper.Api.Conversation;

public sealed record BackgroundConversationTask(
    string Id,
    string Kind,
    string Title,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DueAtUtc = null,
    string? ProjectId = null,
    string? Notes = null,
    string? BranchId = null,
    string? ProjectLabelSnapshot = null,
    IReadOnlyList<string>? ReferenceArtifactsSnapshot = null,
    IReadOnlyList<string>? ProactiveTopicSnapshot = null);

public sealed record ProactiveTopicSubscription(
    string Id,
    string Topic,
    string Frequency,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    string? ProjectId = null);
