namespace Helper.Api.Conversation;

public sealed record HumanLikeCommunicationEvalOptions(
    int MinPreparedRuns = 240,
    bool UseRealModel = false,
    int Seed = 42);

public sealed record HumanLikeCommunicationRubricDimension(
    string Key,
    string DisplayName,
    string Description,
    string ScoringGuidance);

public sealed record HumanLikeCommunicationEvalRubric(
    string Version,
    int MinimumSeedScenarios,
    int MinimumPreparedRuns,
    IReadOnlyList<string> RequiredKinds,
    IReadOnlyList<string> RequiredLabels,
    IReadOnlyList<HumanLikeCommunicationRubricDimension> Dimensions);

public sealed record HumanLikeCommunicationEvalPackagePaths(
    string RootPath,
    string CorpusPath,
    string RubricPath);

public sealed record HumanLikeCommunicationEvalSummary(
    int SeedScenarioCount,
    int PreparedScenarioCount,
    int EndToEndScenarioCount,
    double EndToEndRatio,
    string GateStatus,
    IReadOnlyDictionary<string, int> LanguageDistribution,
    IReadOnlyDictionary<string, int> KindDistribution,
    IReadOnlyDictionary<string, int> LabelDistribution,
    IReadOnlyList<string> MissingKinds,
    IReadOnlyList<string> MissingLabels,
    IReadOnlyList<string> Alerts);

public sealed record HumanLikeCommunicationEvalPackage(
    HumanLikeCommunicationEvalPackagePaths Paths,
    HumanLikeCommunicationEvalRubric Rubric,
    IReadOnlyList<EvalScenarioDefinition> SeedScenarios,
    EvalPreparedRun PreparedRun,
    HumanLikeCommunicationEvalSummary Summary);

public sealed record HumanLikeCommunicationEvalExportResult(
    string JsonPath,
    string MarkdownPath,
    HumanLikeCommunicationEvalPackage Package);

