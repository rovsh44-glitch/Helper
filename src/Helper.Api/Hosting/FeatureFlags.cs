namespace Helper.Api.Hosting;

public interface IFeatureFlags
{
    bool AttachmentsEnabled { get; }
    bool RegenerateEnabled { get; }
    bool BranchingEnabled { get; }
    bool BranchMergeEnabled { get; }
    bool ConversationRepairEnabled { get; }
    bool EnhancedGroundingEnabled { get; }
    bool IntentV2Enabled { get; }
    bool GroundingV2Enabled { get; }
    bool StreamingV2Enabled { get; }
    bool AuthV2Enabled { get; }
    bool MemoryV2Enabled { get; }
}

public sealed class FeatureFlags : IFeatureFlags
{
    public bool AttachmentsEnabled { get; } = ReadFlag("HELPER_FF_ATTACHMENTS", true);
    public bool RegenerateEnabled { get; } = ReadFlag("HELPER_FF_REGENERATE", true);
    public bool BranchingEnabled { get; } = ReadFlag("HELPER_FF_BRANCHING", true);
    public bool BranchMergeEnabled { get; } = ReadFlag("HELPER_FF_BRANCH_MERGE", true);
    public bool ConversationRepairEnabled { get; } = ReadFlag("HELPER_FF_CONVERSATION_REPAIR", true);
    public bool EnhancedGroundingEnabled { get; } = ReadFlag("HELPER_FF_ENHANCED_GROUNDING", true);
    public bool IntentV2Enabled { get; } = ReadFlag("HELPER_FF_INTENT_V2", true);
    public bool GroundingV2Enabled { get; } = ReadFlag("HELPER_FF_GROUNDING_V2", true);
    public bool StreamingV2Enabled { get; } = ReadFlag("HELPER_FF_STREAMING_V2", true);
    public bool AuthV2Enabled { get; } = ReadFlag("HELPER_FF_AUTH_V2", true);
    public bool MemoryV2Enabled { get; } = ReadFlag("HELPER_FF_MEMORY_V2", true);

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }
}

