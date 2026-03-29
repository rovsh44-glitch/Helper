using System.Text;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Conversation;

public sealed class ChatTurnFinalizer : IChatTurnFinalizer
{
    private readonly ICitationGroundingService _grounding;
    private readonly IResponseComposerService _responseComposer;
    private readonly IBackendRuntimePolicyProvider _policyProvider;
    private readonly ITurnLanguageResolver _turnLanguageResolver;
    private readonly IConversationVariationPolicy _variationPolicy;

    public ChatTurnFinalizer(
        ICitationGroundingService grounding,
        IResponseComposerService? responseComposer = null,
        IBackendRuntimePolicyProvider? policyProvider = null,
        ITurnLanguageResolver? turnLanguageResolver = null,
        IConversationVariationPolicy? variationPolicy = null)
    {
        _grounding = grounding;
        _responseComposer = responseComposer ?? ResponseComposerServiceFactory.CreateDefault();
        _policyProvider = policyProvider ?? new BackendOptionsCatalog(new Hosting.ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "dev-key"));
        _turnLanguageResolver = turnLanguageResolver ?? new TurnLanguageResolver();
        _variationPolicy = variationPolicy ?? new ConversationVariationPolicy();
    }

    public Task FinalizeAsync(ChatTurnContext context, CancellationToken ct)
    {
        var language = context.ResolvedTurnLanguage
            ?? _turnLanguageResolver.Resolve(context.Conversation.PreferredLanguage, context.Request.Message, context.History);
        context.ResolvedTurnLanguage = language;
        var isRussian = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase);
        var softBestEffortEntry = IsSoftBestEffortEntry(context);

        if (context.RequiresClarification)
        {
            context.Confidence = context.RequiresConfirmation ? 0.32 : 0.4;
            context.FinalResponse = context.ClarifyingQuestion ?? (isRussian
                ? "Пожалуйста, дайте чуть больше контекста, чтобы я помог точно."
                : "Please provide more details so I can help accurately.");
            context.NextStep = isRussian
                ? "Пришлите нужное уточнение, и я продолжу."
                : "Provide required clarification.";
            context.GroundingStatus = "clarification_required";
            context.CitationCoverage = 0;
            return Task.CompletedTask;
        }

        var output = context.IsCritiqueApproved
            ? context.ExecutionOutput
            : context.CorrectedContent ?? context.ExecutionOutput;

        if (context.ForceBestEffort)
        {
            context.Confidence = Math.Min(context.Confidence, 0.5);
            var reason = string.IsNullOrWhiteSpace(context.ForceBestEffortReason)
                ? (isRussian ? "Продолжаю на разумных допущениях." : "Proceeding with best-effort assumptions.")
                : context.ForceBestEffortReason;
            output = softBestEffortEntry
                ? BuildSoftBestEffortOutput(context, output, reason, isRussian)
                : isRussian
                    ? $"{output}\n\nРежим разумных допущений: {reason}"
                    : $"{output}\n\nBest-effort mode: {reason}";
        }

        if (context.BudgetExceeded)
        {
            context.Confidence = Math.Min(context.Confidence, 0.45);
            output = isRussian
                ? $"{output}\n\nПримечание: лимит бюджета этого хода был достигнут в режиме '{context.ExecutionMode}', поэтому ответ может быть неполным."
                : $"{output}\n\nNote: turn budget limit was reached in '{context.ExecutionMode}' mode, so the response may be incomplete.";
        }

        if (context.Sources.Count > 0)
        {
            context.Confidence = Math.Min(0.95, context.Confidence + 0.1);
        }
        else
        {
            context.Confidence = Math.Max(0.35, context.Confidence - 0.05);
        }

        if (_policyProvider.GetPolicies().GroundingEnabled)
        {
            var grounding = _grounding.Apply(
                output,
                context.Sources,
                context.IsFactualPrompt,
                context.Intent.Intent,
                context.ResolvedTurnLanguage,
                context.ResearchEvidenceItems,
                context.Request.Message);
            output = LocalizeGroundingOutput(grounding.Content, isRussian);
            context.GroundingStatus = grounding.Status;
            context.CitationCoverage = grounding.CitationCoverage;
            context.TotalClaims = grounding.Claims?.Count ?? 0;
            context.VerifiedClaims = grounding.Claims?.Count(x => x.SourceIndex.HasValue) ?? 0;
            if (string.Equals(grounding.Status, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase))
            {
                context.Confidence = Math.Min(context.Confidence, 0.45);
            }
            else if (string.Equals(grounding.Status, "grounded_with_limits", StringComparison.OrdinalIgnoreCase))
            {
                context.Confidence = Math.Min(context.Confidence, 0.58);
            }

            context.ClaimGroundings.Clear();
            if (grounding.Claims is { Count: > 0 })
            {
                context.ClaimGroundings.AddRange(grounding.Claims);
            }

            if (grounding.UncertaintyFlags.Count > 0)
            {
                context.UncertaintyFlags.AddRange(grounding.UncertaintyFlags);
            }
        }
        else
        {
            context.GroundingStatus = "disabled_by_policy";
            context.UncertaintyFlags.Add("grounding_disabled");
        }

        if (!context.Sources.Any())
        {
            if (context.IsFactualPrompt)
            {
                if (!ContainsUnverifiedSourceNote(output))
                {
                    output = isRussian
                        ? $"{output}\n\nНеопределённость: для этого фактического утверждения не удалось получить проверяемые источники."
                        : $"{output}\n\nUncertainty: no verifiable sources were retrieved for this factual claim.";
                }

                context.Confidence = Math.Min(context.Confidence, 0.52);
                context.UncertaintyFlags.Add("factual_without_sources");
            }

            if (context.History.Count >= 10)
            {
                output = isRussian
                    ? $"{output}\n\nСводка действия: продолжил текущую ветку разговора и сохранил недавний контекст."
                    : $"{output}\n\nAction summary: continued the existing thread while preserving recent context.";
            }
        }
        else if (context.History.Count >= 10)
        {
            output = isRussian
                ? $"{output}\n\nСводка действия: ответ собран с переносом контекста и явными источниками."
                : $"{output}\n\nAction summary: response generated with context carry-over and explicit sources.";
        }

        context.NextStep = IntentAwareNextStepPolicy.Resolve(context, output, isRussian, _variationPolicy);
        if (softBestEffortEntry)
        {
            context.NextStep = BuildSoftBestEffortNextStep(context, context.NextStep, isRussian);
        }

        context.FinalResponse = _responseComposer.Compose(context, output);
        return Task.CompletedTask;
    }

    private static bool ContainsUnverifiedSourceNote(string output)
    {
        return output.Contains("Uncertainty: no verifiable source anchors were found for factual claims.", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Неопределённость: для фактических утверждений не удалось найти проверяемые опорные источники.", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Uncertainty: no verifiable sources were retrieved for this factual claim.", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Неопределённость: для этого фактического утверждения не удалось получить проверяемые источники.", StringComparison.OrdinalIgnoreCase);
    }

    private static string LocalizeGroundingOutput(string output, bool isRussian)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        if (isRussian)
        {
            return output
                .Replace(
                    "Uncertainty: no verifiable source anchors were found for factual claims.",
                    "Неопределённость: для фактических утверждений не удалось найти проверяемые опорные источники.",
                    StringComparison.Ordinal)
                .Replace(
                    "Uncertainty: potential contradictions were detected between claims and retrieved sources.",
                    "Неопределённость: между утверждениями и найденными источниками обнаружены возможные противоречия.",
                    StringComparison.Ordinal)
                .Replace(
                    "Uncertainty: grounding here is limited to search-result snippets rather than fully extracted page evidence.",
                    "Неопределённость: опора здесь ограничена сниппетами поисковой выдачи, а не полноценными извлечёнными фрагментами страницы.",
                    StringComparison.Ordinal)
                .Replace(
                    "Uncertainty: grounding here is limited to source links rather than fully extracted page evidence.",
                    "Неопределённость: опора здесь ограничена ссылками на источники, а не полноценными извлечёнными фрагментами.",
                    StringComparison.Ordinal);
        }

        return output
            .Replace(
                "Неопределённость: для фактических утверждений не удалось найти проверяемые опорные источники.",
                "Uncertainty: no verifiable source anchors were found for factual claims.",
                StringComparison.Ordinal)
            .Replace(
                "Неопределённость: между утверждениями и найденными источниками обнаружены возможные противоречия.",
                "Uncertainty: potential contradictions were detected between claims and retrieved sources.",
                StringComparison.Ordinal)
            .Replace(
                "Неопределённость: опора здесь ограничена сниппетами поисковой выдачи, а не полноценными извлечёнными фрагментами страницы.",
                "Uncertainty: grounding here is limited to search-result snippets rather than fully extracted page evidence.",
                StringComparison.Ordinal)
            .Replace(
                "Неопределённость: опора здесь ограничена ссылками на источники, а не полноценными извлечёнными фрагментами.",
                "Uncertainty: grounding here is limited to source links rather than fully extracted page evidence.",
                StringComparison.Ordinal);
    }

    private static bool IsSoftBestEffortEntry(ChatTurnContext context)
    {
        return context.ForceBestEffort &&
               !context.RequiresClarification &&
               !context.RequiresConfirmation &&
               !string.Equals(context.AmbiguityType, nameof(AmbiguityType.SafetyConfirmation), StringComparison.OrdinalIgnoreCase) &&
               (context.UncertaintyFlags.Contains("soft_best_effort_entry") || !string.IsNullOrWhiteSpace(context.ClarifyingQuestion));
    }

    private static string BuildSoftBestEffortOutput(ChatTurnContext context, string output, string reason, bool isRussian)
    {
        var builder = new StringBuilder();
        builder.AppendLine(isRussian
            ? "Дам полезный старт по самой вероятной трактовке, чтобы не стопорить разговор."
            : "I will start with the most useful interpretation so we can keep moving.");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(output)
            ? (isRussian ? "Начну с короткого практического варианта." : "I will begin with a short practical version.")
            : output.Trim());
        builder.AppendLine();
        builder.AppendLine(isRussian ? "Предположения:" : "Assumptions:");
        foreach (var assumption in BuildSoftBestEffortAssumptions(context, reason, isRussian))
        {
            builder.Append("- ");
            builder.AppendLine(assumption);
        }

        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> BuildSoftBestEffortAssumptions(ChatTurnContext context, string reason, bool isRussian)
    {
        var assumptions = new List<string> { reason };

        switch (context.AmbiguityType)
        {
            case nameof(AmbiguityType.Goal):
                assumptions.Add(isRussian
                    ? "Считаю, что сейчас важнее дать быстрый полезный старт, а не полный разбор всех вариантов."
                    : "I assume the priority is a fast useful start rather than a full analysis of every possible direction.");
                assumptions.Add(isRussian
                    ? "Формат беру краткий и практический, чтобы его было легко скорректировать следующим сообщением."
                    : "I am using a concise practical format so it is easy to refine in the next message.");
                break;
            case nameof(AmbiguityType.Format):
                assumptions.Add(isRussian
                    ? "Пока беру формат, который проще всего читать и править по ходу."
                    : "For now I am using the format that is easiest to scan and refine.");
                break;
            case nameof(AmbiguityType.Constraints):
                assumptions.Add(isRussian
                    ? "Иду от безопасных стандартных ограничений и не добавляю рискованных допущений."
                    : "I am proceeding from safe standard limits and avoiding risky assumptions.");
                break;
            case nameof(AmbiguityType.Data):
                assumptions.Add(isRussian
                    ? "Там, где не хватает входных данных, использую общий безопасный пример вместо точного исполнения."
                    : "Where input data is missing, I am using a safe general example instead of pretending to execute precisely.");
                break;
            case nameof(AmbiguityType.Scope):
                assumptions.Add(isRussian
                    ? "Начинаю с минимального полезного объёма, который можно расширить без переделки всего ответа."
                    : "I am starting with the smallest useful scope that can be expanded without redoing the whole answer.");
                break;
            default:
                assumptions.Add(isRussian
                    ? "Если направление нужно сместить, это лучше сделать одним коротким уточнением после первого полезного шага."
                    : "If the direction needs to shift, it is easiest to do that with one short refinement after the first useful step.");
                break;
        }

        return assumptions;
    }

    private static string BuildSoftBestEffortNextStep(ChatTurnContext context, string? fallbackNextStep, bool isRussian)
    {
        var refinement = string.IsNullOrWhiteSpace(context.ClarifyingQuestion)
            ? (isRussian
                ? "Если хотите, одним сообщением уточните приоритет, формат или ограничения, и я сразу перестрою ответ."
                : "If you want, reply with the priority, format, or constraints in one message and I will reshape the answer immediately.")
            : isRussian
                ? $"Если захотите скорректировать направление, просто ответьте на это уточнение: {context.ClarifyingQuestion}"
                : $"If you want to steer this more precisely, just answer this quick question: {context.ClarifyingQuestion}";

        if (string.IsNullOrWhiteSpace(fallbackNextStep))
        {
            return refinement;
        }

        return $"{fallbackNextStep}\n{refinement}";
    }
}

