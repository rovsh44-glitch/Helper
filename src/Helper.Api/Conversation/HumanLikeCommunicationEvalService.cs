namespace Helper.Api.Conversation;

public interface IHumanLikeCommunicationEvalService
{
    Task<HumanLikeCommunicationEvalPackage> PrepareAsync(string packageRoot, HumanLikeCommunicationEvalOptions options, CancellationToken ct);

    Task<HumanLikeCommunicationEvalExportResult> ExportAsync(
        string packageRoot,
        string outputDirectory,
        HumanLikeCommunicationEvalOptions options,
        CancellationToken ct);
}

public sealed class HumanLikeCommunicationEvalService : IHumanLikeCommunicationEvalService
{
    private readonly IEvalCorpusLoader _corpusLoader;
    private readonly IEvalRunnerV2 _runner;
    private readonly IHumanLikeCommunicationEvalRubricLoader _rubricLoader;
    private readonly IHumanLikeCommunicationEvalReportWriter _reportWriter;

    public HumanLikeCommunicationEvalService(
        IEvalCorpusLoader? corpusLoader = null,
        IEvalRunnerV2? runner = null,
        IHumanLikeCommunicationEvalRubricLoader? rubricLoader = null,
        IHumanLikeCommunicationEvalReportWriter? reportWriter = null)
    {
        _corpusLoader = corpusLoader ?? new EvalCorpusLoader();
        _runner = runner ?? new EvalRunnerV2(_corpusLoader);
        _rubricLoader = rubricLoader ?? new HumanLikeCommunicationEvalRubricLoader();
        _reportWriter = reportWriter ?? new HumanLikeCommunicationEvalReportWriter();
    }

    public async Task<HumanLikeCommunicationEvalPackage> PrepareAsync(
        string packageRoot,
        HumanLikeCommunicationEvalOptions options,
        CancellationToken ct)
    {
        var paths = ResolvePaths(packageRoot);
        var rubric = await _rubricLoader.LoadAsync(paths.RubricPath, ct).ConfigureAwait(false);
        var seedScenarios = await _corpusLoader.LoadAsync(paths.CorpusPath, ct).ConfigureAwait(false);
        var effectivePreparedRuns = Math.Max(
            rubric.MinimumPreparedRuns,
            options.MinPreparedRuns);
        var preparedRun = await _runner
            .PrepareRunAsync(
                seedScenarios,
                new EvalRunOptions(effectivePreparedRuns, options.UseRealModel, options.Seed),
                ct)
            .ConfigureAwait(false);

        var summary = BuildSummary(seedScenarios, preparedRun, rubric);
        return new HumanLikeCommunicationEvalPackage(paths, rubric, seedScenarios, preparedRun, summary);
    }

    public async Task<HumanLikeCommunicationEvalExportResult> ExportAsync(
        string packageRoot,
        string outputDirectory,
        HumanLikeCommunicationEvalOptions options,
        CancellationToken ct)
    {
        var package = await PrepareAsync(packageRoot, options, ct).ConfigureAwait(false);
        return await _reportWriter.WriteAsync(package, outputDirectory, ct).ConfigureAwait(false);
    }

    private static HumanLikeCommunicationEvalPackagePaths ResolvePaths(string packageRoot)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            throw new ArgumentException("Package root must not be empty.", nameof(packageRoot));
        }

        var resolvedRoot = Path.GetFullPath(packageRoot);
        var corpusPath = Path.Combine(resolvedRoot, "corpus.jsonl");
        var rubricPath = Path.Combine(resolvedRoot, "rubric.json");

        if (!Directory.Exists(resolvedRoot))
        {
            throw new DirectoryNotFoundException($"Human-like communication eval package directory was not found: {resolvedRoot}");
        }

        return new HumanLikeCommunicationEvalPackagePaths(resolvedRoot, corpusPath, rubricPath);
    }

    private static HumanLikeCommunicationEvalSummary BuildSummary(
        IReadOnlyList<EvalScenarioDefinition> seedScenarios,
        EvalPreparedRun preparedRun,
        HumanLikeCommunicationEvalRubric rubric)
    {
        var languageDistribution = seedScenarios
            .GroupBy(scenario => scenario.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var kindDistribution = seedScenarios
            .GroupBy(scenario => scenario.Kind, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var labelDistribution = seedScenarios
            .SelectMany(scenario => scenario.Labels ?? Array.Empty<string>())
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var missingKinds = rubric.RequiredKinds
            .Except(kindDistribution.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingLabels = rubric.RequiredLabels
            .Except(labelDistribution.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var alerts = new List<string>();
        if (seedScenarios.Count < rubric.MinimumSeedScenarios)
        {
            alerts.Add($"Seed scenario count {seedScenarios.Count} is below rubric minimum {rubric.MinimumSeedScenarios}.");
        }

        if (preparedRun.Scenarios.Count < rubric.MinimumPreparedRuns)
        {
            alerts.Add($"Prepared run count {preparedRun.Scenarios.Count} is below rubric minimum {rubric.MinimumPreparedRuns}.");
        }

        if (missingKinds.Length > 0)
        {
            alerts.Add($"Missing required scenario classes: {string.Join(", ", missingKinds)}.");
        }

        if (missingLabels.Length > 0)
        {
            alerts.Add($"Missing required labels: {string.Join(", ", missingLabels)}.");
        }

        var gateStatus = alerts.Count == 0 ? "pass" : "fail";
        return new HumanLikeCommunicationEvalSummary(
            SeedScenarioCount: seedScenarios.Count,
            PreparedScenarioCount: preparedRun.Scenarios.Count,
            EndToEndScenarioCount: preparedRun.Summary.EndToEndScenarios,
            EndToEndRatio: preparedRun.Summary.EndToEndRatio,
            GateStatus: gateStatus,
            LanguageDistribution: languageDistribution,
            KindDistribution: kindDistribution,
            LabelDistribution: labelDistribution,
            MissingKinds: missingKinds,
            MissingLabels: missingLabels,
            Alerts: alerts);
    }
}

