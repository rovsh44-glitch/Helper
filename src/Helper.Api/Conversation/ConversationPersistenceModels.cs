using System.Text.Json;
using Helper.Api.Backend.Application;

namespace Helper.Api.Conversation;

internal sealed class PersistedConversationState
{
    public string Id { get; set; } = string.Empty;
    public List<Helper.Api.Hosting.ChatMessageDto> Messages { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
    public string? RollingSummary { get; set; }
    public List<string> Preferences { get; set; } = new();
    public List<string> OpenTasks { get; set; } = new();
    public List<ConversationMemoryItem> MemoryItems { get; set; } = new();
    public bool LongTermMemoryEnabled { get; set; }
    public bool PersonalMemoryConsentGranted { get; set; }
    public DateTimeOffset? PersonalMemoryConsentAt { get; set; }
    public int SessionMemoryTtlMinutes { get; set; } = 720;
    public int TaskMemoryTtlHours { get; set; } = 336;
    public int LongTermMemoryTtlDays { get; set; } = 180;
    public string PreferredLanguage { get; set; } = "auto";
    public string DetailLevel { get; set; } = "balanced";
    public string Formality { get; set; } = "neutral";
    public string DomainFamiliarity { get; set; } = "intermediate";
    public string PreferredStructure { get; set; } = "auto";
    public string Warmth { get; set; } = "balanced";
    public string Enthusiasm { get; set; } = "balanced";
    public string Directness { get; set; } = "balanced";
    public string DefaultAnswerShape { get; set; } = "auto";
    public string? SearchLocalityHint { get; set; }
    public string DecisionAssertiveness { get; set; } = "balanced";
    public string ClarificationTolerance { get; set; } = "balanced";
    public string CitationPreference { get; set; } = "adaptive";
    public string RepairStyle { get; set; } = "direct_fix";
    public string ReasoningStyle { get; set; } = "concise";
    public string ReasoningEffort { get; set; } = "balanced";
    public SharedUnderstandingState? SharedUnderstanding { get; set; }
    public UserUnderstandingState? UserUnderstanding { get; set; }
    public ProjectUnderstandingState? ProjectUnderstanding { get; set; }
    public ProjectContextState? ProjectContext { get; set; }
    public PersonalizationProfile? PersonalizationProfile { get; set; }
    public CommunicationQualityState? CommunicationQuality { get; set; }
    public bool BackgroundResearchEnabled { get; set; } = true;
    public bool ProactiveUpdatesEnabled { get; set; }
    public List<BackgroundConversationTask> BackgroundTasks { get; set; } = new();
    public List<ProactiveTopicSubscription> ProactiveTopics { get; set; } = new();
    public string? ActiveTurnId { get; set; }
    public string? ActiveTurnUserMessage { get; set; }
    public DateTimeOffset? ActiveTurnStartedAt { get; set; }
    public TurnExecutionCheckpoint? ActiveTurnCheckpoint { get; set; }
    public int ConsecutiveClarificationTurns { get; set; }
    public DateTimeOffset? LastClarificationAt { get; set; }
    public string ActiveBranchId { get; set; } = "main";
    public List<ConversationBranchSummary> BranchSummaries { get; set; } = new();
    public List<BranchDescriptor> Branches { get; set; } = new();
    public List<SearchSessionState> SearchSessions { get; set; } = new();
}

internal sealed class PersistedConversationEnvelope
{
    public int SchemaVersion { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public List<PersistedConversationState> Conversations { get; set; } = new();
}

internal sealed class PersistedConversationJournalEntry
{
    public int SchemaVersion { get; set; }
    public DateTimeOffset PersistedAtUtc { get; set; }
    public PersistedConversationState Conversation { get; set; } = new();
}

internal static class ConversationPersistenceModelMapper
{
    public static PersistedConversationState ToPersistenceModel(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            return new PersistedConversationState
            {
                Id = state.Id,
                Messages = state.Messages.ToList(),
                UpdatedAt = state.UpdatedAt,
                RollingSummary = state.RollingSummary,
                Preferences = state.Preferences.ToList(),
                OpenTasks = state.OpenTasks.ToList(),
                MemoryItems = state.MemoryItems.ToList(),
                LongTermMemoryEnabled = state.LongTermMemoryEnabled,
                PersonalMemoryConsentGranted = state.PersonalMemoryConsentGranted,
                PersonalMemoryConsentAt = state.PersonalMemoryConsentAt,
                SessionMemoryTtlMinutes = state.SessionMemoryTtlMinutes,
                TaskMemoryTtlHours = state.TaskMemoryTtlHours,
                LongTermMemoryTtlDays = state.LongTermMemoryTtlDays,
                PreferredLanguage = state.PreferredLanguage,
                DetailLevel = state.DetailLevel,
                Formality = state.Formality,
                DomainFamiliarity = state.DomainFamiliarity,
                PreferredStructure = state.PreferredStructure,
                Warmth = state.Warmth,
                Enthusiasm = state.Enthusiasm,
                Directness = state.Directness,
                DefaultAnswerShape = state.DefaultAnswerShape,
                SearchLocalityHint = state.SearchLocalityHint,
                DecisionAssertiveness = state.DecisionAssertiveness,
                ClarificationTolerance = state.ClarificationTolerance,
                CitationPreference = state.CitationPreference,
                RepairStyle = state.RepairStyle,
                ReasoningStyle = state.ReasoningStyle,
                ReasoningEffort = state.ReasoningEffort,
                SharedUnderstanding = state.SharedUnderstanding,
                UserUnderstanding = state.UserUnderstanding,
                ProjectUnderstanding = state.ProjectUnderstanding,
                ProjectContext = state.ProjectContext,
                PersonalizationProfile = state.PersonalizationProfile,
                CommunicationQuality = state.CommunicationQuality,
                BackgroundResearchEnabled = state.BackgroundResearchEnabled,
                ProactiveUpdatesEnabled = state.ProactiveUpdatesEnabled,
                BackgroundTasks = state.BackgroundTasks.ToList(),
                ProactiveTopics = state.ProactiveTopics.ToList(),
                ActiveTurnId = state.ActiveTurnId,
                ActiveTurnUserMessage = state.ActiveTurnUserMessage,
                ActiveTurnStartedAt = state.ActiveTurnStartedAt,
                ActiveTurnCheckpoint = state.ActiveTurnCheckpoint,
                ConsecutiveClarificationTurns = state.ConsecutiveClarificationTurns,
                LastClarificationAt = state.LastClarificationAt,
                ActiveBranchId = state.ActiveBranchId,
                BranchSummaries = state.BranchSummaries.Values.ToList(),
                Branches = state.Branches.Values.ToList(),
                SearchSessions = state.SearchSessions.Values.ToList()
            };
        }
    }

    public static ConversationState FromPersistenceModel(PersistedConversationState item)
    {
        var state = new ConversationState(item.Id)
        {
            UpdatedAt = item.UpdatedAt,
            RollingSummary = item.RollingSummary,
            LongTermMemoryEnabled = item.LongTermMemoryEnabled,
            PersonalMemoryConsentGranted = item.PersonalMemoryConsentGranted,
            PersonalMemoryConsentAt = item.PersonalMemoryConsentAt,
            SessionMemoryTtlMinutes = item.SessionMemoryTtlMinutes,
            TaskMemoryTtlHours = item.TaskMemoryTtlHours,
            LongTermMemoryTtlDays = item.LongTermMemoryTtlDays,
            PreferredLanguage = string.IsNullOrWhiteSpace(item.PreferredLanguage) ? "auto" : item.PreferredLanguage,
            DetailLevel = string.IsNullOrWhiteSpace(item.DetailLevel) ? "balanced" : item.DetailLevel,
            Formality = string.IsNullOrWhiteSpace(item.Formality) ? "neutral" : item.Formality,
            DomainFamiliarity = string.IsNullOrWhiteSpace(item.DomainFamiliarity) ? "intermediate" : item.DomainFamiliarity,
            PreferredStructure = string.IsNullOrWhiteSpace(item.PreferredStructure) ? "auto" : item.PreferredStructure,
            Warmth = string.IsNullOrWhiteSpace(item.Warmth) ? "balanced" : item.Warmth,
            Enthusiasm = string.IsNullOrWhiteSpace(item.Enthusiasm) ? "balanced" : item.Enthusiasm,
            Directness = string.IsNullOrWhiteSpace(item.Directness) ? "balanced" : item.Directness,
            DefaultAnswerShape = string.IsNullOrWhiteSpace(item.DefaultAnswerShape) ? "auto" : item.DefaultAnswerShape,
            SearchLocalityHint = string.IsNullOrWhiteSpace(item.SearchLocalityHint) ? null : item.SearchLocalityHint.Trim(),
            DecisionAssertiveness = string.IsNullOrWhiteSpace(item.DecisionAssertiveness) ? "balanced" : item.DecisionAssertiveness,
            ClarificationTolerance = string.IsNullOrWhiteSpace(item.ClarificationTolerance) ? "balanced" : item.ClarificationTolerance,
            CitationPreference = string.IsNullOrWhiteSpace(item.CitationPreference) ? "adaptive" : item.CitationPreference,
            RepairStyle = string.IsNullOrWhiteSpace(item.RepairStyle) ? "direct_fix" : item.RepairStyle,
            ReasoningStyle = string.IsNullOrWhiteSpace(item.ReasoningStyle) ? "concise" : item.ReasoningStyle,
            ReasoningEffort = string.IsNullOrWhiteSpace(item.ReasoningEffort) ? "balanced" : item.ReasoningEffort,
            SharedUnderstanding = item.SharedUnderstanding,
            UserUnderstanding = item.UserUnderstanding,
            ProjectUnderstanding = item.ProjectUnderstanding,
            ProjectContext = item.ProjectContext,
            PersonalizationProfile = item.PersonalizationProfile,
            CommunicationQuality = item.CommunicationQuality,
            BackgroundResearchEnabled = item.BackgroundResearchEnabled,
            ProactiveUpdatesEnabled = item.ProactiveUpdatesEnabled,
            ActiveTurnId = item.ActiveTurnId,
            ActiveTurnUserMessage = item.ActiveTurnUserMessage,
            ActiveTurnStartedAt = item.ActiveTurnStartedAt,
            ActiveTurnCheckpoint = item.ActiveTurnCheckpoint,
            ConsecutiveClarificationTurns = Math.Max(0, item.ConsecutiveClarificationTurns),
            LastClarificationAt = item.LastClarificationAt,
            ActiveBranchId = string.IsNullOrWhiteSpace(item.ActiveBranchId) ? "main" : item.ActiveBranchId
        };

        foreach (var message in item.Messages.OrderBy(x => x.Timestamp))
        {
            state.Messages.Add(message);
        }

        foreach (var preference in item.Preferences.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            state.Preferences.Add(preference);
        }

        foreach (var task in item.OpenTasks.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            state.OpenTasks.Add(task);
        }

        foreach (var memory in item.MemoryItems)
        {
            state.MemoryItems.Add(memory);
        }

        if (state.MemoryItems.Count == 0)
        {
            foreach (var preference in item.Preferences.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                state.MemoryItems.Add(new ConversationMemoryItem(
                    Guid.NewGuid().ToString("N"),
                    "long_term",
                    preference,
                    item.UpdatedAt,
                    item.UpdatedAt.AddDays(Math.Clamp(item.LongTermMemoryTtlDays, 1, 3650)),
                    null,
                    IsPersonal: false));
            }

            foreach (var task in item.OpenTasks.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                state.MemoryItems.Add(new ConversationMemoryItem(
                    Guid.NewGuid().ToString("N"),
                    "task",
                    task,
                    item.UpdatedAt,
                    item.UpdatedAt.AddHours(Math.Clamp(item.TaskMemoryTtlHours, 1, 24 * 60)),
                    null,
                    IsPersonal: false));
            }
        }

        state.BackgroundTasks.Clear();
        state.BackgroundTasks.AddRange(item.BackgroundTasks);
        state.ProactiveTopics.Clear();
        state.ProactiveTopics.AddRange(item.ProactiveTopics);

        state.Branches.Clear();
        if (item.Branches.Count == 0)
        {
            state.Branches["main"] = new BranchDescriptor("main", null, null, DateTimeOffset.UtcNow);
        }
        else
        {
            foreach (var branch in item.Branches)
            {
                if (string.IsNullOrWhiteSpace(branch.BranchId))
                {
                    continue;
                }

                state.Branches[branch.BranchId] = branch;
            }
        }

        if (!state.Branches.ContainsKey(state.ActiveBranchId))
        {
            state.Branches[state.ActiveBranchId] = new BranchDescriptor(state.ActiveBranchId, null, null, DateTimeOffset.UtcNow);
        }

        state.BranchSummaries.Clear();
        foreach (var summary in item.BranchSummaries.Where(x => !string.IsNullOrWhiteSpace(x.BranchId)))
        {
            state.BranchSummaries[summary.BranchId] = summary;
        }

        state.SearchSessions.Clear();
        foreach (var searchSession in item.SearchSessions.Where(x => !string.IsNullOrWhiteSpace(x.BranchId)))
        {
            state.SearchSessions[searchSession.BranchId] = searchSession;
        }

        return state;
    }

    public static List<PersistedConversationState> DeserializeSnapshot(string raw, int currentSchemaVersion, out bool migratedFromLegacy)
    {
        migratedFromLegacy = false;
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            migratedFromLegacy = true;
            return JsonSerializer.Deserialize<List<PersistedConversationState>>(raw) ?? new List<PersistedConversationState>();
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new List<PersistedConversationState>();
        }

        if (!TryGetPropertyCaseInsensitive(doc.RootElement, "schemaVersion", out var schemaNode))
        {
            migratedFromLegacy = true;
            if (TryGetPropertyCaseInsensitive(doc.RootElement, "conversations", out var legacyConversations))
            {
                return JsonSerializer.Deserialize<List<PersistedConversationState>>(legacyConversations.GetRawText())
                    ?? new List<PersistedConversationState>();
            }

            return new List<PersistedConversationState>();
        }

        var schemaVersion = schemaNode.GetInt32();
        if (schemaVersion == currentSchemaVersion)
        {
            var envelope = JsonSerializer.Deserialize<PersistedConversationEnvelope>(raw);
            return envelope?.Conversations ?? new List<PersistedConversationState>();
        }

        if (schemaVersion is 1 or 2 or 3 or 4 or 5)
        {
            migratedFromLegacy = true;
            if (TryGetPropertyCaseInsensitive(doc.RootElement, "conversations", out var conversationsNode))
            {
                return JsonSerializer.Deserialize<List<PersistedConversationState>>(conversationsNode.GetRawText())
                    ?? new List<PersistedConversationState>();
            }
        }

        return new List<PersistedConversationState>();
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

