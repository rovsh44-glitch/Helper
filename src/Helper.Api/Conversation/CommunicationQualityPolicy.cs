namespace Helper.Api.Conversation;

public interface ICommunicationQualityPolicy
{
    CommunicationQualitySnapshot GetSnapshot(ConversationState state);
    void RecordCompletedTurn(ConversationState state, ChatTurnContext context, ConversationStyleTelemetry styleTelemetry, DateTimeOffset now);
    string? FilterNextStep(ChatTurnContext context, string? nextStep, bool isRussian);
}

public sealed class CommunicationQualityPolicy : ICommunicationQualityPolicy
{
    public CommunicationQualitySnapshot GetSnapshot(ConversationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (state.SyncRoot)
        {
            var current = state.CommunicationQuality;
            return current is null
                ? CommunicationQualitySnapshot.Empty
                : new CommunicationQualitySnapshot(
                    current.GenericClarificationPressure,
                    current.GenericNextStepPressure,
                    current.MixedLanguagePressure,
                    current.LowStyleFeedbackPressure);
        }
    }

    public void RecordCompletedTurn(ConversationState state, ChatTurnContext context, ConversationStyleTelemetry styleTelemetry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(styleTelemetry);

        lock (state.SyncRoot)
        {
            var current = state.CommunicationQuality ?? new CommunicationQualityState(0, 0, 0, 0, now);
            state.CommunicationQuality = new CommunicationQualityState(
                GenericClarificationPressure: styleTelemetry.GenericClarificationDetected
                    ? Math.Min(current.GenericClarificationPressure + 1, 3)
                    : Math.Max(current.GenericClarificationPressure - 1, 0),
                GenericNextStepPressure: styleTelemetry.GenericNextStepDetected
                    ? Math.Min(current.GenericNextStepPressure + 1, 3)
                    : Math.Max(current.GenericNextStepPressure - 1, 0),
                MixedLanguagePressure: styleTelemetry.MixedLanguageDetected
                    ? Math.Min(current.MixedLanguagePressure + 1, 3)
                    : Math.Max(current.MixedLanguagePressure - 1, 0),
                LowStyleFeedbackPressure: current.LowStyleFeedbackPressure,
                UpdatedAtUtc: now);
        }
    }

    public string? FilterNextStep(ChatTurnContext context, string? nextStep, bool isRussian)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(nextStep))
        {
            return null;
        }

        var snapshot = context.CommunicationQualitySnapshot;
        if (snapshot is null)
        {
            return nextStep;
        }

        if (snapshot.GenericNextStepPressure >= 2 && IntentAwareNextStepPolicy.IsGenericTemplate(nextStep))
        {
            return null;
        }

        if (context.InteractionPolicy?.SuppressGenericNextStep == true && IntentAwareNextStepPolicy.IsGenericTemplate(nextStep))
        {
            return null;
        }

        if (snapshot.MixedLanguagePressure >= 2 && context.CollaborationIntent.PrefersAnswerOverClarification)
        {
            return isRussian
                ? "Если нужно, следующей репликой задайте одно точное ограничение или приоритет."
                : "If needed, reply with one precise constraint or priority.";
        }

        if (context.InteractionPolicy?.CompressStructure == true)
        {
            return isRussian
                ? "Если нужно, ответом укажите только один главный приоритет или ограничение."
                : "If needed, reply with only the single most important priority or constraint.";
        }

        return nextStep;
    }
}
