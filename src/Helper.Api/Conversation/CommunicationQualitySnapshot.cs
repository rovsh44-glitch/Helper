namespace Helper.Api.Conversation;

public sealed record CommunicationQualityState(
    int GenericClarificationPressure,
    int GenericNextStepPressure,
    int MixedLanguagePressure,
    int LowStyleFeedbackPressure,
    DateTimeOffset UpdatedAtUtc);

public sealed record CommunicationQualitySnapshot(
    int GenericClarificationPressure,
    int GenericNextStepPressure,
    int MixedLanguagePressure,
    int LowStyleFeedbackPressure)
{
    public static CommunicationQualitySnapshot Empty { get; } = new(0, 0, 0, 0);
}
