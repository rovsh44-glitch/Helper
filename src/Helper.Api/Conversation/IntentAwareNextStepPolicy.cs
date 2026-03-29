using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal enum ContextualNextStepRoute
{
    None,
    Research,
    Coding,
    Planning,
    Memory,
    Repair
}

internal enum EvidenceQuality
{
    None,
    Low,
    Medium,
    High
}

internal static class IntentAwareNextStepPolicy
{
    private static readonly Regex ListLineRegex = new(@"(^|\n)\s*(([-*])|(\d+\.))\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> GenericTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "tell me if you want a deeper breakdown or a concrete execution plan.",
        "tell me if you want a deeper breakdown or an executable plan from these sources.",
        "if you need stronger verification, ask for a cited research mode answer.",
        "provide required clarification.",
        "retry with narrower scope or provide stricter constraints.",
        "rephrase your request without policy-violating instructions.",
        "describe the target project scope and constraints; i will help iteratively instead of full project generation.",
        "research mode is disabled for this runtime profile. ask for a direct answer or enable research policy.",
        "please narrow scope, split the request, or ask for a concise answer.",
        "if you need project generation, explicitly ask to generate/create/build a project and provide concrete constraints.",
        "retry with a narrower prompt or ask for a reformatted answer.",
        "provide explicit scope/constraints to continue safely.",
        "retry the request or provide constraints to continue from this recovered turn.",
        "если нужна более строгая проверка, попросите ответ в исследовательском режиме с источниками.",
        "если хотите, я могу дальше развернуть выводы или собрать по этим источникам исполнимый план.",
        "сузьте область задачи, разбейте её на части или попросите более короткий ответ.",
        "опишите целевую область проекта и ограничения, и я помогу итеративно вместо полного запуска генерации.",
        "если нужна генерация проекта, попросите об этом явно и добавьте конкретные ограничения.",
        "исследовательский режим отключён для этого профиля выполнения. можно запросить прямой ответ или включить исследовательскую политику."
    };

    private static readonly HashSet<string> PlaceholderNextSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        "done",
        "ok",
        "okay",
        "готово",
        "сделано"
    };

    private static readonly HashSet<string> PlanningSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "plan",
        "roadmap",
        "checklist",
        "rollout",
        "steps",
        "step-by-step",
        "migration plan",
        "implementation plan",
        "план",
        "дорож",
        "чек-лист",
        "чеклист",
        "этап",
        "поэтап",
        "роллаут",
        "пошаг"
    };

    private static readonly HashSet<string> PinnedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "generation_disabled",
        "generation_admission_denied",
        "research_disabled",
        "turn_pipeline_recovered",
        "deterministic_memory_capture"
    };

    public static string? Resolve(ChatTurnContext context, string output, bool isRussian, IConversationVariationPolicy variationPolicy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(variationPolicy);

        if (TryGetPreservedNextStep(context, out var preserved))
        {
            return preserved;
        }

        var route = ResolveRoute(context, output);
        if (route is ContextualNextStepRoute.None or ContextualNextStepRoute.Memory)
        {
            return route == ContextualNextStepRoute.Memory
                ? NormalizeOptional(context.NextStep)
                : null;
        }

        var candidates = BuildCandidates(route, ResolveEvidenceQuality(context), context, isRussian, output);
        if (candidates.Count == 0)
        {
            return null;
        }

        return variationPolicy.Select(DialogAct.NextStep, VariationSlot.ContextualNextStep, context, candidates);
    }

    public static bool ShouldRender(ChatTurnContext context, string solution, string? nextStep)
    {
        ArgumentNullException.ThrowIfNull(context);

        var normalizedNextStep = NormalizeText(nextStep);
        if (string.IsNullOrWhiteSpace(normalizedNextStep) || PlaceholderNextSteps.Contains(normalizedNextStep))
        {
            return false;
        }

        var nextFingerprint = ConversationVariationPolicy.BuildFingerprint(nextStep);
        if (nextFingerprint.Length == 0)
        {
            return false;
        }

        var solutionFingerprint = ConversationVariationPolicy.BuildFingerprint(solution);
        if (string.Equals(nextFingerprint, solutionFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !NormalizeText(solution).Contains(normalizedNextStep, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGenericTemplate(string? nextStep)
    {
        var normalized = NormalizeText(nextStep);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (GenericTemplates.Contains(normalized))
        {
            return true;
        }

        return normalized.StartsWith("tell me if you want", StringComparison.Ordinal) ||
               normalized.StartsWith("if you need stronger verification", StringComparison.Ordinal) ||
               normalized.StartsWith("retry with narrower scope", StringComparison.Ordinal) ||
               normalized.StartsWith("provide required clarification", StringComparison.Ordinal) ||
               normalized.StartsWith("retry the request or provide constraints", StringComparison.Ordinal);
    }

    private static bool TryGetPreservedNextStep(ChatTurnContext context, out string? nextStep)
    {
        nextStep = NormalizeOptional(context.NextStep);
        if (string.IsNullOrWhiteSpace(nextStep))
        {
            return false;
        }

        if (HasPinnedSystemNextStep(context) || !IsGenericTemplate(nextStep))
        {
            return true;
        }

        nextStep = null;
        return false;
    }

    private static bool HasPinnedSystemNextStep(ChatTurnContext context)
    {
        if (context.RequiresClarification || context.RequiresConfirmation || context.BudgetExceeded)
        {
            return true;
        }

        if (string.Equals(context.GroundingStatus, "memory_captured", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return context.UncertaintyFlags.Any(flag =>
            PinnedFlags.Contains(flag) ||
            flag.StartsWith("stage_timeout:", StringComparison.OrdinalIgnoreCase));
    }

    private static ContextualNextStepRoute ResolveRoute(ChatTurnContext context, string output)
    {
        if (string.Equals(context.GroundingStatus, "memory_captured", StringComparison.OrdinalIgnoreCase) ||
            context.UncertaintyFlags.Contains("deterministic_memory_capture"))
        {
            return ContextualNextStepRoute.Memory;
        }

        if (!context.IsCritiqueApproved || !string.IsNullOrWhiteSpace(context.CritiqueFeedback))
        {
            return ContextualNextStepRoute.Repair;
        }

        if (context.Intent.Intent == IntentType.Generate ||
            context.ToolCalls.Any(tool => string.Equals(tool, "helper.generate", StringComparison.OrdinalIgnoreCase)) ||
            IsGenerationOutput(output))
        {
            return ContextualNextStepRoute.Coding;
        }

        if (context.Intent.Intent == IntentType.Research || context.Sources.Count > 0 || context.IsFactualPrompt)
        {
            return ContextualNextStepRoute.Research;
        }

        if (LooksLikePlanningTurn(context, output))
        {
            return ContextualNextStepRoute.Planning;
        }

        return ContextualNextStepRoute.None;
    }

    private static bool LooksLikePlanningTurn(ChatTurnContext context, string output)
    {
        var structure = context.Conversation.PreferredStructure;
        if (string.Equals(structure, "checklist", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(structure, "step_by_step", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var request = context.Request.Message ?? string.Empty;
        foreach (var signal in PlanningSignals)
        {
            if (request.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ListLineRegex.IsMatch(output);
    }

    private static EvidenceQuality ResolveEvidenceQuality(ChatTurnContext context)
    {
        if (context.UncertaintyFlags.Contains("factual_without_sources") ||
            context.UncertaintyFlags.Contains("uncertainty.search_hit_only_evidence") ||
            context.UncertaintyFlags.Contains("uncertainty.source_url_only_evidence") ||
            string.Equals(context.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.GroundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceQuality.Low;
        }

        if (context.Sources.Count == 0)
        {
            return EvidenceQuality.None;
        }

        if (context.VerifiedClaims > 0 || context.CitationCoverage >= 0.45 || context.Sources.Count >= 2)
        {
            return EvidenceQuality.High;
        }

        return context.CitationCoverage >= 0.15
            ? EvidenceQuality.Medium
            : EvidenceQuality.Low;
    }

    private static IReadOnlyList<string> BuildCandidates(ContextualNextStepRoute route, EvidenceQuality quality, ChatTurnContext context, bool isRussian, string output)
    {
        return route switch
        {
            ContextualNextStepRoute.Research => BuildResearchCandidates(isRussian, quality, context),
            ContextualNextStepRoute.Coding => BuildCodingCandidates(isRussian, output),
            ContextualNextStepRoute.Planning => BuildPlanningCandidates(isRussian),
            ContextualNextStepRoute.Repair => BuildRepairCandidates(isRussian),
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> BuildResearchCandidates(bool isRussian, EvidenceQuality quality, ChatTurnContext context)
    {
        if (quality == EvidenceQuality.High)
        {
            return isRussian
                ? new[]
                {
                    "Могу сопоставить эти источники между собой или строго проверить по ним один конкретный тезис.",
                    "Могу собрать по этим источникам decision-ready summary или отдельно перепроверить один спорный пункт.",
                    "Могу выделить самые сильные выводы из этих источников или глубже проверить один факт."
                }
                : new[]
                {
                    "I can compare these sources side by side or verify one specific claim against them.",
                    "I can turn this evidence into a decision-ready summary or pressure-test one point more strictly.",
                    "I can extract the strongest takeaways from these sources or check one claim in more detail."
                };
        }

        if (quality == EvidenceQuality.Medium)
        {
            return isRussian
                ? new[]
                {
                    "Могу усилить самую слабую часть доказательной базы или собрать из этого короткий практический список действий.",
                    "Могу перепроверить один тезис внимательнее или ужать это в более короткий brief по источникам.",
                    "Могу сфокусироваться на самом спорном пункте и подтянуть под него более надёжные источники."
                }
                : new[]
                {
                    "I can tighten the weakest part of the evidence or turn this into a short practical checklist.",
                    "I can check one claim more carefully or compress this into a shorter evidence-backed brief.",
                    "I can focus on the shakiest point first and strengthen it with better sources."
                };
        }

        if (context.IsFactualPrompt || context.UncertaintyFlags.Contains("factual_without_sources"))
        {
            return isRussian
                ? new[]
                {
                    "Назовите точный тезис для проверки, и я сфокусирую поиск источников именно на нём.",
                    "Могу сузить вопрос до одного факта и собрать под него более надёжные источники.",
                    "Если нужен grounded-ответ, укажите самый важный факт для верификации, и я начну с него."
                }
                : new[]
                {
                    "Name the exact claim to verify, and I will focus retrieval on that point.",
                    "I can narrow this to one factual question and gather stronger sources around it.",
                    "If you want a grounded answer, point me to the most important fact to verify first."
                };
        }

        return isRussian
            ? new[]
            {
                "Могу сузить тему до самого важного угла и уже под него собрать более сильные источники.",
                "Могу превратить это в рабочий план и отдельно отметить, где ещё нужна верификация.",
                "Могу продолжить с более узким исследовательским фокусом, чтобы опора на источники была сильнее."
            }
            : new[]
            {
                "I can narrow this to the most important angle and gather stronger sources around it.",
                "I can turn this into a working plan and call out where verification is still missing.",
                "I can continue with a tighter research focus so the grounding gets stronger."
            };
    }

    private static IReadOnlyList<string> BuildCodingCandidates(bool isRussian, string output)
    {
        var degraded = output.StartsWith("Failed to generate project.", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("Проект успешно", StringComparison.OrdinalIgnoreCase) == false &&
                       output.Contains("Project successfully generated", StringComparison.OrdinalIgnoreCase) == false &&
                       output.Contains("Diagnostics:", StringComparison.OrdinalIgnoreCase);

        if (degraded)
        {
            return isRussian
                ? new[]
                {
                    "Могу сузить объём генерации, изолировать сбойный шаг или восстановить только первый рабочий срез.",
                    "Могу ужать стек до минимального ядра, а потом вернуть остальное по частям.",
                    "Могу превратить этот сбой в короткий recovery-plan: первый фикс, первая пересборка, первая проверка."
                }
                : new[]
                {
                    "I can narrow the generation scope, isolate the failing step, or rebuild only the first working slice.",
                    "I can strip this down to the minimal core path and add the rest back in stages.",
                    "I can turn this failure into a short recovery plan: first fix, first rebuild, then first validation."
                };
        }

        return isRussian
            ? new[]
            {
                "Могу разложить это на первый implementation slice, нужные тесты и список файлов для правки.",
                "Могу пройтись по сгенерированной структуре, подключить первый feature или разобрать первую build-проблему.",
                "Могу превратить это в конкретное продолжение: какие файлы открыть, какие тесты прогнать и с какой команды начать."
            }
            : new[]
            {
                "I can turn this into the first implementation slice, required tests, and a file-by-file change list.",
                "I can inspect the generated structure, wire the first feature, or debug the first build issue.",
                "I can turn this into concrete follow-through: which files to open, which tests to run, and the first command to execute."
            };
    }

    private static IReadOnlyList<string> BuildPlanningCandidates(bool isRussian)
    {
        return isRussian
            ? new[]
            {
                "Могу превратить это в milestones, чек-лист или самый первый исполнимый шаг.",
                "Могу расставить приоритеты в плане, ужать его в короткий checklist или развернуть один этап подробнее.",
                "Могу разбить это на owner-by-owner задачи или на короткую последовательность rollout-шагов."
            }
            : new[]
            {
                "I can turn this into milestones, a checklist, or the very first concrete step to execute.",
                "I can prioritize the plan, compress it into a short checklist, or expand one stage in detail.",
                "I can split this into owner-by-owner tasks or a short rollout sequence."
            };
    }

    private static IReadOnlyList<string> BuildRepairCandidates(bool isRussian)
    {
        return isRussian
            ? new[]
            {
                "Если что-то ещё мимо, укажите конкретный фрагмент, и я перепишу только его, а не весь ответ.",
                "Могу переделать это в другом формате или точечно поправить ту часть, которая звучит не так.",
                "Если хотите, я подстрою ответ под точное misunderstanding, не переделывая всё с нуля."
            }
            : new[]
            {
                "If one part still misses the mark, point to the exact section and I will rewrite only that part.",
                "I can restate this in a different format or tighten only the piece that feels off.",
                "If you want, I can adjust the answer around the specific misunderstanding instead of redoing everything."
            };
    }

    private static bool IsGenerationOutput(string output)
    {
        return output.StartsWith("Failed to generate project.", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Project successfully generated at:", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Проект успешно сгенерирован по пути:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Trim().Trim('"', '\'', '`').Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

