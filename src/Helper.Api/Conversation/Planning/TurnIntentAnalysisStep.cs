using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed class TurnIntentAnalysisStep
{
    private readonly IIntentClassifier _intentClassifier;
    private readonly IIntentTelemetryService _intentTelemetry;
    private readonly IFeatureFlags _featureFlags;

    public TurnIntentAnalysisStep(
        IIntentClassifier intentClassifier,
        IIntentTelemetryService intentTelemetry,
        IFeatureFlags featureFlags)
    {
        _intentClassifier = intentClassifier;
        _intentTelemetry = intentTelemetry;
        _featureFlags = featureFlags;
    }

    public async Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        var turn = planningContext.Turn;
        var intent = _featureFlags.IntentV2Enabled
            ? await _intentClassifier.ClassifyAsync(turn.Request.Message, ct).ConfigureAwait(false)
            : TurnPlanningRules.BuildLegacyIntent(turn.Request.Message);
        planningState.IntentClassification = intent;
        _intentTelemetry.Record(intent);
        turn.Intent = intent.Analysis;
        turn.IntentConfidence = intent.Confidence;
        turn.IntentSource = intent.Source;
        turn.IntentSignals.Clear();
        turn.IntentSignals.AddRange(intent.Signals);
    }
}
