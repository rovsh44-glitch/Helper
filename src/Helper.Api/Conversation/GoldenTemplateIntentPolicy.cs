using Helper.Runtime.Generation;

namespace Helper.Api.Conversation;

public static class GoldenTemplateIntentPolicy
{
    public static bool HasExplicitGoldenTemplateRequest(string? text)
        => GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(text);
}

