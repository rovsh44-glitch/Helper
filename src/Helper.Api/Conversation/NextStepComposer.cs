namespace Helper.Api.Conversation;

internal interface INextStepComposer
{
    string SelectNextStepHeader(ChatTurnContext context, ComposerLocalization localization);
    string SelectNextStepBridge(ChatTurnContext context);
    string? ResolveEffectiveNextStep(ChatTurnContext context, string solution, ComposerLocalization localization, ResponseCompositionMode mode);
}

internal sealed class NextStepComposer : INextStepComposer
{
    private readonly IConversationVariationPolicy _variationPolicy;

    public NextStepComposer(IConversationVariationPolicy variationPolicy)
    {
        _variationPolicy = variationPolicy;
    }

    public string SelectNextStepHeader(ChatTurnContext context, ComposerLocalization localization)
    {
        return _variationPolicy.Select(
            DialogAct.NextStep,
            VariationSlot.NextStepHeader,
            context,
            localization.NextStepHeaders);
    }

    public string SelectNextStepBridge(ChatTurnContext context)
    {
        var isRussian = string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        var directness = NormalizeDirectness(context.Conversation.Directness);
        var warmth = NormalizeWarmth(context.Conversation.Warmth);
        return _variationPolicy.Select(
            DialogAct.NextStep,
            VariationSlot.NextStepBridge,
            context,
            isRussian
                ? ResolveRussianNextStepBridges(directness, warmth)
                : ResolveEnglishNextStepBridges(directness, warmth));
    }

    public string? ResolveEffectiveNextStep(
        ChatTurnContext context,
        string solution,
        ComposerLocalization localization,
        ResponseCompositionMode mode)
    {
        var isRussian = ReferenceEquals(localization, ComposerLocalization.Russian);
        var effective = NormalizeOptional(context.NextStep)
            ?? IntentAwareNextStepPolicy.Resolve(context, solution, isRussian, _variationPolicy)
            ?? (mode == ResponseCompositionMode.OperatorSummary
                ? localization.DefaultOperationalNextStep
                : null);

        return IntentAwareNextStepPolicy.ShouldRender(context, solution, effective)
            ? effective
            : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static IReadOnlyList<string> ResolveEnglishNextStepBridges(string directness, string warmth)
    {
        if (directness == "direct")
        {
            return new[]
            {
                "Next:",
                "The next useful move is:",
                "To move this forward:"
            };
        }

        if (warmth == "warm")
        {
            return new[]
            {
                "If helpful, I can next:",
                "If you want, I can keep going with:",
                "A useful next step would be:"
            };
        }

        return new[]
        {
            "If you want, I can next:",
            "If we continue, the next useful step is:",
            "To keep this moving, I can:"
        };
    }

    private static IReadOnlyList<string> ResolveRussianNextStepBridges(string directness, string warmth)
    {
        if (directness == "direct")
        {
            return new[]
            {
                "Дальше:",
                "Следующий полезный шаг:",
                "Чтобы двинуться дальше:"
            };
        }

        if (warmth == "warm")
        {
            return new[]
            {
                "Если будет полезно, дальше могу:",
                "Если хотите, могу продолжить так:",
                "Хороший следующий шаг здесь:"
            };
        }

        return new[]
        {
            "Если хотите, дальше могу:",
            "Если продолжим, следующим шагом могу:",
            "Чтобы продвинуться дальше, могу:"
        };
    }

    private static string NormalizeWarmth(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "balanced"
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeDirectness(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "balanced"
            : value.Trim().ToLowerInvariant();
    }
}

