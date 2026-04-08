using Helper.Api.Backend.Application;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Conversation;

internal enum ChatTurnHandlingMode
{
    Standard,
    Streaming
}

internal sealed record ChatTurnExecutorDependencies(
    AILink Ai,
    IModelGateway ModelGateway,
    IModelOrchestrator ModelOrchestrator,
    IResearchService ResearchService,
    IShortHorizonResearchCache ResearchCache,
    IChatResiliencePolicy Resilience,
    IUserProfileService UserProfileService,
    ITurnLanguageResolver TurnLanguageResolver,
    IHelperOrchestrator Orchestrator,
    Hosting.ApiRuntimeConfig Config,
    IFailureEnvelopeFactory FailureEnvelopeFactory,
    IToolAuditService? ToolAudit,
    ISourceNormalizationService SourceNormalizer,
    IConversationVariationPolicy VariationPolicy,
    IBackendRuntimePolicyProvider PolicyProvider,
    IConversationPromptPolicy? PromptPolicy,
    IConversationModelSelectionPolicy? ModelSelectionPolicy,
    bool ProjectGenerationEnabled,
    double GenerateMinConfidence,
    ILocalBaselineAnswerService? LocalBaselineAnswerService = null,
    IWebSearchOrchestrator? WebSearchOrchestrator = null,
    IConversationContextAssembler? ContextAssembler = null,
    IReasoningBranchExecutor? ReasoningBranchExecutor = null);

public sealed record ChatTurnPreparedInvocation(string Prompt, string? PreferredModel, string SystemInstruction);

internal sealed record ChatTurnMessage(ChatStreamChunkType ChunkType, string Content, string? WarningCode = null);

internal sealed record ChatTurnImmediateOutcome(IReadOnlyList<ChatTurnMessage> Messages);

internal static class ChatTurnExecutionSupport
{
    public static string FormatGenerationFailure(IReadOnlyList<FailureEnvelope> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return "Failed to generate project.\nDiagnostics:\n- [unknown/UNKNOWN] No structured diagnostics were produced.";
        }

        var lines = diagnostics
            .Take(6)
            .Select(d =>
                $"- [{d.Stage}/{d.ErrorCode}] {d.Evidence} | retryable: {(d.Retryable ? "yes" : "no")} | action: {d.UserAction}");
        return "Failed to generate project.\nDiagnostics:\n" + string.Join("\n", lines);
    }

    public static CancellationTokenSource CreateStageCancellation(string stage, ChatTurnContext context, CancellationToken requestToken)
    {
        var timeout = ResolveStageTimeout(stage, context.TimeBudget);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    public static void ApplyStageTimeoutFallback(ChatTurnContext context, string stage)
    {
        context.BudgetExceeded = true;
        context.Confidence = Math.Min(context.Confidence, 0.32);
        context.GroundingStatus = "degraded";
        context.CitationCoverage = 0;
        context.UncertaintyFlags.Add($"stage_timeout:{stage}");
        context.UncertaintyFlags.Add("timeout_degraded_response");
        if (IsRussian(context))
        {
            context.NextStep = "Сузьте область задачи, разбейте её на части или попросите более короткий ответ.";
            context.ExecutionOutput = $"Я не успел завершить этот ход в пределах допустимой задержки (этап: {stage}). Могу продолжить с более узкой областью.";
            return;
        }

        context.NextStep = "Please narrow scope, split the request, or ask for a concise answer.";
        context.ExecutionOutput = $"I could not complete this turn within the latency budget (stage: {stage}). I can continue with a narrower scope.";
    }

    public static void ApplyGenerationDisabledFallback(ChatTurnContext context)
    {
        context.BudgetExceeded = false;
        context.Confidence = Math.Min(context.Confidence, 0.45);
        context.GroundingStatus = "degraded";
        context.UncertaintyFlags.Add("generation_disabled");
        if (IsRussian(context))
        {
            context.NextStep = "Опишите целевую область проекта и ограничения, и я помогу итеративно вместо полного запуска генерации.";
            context.ExecutionOutput = "Генерация проектов отключена для этого профиля выполнения. Я всё ещё могу помочь с архитектурой, структурой файлов и пошаговой реализацией.";
            return;
        }

        context.NextStep = "Describe the target project scope and constraints; I will help iteratively instead of full project generation.";
        context.ExecutionOutput = "Project generation is disabled for this runtime profile. I can still help by planning architecture, files, and step-by-step implementation.";
    }

    public static void ApplyGenerationAdmissionFallback(ChatTurnContext context)
    {
        context.BudgetExceeded = false;
        context.Confidence = Math.Min(context.Confidence, 0.42);
        context.GroundingStatus = "degraded";
        context.UncertaintyFlags.Add("generation_admission_denied");
        if (IsRussian(context))
        {
            context.NextStep = "Если нужна генерация проекта, попросите об этом явно и добавьте конкретные ограничения.";
            context.ExecutionOutput = "Перед тяжёлой генерацией мне нужно явное намерение на создание проекта и конкретные ограничения. Уточните стек, область и ожидаемые артефакты.";
            return;
        }

        context.NextStep = "If you need project generation, explicitly ask to generate/create/build a project and provide concrete constraints.";
        context.ExecutionOutput = "I need explicit project-generation intent and constraints before running heavy generation. Please clarify target stack, scope, and deliverables.";
    }

    public static void ApplyResearchDisabledFallback(ChatTurnContext context)
    {
        context.BudgetExceeded = false;
        context.Confidence = Math.Min(context.Confidence, 0.4);
        context.GroundingStatus = "degraded";
        context.UncertaintyFlags.Add("research_disabled");
        if (IsRussian(context))
        {
            context.NextStep = "Исследовательский режим отключён для этого профиля выполнения. Можно запросить прямой ответ или включить исследовательскую политику.";
            context.ExecutionOutput = "Исследовательский режим отключён для этого профиля выполнения. Я всё ещё могу помочь прямым ответом или более узким объяснением.";
            return;
        }

        context.NextStep = "Research mode is disabled for this runtime profile. Ask for a direct answer or enable research policy.";
        context.ExecutionOutput = "Research mode is disabled for this runtime profile. I can still help with a direct answer or a scoped explanation.";
    }

    public static bool TryApplyDeterministicMemoryCapture(ChatTurnContext context, IConversationVariationPolicy? variationPolicy = null)
    {
        var message = context.Request.Message?.Trim() ?? string.Empty;
        if (!RememberDirectiveParser.TryExtractFact(message, out var fact))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(fact))
        {
            context.RequiresClarification = true;
            context.RequiresConfirmation = true;
            context.GroundingStatus = "clarification_required";
            context.ClarifyingQuestion = IsRussian(context, message)
                ? "Что именно запомнить для этого диалога?"
                : "What should I remember for this conversation?";
            context.ExecutionOutput = context.ClarifyingQuestion;
            return true;
        }

        var trimmedFact = fact.Length <= 120 ? fact : fact[..120].TrimEnd() + "...";
        context.BudgetExceeded = false;
        context.RequiresConfirmation = false;
        context.Confidence = Math.Max(context.Confidence, 0.96);
        context.GroundingStatus = "memory_captured";
        context.CitationCoverage = 0;
        var resolvedVariationPolicy = variationPolicy ?? new ConversationVariationPolicy();
        var isRussian = IsRussian(context, message);
        context.NextStep = MemoryAcknowledgementCatalog.SelectNextStep(resolvedVariationPolicy, context, isRussian);
        var lead = MemoryAcknowledgementCatalog.SelectLead(resolvedVariationPolicy, context, isRussian);
        context.ExecutionOutput = $"{lead}: \"{trimmedFact}\".";
        context.UncertaintyFlags.Add("deterministic_memory_capture");
        return true;
    }

    public static string BuildClarificationFallbackPrompt(ChatTurnContext context, IConversationVariationPolicy? variationPolicy = null)
    {
        var resolvedVariationPolicy = variationPolicy ?? new ConversationVariationPolicy();
        return resolvedVariationPolicy.Select(
            DialogAct.Clarify,
            VariationSlot.ClarifyPrompt,
            context,
            IsRussian(context)
                ? new[]
                {
                    "Подскажите чуть точнее, чего вы хотите добиться.",
                    "Нужна одна короткая деталь, чтобы я не промахнулся с ответом.",
                    "Уточните запрос одним сообщением, и я сразу продолжу."
                }
                : new[]
                {
                    "Tell me one more detail so I can aim the answer properly.",
                    "I need one short clarification to avoid heading in the wrong direction.",
                    "Clarify the request in one message and I will continue right away."
                });
    }

    public static string BuildToolBudgetExceededMessage(ChatTurnContext context)
    {
        return IsRussian(context)
            ? "Для этого хода исчерпан бюджет инструментов. Пожалуйста, сузьте запрос."
            : "Tool budget exceeded for this turn. Please narrow the request.";
    }

    public static string BuildGenerationStartMessage(ChatTurnContext context)
    {
        return IsRussian(context)
            ? "Запускаю конвейер генерации. Это может занять несколько минут...\n"
            : "Starting generation pipeline. This may take a few minutes...\n";
    }

    public static string BuildGenerationSuccessMessage(ChatTurnContext context, string projectPath, int fileCount)
    {
        return IsRussian(context)
            ? $"Проект успешно сгенерирован по пути: {projectPath}\n\nСоздано файлов: {fileCount}."
            : $"Project successfully generated at: {projectPath}\n\nGenerated {fileCount} files.";
    }

    public static bool ShouldBypassGenerationAdmissionForGoldenTemplate(ChatTurnContext context)
    {
        return GoldenTemplateIntentPolicy.HasExplicitGoldenTemplateRequest(context.Request.Message);
    }

    public static HelperModelClass ResolveModelClass(ChatTurnContext context)
    {
        if (string.Equals(context.ModelRouteKey, "vision", StringComparison.OrdinalIgnoreCase))
        {
            return HelperModelClass.Vision;
        }

        if (string.Equals(context.ModelRouteKey, "coder", StringComparison.OrdinalIgnoreCase))
        {
            return HelperModelClass.Coder;
        }

        if (string.Equals(context.ModelRouteKey, "verifier", StringComparison.OrdinalIgnoreCase))
        {
            return HelperModelClass.Critic;
        }

        return context.BudgetProfile switch
        {
            TurnBudgetProfile.Generation => HelperModelClass.Coder,
            TurnBudgetProfile.HighRisk => HelperModelClass.Critic,
            TurnBudgetProfile.Research => HelperModelClass.Reasoning,
            _ => context.ExecutionMode == TurnExecutionMode.Fast
                ? HelperModelClass.Fast
                : HelperModelClass.Reasoning
        };
    }

    public static string BuildStageTimeoutDetails(ChatTurnContext context, string stage)
    {
        var elapsedMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - context.StartedAt).TotalMilliseconds);
        return $"stage_timeout:{stage};intent={context.Intent.Intent};executionMode={context.ExecutionMode};elapsedMs={elapsedMs};exceptionType=OperationCanceledException;correlationId={context.Conversation.Id}";
    }

    public static ToolAuditEntry BuildAuditEntry(
        ChatTurnContext context,
        DateTimeOffset timestamp,
        string toolName,
        string operation,
        bool success,
        string? error = null,
        string? details = null)
    {
        var correlationId = context.Conversation.Id;
        return new ToolAuditEntry(
            timestamp,
            toolName,
            operation,
            success,
            error,
            details,
            Source: "chat_execute",
            CorrelationId: correlationId,
            TurnId: context.TurnId);
    }

    public static bool ReadProjectGenerationFlag()
    {
        var explicitFlag = Environment.GetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION");
        if (bool.TryParse(explicitFlag, out var explicitValue))
        {
            return explicitValue;
        }

        return !IsEvalOrCertMode();
    }

    public static double ReadDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static TimeSpan ResolveStageTimeout(string stage, TimeSpan turnBudget)
    {
        var fallback = stage switch
        {
            "generate" => TimeSpan.FromSeconds(30),
            "research" => TimeSpan.FromSeconds(20),
            "llm" => TimeSpan.FromSeconds(20),
            _ => TimeSpan.FromSeconds(15)
        };

        var envName = stage switch
        {
            "generate" => "HELPER_CHAT_STAGE_TIMEOUT_GENERATE_SEC",
            "research" => "HELPER_CHAT_STAGE_TIMEOUT_RESEARCH_SEC",
            "llm" => "HELPER_CHAT_STAGE_TIMEOUT_LLM_SEC",
            _ => "HELPER_CHAT_STAGE_TIMEOUT_SEC"
        };

        var configured = ReadInt(envName, (int)fallback.TotalSeconds, 3, 180);
        var hardCap = TimeSpan.FromSeconds(configured);
        return turnBudget <= TimeSpan.Zero
            ? hardCap
            : TimeSpan.FromSeconds(Math.Max(1, Math.Min(hardCap.TotalSeconds, turnBudget.TotalSeconds)));
    }

    private static bool IsEvalOrCertMode()
    {
        return ReadBool("HELPER_CERT_MODE", false) || ReadBool("HELPER_EVAL_MODE", false);
    }

    private static bool ReadBool(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static bool IsRussian(ChatTurnContext context, string? fallbackText = null)
    {
        if (string.Equals(context.ResolvedTurnLanguage, "ru", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(context.ResolvedTurnLanguage, "en", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsLikelyRussianText(fallbackText ?? context.Request.Message ?? string.Empty);
    }

    private static bool IsLikelyRussianText(string text)
    {
        foreach (var ch in text)
        {
            if ((ch >= '\u0400' && ch <= '\u04FF') || ch == '\u0451' || ch == '\u0401')
            {
                return true;
            }
        }

        return false;
    }

}

