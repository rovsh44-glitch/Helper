using Helper.Api.Hosting;
using Helper.Api.Backend.Application;

namespace Helper.Api.Conversation;

public sealed class ConversationState
{
    public ConversationState(string id)
    {
        Id = id;
    }

    public string Id { get; }
    public object SyncRoot { get; } = new();
    public List<ChatMessageDto> Messages { get; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? RollingSummary { get; set; }
    public HashSet<string> Preferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> OpenTasks { get; } = new();
    public List<ConversationMemoryItem> MemoryItems { get; } = new();
    public bool LongTermMemoryEnabled { get; set; } = false;
    public bool PersonalMemoryConsentGranted { get; set; } = false;
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
    public string? PersonaBundleId { get; set; }
    public SharedUnderstandingState? SharedUnderstanding { get; set; }
    public UserUnderstandingState? UserUnderstanding { get; set; }
    public ProjectUnderstandingState? ProjectUnderstanding { get; set; }
    public ProjectContextState? ProjectContext { get; set; }
    public PersonalizationProfile? PersonalizationProfile { get; set; }
    public CommunicationQualityState? CommunicationQuality { get; set; }
    public bool BackgroundResearchEnabled { get; set; } = true;
    public bool ProactiveUpdatesEnabled { get; set; }
    public List<BackgroundConversationTask> BackgroundTasks { get; } = new();
    public List<ProactiveTopicSubscription> ProactiveTopics { get; } = new();
    public string? ActiveTurnId { get; set; }
    public string? ActiveTurnUserMessage { get; set; }
    public DateTimeOffset? ActiveTurnStartedAt { get; set; }
    public TurnExecutionCheckpoint? ActiveTurnCheckpoint { get; set; }
    public int ConsecutiveClarificationTurns { get; set; }
    public DateTimeOffset? LastClarificationAt { get; set; }
    public string ActiveBranchId { get; set; } = "main";
    public Dictionary<string, ConversationBranchSummary> BranchSummaries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, BranchDescriptor> Branches { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["main"] = new BranchDescriptor("main", null, null, DateTimeOffset.UtcNow)
    };
    internal Dictionary<string, SearchSessionState> SearchSessions { get; } = new(StringComparer.OrdinalIgnoreCase);
}

