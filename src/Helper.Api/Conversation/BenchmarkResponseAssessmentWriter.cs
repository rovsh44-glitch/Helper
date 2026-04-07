using System.Text;
using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IBenchmarkResponseAssessmentWriter
{
    string BuildAnalysis(ChatTurnContext context, string solution, bool isFallback, string? topicalBody);
    string BuildConclusion(ChatTurnContext context, bool isFallback, string? topicalBody);
    string BuildOpinion(ChatTurnContext context, bool isFallback, string? topicalBody);
}

internal sealed class BenchmarkResponseAssessmentWriter : IBenchmarkResponseAssessmentWriter
{
    private readonly IBenchmarkDraftQualityPolicy _qualityPolicy;
    private readonly IBenchmarkResponseStructurePolicy _structurePolicy;

    public BenchmarkResponseAssessmentWriter(
        IBenchmarkDraftQualityPolicy qualityPolicy,
        IBenchmarkResponseStructurePolicy structurePolicy)
    {
        _qualityPolicy = qualityPolicy;
        _structurePolicy = structurePolicy;
    }

    public string BuildAnalysis(ChatTurnContext context, string solution, bool isFallback, string? topicalBody)
    {
        var answerModeLead = BuildAnswerModeLead(context);
        var evidenceLevel = BenchmarkEvidenceFallbackSummaryBuilder.GetPassageEvidenceSupportLevel(context);
        if (isFallback && !string.IsNullOrWhiteSpace(topicalBody))
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(answerModeLead))
            {
                builder.Append(answerModeLead);
                builder.Append(' ');
            }
            builder.Append("Базовый локальный каркас по теме выглядит так: ");
            builder.Append(topicalBody.Trim());
            if (context.Sources.Count > 0)
            {
                if (evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Strong)
                {
                    builder.Append(" При этом внешняя сверка действительно состоялась: найдены релевантные источники и passage-level evidence, но черновой ответ пришлось пересобрать вручную, потому что исходная формулировка была слабой.");
                }
                else if (evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Substantial)
                {
                    builder.Append(BenchmarkEvidenceFallbackSummaryBuilder.HasMixedPassageEvidence(context)
                        ? " При этом внешняя сверка действительно состоялась: найдены релевантные источники и passage-level evidence, но часть evidence между источниками остаётся неоднородной, поэтому итог пришлось формулировать как осторожный synthesis, а не как жёсткое утверждение."
                        : " При этом внешняя сверка действительно состоялась: релевантные источники и passage-level evidence уже есть, но их объём всё ещё ограничен, поэтому итог лучше читать как осторожный synthesis, а не как окончательно закрытый вывод.");
                }
                else
                {
                    builder.Append(" При этом внешняя сверка сработала лишь частично: источники были найдены, но устойчивого page/document-level извлечения не хватило, поэтому свежие, спорные или точные фактические детали нельзя считать полностью подтверждёнными.");
                }
            }
            else
            {
                builder.Append(" При этом внешняя проверка не состоялась, поэтому свежие, спорные или точные фактические детали нельзя считать подтверждёнными.");
            }

            if (context.RequireExplicitBenchmarkUncertainty ||
                string.Equals(context.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(context.GroundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase) ||
                context.UncertaintyFlags.Contains("uncertainty.search_hit_only_evidence"))
            {
                builder.Append(" Уровень неопределённости здесь высокий; доказательная база разреженная, а ограничения evidence остаются существенными.");
            }

            if (context.BudgetExceeded)
            {
                builder.Append(" Дополнительно ход был ограничен бюджетом, поэтому ответ мог оборваться раньше нормальной доработки.");
            }

            return builder.ToString();
        }

        if (!isFallback)
        {
            var normalized = _structurePolicy.StripFollowUpTail(solution);
            normalized = Regex.Replace(normalized, @"(?im)^\s*Research request:\s*", string.Empty).Trim();
            if (_qualityPolicy.LooksLowQualityBenchmarkDraft(context, normalized))
            {
                var builder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(answerModeLead))
                {
                    builder.Append(answerModeLead);
                    builder.Append(' ');
                }
                builder.Append("Черновой ответ был сгенерирован, но содержит заметный смешанный языковой шум и поэтому не годится как финальная формулировка без нормализации.");
                if (context.Sources.Count == 0)
                {
                    builder.Append(" Это особенно заметно в local-first режиме без внешней сверки, где ошибка формулировки не была исправлена источниками.");
                }

                if (context.BudgetExceeded)
                {
                    builder.Append(" Дополнительно ход был ограничен бюджетом, поэтому качество изложения могло деградировать раньше времени.");
                }

                return builder.ToString();
            }

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(answerModeLead))
        {
            parts.Add(answerModeLead);
        }
        if (_qualityPolicy.LooksLowQualityBenchmarkDraft(context, solution))
        {
            parts.Add("Черновой ответ содержит заметный смешанный языковой шум или placeholder-паттерны, поэтому его нельзя выдавать как готовый ответ без нормализации.");
        }

        parts.Add("Поскольку ни локальная опора, ни web-подтверждение не оказались достаточно сильными, гладкий ответ по существу здесь был бы спекулятивным.");
        if (context.RequireExplicitBenchmarkUncertainty ||
            string.Equals(context.GroundingStatus, "unverified", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.GroundingStatus, "degraded", StringComparison.OrdinalIgnoreCase) ||
            context.UncertaintyFlags.Count > 0)
        {
            parts.Add("Уровень неопределённости здесь высокий, а доказательная база ограничена.");
        }

        parts.Add(context.IsFactualPrompt
            ? "Для фактического вопроса это особенно критично: без проверяемых источников нельзя честно утверждать, что вывод подтверждён."
            : "Для аналитического запроса это означает, что можно честно зафиксировать предел текущего evidence, но нельзя выдавать это за полноценный разбор.");

        if (context.BudgetExceeded)
        {
            parts.Add("Дополнительно ход был ограничен бюджетом, поэтому неполнота ответа является ожидаемой, а не скрытой.");
        }

        return string.Join(" ", parts);
    }

    public string BuildConclusion(ChatTurnContext context, bool isFallback, string? topicalBody)
    {
        var answerModeLead = BuildAnswerModeLead(context);
        var evidenceLevel = BenchmarkEvidenceFallbackSummaryBuilder.GetPassageEvidenceSupportLevel(context);
        if (!isFallback)
        {
            if (context.Sources.Count > 0)
            {
                var result = "Итог: ответ нужно читать как сочетание локального базового понимания и внешней сверки; ссылки выше показывают, что веб-проверка действительно участвовала в результате.";
                return string.IsNullOrWhiteSpace(answerModeLead) ? result : $"{answerModeLead} {result}";
            }

            var fallback = context.IsFactualPrompt
                ? "Итог: это локальный explanatory answer без внешней проверки свежести, поэтому для текущих или спорных фактов понадобилась бы отдельная веб-сверка."
                : "Итог: это local-first объяснение без live web-проверки. Для базового понимания этого достаточно, но для спорных или быстро меняющихся тем нужна внешняя сверка.";
            return string.IsNullOrWhiteSpace(answerModeLead) ? fallback : $"{answerModeLead} {fallback}";
        }

        if (!string.IsNullOrWhiteSpace(topicalBody))
        {
            if (context.Sources.Count > 0)
            {
                var baseConclusion = evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Strong
                    ? context.IsFactualPrompt
                        ? "Сейчас корректный вывод такой: локальный каркас уже усилен реальной внешней evidence-опорой, поэтому предметный вывод можно формулировать осторожно, но не как полный отказ от ответа."
                        : "Сейчас корректный вывод такой: есть полезный локальный каркас и реальная внешняя evidence-опора, но финальный текст всё равно нужно читать как осторожный synthesis, а не как окончательно исчерпывающий разбор."
                    : evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Substantial
                        ? BenchmarkEvidenceFallbackSummaryBuilder.HasMixedPassageEvidence(context)
                            ? context.IsFactualPrompt
                                ? "Сейчас корректный вывод такой: внешняя evidence-опора уже есть и passages действительно извлечены, но данные между источниками не полностью однородны, поэтому вывод нужно формулировать осторожно и без сильных обещаний."
                                : "Сейчас корректный вывод такой: passages действительно найдены, но evidence между источниками неоднороден, поэтому итог нужно читать как осторожный synthesis, а не как окончательно закрытый разбор."
                            : context.IsFactualPrompt
                                ? "Сейчас корректный вывод такой: внешняя evidence-опора уже есть и passages действительно извлечены, но доказательная база пока ограничена по объёму, поэтому вывод должен оставаться осторожным."
                                : "Сейчас корректный вывод такой: passages действительно найдены, но доказательная база пока ограничена по объёму, поэтому итог нужно читать как осторожный synthesis, а не как исчерпывающий разбор."
                    : context.IsFactualPrompt
                        ? "Сейчас корректный вывод такой: локально удерживается полезный базовый ориентир, а внешние источники добавляют частичную опору, но без устойчивого page/document extraction итог всё ещё нельзя считать полностью подтверждённым."
                        : "Сейчас корректный вывод такой: есть полезный локальный каркас и частичная внешняя сверка, но без устойчивого извлечения содержимого этот разбор остаётся ограниченным и не должен подаваться как окончательно подтверждённый.";

                if (context.RequireExplicitBenchmarkUncertainty ||
                    string.Equals(context.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(context.GroundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{baseConclusion} Данные пока выглядят умеренно убедительными, но не окончательными.";
                }

                return baseConclusion;
            }

            return context.IsFactualPrompt
                ? "Сейчас корректный вывод такой: локально можно удержать только базовый ориентир по теме, но подтверждённого source-backed вывода в этом ходе нет; уровень неопределённости остаётся высоким."
                : "Сейчас корректный вывод такой: базовый локальный каркас полезен как старт, но без внешней сверки этот разбор нельзя считать подтверждённым, а уровень неопределённости остаётся высоким.";
        }

        return context.IsFactualPrompt
            ? "Сейчас корректный вывод такой: вопрос остаётся неподтверждённым в текущем runtime, уровень неопределённости высокий, и требуется повторная проверка по рабочему web/document path или по предоставленному вами тексту источника."
            : "Сейчас корректный вывод такой: вместо притворного разбора лучше честно остановиться на пределе данных; уровень неопределённости высокий, и анализ стоит повторить после восстановления источникового пути.";
    }

    public string BuildOpinion(ChatTurnContext context, bool isFallback, string? topicalBody)
    {
        var answerModeLead = BuildAnswerModeLead(context);
        var evidenceLevel = BenchmarkEvidenceFallbackSummaryBuilder.GetPassageEvidenceSupportLevel(context);
        if (!isFallback)
        {
            var opinion = context.Sources.Count > 0
                ? "Моё мнение: такой формат полезен, потому что видно, где локальная база была дополнена реальными источниками, а не подменена ими."
                : "Моё мнение: для учебного или базового объяснения local-first ответ допустим, но финальную уверенность я бы давал только после внешней проверки там, где цена ошибки выше.";
            return string.IsNullOrWhiteSpace(answerModeLead) ? opinion : $"{answerModeLead} {opinion}";
        }

        if (!string.IsNullOrWhiteSpace(topicalBody))
        {
            if (context.Sources.Count > 0)
            {
                var baseOpinion = evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Strong
                    ? context.IsFactualPrompt
                        ? "Моё мнение: если сильные источники и passages уже найдены, система должна давать осторожный предметный вывод, а не откатываться в общий отказной boilerplate."
                        : "Моё мнение: при реальной passage-level опоре лучше пересобрать ответ из evidence, чем повторять generic fallback про слабый runtime."
                    : evidenceLevel == BenchmarkPassageEvidenceSupportLevel.Substantial
                        ? BenchmarkEvidenceFallbackSummaryBuilder.HasMixedPassageEvidence(context)
                            ? context.IsFactualPrompt
                                ? "Моё мнение: когда passages уже есть, система должна честно говорить о mixed evidence и давать осторожный synthesis вместо boilerplate про якобы провалившуюся extraction."
                                : "Моё мнение: при реальной passage-level опоре лучше честно отметить mixed evidence, чем сводить всё к общему fallback про слабый runtime."
                            : context.IsFactualPrompt
                                ? "Моё мнение: если passages уже есть, система должна опираться на них и формулировать осторожный предметный вывод, а не делать вид, будто extraction вообще не состоялась."
                                : "Моё мнение: passage-level evidence уже достаточно, чтобы дать осторожный synthesis вместо общего fallback про слабую extraction."
                    : context.IsFactualPrompt
                        ? "Моё мнение: в таком режиме лучше честно использовать частичную внешнюю опору как ориентир, но не маскировать её под полностью подтверждённый factual вывод."
                        : "Моё мнение: частичная внешняя сверка всё равно лучше, чем чисто локальный boilerplate, если прямо сказано, что extraction остался ограниченным.";

                if (context.RequireExplicitBenchmarkUncertainty ||
                    string.Equals(context.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(context.GroundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{baseOpinion} Для sparse evidence case это важно особенно: текущие данные дают только осторожный, а не сильный вывод.";
                }

                return baseOpinion;
            }

            return context.IsFactualPrompt
                ? "Моё мнение: в таком режиме лучше сохранить полезный локальный ориентир, но явно отделить его от подтверждённого factual вывода."
                : "Моё мнение: даже неполный локальный каркас лучше, чем пустой boilerplate, если честно помечены пределы проверки.";
        }

        return context.IsFactualPrompt
            ? "Моё мнение: в таком состоянии системы лучше дать осторожный отказ от уверенного фактического вывода, чем написать убедительно звучащий, но непроверенный ответ."
            : "Моё мнение: для research-режима честный структурированный fallback лучше, чем гладкий boilerplate, который делает вид, будто анализ уже состоялся.";
    }

    private static string? BuildAnswerModeLead(ChatTurnContext context)
    {
        return context.EpistemicAnswerMode switch
        {
            Epistemic.EpistemicAnswerMode.BestEffortHypothesis => "Эпистемический режим: ответ сформулирован как best-effort hypothesis, а не как окончательно установленный факт.",
            Epistemic.EpistemicAnswerMode.NeedsVerification => "Эпистемический режим: ответ требует дополнительной проверки перед сильным утверждением.",
            Epistemic.EpistemicAnswerMode.Abstain => "Эпистемический режим: система сознательно воздерживается от сильного фактического вывода.",
            _ => null
        };
    }
}

