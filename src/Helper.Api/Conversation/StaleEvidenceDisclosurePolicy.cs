namespace Helper.Api.Conversation;

public interface IStaleEvidenceDisclosurePolicy
{
    void Apply(
        ChatTurnContext context,
        CachedWebEvidenceSnapshot snapshot,
        WebEvidenceFreshnessAssessment assessment,
        WebEvidenceRefreshDecision decision,
        bool refreshAttempted,
        bool refreshFailed,
        string? refreshFailureReason = null);
}

public sealed class StaleEvidenceDisclosurePolicy : IStaleEvidenceDisclosurePolicy
{
    public void Apply(
        ChatTurnContext context,
        CachedWebEvidenceSnapshot snapshot,
        WebEvidenceFreshnessAssessment assessment,
        WebEvidenceRefreshDecision decision,
        bool refreshAttempted,
        bool refreshFailed,
        string? refreshFailureReason = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.RetrievalTrace.Add($"web_cache.category={assessment.Category}");
        context.RetrievalTrace.Add($"web_cache.state={assessment.State.ToString().ToLowerInvariant()}");
        context.RetrievalTrace.Add($"web_cache.age_minutes={Math.Max(0, (int)Math.Round(assessment.Age.TotalMinutes))}");
        context.RetrievalTrace.Add($"web_cache.decision={decision.Reason}");
        if (refreshAttempted)
        {
            context.RetrievalTrace.Add("web_cache.refresh_attempted=yes");
        }

        if (refreshFailed)
        {
            context.RetrievalTrace.Add("web_cache.refresh_failed=yes");
            if (!string.IsNullOrWhiteSpace(refreshFailureReason))
            {
                context.RetrievalTrace.Add($"web_cache.refresh_failure_reason={Sanitize(refreshFailureReason)}");
            }
        }

        context.IntentSignals.Add($"web_search:cache_state:{assessment.State.ToString().ToLowerInvariant()}");

        if (!decision.RequiresDisclosure && !refreshFailed)
        {
            return;
        }

        var disclosure = BuildDisclosure(context, assessment, refreshFailed);
        if (!string.IsNullOrWhiteSpace(disclosure) &&
            !context.ExecutionOutput.Contains(disclosure, StringComparison.Ordinal))
        {
            context.ExecutionOutput = string.IsNullOrWhiteSpace(context.ExecutionOutput)
                ? disclosure
                : $"{context.ExecutionOutput}\n\n{disclosure}";
        }

        context.UncertaintyFlags.Add(refreshFailed
            ? "web_cache_refresh_failed_fallback"
            : $"web_cache_{assessment.State.ToString().ToLowerInvariant()}_disclosed");
        context.Confidence = assessment.State switch
        {
            WebEvidenceFreshnessState.Aging => Math.Min(context.Confidence, 0.72),
            WebEvidenceFreshnessState.Stale => Math.Min(context.Confidence, 0.60),
            _ when refreshFailed => Math.Min(context.Confidence, 0.68),
            _ => context.Confidence
        };
    }

    private static string BuildDisclosure(ChatTurnContext context, WebEvidenceFreshnessAssessment assessment, bool refreshFailed)
    {
        var ageLabel = DescribeAge(assessment.Age);
        var isRussian = string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase);

        if (refreshFailed)
        {
            return isRussian
                ? $"Примечание по свежести: не удалось обновить веб-данные, поэтому я использую кэшированное исследование возрастом около {ageLabel}. Для чувствительных ко времени деталей нужна повторная онлайн-проверка."
                : $"Freshness note: live refresh failed, so I am using cached web research from about {ageLabel}. Time-sensitive details should be rechecked online.";
        }

        return assessment.State switch
        {
            WebEvidenceFreshnessState.Aging => isRussian
                ? $"Примечание по свежести: использую кэшированное веб-исследование возрастом около {ageLabel}. Оно может уже не отражать самые последние изменения."
                : $"Freshness note: using cached web research from about {ageLabel}. It may no longer reflect the very latest updates.",
            WebEvidenceFreshnessState.Stale => isRussian
                ? $"Примечание по свежести: использую устаревающее кэшированное веб-исследование возрастом около {ageLabel}. Для актуальных данных лучше выполнить повторный live fetch."
                : $"Freshness note: using aging cached web research from about {ageLabel}. For current information, a fresh live fetch is safer.",
            _ => string.Empty
        };
    }

    private static string DescribeAge(TimeSpan age)
    {
        if (age.TotalHours >= 1)
        {
            return $"{Math.Max(1, (int)Math.Round(age.TotalHours))}h";
        }

        return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))}m";
    }

    private static string Sanitize(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

