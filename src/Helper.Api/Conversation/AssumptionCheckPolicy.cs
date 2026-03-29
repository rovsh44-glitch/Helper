namespace Helper.Api.Conversation;

public sealed record AssumptionCheckDecision(bool RequiresClarification, string? ClarifyingQuestion, string? Flag);

public interface IAssumptionCheckPolicy
{
    AssumptionCheckDecision Evaluate(ChatTurnContext context);
}

public sealed class AssumptionCheckPolicy : IAssumptionCheckPolicy
{
    private static readonly string[] RiskyActionTokens =
    {
        "deploy",
        "publish",
        "release",
        "delete",
        "remove",
        "drop",
        "shutdown",
        "overwrite",
        "удали",
        "удалить",
        "деплой",
        "перезапиши"
    };

    private static readonly string[] ExplicitConstraintTokens =
    {
        "dry-run",
        "safe mode",
        "только",
        "only",
        "confirm",
        "подтверд",
        "without destructive",
        "без удаления"
    };

    public AssumptionCheckDecision Evaluate(ChatTurnContext context)
    {
        var message = context.Request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return new AssumptionCheckDecision(false, null, null);
        }

        if (context.Intent.Intent == Helper.Runtime.Core.IntentType.Research)
        {
            return new AssumptionCheckDecision(false, null, null);
        }

        var hasRiskyAction = RiskyActionTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (!hasRiskyAction)
        {
            return new AssumptionCheckDecision(false, null, null);
        }

        var hasExplicitConstraint = ExplicitConstraintTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (hasExplicitConstraint)
        {
            return new AssumptionCheckDecision(false, null, null);
        }

        return new AssumptionCheckDecision(
            true,
            string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                ? "Перед продолжением уточню: использовать безопасный режим без разрушительных изменений или вы подтверждаете полный объём действия?"
                : "Before I proceed: should I use safe mode (no destructive changes), or do you confirm full action scope?",
            "assumption_check_required");
    }
}

