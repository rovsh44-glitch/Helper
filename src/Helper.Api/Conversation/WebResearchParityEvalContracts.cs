namespace Helper.Api.Conversation;

public sealed record WebResearchParityEvalOptions(
    int MinPreparedRuns = 240,
    bool UseRealModel = false,
    int Seed = 42);

public sealed record WebResearchParityRubricDimension(
    string Key,
    string DisplayName,
    string Description);

public sealed record WebResearchParityEvalRubric(
    string Version,
    int MinimumSeedScenarios,
    int MinimumPreparedRuns,
    int MinimumProviderFixtures,
    int MinimumPageFixtures,
    IReadOnlyList<string> RequiredKinds,
    IReadOnlyList<string> RequiredLabels,
    IReadOnlyList<string> RequiredMetrics,
    IReadOnlyList<WebResearchParityRubricDimension> Dimensions);

public sealed record WebResearchParityEvalPackagePaths(
    string RootPath,
    string CorpusPath,
    string RubricPath,
    string ProviderFixturesPath,
    string PageFixturesPath);

public sealed record WebResearchParityEvalSummary(
    int SeedScenarioCount,
    int PreparedScenarioCount,
    int EndToEndScenarioCount,
    double EndToEndRatio,
    int ProviderFixtureCount,
    int PageFixtureCount,
    string GateStatus,
    IReadOnlyDictionary<string, int> LanguageDistribution,
    IReadOnlyDictionary<string, int> KindDistribution,
    IReadOnlyDictionary<string, int> LabelDistribution,
    IReadOnlyList<string> MissingKinds,
    IReadOnlyList<string> MissingLabels,
    IReadOnlyList<string> MissingMetrics,
    IReadOnlyList<string> Alerts);

public sealed record WebResearchParityEvalPackage(
    WebResearchParityEvalPackagePaths Paths,
    WebResearchParityEvalRubric Rubric,
    IReadOnlyList<EvalScenarioDefinition> SeedScenarios,
    EvalPreparedRun PreparedRun,
    WebResearchParityEvalSummary Summary);

public sealed record WebResearchParityEvalExportResult(
    string JsonPath,
    string MarkdownPath,
    WebResearchParityEvalPackage Package);

