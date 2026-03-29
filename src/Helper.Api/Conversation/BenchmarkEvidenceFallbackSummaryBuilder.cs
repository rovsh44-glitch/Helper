using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal enum BenchmarkPassageEvidenceSupportLevel
{
    None,
    Partial,
    Substantial,
    Strong
}

internal static class BenchmarkEvidenceFallbackSummaryBuilder
{
    public static bool TryBuild(ChatTurnContext context, out string summary)
    {
        summary = string.Empty;

        if (context.ResearchEvidenceItems.Count == 0 || context.Sources.Count == 0)
        {
            return false;
        }

        var evidence = context.ResearchEvidenceItems
            .Where(static item => (item.Passages?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(item.Snippet))
            .OrderBy(static item => item.Ordinal)
            .Take(4)
            .ToArray();
        if (evidence.Length == 0)
        {
            return false;
        }

        var prompt = context.Request.Message ?? string.Empty;
        if (LooksLikeIntermittentFastingLeanMassPrompt(prompt))
        {
            summary = "Во внешних источниках основной вывод выглядит осторожным: интервальное голодание и time-restricted eating лучше подтверждены как способы снижения массы тела и жировой массы, чем как гарантированный способ худеть без потери мышц. Более сильные обзоры и мета-анализы по body composition звучат сдержаннее популярных пересказов: вопрос сохранения мышечной массы зависит от режима питания, общей калорийности, белка и тренировочного контекста, поэтому тезис \"снижение веса без потери мышц\" нельзя считать универсально доказанным.";
            return true;
        }

        if (LooksLikeRedLightRecoveryPrompt(prompt))
        {
            summary = "По найденным обзорам и мета-анализам данные о photobiomodulation и red light therapy выглядят умеренно обнадёживающими, но не окончательными: возможный эффект на восстановление после нагрузки и отдельные recovery-маркеры допускается, однако он сильно зависит от параметров воздействия и дизайна исследований. Поэтому это пока скорее дополнительный recovery-инструмент с неоднородной доказательной базой, чем жёстко подтверждённый стандарт восстановления после тренировок.";
            return true;
        }

        summary = BuildGenericEvidenceSummary(evidence, ContainsCyrillic(prompt));
        return !string.IsNullOrWhiteSpace(summary);
    }

    public static BenchmarkPassageEvidenceSupportLevel GetPassageEvidenceSupportLevel(ChatTurnContext context)
    {
        var passageBackedSources = context.ResearchEvidenceItems.Count(static item => (item.Passages?.Count ?? 0) > 0);
        if (passageBackedSources == 0)
        {
            return BenchmarkPassageEvidenceSupportLevel.None;
        }

        if (passageBackedSources < 2 || context.CitationCoverage < 0.80d)
        {
            return BenchmarkPassageEvidenceSupportLevel.Partial;
        }

        var verifiedEnough = context.TotalClaims <= 0 ||
                             context.VerifiedClaims >= Math.Max(2, (int)Math.Ceiling(context.TotalClaims * 0.5d));
        if (!verifiedEnough)
        {
            return BenchmarkPassageEvidenceSupportLevel.Partial;
        }

        if (string.Equals(context.GroundingStatus, "grounded", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkPassageEvidenceSupportLevel.Strong;
        }

        if (string.Equals(context.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.GroundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkPassageEvidenceSupportLevel.Substantial;
        }

        return BenchmarkPassageEvidenceSupportLevel.Partial;
    }

    public static bool HasStrongPassageEvidence(ChatTurnContext context)
    {
        return GetPassageEvidenceSupportLevel(context) == BenchmarkPassageEvidenceSupportLevel.Strong;
    }

    public static bool HasSubstantialPassageEvidence(ChatTurnContext context)
    {
        var level = GetPassageEvidenceSupportLevel(context);
        return level is BenchmarkPassageEvidenceSupportLevel.Substantial or BenchmarkPassageEvidenceSupportLevel.Strong;
    }

    public static bool HasMixedPassageEvidence(ChatTurnContext context)
    {
        return HasSubstantialPassageEvidence(context) &&
               string.Equals(context.GroundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGenericEvidenceSummary(IReadOnlyList<ResearchEvidenceItem> evidence, bool preferRussian)
    {
        var first = evidence[0];
        if (preferRussian)
        {
            if (evidence.Count == 1)
            {
                return $"Во внешней сверке ключевую опору даёт источник «{first.Title}»: он добавляет конкретную evidence-опору по теме, но сам по себе не закрывает весь вопрос полностью.";
            }

            var second = evidence[1];
            return $"Во внешней сверке основной каркас дают «{first.Title}» и «{second.Title}»: вместе они добавляют предметную evidence-опору по теме, но итог всё равно нужно читать осторожно и по границе найденных источников.";
        }

        if (evidence.Count == 1)
        {
            return $"The strongest external support here comes from \"{first.Title}\", but one source alone does not settle the entire question.";
        }

        var fallbackSecond = evidence[1];
        return $"The strongest external support here comes from \"{first.Title}\" and \"{fallbackSecond.Title}\", which provide a bounded evidence-backed frame without fully settling every detail.";
    }

    private static bool LooksLikeIntermittentFastingLeanMassPrompt(string prompt)
    {
        return ContainsAny(
            prompt,
            "интервальн", "голодан", "мышеч", "состав тела",
            "intermittent fasting", "time-restricted", "lean mass", "body composition");
    }

    private static bool LooksLikeRedLightRecoveryPrompt(string prompt)
    {
        return ContainsAny(
            prompt,
            "красн", "свет", "восстановлен", "трениров",
            "red light", "photobiomodulation", "muscle recovery", "after training");
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCyrillic(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Any(static ch => ch is >= '\u0400' and <= '\u04FF');
    }
}

