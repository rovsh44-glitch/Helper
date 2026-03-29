using System.Diagnostics;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed class ChatTurnPlanner : IChatTurnPlanner
{
    private readonly IIntentClassifier _intentClassifier;
    private readonly IAmbiguityDetector _ambiguityDetector;
    private readonly IClarificationPolicy _clarificationPolicy;
    private readonly IIntentTelemetryService _intentTelemetry;
    private readonly IUserProfileService _userProfileService;
    private readonly ITurnLanguageResolver _turnLanguageResolver;
    private readonly ILiveWebRequirementPolicy _liveWebRequirementPolicy;
    private readonly ILocalFirstBenchmarkPolicy _localFirstBenchmarkPolicy;
    private readonly ILatencyBudgetPolicy _latencyBudgetPolicy;
    private readonly IAssumptionCheckPolicy _assumptionCheckPolicy;
    private readonly bool _intentV2Enabled;
    private readonly IConversationStageMetricsService? _stageMetrics;

    public ChatTurnPlanner(
        IIntentClassifier intentClassifier,
        IAmbiguityDetector ambiguityDetector,
        IClarificationPolicy clarificationPolicy,
        IIntentTelemetryService intentTelemetry,
        ILatencyBudgetPolicy? latencyBudgetPolicy = null,
        IAssumptionCheckPolicy? assumptionCheckPolicy = null,
        IFeatureFlags? featureFlags = null,
        IConversationStageMetricsService? stageMetrics = null,
        IUserProfileService? userProfileService = null,
        ITurnLanguageResolver? turnLanguageResolver = null,
        ILiveWebRequirementPolicy? liveWebRequirementPolicy = null,
        ILocalFirstBenchmarkPolicy? localFirstBenchmarkPolicy = null)
    {
        _intentClassifier = intentClassifier;
        _ambiguityDetector = ambiguityDetector;
        _clarificationPolicy = clarificationPolicy;
        _intentTelemetry = intentTelemetry;
        _userProfileService = userProfileService ?? new UserProfileService();
        _turnLanguageResolver = turnLanguageResolver ?? new TurnLanguageResolver();
        _liveWebRequirementPolicy = liveWebRequirementPolicy ?? new LiveWebRequirementPolicy();
        _localFirstBenchmarkPolicy = localFirstBenchmarkPolicy ?? new LocalFirstBenchmarkPolicy();
        _latencyBudgetPolicy = latencyBudgetPolicy ?? new LatencyBudgetPolicy();
        _assumptionCheckPolicy = assumptionCheckPolicy ?? new AssumptionCheckPolicy();
        _intentV2Enabled = featureFlags?.IntentV2Enabled ?? ReadFlag("HELPER_FF_INTENT_V2", true);
        _stageMetrics = stageMetrics;
    }

    public async Task PlanAsync(ChatTurnContext context, CancellationToken ct)
    {
        var classifyTimer = Stopwatch.StartNew();
        var intent = _intentV2Enabled
            ? await _intentClassifier.ClassifyAsync(context.Request.Message, ct)
            : BuildLegacyIntent(context.Request.Message);
        _intentTelemetry.Record(intent);
        context.Intent = intent.Analysis;
        context.IntentConfidence = intent.Confidence;
        context.IntentSource = intent.Source;
        context.IntentSignals.Clear();
        context.IntentSignals.AddRange(intent.Signals);
        _stageMetrics?.Record("classify", classifyTimer.ElapsedMilliseconds, success: true);

        var trimmed = context.Request.Message.Trim();
        var profile = _userProfileService.Resolve(context.Conversation);
        context.ResolvedTurnLanguage = _turnLanguageResolver.Resolve(profile, trimmed, context.History);
        var benchmarkDecision = _localFirstBenchmarkPolicy.Evaluate(context.Request, context.ResolvedTurnLanguage);
        context.IsLocalFirstBenchmarkTurn = benchmarkDecision.IsBenchmark;
        context.LocalFirstBenchmarkMode = benchmarkDecision.Mode;
        context.RequireExplicitBenchmarkUncertainty = benchmarkDecision.RequireExplicitUncertainty;
        context.IsFactualPrompt = IsLikelyFactualPrompt(trimmed);
        var planTimer = Stopwatch.StartNew();
        var budget = _latencyBudgetPolicy.Resolve(context);
        context.ExecutionMode = budget.Mode;
        context.BudgetProfile = budget.Profile;
        context.TimeBudget = budget.TimeBudget;
        context.ToolCallBudget = budget.ToolCallBudget;
        context.TokenBudget = budget.TokenBudget;
        context.ModelCallBudget = budget.ModelCallBudget;
        context.BackgroundBudget = budget.BackgroundBudget;
        context.BudgetReason = budget.Reason;
        ApplyLiveWebRequirement(context, trimmed);
        ApplyLocalFirstBenchmarkPolicy(context, benchmarkDecision);
        var priorClarificationTurns = GetPriorClarificationTurns(context.Conversation);
        var ambiguity = _ambiguityDetector.Analyze(trimmed);
        context.AmbiguityType = ambiguity.Type.ToString();
        context.AmbiguityConfidence = ambiguity.Confidence;
        context.AmbiguityReason = ambiguity.Reason;
        if (ambiguity.IsAmbiguous)
        {
            if (ambiguity.Type == AmbiguityType.SafetyConfirmation)
            {
                context.RequiresClarification = true;
                context.RequiresConfirmation = true;
                context.ClarifyingQuestion = _clarificationPolicy.BuildQuestion(ambiguity, intent, priorClarificationTurns + 1, context.ResolvedTurnLanguage);
                _stageMetrics?.Record("plan", planTimer.ElapsedMilliseconds, success: true);
                return;
            }

            if (_clarificationPolicy.ShouldForceBestEffort(ambiguity, priorClarificationTurns))
            {
                context.RequiresClarification = false;
                context.ForceBestEffort = true;
                context.ForceBestEffortReason = string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "Лимит уточнений исчерпан. Продолжу с разумными допущениями и явно отмечу их."
                    : "We have used up the clarification budget. I will continue with best-effort assumptions and label them clearly.";
                context.UncertaintyFlags.Add("clarification_budget_exhausted");
            }
            else
            {
                context.ClarifyingQuestion = _clarificationPolicy.BuildQuestion(ambiguity, intent, priorClarificationTurns + 1, context.ResolvedTurnLanguage);
                context.ForceBestEffort = true;
                context.ForceBestEffortReason = BuildSoftBestEffortReason(ambiguity.Type, context.ResolvedTurnLanguage);
                context.UncertaintyFlags.Add("soft_best_effort_entry");
                context.UncertaintyFlags.Add($"soft_best_effort_{ambiguity.Type.ToString().ToLowerInvariant()}");
            }
        }

        if (!context.RequiresClarification)
        {
            var assumption = _assumptionCheckPolicy.Evaluate(context);
            if (assumption.RequiresClarification)
            {
                context.RequiresClarification = true;
                context.RequiresConfirmation = true;
                context.ClarifyingQuestion = assumption.ClarifyingQuestion;
                if (!string.IsNullOrWhiteSpace(assumption.Flag))
                {
                    context.UncertaintyFlags.Add(assumption.Flag);
                }
                _stageMetrics?.Record("plan", planTimer.ElapsedMilliseconds, success: true);
                return;
            }
        }

        if (!context.RequiresClarification &&
            !context.ForceBestEffort &&
            context.Intent.Intent == IntentType.Unknown &&
            GoldenTemplateIntentPolicy.HasExplicitGoldenTemplateRequest(trimmed))
        {
            context.Intent = context.Intent with { Intent = IntentType.Generate };
            context.IntentConfidence = Math.Max(context.IntentConfidence, 0.9);
            context.IntentSource = string.IsNullOrWhiteSpace(context.IntentSource)
                ? "planner_golden_template_override"
                : $"{context.IntentSource}+planner_golden_template_override";
            context.IntentSignals.Add("planner:explicit_golden_template_override");
            context.UncertaintyFlags.Add("golden_template_intent_forced_from_prompt");
        }

        if (!context.RequiresClarification &&
            !context.ForceBestEffort &&
            context.Intent.Intent == IntentType.Unknown &&
            IsExplicitResearchPrompt(trimmed))
        {
            context.Intent = context.Intent with { Intent = IntentType.Research };
            context.IntentConfidence = Math.Max(context.IntentConfidence, 0.55);
            context.IntentSource = string.IsNullOrWhiteSpace(context.IntentSource)
                ? "planner_research_override"
                : $"{context.IntentSource}+planner_research_override";
            context.IntentSignals.Add("planner:explicit_research_override");
            context.UncertaintyFlags.Add("research_intent_forced_from_prompt");
        }

        if (!context.ForceBestEffort && context.IntentConfidence > 0 && context.IntentConfidence <= 0.50)
        {
            if (context.Intent.Intent == IntentType.Research && IsExplicitResearchPrompt(trimmed))
            {
                context.UncertaintyFlags.Add("intent_low_confidence_research_exec");
                context.IntentConfidence = Math.Max(context.IntentConfidence, 0.5);
                context.IsFactualPrompt = true;
                return;
            }

            if (priorClarificationTurns >= _clarificationPolicy.MaxClarificationTurns)
            {
                context.ForceBestEffort = true;
                context.ForceBestEffortReason = string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "Даже после уточнений осталось несколько трактовок. Продолжу с разумными допущениями и явно отмечу их."
                    : "Even after clarification, multiple interpretations remain. I will continue with best-effort assumptions and label them clearly.";
                context.UncertaintyFlags.Add("intent_low_confidence_fallback");
            }
            else
            {
                context.RequiresClarification = true;
                context.ClarifyingQuestion = _clarificationPolicy.BuildLowConfidenceQuestion(intent, priorClarificationTurns + 1, context.ResolvedTurnLanguage);
                _stageMetrics?.Record("plan", planTimer.ElapsedMilliseconds, success: true);
                return;
            }
        }
        _stageMetrics?.Record("plan", planTimer.ElapsedMilliseconds, success: true);
    }

    private static void ApplyLocalFirstBenchmarkPolicy(ChatTurnContext context, LocalFirstBenchmarkDecision decision)
    {
        if (!decision.IsBenchmark)
        {
            return;
        }

        context.IntentSignals.Add("planner:local_first_benchmark");
        context.IsFactualPrompt = true;

        if (string.Equals(NormalizeLiveWebMode(context.Request.LiveWebMode), "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (decision.Mode)
        {
            case LocalFirstBenchmarkMode.LocalOnly:
                PromoteBenchmarkResearch(context, decision.ReasonCode, 0.64, "planner:benchmark_local_only");
                if (string.IsNullOrWhiteSpace(context.ResolvedLiveWebRequirement) ||
                    !string.Equals(context.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase))
                {
                    context.ResolvedLiveWebRequirement = "no_web_needed";
                    context.ResolvedLiveWebReason = decision.ReasonCode;
                }

                context.UncertaintyFlags.Add("benchmark_local_first_local_only_route");
                break;

            case LocalFirstBenchmarkMode.WebRecommended:
                PromoteBenchmarkResearch(context, decision.ReasonCode, 0.68, "planner:benchmark_web_recommended");
                if (string.Equals(context.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase))
                {
                    context.ResolvedLiveWebRequirement = "web_helpful";
                    context.ResolvedLiveWebReason = decision.ReasonCode;
                    context.LiveWebSignals.Add("benchmark:recommended_web");
                }

                context.UncertaintyFlags.Add("benchmark_local_first_web_recommended_route");
                break;

            case LocalFirstBenchmarkMode.WebRequired:
                PromoteBenchmarkResearch(context, decision.ReasonCode, 0.74, "planner:benchmark_web_required");
                context.ResolvedLiveWebRequirement = "web_required";
                context.ResolvedLiveWebReason = decision.ReasonCode;
                context.LiveWebSignals.Add("benchmark:mandatory_web");
                context.UncertaintyFlags.Add("benchmark_local_first_web_required_route");
                break;
        }
    }

    private void ApplyLiveWebRequirement(ChatTurnContext context, string trimmed)
    {
        var requestedMode = NormalizeLiveWebMode(context.Request.LiveWebMode);
        var decision = requestedMode switch
        {
            "force_search" => new LiveWebRequirementDecision(
                LiveWebRequirementLevel.WebRequired,
                "user_forced_search",
                new[] { "user:force_search" }),
            "no_web" => new LiveWebRequirementDecision(
                LiveWebRequirementLevel.NoWebNeeded,
                "user_disabled_web",
                new[] { "user:no_web" }),
            _ => _liveWebRequirementPolicy.Evaluate(trimmed, context.Intent)
        };
        context.ResolvedLiveWebRequirement = decision.Requirement switch
        {
            LiveWebRequirementLevel.WebRequired => "web_required",
            LiveWebRequirementLevel.WebHelpful => "web_helpful",
            _ => "no_web_needed"
        };
        context.ResolvedLiveWebReason = decision.ReasonCode;
        context.LiveWebSignals.Clear();
        context.LiveWebSignals.AddRange(decision.Signals);

        if (string.Equals(requestedMode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Intent.Intent == IntentType.Research)
            {
                context.Intent = context.Intent with { Intent = IntentType.Unknown };
                context.IntentConfidence = Math.Min(context.IntentConfidence, 0.49);
                context.IntentSource = string.IsNullOrWhiteSpace(context.IntentSource)
                    ? "planner_live_web_disabled_by_user"
                    : $"{context.IntentSource}+planner_live_web_disabled_by_user";
                context.IntentSignals.Add("planner:live_web_disabled_by_user");
            }

            context.UncertaintyFlags.Add("live_web_disabled_by_user");
            return;
        }

        if (decision.Requirement != LiveWebRequirementLevel.NoWebNeeded)
        {
            context.IsFactualPrompt = true;
        }

        if (string.Equals(requestedMode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Intent.Intent != IntentType.Research)
            {
                context.Intent = context.Intent with { Intent = IntentType.Research };
            }

            context.IntentConfidence = Math.Max(context.IntentConfidence, 0.85);
            context.IntentSource = string.IsNullOrWhiteSpace(context.IntentSource)
                ? "planner_live_web_forced_by_user"
                : $"{context.IntentSource}+planner_live_web_forced_by_user";
            context.IntentSignals.Add("planner:live_web_forced_by_user");
            if (IsProtectedGeneratePrompt(trimmed))
            {
                context.UncertaintyFlags.Add("live_web_force_search_overrode_generate_route");
            }

            return;
        }

        if (!ShouldPromoteToResearch(context.Intent.Intent, trimmed, decision))
        {
            return;
        }

        if (context.Intent.Intent != IntentType.Research)
        {
            context.Intent = context.Intent with { Intent = IntentType.Research };
        }

        var required = decision.Requirement == LiveWebRequirementLevel.WebRequired;
        context.IntentConfidence = Math.Max(context.IntentConfidence, required ? 0.72 : 0.60);
        context.IntentSource = string.IsNullOrWhiteSpace(context.IntentSource)
            ? (required ? "planner_live_web_required_override" : "planner_live_web_helpful_override")
            : $"{context.IntentSource}+{(required ? "planner_live_web_required_override" : "planner_live_web_helpful_override")}";
        context.IntentSignals.Add(required ? "planner:live_web_required" : "planner:live_web_helpful");
        context.UncertaintyFlags.Add(required ? "live_web_required_route_override" : "live_web_helpful_route_override");
    }

    private static string NormalizeLiveWebMode(string? mode)
    {
        if (string.Equals(mode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            return "force_search";
        }

        if (string.Equals(mode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return "no_web";
        }

        return "auto";
    }

    private static int GetPriorClarificationTurns(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            return Math.Max(0, state.ConsecutiveClarificationTurns);
        }
    }

    private static bool IsLikelyFactualPrompt(string prompt)
    {
        var factualTokens = new[] { "what", "when", "where", "who", "когда", "где", "кто", "факт", "источник", "сколько" };
        return factualTokens.Any(token => prompt.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExplicitResearchPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        return ResearchIntentPolicy.HasExplicitResearchRequest(prompt) ||
               ResearchIntentPolicy.CountWeakResearchSignals(prompt) >= 2;
    }

    private static bool ShouldPromoteToResearch(IntentType currentIntent, string prompt, LiveWebRequirementDecision decision)
    {
        if (decision.Requirement == LiveWebRequirementLevel.NoWebNeeded)
        {
            return false;
        }

        if (currentIntent == IntentType.Research || currentIntent == IntentType.Unknown)
        {
            return true;
        }

        if (currentIntent != IntentType.Generate)
        {
            return false;
        }

        return !IsProtectedGeneratePrompt(prompt);
    }

    private static void PromoteBenchmarkResearch(ChatTurnContext context, string sourceSuffix, double minConfidence, string signal)
    {
        if (context.Intent.Intent != IntentType.Research)
        {
            context.Intent = context.Intent with { Intent = IntentType.Research };
        }

        context.IntentConfidence = Math.Max(context.IntentConfidence, minConfidence);
        context.IntentSource = string.IsNullOrWhiteSpace(context.IntentSource)
            ? sourceSuffix
            : $"{context.IntentSource}+{sourceSuffix}";
        context.IntentSignals.Add(signal);
    }

    private static bool IsProtectedGeneratePrompt(string prompt)
    {
        if (GoldenTemplateIntentPolicy.HasExplicitGoldenTemplateRequest(prompt))
        {
            return true;
        }

        if (!ResearchIntentPolicy.HasExplicitGenerateRequest(prompt))
        {
            return false;
        }

        return ContainsAny(prompt, "code", "app", "project", "template", "service", "api", "endpoint", "library", "class", "function",
            "код", "приложение", "проект", "шаблон", "сервис", "эндпоинт", "библиотека", "класс", "функция");
    }

    private static IntentClassification BuildLegacyIntent(string message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new IntentClassification(new IntentAnalysis(Helper.Runtime.Core.IntentType.Unknown, string.Empty), 0.0, "legacy", Array.Empty<string>());
        }

        var isResearch = text.Contains("research", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("исслед", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("источник", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("compare", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("сравни", StringComparison.OrdinalIgnoreCase);

        var intent = isResearch ? Helper.Runtime.Core.IntentType.Research : Helper.Runtime.Core.IntentType.Generate;
        return new IntentClassification(
            new IntentAnalysis(intent, string.Empty),
            0.55,
            "legacy",
            new[] { "legacy:intent_v1" });
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static bool ContainsAny(string prompt, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (prompt.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildSoftBestEffortReason(AmbiguityType type, string? resolvedLanguage)
    {
        var isRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        return (type, isRussian) switch
        {
            (AmbiguityType.Goal, true) => "Запрос пока слишком общий, поэтому беру самый полезный практический старт.",
            (AmbiguityType.Format, true) => "Точный формат не задан, поэтому начну с краткой практической структуры.",
            (AmbiguityType.Constraints, true) => "Жёсткие ограничения не заданы, поэтому двигаюсь от безопасных стандартных допущений.",
            (AmbiguityType.Data, true) => "Не хватает входных данных, поэтому начну с безопасного общего варианта и отмечу допущения.",
            (AmbiguityType.Scope, true) => "Область пока широкая, поэтому начну с минимального полезного объёма.",
            (_, true) => "Деталей пока недостаточно, поэтому начинаю с наиболее вероятной полезной трактовки.",

            (AmbiguityType.Goal, false) => "The request is still broad, so I am starting with the most useful practical interpretation.",
            (AmbiguityType.Format, false) => "The exact format is unspecified, so I will begin with a concise practical structure.",
            (AmbiguityType.Constraints, false) => "Hard constraints are missing, so I am proceeding from safe default assumptions.",
            (AmbiguityType.Data, false) => "Key input data is missing, so I will start with a safe general version and label assumptions.",
            (AmbiguityType.Scope, false) => "The scope is still broad, so I will start with the smallest useful slice.",
            _ => "Details are still incomplete, so I am starting from the most likely useful interpretation."
        };
    }
}

