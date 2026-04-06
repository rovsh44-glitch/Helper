namespace Helper.Api.Conversation;

public sealed record CollaborationIntentAnalysis(
    bool IsGuidanceSeeking,
    bool TrustsBestJudgment,
    bool SeeksDelegatedExecution,
    bool PrefersAnswerOverClarification,
    bool HasHardConstraintLanguage,
    string PrimaryMode,
    IReadOnlyList<string> Signals)
{
    public static CollaborationIntentAnalysis None { get; } = new(
        IsGuidanceSeeking: false,
        TrustsBestJudgment: false,
        SeeksDelegatedExecution: false,
        PrefersAnswerOverClarification: false,
        HasHardConstraintLanguage: false,
        PrimaryMode: "none",
        Signals: Array.Empty<string>());
}

public interface ICollaborationIntentDetector
{
    CollaborationIntentAnalysis Analyze(string? message, string? resolvedLanguage);
}
