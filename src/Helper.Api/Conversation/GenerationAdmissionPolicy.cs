namespace Helper.Api.Conversation;

public static class GenerationAdmissionPolicy
{
    public static readonly string[] ResearchLexemes = Helper.Runtime.Core.ResearchIntentPolicy.AllResearchLexemes;
    public static readonly string[] GenerateLexemes = Helper.Runtime.Core.ResearchIntentPolicy.GenerateLexemes;

    public static bool IsGenerateAdmitted(string? text, double confidence, double minConfidence)
    {
        return HasExplicitGenerateLexeme(text) && confidence >= minConfidence;
    }

    public static bool HasExplicitGenerateLexeme(string? text)
    {
        return Helper.Runtime.Core.ResearchIntentPolicy.HasExplicitGenerateRequest(text);
    }
}

