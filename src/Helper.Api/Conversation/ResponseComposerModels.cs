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
    string BestEffortLead,
    string BestEffortLabel,
    string NeedsVerificationNotice,
    string AbstentionLead,
    string AbstentionNextMove,
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
        BestEffortLead: "Best-effort hypothesis:",
        BestEffortLabel: "Best-effort mode",
        NeedsVerificationNotice: "Verification status: this answer remains provisional and should be checked against stronger evidence before being treated as settled fact.",
        AbstentionLead: "I cannot responsibly assert this as established fact yet.",
        AbstentionNextMove: "Provide stronger evidence, let me run a deeper verification path, or narrow the claim that needs confirmation.",
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
        BestEffortLead: "Гипотеза с разумными допущениями:",
        BestEffortLabel: "Режим разумных допущений",
        NeedsVerificationNotice: "Статус проверки: ответ пока предварительный и требует дополнительной сверки, прежде чем считать его установленным фактом.",
        AbstentionLead: "Сейчас я не могу ответственно утверждать это как установленный факт.",
        AbstentionNextMove: "Нужны более сильные источники, более глубокая проверка или сужение утверждения, которое нужно подтвердить.",
        OperatorSummaryHeaders: new[] { "Сводка выполнения:", "Итог по ходу:", "Коротко по выполнению:" },
        NextStepHeaders: new[] { "Следующий шаг:", "Что можно сделать дальше:", "Если продолжим, следующий шаг:" });
}

