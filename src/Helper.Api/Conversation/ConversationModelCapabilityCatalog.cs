namespace Helper.Api.Conversation;

public sealed record ConversationModelCapability(
    string ModelName,
    string Role,
    int DialogueQuality,
    int ReasoningStrength,
    int MultilingualQuality,
    int ToolUseQuality,
    bool SupportsVision,
    string LatencyProfile);

public interface IConversationModelCapabilityCatalog
{
    IReadOnlyList<ConversationModelCapability> GetCatalog();
    string ResolveBestModel(string role, IReadOnlyList<string> availableModels);
}

public sealed class ConversationModelCapabilityCatalog : IConversationModelCapabilityCatalog
{
    private static readonly ConversationModelCapability[] Defaults =
    {
        new("qwen3:30b", "primary_dialogue", 95, 92, 90, 80, false, "medium"),
        new("qwen3-vl:8b", "vision", 72, 74, 78, 62, true, "medium"),
        new("qwen2.5vl:7b", "vision", 68, 70, 72, 60, true, "medium"),
        new("qwen2.5-coder:14b", "coder", 65, 78, 70, 92, false, "medium"),
        new("command-r7b:7b", "fast", 70, 62, 72, 60, false, "low"),
        new("deepseek-r1:14b", "reasoning", 72, 88, 70, 58, false, "medium"),
        new("deepseek-r1:8b", "fast_reasoning", 60, 75, 62, 52, false, "low")
    };

    public IReadOnlyList<ConversationModelCapability> GetCatalog() => Defaults;

    public string ResolveBestModel(string role, IReadOnlyList<string> availableModels)
    {
        var available = availableModels ?? Array.Empty<string>();
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "primary_dialogue" : role.Trim().ToLowerInvariant();

        var preferred = Defaults
            .Where(capability => capability.Role.Equals(normalizedRole, StringComparison.OrdinalIgnoreCase))
            .Concat(Defaults.Where(capability => normalizedRole == "reasoning" && capability.Role == "primary_dialogue"))
            .Select(capability => capability.ModelName)
            .FirstOrDefault(model => available.Contains(model, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return normalizedRole switch
        {
            "vision" => available.FirstOrDefault(model => model.Contains("vl", StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault() ?? "qwen3:30b",
            "coder" => available.FirstOrDefault(model => model.Contains("coder", StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault() ?? "qwen3:30b",
            "fast" => available.FirstOrDefault(model => model.Contains("7b", StringComparison.OrdinalIgnoreCase) || model.Contains("8b", StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault() ?? "qwen3:30b",
            _ => available.FirstOrDefault(model => model.Equals("qwen3:30b", StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault()
                ?? "qwen3:30b"
        };
    }
}
