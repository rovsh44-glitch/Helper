using System.Text;
using Helper.Api.Conversation.Epistemic;

namespace Helper.Api.Conversation;

public interface IResponseComposerService
{
    string Compose(ChatTurnContext context, string preparedOutput);
}

public sealed class ResponseComposerService : IResponseComposerService
{
    private readonly IDialogActPlanner _dialogActPlanner;
    private readonly IConversationVariationPolicy _variationPolicy;
    private readonly IResponseTextDeduplicator _textDeduplicator;
    private readonly IAnswerShapePolicy _answerShapePolicy;
    private readonly INextStepComposer _nextStepComposer;
    private readonly IComposerLocalizationResolver _localizationResolver;
    private readonly IBenchmarkResponseFormatter _benchmarkResponseFormatter;

    internal ResponseComposerService(
        IDialogActPlanner dialogActPlanner,
        IConversationVariationPolicy variationPolicy,
        IResponseTextDeduplicator textDeduplicator,
        IAnswerShapePolicy answerShapePolicy,
        INextStepComposer nextStepComposer,
        IComposerLocalizationResolver localizationResolver,
        IBenchmarkResponseFormatter benchmarkResponseFormatter)
    {
        _dialogActPlanner = dialogActPlanner;
        _variationPolicy = variationPolicy;
        _textDeduplicator = textDeduplicator;
        _answerShapePolicy = answerShapePolicy;
        _nextStepComposer = nextStepComposer;
        _localizationResolver = localizationResolver;
        _benchmarkResponseFormatter = benchmarkResponseFormatter;
    }

    public string Compose(ChatTurnContext context, string preparedOutput)
    {
        var localization = _localizationResolver.Resolve(context);
        var solution = _textDeduplicator.NormalizePreparedOutput(preparedOutput, localization);
        solution = EpistemicAnswerModeRenderer.Apply(context, solution, localization);
        if (_benchmarkResponseFormatter.TryComposeLocalFirstBenchmarkResponse(context, solution, out var benchmarkResponse))
        {
            return benchmarkResponse;
        }

        solution = _answerShapePolicy.ApplyTaskClassFormatting(context, solution, localization);
        solution = _answerShapePolicy.ApplyAnswerShapePreference(context, solution, localization);
        solution = _answerShapePolicy.ApplyConversationalNaturalness(context, solution);
        var mode = ResponseCompositionRecoveryModePolicy.Promote(ResolveMode(context, solution), context);
        context.NextStep = _nextStepComposer.ResolveEffectiveNextStep(context, solution, localization, mode);
        var plan = _dialogActPlanner.BuildPlan(context, mode, solution);

        return mode switch
        {
            ResponseCompositionMode.OperatorSummary => ComposeOperational(context, solution, localization, plan),
            ResponseCompositionMode.EvidenceBrief => ComposeEvidenceBrief(context, solution, localization, plan),
            ResponseCompositionMode.StructuredAnswer => ComposeStructuredAnswer(context, solution, localization, plan),
            _ => ComposeFreeformShort(context, solution, plan)
        };
    }

    private string ComposeFreeformShort(ChatTurnContext context, string solution, DialogActPlan plan)
    {
        var nextStep = NormalizeOptional(context.NextStep);
        if (string.IsNullOrWhiteSpace(nextStep) || !plan.Contains(DialogAct.NextStep) || !_nextStepComposer.ShouldRender(context, solution, nextStep))
        {
            return solution;
        }

        var bridge = _nextStepComposer.SelectNextStepBridge(context);
        return $"{solution}\n\n{bridge}\n{nextStep}".TrimEnd();
    }

    private string ComposeStructuredAnswer(ChatTurnContext context, string solution, ComposerLocalization localization, DialogActPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(solution);
        AppendNextStepSection(builder, localization, context.NextStep, context, plan);
        return builder.ToString().TrimEnd();
    }

    private string ComposeEvidenceBrief(ChatTurnContext context, string solution, ComposerLocalization localization, DialogActPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(solution);
        AppendSourcesSection(builder, localization, context, solution);
        AppendNextStepSection(builder, localization, context.NextStep, context, plan);
        return builder.ToString().TrimEnd();
    }

    private string ComposeOperational(ChatTurnContext context, string solution, ComposerLocalization localization, DialogActPlan plan)
    {
        var outcome = string.IsNullOrWhiteSpace(solution)
            ? localization.NoOperationalOutputProduced
            : solution;
        var nextStep = string.IsNullOrWhiteSpace(context.NextStep)
            ? localization.DefaultOperationalNextStep
            : context.NextStep.Trim();
        var status = outcome.StartsWith("Failed to generate project.", StringComparison.OrdinalIgnoreCase) ||
                     context.UncertaintyFlags.Contains("turn_pipeline_recovered")
            ? localization.DegradedStatus
            : localization.OkStatus;

        var builder = new StringBuilder();
        builder.AppendLine(SelectOperationalSummaryHeader(context, localization));
        builder.AppendLine(localization.FormatExecutionSummary(context, status));
        builder.AppendLine();
        builder.AppendLine(localization.ResultHeader);
        builder.AppendLine(outcome);

        if (context.Sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(localization.ArtifactsHeader);
            foreach (var source in context.Sources.Distinct().Take(8))
            {
                builder.Append("- ");
                builder.AppendLine(source);
            }
        }

        if (context.UncertaintyFlags.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(localization.FlagsHeader);
            foreach (var flag in context.UncertaintyFlags.Distinct().Take(8))
            {
                builder.Append("- ");
                builder.AppendLine(flag);
            }
        }

        builder.AppendLine();
        builder.AppendLine(_nextStepComposer.SelectNextStepHeader(context, localization));
        builder.AppendLine(nextStep);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSourcesSection(StringBuilder builder, ComposerLocalization localization, ChatTurnContext context, string solution)
    {
        if ((context.Sources.Count == 0 && context.ResearchEvidenceItems.Count == 0) ||
            ResponseComposerSupport.ContainsSourcesSection(solution))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(localization.SourcesHeader);
        var webSources = ConversationSourceClassifier.GetWebSources(context);
        var localSources = ConversationSourceClassifier.GetLocalSources(context);
        if (webSources.Count > 0 || localSources.Count > 0)
        {
            foreach (var source in webSources.Take(8))
            {
                builder.Append("- web: ");
                builder.AppendLine(source);
            }

            foreach (var source in localSources.Take(Math.Max(0, 8 - webSources.Count)))
            {
                builder.Append("- local: ");
                builder.AppendLine(source);
            }

            return;
        }

        foreach (var source in context.Sources.Distinct().Take(8))
        {
            builder.Append("- ");
            builder.AppendLine(source);
        }
    }

    private void AppendNextStepSection(StringBuilder builder, ComposerLocalization localization, string? nextStep, ChatTurnContext context, DialogActPlan plan)
    {
        var normalized = NormalizeOptional(nextStep);
        if (string.IsNullOrWhiteSpace(normalized) || !plan.Contains(DialogAct.NextStep) || !_nextStepComposer.ShouldRender(context, builder.ToString(), normalized))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(_nextStepComposer.SelectNextStepHeader(context, localization));
        builder.AppendLine(normalized);
    }

    private ResponseCompositionMode ResolveMode(ChatTurnContext context, string solution)
    {
        if (ResponseComposerSupport.IsOperationalTurn(context))
        {
            return ResponseCompositionMode.OperatorSummary;
        }

        if (context.Sources.Count > 0 || ResponseComposerSupport.ContainsSourcesSection(solution))
        {
            return ResponseCompositionMode.EvidenceBrief;
        }

        if (_answerShapePolicy.HasStructuredShape(solution))
        {
            return ResponseCompositionMode.StructuredAnswer;
        }

        return ResponseCompositionMode.FreeformShort;
    }

    private string SelectOperationalSummaryHeader(ChatTurnContext context, ComposerLocalization localization)
    {
        return _variationPolicy.Select(
            DialogAct.Summarize,
            VariationSlot.OperatorSummaryHeader,
            context,
            localization.OperatorSummaryHeaders);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

