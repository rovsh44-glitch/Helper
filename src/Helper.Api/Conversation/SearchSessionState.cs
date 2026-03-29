namespace Helper.Api.Conversation;

internal sealed record SearchSessionState(
    string BranchId,
    string RootQuery,
    string LastUserQuery,
    string LastEffectiveQuery,
    string? LastTurnId,
    DateTimeOffset UpdatedAtUtc,
    string? CategoryHint,
    IReadOnlyList<string> SourceUrls,
    IReadOnlyList<CitationLineageEntry> CitationLineage,
    int ContinuationDepth,
    string? LastReuseReason = null,
    string? LastInputMode = null,
    IReadOnlyList<SelectiveEvidenceMemoryEntry>? EvidenceMemory = null)
{
    public IReadOnlyList<SelectiveEvidenceMemoryEntry> EffectiveEvidenceMemory =>
        EvidenceMemory ?? Array.Empty<SelectiveEvidenceMemoryEntry>();
}

internal static class SearchSessionStateAccessor
{
    public static string ResolveBranchId(ConversationState conversation, string? requestedBranchId)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return string.IsNullOrWhiteSpace(requestedBranchId)
            ? conversation.ActiveBranchId
            : requestedBranchId.Trim();
    }

    public static SearchSessionState? Get(ConversationState conversation, string? requestedBranchId)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var branchId = ResolveBranchId(conversation, requestedBranchId);
        lock (conversation.SyncRoot)
        {
            return conversation.SearchSessions.TryGetValue(branchId, out var state)
                ? state
                : null;
        }
    }

    public static void Set(ConversationState conversation, SearchSessionState state)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(state);
        lock (conversation.SyncRoot)
        {
            conversation.SearchSessions[state.BranchId] = state;
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}

