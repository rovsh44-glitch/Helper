using System.Text;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed partial class InMemoryConversationStore
{
    public IReadOnlyList<ChatMessageDto> GetRecentMessages(ConversationState state, int maxHistory)
        => GetRecentMessages(state, state.ActiveBranchId, maxHistory);

    public IReadOnlyList<ChatMessageDto> GetRecentMessages(ConversationState state, string branchId, int maxHistory)
    {
        var limit = Math.Clamp(maxHistory, 1, 200);
        lock (state.SyncRoot)
        {
            var activeMemoryItems = _memoryPolicy.GetActiveItems(state, DateTimeOffset.UtcNow);
            var visibleMemoryItems = activeMemoryItems
                .Where(item => _projectMemoryBoundaryPolicy.ShouldInclude(item, state.ProjectContext))
                .ToList();
            var branchMessages = state.Messages
                .Where(m => string.Equals(m.BranchId ?? "main", branchId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var collapsed = CollapseAssistantVersions(branchMessages);
            var skip = Math.Max(0, collapsed.Count - limit);
            var messages = collapsed.Skip(skip).ToList();
            if (state.BranchSummaries.TryGetValue(branchId, out var branchSummary) && !string.IsNullOrWhiteSpace(branchSummary.Summary))
            {
                messages.Insert(0, new ChatMessageDto("system", $"Conversation summary ({branchId}): {branchSummary.Summary}", DateTimeOffset.UtcNow));
            }
            else if (!string.IsNullOrWhiteSpace(state.RollingSummary))
            {
                messages.Insert(0, new ChatMessageDto("system", $"Conversation summary: {state.RollingSummary}", DateTimeOffset.UtcNow));
            }

            var visiblePreferences = visibleMemoryItems
                .Where(item => item.Type.Equals("long_term", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Content)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();

            if (visiblePreferences.Length > 0)
            {
                messages.Insert(0, new ChatMessageDto("system", $"User preferences: {string.Join(", ", visiblePreferences)}.", DateTimeOffset.UtcNow));
            }

            if (!string.Equals(state.PreferredLanguage, "auto", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.DetailLevel, "balanced", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.Formality, "neutral", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.DomainFamiliarity, "intermediate", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.PreferredStructure, "auto", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.Warmth, "balanced", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.Enthusiasm, "balanced", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.Directness, "balanced", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.DefaultAnswerShape, "auto", StringComparison.OrdinalIgnoreCase))
            {
                messages.Insert(0, new ChatMessageDto(
                    "system",
                    $"Conversation profile: language={state.PreferredLanguage}, detail={state.DetailLevel}, formality={state.Formality}, domain={state.DomainFamiliarity}, structure={state.PreferredStructure}, warmth={state.Warmth}, enthusiasm={state.Enthusiasm}, directness={state.Directness}, answer_shape={state.DefaultAnswerShape}.",
                    DateTimeOffset.UtcNow));
            }

            var visibleTasks = visibleMemoryItems
                .Where(item => item.Type.Equals("task", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => item.Content)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            if (visibleTasks.Length > 0)
            {
                messages.Insert(0, new ChatMessageDto("system", $"Open tasks: {string.Join(" | ", visibleTasks)}", DateTimeOffset.UtcNow));
            }

            return messages;
        }
    }

    public void SetActiveBranch(ConversationState state, string branchId)
    {
        lock (state.SyncRoot)
        {
            if (!state.Branches.ContainsKey(branchId))
            {
                throw new InvalidOperationException($"Branch '{branchId}' does not exist.");
            }

            state.ActiveBranchId = branchId;
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }
        RequestPersist(state.Id);
    }

    public string GetActiveBranchId(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            return state.ActiveBranchId;
        }
    }

    public IReadOnlyList<string> GetBranchIds(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            return state.Branches.Keys.OrderBy(x => x).ToList();
        }
    }

    public bool CreateBranch(ConversationState state, string fromTurnId, string? requestedBranchId, out string branchId)
    {
        var created = false;
        lock (state.SyncRoot)
        {
            var sourceBranch = state.ActiveBranchId;
            var sourceMessages = state.Messages
                .Where(m => string.Equals(m.BranchId ?? "main", sourceBranch, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var pivotIndex = sourceMessages.FindLastIndex(m => string.Equals(m.TurnId, fromTurnId, StringComparison.OrdinalIgnoreCase));
            if (pivotIndex < 0)
            {
                branchId = string.Empty;
                return false;
            }

            branchId = string.IsNullOrWhiteSpace(requestedBranchId)
                ? $"branch-{state.Branches.Count + 1}"
                : requestedBranchId.Trim();

            if (state.Branches.ContainsKey(branchId))
            {
                return false;
            }

            state.Branches[branchId] = new BranchDescriptor(branchId, sourceBranch, fromTurnId, DateTimeOffset.UtcNow);

            foreach (var message in sourceMessages.Take(pivotIndex + 1))
            {
                state.Messages.Add(message with { BranchId = branchId });
            }

            if (state.BranchSummaries.TryGetValue(sourceBranch, out var sourceSummary))
            {
                state.BranchSummaries[branchId] = sourceSummary with
                {
                    BranchId = branchId,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    SourceMessageCount = pivotIndex + 1
                };
            }

            UpdateBranchSummary(state, branchId, DateTimeOffset.UtcNow);
            state.ActiveBranchId = branchId;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            created = true;
        }

        if (created)
        {
            RequestPersist(state.Id);
        }

        return created;
    }

    public bool MergeBranch(ConversationState state, string sourceBranchId, string targetBranchId, out int mergedMessages, out string? error)
    {
        mergedMessages = 0;
        error = null;

        var source = sourceBranchId?.Trim() ?? string.Empty;
        var target = targetBranchId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            error = "Both sourceBranchId and targetBranchId are required.";
            return false;
        }

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            error = "Source and target branches must differ.";
            return false;
        }

        lock (state.SyncRoot)
        {
            if (!state.Branches.ContainsKey(source))
            {
                error = $"Source branch '{source}' does not exist.";
                return false;
            }

            if (!state.Branches.ContainsKey(target))
            {
                error = $"Target branch '{target}' does not exist.";
                return false;
            }

            var targetKeys = state.Messages
                .Where(m => string.Equals(m.BranchId ?? "main", target, StringComparison.OrdinalIgnoreCase))
                .Select(BuildMergeMessageKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sourceMessages = state.Messages
                .Where(m => string.Equals(m.BranchId ?? "main", source, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Timestamp)
                .ToList();

            foreach (var message in sourceMessages)
            {
                var mergeKey = BuildMergeMessageKey(message);
                if (targetKeys.Contains(mergeKey))
                {
                    continue;
                }

                state.Messages.Add(message with { BranchId = target });
                targetKeys.Add(mergeKey);
                mergedMessages++;
            }

            var now = DateTimeOffset.UtcNow;
            UpdateBranchSummary(state, target, now);
            state.ActiveBranchId = target;
            state.UpdatedAt = now;
        }

        RequestPersist(state.Id);
        return true;
    }

    private void UpdateBranchSummary(ConversationState state, string branchId, DateTimeOffset now)
    {
        var branchMessages = state.Messages
            .Where(m => string.Equals(m.BranchId ?? "main", branchId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Timestamp)
            .ToList();
        state.BranchSummaries.TryGetValue(branchId, out var previous);
        var next = _summarizer.TryBuild(branchId, branchMessages, previous, now);
        if (next == null)
        {
            return;
        }

        state.BranchSummaries[branchId] = next;
        if (string.Equals(branchId, state.ActiveBranchId, StringComparison.OrdinalIgnoreCase))
        {
            state.RollingSummary = next.Summary;
        }
    }

    private static void UpdateRollingSummary(ConversationState state)
    {
        var userMessagesCount = state.Messages.Count(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        if (userMessagesCount == 0 || userMessagesCount % 8 != 0)
        {
            return;
        }

        var recentUserMessages = state.Messages
            .Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .TakeLast(8)
            .Select(m => m.Content.Length > 80 ? m.Content[..80] + "..." : m.Content)
            .ToList();

        if (recentUserMessages.Count > 0)
        {
            var summary = new StringBuilder();
            summary.Append(string.Join(" | ", recentUserMessages));
            state.RollingSummary = summary.ToString();
        }
    }

    private static List<ChatMessageDto> CollapseAssistantVersions(List<ChatMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return messages;
        }

        var latestAssistantByTurn = messages
            .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.TurnId))
            .GroupBy(m => m.TurnId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.TurnVersion).ThenByDescending(x => x.Timestamp).First(),
                StringComparer.OrdinalIgnoreCase);

        var collapsed = new List<ChatMessageDto>(messages.Count);
        foreach (var message in messages)
        {
            if (!message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(message.TurnId))
            {
                collapsed.Add(message);
                continue;
            }

            if (latestAssistantByTurn.TryGetValue(message.TurnId, out var latest) && ReferenceEquals(message, latest))
            {
                collapsed.Add(message);
            }
            else if (latestAssistantByTurn.TryGetValue(message.TurnId, out latest) && latest.Equals(message))
            {
                collapsed.Add(message);
            }
        }

        return collapsed.OrderBy(m => m.Timestamp).ToList();
    }

    private static string BuildMergeMessageKey(ChatMessageDto message)
    {
        var turnId = message.TurnId ?? string.Empty;
        return $"{message.Role}|{turnId}|{message.TurnVersion}|{message.Content}";
    }
}

