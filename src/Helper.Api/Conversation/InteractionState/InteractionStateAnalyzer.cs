namespace Helper.Api.Conversation.InteractionState;

public sealed class InteractionStateAnalyzer : IInteractionStateAnalyzer
{
    private static readonly string[] FrustrationTokens =
    {
        "again", "still", "wrong", "not what i meant", "not helpful", "why did", "опять", "снова", "не то", "не это",
        "не помогло", "не понял", "не туда", "ошибка", "раздраж", "frustrat"
    };

    private static readonly string[] UrgencyTokens =
    {
        "urgent", "asap", "right now", "today", "immediately", "fast", "срочно", "сегодня", "сейчас", "немедленно", "быстро"
    };

    private static readonly string[] ReassuranceTokens =
    {
        "worried", "anxious", "scared", "terrible", "overwhelmed", "stress", "боюсь", "пережива", "трев", "паник", "ужас", "страш"
    };

    private readonly ILatentInteractionSignalProvider? _latentSignalProvider;

    public InteractionStateAnalyzer(ILatentInteractionSignalProvider? latentSignalProvider = null)
    {
        _latentSignalProvider = latentSignalProvider;
    }

    public InteractionStateSnapshot Analyze(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var message = context.Request.Message ?? string.Empty;
        var signals = new List<string>();
        var frustration = DetectFrustration(message, context, signals);
        var urgency = DetectUrgency(message, context, signals);
        var overload = DetectOverload(message, context, signals);
        var reassurance = DetectReassuranceNeed(message, context, signals);
        var assistantPressure = DetectAssistantPressure(message, context, signals);
        var clarificationShift = frustration >= InteractionSignalLevel.Moderate || context.Conversation.ConsecutiveClarificationTurns >= 2
            ? -1
            : reassurance >= InteractionSignalLevel.Moderate
                ? 1
                : 0;

        if (_latentSignalProvider is not null)
        {
            foreach (var signal in _latentSignalProvider.GetSignals(context))
            {
                if (!string.IsNullOrWhiteSpace(signal))
                {
                    signals.Add($"latent:{signal.Trim()}");
                }
            }
        }

        return new InteractionStateSnapshot(
            frustration,
            urgency,
            overload,
            reassurance,
            clarificationShift,
            assistantPressure,
            signals);
    }

    private static InteractionSignalLevel DetectFrustration(string message, ChatTurnContext context, List<string> signals)
    {
        var score = 0;
        if (ContainsAny(message, FrustrationTokens))
        {
            score += 2;
            signals.Add("interaction.frustration:lexical");
        }

        if (context.Conversation.ConsecutiveClarificationTurns >= 2)
        {
            score += 1;
            signals.Add("interaction.frustration:clarification_loop");
        }

        if (!context.IsCritiqueApproved && !string.IsNullOrWhiteSpace(context.CritiqueFeedback))
        {
            score += 1;
            signals.Add("interaction.frustration:repair_feedback");
        }

        return ToLevel(score);
    }

    private static InteractionSignalLevel DetectUrgency(string message, ChatTurnContext context, List<string> signals)
    {
        var score = 0;
        if (ContainsAny(message, UrgencyTokens))
        {
            score += 2;
            signals.Add("interaction.urgency:lexical");
        }

        if (context.CollaborationIntent.SeeksDelegatedExecution && context.CollaborationIntent.HasHardConstraintLanguage)
        {
            score += 1;
            signals.Add("interaction.urgency:delegated_constraints");
        }

        return ToLevel(score);
    }

    private static InteractionSignalLevel DetectOverload(string message, ChatTurnContext context, List<string> signals)
    {
        var score = 0;
        var length = message.Length;
        if (length >= 320)
        {
            score += 2;
            signals.Add("interaction.overload:long_message");
        }
        else if (length >= 180)
        {
            score += 1;
            signals.Add("interaction.overload:medium_long_message");
        }

        if (CountSeparators(message) >= 5)
        {
            score += 1;
            signals.Add("interaction.overload:dense_structure");
        }

        if (context.CommunicationQualitySnapshot?.GenericClarificationPressure >= 2)
        {
            score += 1;
            signals.Add("interaction.overload:clarification_pressure");
        }

        return ToLevel(score);
    }

    private static InteractionSignalLevel DetectReassuranceNeed(string message, ChatTurnContext context, List<string> signals)
    {
        var score = 0;
        if (ContainsAny(message, ReassuranceTokens))
        {
            score += 2;
            signals.Add("interaction.reassurance:lexical");
        }

        if (context.CollaborationIntent.IsGuidanceSeeking && message.Contains('?', StringComparison.Ordinal))
        {
            score += 1;
            signals.Add("interaction.reassurance:guidance_question");
        }

        return ToLevel(score);
    }

    private static InteractionSignalLevel DetectAssistantPressure(string message, ChatTurnContext context, List<string> signals)
    {
        var score = 0;
        if (context.CollaborationIntent.SeeksDelegatedExecution && context.CollaborationIntent.HasHardConstraintLanguage)
        {
            score += 1;
            signals.Add("interaction.assistant_pressure:hard_constraints");
        }

        if (ContainsAny(message, UrgencyTokens))
        {
            score += 1;
            signals.Add("interaction.assistant_pressure:urgent_language");
        }

        if (context.Conversation.ConsecutiveClarificationTurns >= 2)
        {
            score += 1;
            signals.Add("interaction.assistant_pressure:clarification_history");
        }

        return ToLevel(score);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountSeparators(string text)
    {
        return text.Count(static ch => ch is ',' or ';' or ':' or '-' or '\n');
    }

    private static InteractionSignalLevel ToLevel(int score)
    {
        return score switch
        {
            >= 4 => InteractionSignalLevel.High,
            >= 2 => InteractionSignalLevel.Moderate,
            >= 1 => InteractionSignalLevel.Low,
            _ => InteractionSignalLevel.None
        };
    }
}
