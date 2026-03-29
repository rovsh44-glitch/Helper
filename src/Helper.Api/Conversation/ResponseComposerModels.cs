namespace Helper.Api.Conversation;

internal enum ResponseCompositionMode
{
    FreeformShort,
    StructuredAnswer,
    EvidenceBrief,
    OperatorSummary
}

internal sealed record ComposerLocalization(
    string SourcesHeader,
    string ResultHeader,
    string ArtifactsHeader,
    string FlagsHeader,
    string NoConcreteSolutionGenerated,
    string NoOperationalOutputProduced,
    string DefaultOperationalNextStep,
    string TurnLabel,
    string ModeLabel,
    string StatusLabel,
    string ConfidenceLabel,
    string OkStatus,
    string DegradedStatus,
    IReadOnlyList<string> OperatorSummaryHeaders,
    IReadOnlyList<string> NextStepHeaders)
{
    public string FormatExecutionSummary(ChatTurnContext context, string status)
    {
        return $"{TurnLabel}={context.TurnId}; {ModeLabel}={FormatMode(context.ExecutionMode)}; {StatusLabel}={status}; {ConfidenceLabel}={context.Confidence:0.00}";
    }

    private string FormatMode(TurnExecutionMode mode)
    {
        if (ReferenceEquals(this, Russian))
        {
            return mode switch
            {
                TurnExecutionMode.Fast => "быстрый",
                TurnExecutionMode.Balanced => "сбалансированный",
                TurnExecutionMode.Deep => "глубокий",
                _ => mode.ToString().ToLowerInvariant()
            };
        }

        return mode.ToString().ToLowerInvariant();
    }

    public static readonly ComposerLocalization English = new(
        SourcesHeader: "Sources:",
        ResultHeader: "Result:",
        ArtifactsHeader: "Artifacts:",
        FlagsHeader: "Flags:",
        NoConcreteSolutionGenerated: "No concrete solution was generated for this turn.",
        NoOperationalOutputProduced: "No operational output was produced.",
        DefaultOperationalNextStep: "Retry with narrower scope or provide stricter constraints.",
        TurnLabel: "turn",
        ModeLabel: "mode",
        StatusLabel: "status",
        ConfidenceLabel: "confidence",
        OkStatus: "ok",
        DegradedStatus: "degraded",
        OperatorSummaryHeaders: new[] { "Execution Summary:", "Run Summary:", "What Happened:" },
        NextStepHeaders: new[] { "Next step:", "Useful follow-up:", "If you want to continue:" });

    public static readonly ComposerLocalization Russian = new(
        SourcesHeader: "Источники:",
        ResultHeader: "Результат:",
        ArtifactsHeader: "Артефакты:",
        FlagsHeader: "Флаги:",
        NoConcreteSolutionGenerated: "Для этого хода не удалось сформировать конкретное решение.",
        NoOperationalOutputProduced: "Операционный результат для этого хода не был получен.",
        DefaultOperationalNextStep: "Сузьте область задачи или задайте более жёсткие ограничения.",
        TurnLabel: "ход",
        ModeLabel: "режим",
        StatusLabel: "статус",
        ConfidenceLabel: "уверенность",
        OkStatus: "норма",
        DegradedStatus: "ограничен",
        OperatorSummaryHeaders: new[] { "Сводка выполнения:", "Итог по ходу:", "Коротко по выполнению:" },
        NextStepHeaders: new[] { "Следующий шаг:", "Что можно сделать дальше:", "Если продолжим, следующий шаг:" });
}

