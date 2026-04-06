namespace Helper.Api.Conversation;

public enum MisunderstandingRepairKind
{
    None,
    Scope,
    Tone,
    Content
}

public interface IMisunderstandingRepairPolicy
{
    MisunderstandingRepairKind Classify(ChatTurnContext context);
    string? BuildRepairNextStep(ChatTurnContext context, bool isRussian);
}

public sealed class MisunderstandingRepairPolicy : IMisunderstandingRepairPolicy
{
    public MisunderstandingRepairKind Classify(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = $"{context.Request.Message}\n{context.CritiqueFeedback}".Trim();
        if (text.Contains("tone", StringComparison.OrdinalIgnoreCase) || text.Contains("тон", StringComparison.OrdinalIgnoreCase))
        {
            return MisunderstandingRepairKind.Tone;
        }

        if (text.Contains("scope", StringComparison.OrdinalIgnoreCase) || text.Contains("объем", StringComparison.OrdinalIgnoreCase) || text.Contains("scope", StringComparison.OrdinalIgnoreCase))
        {
            return MisunderstandingRepairKind.Scope;
        }

        if (!context.IsCritiqueApproved)
        {
            return MisunderstandingRepairKind.Content;
        }

        return MisunderstandingRepairKind.None;
    }

    public string? BuildRepairNextStep(ChatTurnContext context, bool isRussian)
    {
        var narrowRepairScope = context.InteractionPolicy?.NarrowRepairScope == true;
        var reassurance = context.InteractionPolicy?.IncreaseReassurance == true;

        return Classify(context) switch
        {
            MisunderstandingRepairKind.Scope => isRussian
                ? narrowRepairScope
                    ? "Одним сообщением укажите только тот scope, который нужно взять первым, и я перестрою ответ без лишних изменений."
                    : "Одним сообщением укажите, какой точный scope взять вместо текущего."
                : narrowRepairScope
                    ? "Reply with only the scope that should come first, and I will rebuild the answer without changing extra parts."
                    : "Reply with the exact scope you want instead of the current one.",
            MisunderstandingRepairKind.Tone => isRussian
                ? reassurance
                    ? "Одним сообщением укажите желаемый тон: нейтральный, формальный или более прямой. Я скорректирую только подачу, не ломая смысл."
                    : "Одним сообщением укажите желаемый тон: нейтральный, формальный или более прямой."
                : reassurance
                    ? "Reply with the tone you want: neutral, formal, or more direct. I will adjust the delivery without reworking the whole meaning."
                    : "Reply with the tone you want: neutral, formal, or more direct.",
            MisunderstandingRepairKind.Content => isRussian
                ? narrowRepairScope
                    ? "Одним сообщением укажите, где именно смысл ушёл не туда, и я исправлю только этот один участок."
                    : "Одним сообщением укажите, где именно смысл ушёл не туда, и я исправлю только этот участок."
                : narrowRepairScope
                    ? "Reply with the exact place where the meaning drifted, and I will correct only that one part."
                    : "Reply with the exact place where the meaning drifted, and I will correct only that part.",
            _ => null
        };
    }
}
