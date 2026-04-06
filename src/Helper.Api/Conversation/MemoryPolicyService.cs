using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed class MemoryPolicyService : IMemoryPolicyService
{
    private static readonly string[] TaskMarkers = { "todo", "нужно", "сделай", "fix", "implement", "почин" };
    private static readonly string[] PersonalMarkers =
    {
        "my ",
        "i am ",
        "i'm ",
        "me ",
        "мой ",
        "моя ",
        "мое ",
        "моё ",
        "меня ",
        "мне "
    };
    private readonly IMemoryPriorityPolicy _priorityPolicy;

    public MemoryPolicyService(IMemoryPriorityPolicy? priorityPolicy = null)
    {
        _priorityPolicy = priorityPolicy ?? new MemoryPriorityPolicy();
    }

    public void CaptureFromUserMessage(ConversationState state, ChatMessageDto message, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(message);

        if (!message.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            CleanupExpiredUnsafe(state, now);
            var content = message.Content.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            var sessionContent = Truncate(content, 180);
            AddOrRefreshUnsafe(
                state,
                "session",
                sessionContent,
                now,
                now.AddMinutes(ClampSessionTtlMinutes(state.SessionMemoryTtlMinutes)),
                message.TurnId,
                isPersonal: false,
                scope: MemoryScope.Session,
                retention: "session_ttl",
                whyRemembered: "recent_conversation_continuity",
                priority: _priorityPolicy.Score(MemoryScope.Session, false, sessionContent),
                sourceProjectId: state.ProjectContext?.ProjectId,
                userEditable: false);

            if (ContainsAnyToken(content, TaskMarkers))
            {
                AddOrRefreshUnsafe(
                    state,
                    "task",
                    Truncate(content, 180),
                    now,
                    now.AddHours(ClampTaskTtlHours(state.TaskMemoryTtlHours)),
                    message.TurnId,
                    isPersonal: false,
                    scope: state.ProjectContext is null ? MemoryScope.Task : MemoryScope.Project,
                    retention: state.ProjectContext is null ? "task_ttl" : "project_ttl",
                    whyRemembered: state.ProjectContext is null ? "task_signal" : "project_task_signal",
                    priority: _priorityPolicy.Score(state.ProjectContext is null ? MemoryScope.Task : MemoryScope.Project, false, content),
                    sourceProjectId: state.ProjectContext?.ProjectId,
                    userEditable: true);
            }

            if (RememberDirectiveParser.TryExtractFact(content, out var fact))
            {
                if (!string.IsNullOrWhiteSpace(fact) && state.LongTermMemoryEnabled)
                {
                    var isPersonal = IsPersonalFact(fact);
                    if (!isPersonal || state.PersonalMemoryConsentGranted)
                    {
                        AddOrRefreshUnsafe(
                            state,
                            "long_term",
                            Truncate(fact, 220),
                            now,
                            now.AddDays(ClampLongTermTtlDays(state.LongTermMemoryTtlDays)),
                            message.TurnId,
                            isPersonal,
                            scope: isPersonal ? MemoryScope.User : (state.ProjectContext is null ? MemoryScope.Session : MemoryScope.Project),
                            retention: "long_term_ttl",
                            whyRemembered: "explicit_remember_directive",
                            priority: _priorityPolicy.Score(isPersonal ? MemoryScope.User : (state.ProjectContext is null ? MemoryScope.Session : MemoryScope.Project), isPersonal, fact),
                            sourceProjectId: state.ProjectContext?.ProjectId,
                            userEditable: true);
                    }
                }
            }

            SyncLegacyCollectionsUnsafe(state);
            state.UpdatedAt = now;
        }
    }

    public void ApplyPreferences(ConversationState state, ConversationPreferenceDto dto, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dto);

        lock (state.SyncRoot)
        {
            if (dto.LongTermMemoryEnabled.HasValue)
            {
                state.LongTermMemoryEnabled = dto.LongTermMemoryEnabled.Value;
            }

            if (dto.PersonalMemoryConsentGranted.HasValue)
            {
                state.PersonalMemoryConsentGranted = dto.PersonalMemoryConsentGranted.Value;
                state.PersonalMemoryConsentAt = dto.PersonalMemoryConsentGranted.Value ? now : null;
            }

            if (dto.SessionMemoryTtlMinutes.HasValue)
            {
                state.SessionMemoryTtlMinutes = ClampSessionTtlMinutes(dto.SessionMemoryTtlMinutes.Value);
            }

            if (dto.TaskMemoryTtlHours.HasValue)
            {
                state.TaskMemoryTtlHours = ClampTaskTtlHours(dto.TaskMemoryTtlHours.Value);
            }

            if (dto.LongTermMemoryTtlDays.HasValue)
            {
                state.LongTermMemoryTtlDays = ClampLongTermTtlDays(dto.LongTermMemoryTtlDays.Value);
            }

            CleanupExpiredUnsafe(state, now);
            SyncLegacyCollectionsUnsafe(state);
            state.UpdatedAt = now;
        }
    }

    public ConversationMemoryPolicySnapshot GetPolicySnapshot(ConversationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (state.SyncRoot)
        {
            return new ConversationMemoryPolicySnapshot(
                state.LongTermMemoryEnabled,
                state.PersonalMemoryConsentGranted,
                state.PersonalMemoryConsentAt,
                ClampSessionTtlMinutes(state.SessionMemoryTtlMinutes),
                ClampTaskTtlHours(state.TaskMemoryTtlHours),
                ClampLongTermTtlDays(state.LongTermMemoryTtlDays));
        }
    }

    public IReadOnlyList<ConversationMemoryItem> GetActiveItems(ConversationState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (state.SyncRoot)
        {
            CleanupExpiredUnsafe(state, now);
            SyncLegacyCollectionsUnsafe(state);
            return state.MemoryItems
                .OrderByDescending(item => item.CreatedAt)
                .ThenBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool DeleteItem(ConversationState state, string memoryId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            var before = state.MemoryItems.Count;
            state.MemoryItems.RemoveAll(item => item.Id.Equals(memoryId, StringComparison.OrdinalIgnoreCase));
            var removed = before != state.MemoryItems.Count;
            if (removed)
            {
                CleanupExpiredUnsafe(state, now);
                SyncLegacyCollectionsUnsafe(state);
                state.UpdatedAt = now;
            }

            return removed;
        }
    }

    private static void AddOrRefreshUnsafe(
        ConversationState state,
        string type,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt,
        string? sourceTurnId,
        bool isPersonal,
        MemoryScope scope,
        string retention,
        string whyRemembered,
        int priority,
        string? sourceProjectId,
        bool userEditable)
    {
        var existingIndex = state.MemoryItems.FindIndex(item =>
            item.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
            item.Content.Equals(content, StringComparison.OrdinalIgnoreCase) &&
            item.Scope == scope &&
            string.Equals(item.SourceProjectId, sourceProjectId, StringComparison.OrdinalIgnoreCase));

        var normalizedType = type.Trim().ToLowerInvariant();
        var nextItem = new ConversationMemoryItem(
            existingIndex >= 0 ? state.MemoryItems[existingIndex].Id : Guid.NewGuid().ToString("N"),
            normalizedType,
            content,
            createdAt,
            expiresAt,
            sourceTurnId,
            isPersonal,
            scope,
            retention,
            whyRemembered,
            priority,
            sourceProjectId,
            userEditable);

        if (existingIndex >= 0)
        {
            state.MemoryItems[existingIndex] = nextItem;
            return;
        }

        state.MemoryItems.Add(nextItem);
        if (state.MemoryItems.Count > 300)
        {
            var removeCount = state.MemoryItems.Count - 300;
            state.MemoryItems.RemoveRange(0, removeCount);
        }
    }

    private static void CleanupExpiredUnsafe(ConversationState state, DateTimeOffset now)
    {
        state.MemoryItems.RemoveAll(item => item.ExpiresAt.HasValue && item.ExpiresAt.Value <= now);
    }

    private static void SyncLegacyCollectionsUnsafe(ConversationState state)
    {
        var longTermFacts = state.MemoryItems
            .Where(item => item.Type.Equals("long_term", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Content)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToList();

        state.Preferences.Clear();
        foreach (var fact in longTermFacts)
        {
            state.Preferences.Add(fact);
        }

        var tasks = state.MemoryItems
            .Where(item => item.Type.Equals("task", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => item.Content)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

        state.OpenTasks.Clear();
        state.OpenTasks.AddRange(tasks);
    }

    private static bool ContainsAnyToken(string content, IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (content.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPersonalFact(string fact)
    {
        if (ContainsAnyToken(fact, PersonalMarkers))
        {
            return true;
        }

        if (fact.Contains("@", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fact.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
               fact.Contains("телефон", StringComparison.OrdinalIgnoreCase) ||
               fact.Contains("address", StringComparison.OrdinalIgnoreCase) ||
               fact.Contains("адрес", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength) + "...";
    }

    private static int ClampSessionTtlMinutes(int value) => Math.Clamp(value, 15, 24 * 60);
    private static int ClampTaskTtlHours(int value) => Math.Clamp(value, 1, 24 * 60);
    private static int ClampLongTermTtlDays(int value) => Math.Clamp(value, 1, 3650);
}

