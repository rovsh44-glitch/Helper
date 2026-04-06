using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed class TurnPlanningState
{
    public IntentClassification IntentClassification { get; set; } =
        new(new IntentAnalysis(IntentType.Unknown, string.Empty), 0.0, "unknown", Array.Empty<string>());

    public ConversationUserProfile? Profile { get; set; }

    public PersonalizationProfile Personalization { get; set; } = PersonalizationProfile.Default;

    public LocalFirstBenchmarkDecision BenchmarkDecision { get; set; } =
        new(false, LocalFirstBenchmarkMode.None, RequireRussianOutput: false, RequireExplicitUncertainty: false, "none");

    public int PriorClarificationTurns { get; set; }

    public AmbiguityDecision Ambiguity { get; set; } =
        new(false, AmbiguityType.None, 0.0, string.Empty);

    public bool StopRequested { get; private set; }

    public void Stop() => StopRequested = true;
}
