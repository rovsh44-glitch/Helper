using System.Text;
using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IBenchmarkResponseSectionRenderer
{
    string BuildResponse(ChatTurnContext context, string solution, bool isFallback, string? topicalBody);
}

internal sealed class BenchmarkResponseSectionRenderer : IBenchmarkResponseSectionRenderer
{
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[\.\!\?])\s+", RegexOptions.Compiled);

    private readonly IBenchmarkDraftQualityPolicy _qualityPolicy;
    private readonly IBenchmarkResponseAssessmentWriter _assessmentWriter;

    public BenchmarkResponseSectionRenderer(
        IBenchmarkDraftQualityPolicy qualityPolicy,
        IBenchmarkResponseAssessmentWriter assessmentWriter)
    {
        _qualityPolicy = qualityPolicy;
        _assessmentWriter = assessmentWriter;
    }

    public string BuildResponse(ChatTurnContext context, string solution, bool isFallback, string? topicalBody)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Local Findings");
        builder.AppendLine(BuildLocalFindings(context, solution, isFallback, topicalBody));
        builder.AppendLine();
        builder.AppendLine("## Web Findings");
        builder.AppendLine(BuildWebFindings(context, isFallback));
        builder.AppendLine();
        builder.AppendLine("## Sources");
        AppendSources(builder, context, isFallback);
        builder.AppendLine();
        builder.AppendLine("## Analysis");
        builder.AppendLine(_assessmentWriter.BuildAnalysis(context, solution, isFallback, topicalBody));
        builder.AppendLine();
        builder.AppendLine("## Conclusion");
        builder.AppendLine(_assessmentWriter.BuildConclusion(context, isFallback, topicalBody));
        builder.AppendLine();
        builder.AppendLine("## Opinion");
        builder.AppendLine(_assessmentWriter.BuildOpinion(context, isFallback, topicalBody));
        return builder.ToString().TrimEnd();
    }

    private string BuildLocalFindings(ChatTurnContext context, string solution, bool isFallback, string? topicalBody)
    {
        var hasWebSources = ConversationSourceClassifier.HasWebSource(context);
        var hasLocalSources = ConversationSourceClassifier.HasLocalSource(context);
        if (isFallback)
        {
            if (!string.IsNullOrWhiteSpace(topicalBody))
            {
                return $"Удалось удержать только базовый локальный каркас по теме: {BuildSectionPreview(topicalBody)}";
            }

            if (ShouldDescribeWebAsUnused(context))
            {
                return "Локальный draft был сгенерирован, но его нельзя принять как надёжный ответ: в нём появились неподтверждённые формулировки или выдуманные source-like элементы.";
            }

            return context.IsFactualPrompt
                ? "В этом ходе у меня не появилось достаточно надёжной локальной опоры, чтобы выдавать проверенный фактический ответ без внешней проверки."
            : "Локальная база здесь дала только общий контур вопроса, но не дала достаточно надёжной опоры для уверенного ответа по существу.";
        }

        if (hasWebSources)
        {
            return "Сначала ответ опирался на локальный контекст и базовые знания системы, а затем был дополнен внешней сверкой.";
        }

        if (hasLocalSources)
        {
            return "Локальная библиотека дала дополнительный контекст, но он не заменяет live web-проверку для свежих или регуляторных фактов.";
        }

        if (_qualityPolicy.LooksLowQualityBenchmarkDraft(context, solution))
        {
            return "Локальный draft был сгенерирован, но по качеству формулировок он требует нормализации перед тем, как считать его финальным ответом.";
        }

        return context.IsFactualPrompt
            ? "Этот ход опирался на local-first режим: базовый ответ сформирован из локального контекста без обязательной live web-проверки."
            : "Этот ход остался в local-first режиме: локального контекста оказалось достаточно для базового объяснения без обязательного обращения к live web.";
    }

    private static string BuildWebFindings(ChatTurnContext context, bool isFallback)
    {
        var evidenceLevel = BenchmarkEvidenceFallbackSummaryBuilder.GetPassageEvidenceSupportLevel(context);
        var hasWebSources = ConversationSourceClassifier.HasWebSource(context);
        if (hasWebSources)
        {
            if (isFallback && evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Strong)
            {
                return "Веб-маршрут был задействован; найдено несколько релевантных источников, и часть из них была извлечена на уровне page/passage evidence. Внешняя сверка здесь реальна, но финальный ответ пришлось пересобрать из-за слабого чернового текста.";
            }

            if (isFallback && evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Substantial)
            {
                return BenchmarkEvidenceFallbackSummaryBuilder.HasMixedPassageEvidence(context)
                    ? "Веб-маршрут был задействован; найдено несколько релевантных источников, и часть из них подтверждена page/passage evidence. Внешняя сверка состоялась, но evidence между источниками остаётся неоднородным, поэтому итог сформулирован осторожно."
                    : "Веб-маршрут был задействован; найдены релевантные источники и passage-level evidence. Внешняя сверка состоялась, но evidence пока ограничен по объёму, поэтому итог сформулирован осторожно.";
            }

            return isFallback
                ? "Веб-маршрут был задействован; источники были найдены, но page/document extraction оказалось недостаточно устойчивым, поэтому внешняя сверка здесь сработала частично, а не как полноценно подтверждённая."
                : "Live web-маршрут был использован для уточнения ответа; перечень реально использованных источников приведён ниже.";
        }

        if (ConversationSourceClassifier.HasLiveWebAttempt(context))
        {
            return "Live web-маршрут был задействован, но проверяемые live web-источники не были получены или не прошли evidence-quality фильтр; локальные библиотечные источники не считаются заменой свежей внешней проверки.";
        }

        if (ShouldDescribeWebAsUnused(context))
        {
            return "Live web-поиск в этом ходе не использовался, поэтому внешняя сверка и проверка актуальности поверх локального ответа не проводились.";
        }

        return isFallback
            ? "Проверяемые live web-источники в этом ходе не были извлечены, поэтому внешняя проверка фактов и обновление локальной базы не состоялись."
            : "Live web-поиск в этом ходе не использовался, поэтому актуальность и внешняя сверка не добавлялись сверх локального ответа.";
    }

    private static void AppendSources(StringBuilder builder, ChatTurnContext context, bool isFallback)
    {
        var webSources = ConversationSourceClassifier.GetWebSources(context);
        var localSources = ConversationSourceClassifier.GetLocalSources(context);
        if (webSources.Count == 0 && localSources.Count == 0)
        {
            builder.AppendLine(ConversationSourceClassifier.HasLiveWebAttempt(context)
                ? "- Проверяемые live web-источники в этом ходе не были получены."
                : ShouldDescribeWebAsUnused(context)
                ? "- Live web-источники в этом ходе не использовались."
                : isFallback
                    ? "- В этом ходе проверяемые источники не были получены."
                    : "- Live web-источники в этом ходе не использовались.");
            return;
        }

        var remaining = 8;
        if (webSources.Count > 0)
        {
            builder.AppendLine("- Web sources:");
            foreach (var source in webSources.Take(remaining))
            {
                builder.Append("  - web: ");
                builder.AppendLine(source);
            }

            remaining = Math.Max(0, remaining - webSources.Count);
        }

        if (localSources.Count > 0)
        {
            builder.AppendLine("- Local library sources:");
            foreach (var source in localSources.Take(Math.Max(1, remaining)))
            {
                builder.Append("  - ");
                builder.Append(webSources.Count > 0 ? "local: " : "local-only: ");
                builder.AppendLine(source);
            }
        }
    }

    private static bool ShouldDescribeWebAsUnused(ChatTurnContext context)
    {
        return context.Sources.Count == 0 &&
               string.Equals(context.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase) &&
               !context.ToolCalls.Any(static call => string.Equals(call, "research.search", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSectionPreview(string content)
    {
        var normalized = Regex.Replace(content, @"\s+", " ").Trim();
        var sentences = SentenceSplitRegex
            .Split(normalized)
            .Select(static part => part.Trim())
            .Where(static part => part.Length > 0)
            .Take(2)
            .ToArray();
        return sentences.Length == 0 ? normalized : string.Join(" ", sentences);
    }
}

