namespace Helper.Api.Conversation;

public interface IWebResearchParityEvalService
{
    Task<WebResearchParityEvalPackage> PrepareAsync(string packageRoot, WebResearchParityEvalOptions options, CancellationToken ct);

    Task<WebResearchParityEvalExportResult> ExportAsync(
        string packageRoot,
        string outputDirectory,
        WebResearchParityEvalOptions options,
        CancellationToken ct);
}

public sealed class WebResearchParityEvalService : IWebResearchParityEvalService
{
    private readonly IEvalCorpusLoader _corpusLoader;
    private readonly IEvalRunnerV2 _runner;
    private readonly IWebResearchParityEvalRubricLoader _rubricLoader;
    private readonly IWebResearchParityEvalReportWriter _reportWriter;

    public WebResearchParityEvalService(
        IEvalCorpusLoader? corpusLoader = null,
        IEvalRunnerV2? runner = null,
        IWebResearchParityEvalRubricLoader? rubricLoader = null,
        IWebResearchParityEvalReportWriter? reportWriter = null)
    {
        _corpusLoader = corpusLoader ?? new EvalCorpusLoader();
        _runner = runner ?? new EvalRunnerV2(_corpusLoader);
        _rubricLoader = rubricLoader ?? new WebResearchParityEvalRubricLoader();
        _reportWriter = reportWriter ?? new WebResearchParityEvalReportWriter();
    }

    public async Task<WebResearchParityEvalPackage> PrepareAsync(
        string packageRoot,
        WebResearchParityEvalOptions options,
        CancellationToken ct)
    {
        var paths = ResolvePaths(packageRoot);
        var rubric = await _rubricLoader.LoadAsync(paths.RubricPath, ct).ConfigureAwait(false);
        var seedScenarios = await _corpusLoader.LoadAsync(paths.CorpusPath, ct).ConfigureAwait(false);
        var preparedRuns = Math.Max(rubric.MinimumPreparedRuns, options.MinPreparedRuns);
        var prepared = await _runner
            .PrepareRunAsync(
                seedScenarios,
                new EvalRunOptions(preparedRuns, options.UseRealModel, options.Seed),
                ct)
            .ConfigureAwait(false);
        var summary = BuildSummary(paths, rubric, seedScenarios, prepared);
        return new WebResearchParityEvalPackage(paths, rubric, seedScenarios, prepared, summary);
    }

    public async Task<WebResearchParityEvalExportResult> ExportAsync(
        string packageRoot,
        string outputDirectory,
        WebResearchParityEvalOptions options,
        CancellationToken ct)
    {
        var package = await PrepareAsync(packageRoot, options, ct).ConfigureAwait(false);
        return await _reportWriter.WriteAsync(package, outputDirectory, ct).ConfigureAwait(false);
    }

    private static WebResearchParityEvalSummary BuildSummary(
        WebResearchParityEvalPackagePaths paths,
        WebResearchParityEvalRubric rubric,
        IReadOnlyList<EvalScenarioDefinition> seedScenarios,
        EvalPreparedRun prepared)
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
        var providerFixtureCount = CountFixtures(paths.ProviderFixturesPath);
        var pageFixtureCount = CountFixtures(paths.PageFixturesPath);
        var missingKinds = rubric.RequiredKinds
            .Except(kindDistribution.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingLabels = rubric.RequiredLabels
            .Except(labelDistribution.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingMetrics = rubric.RequiredMetrics
            .Except(GetMetricCatalog(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var alerts = new List<string>();

        if (seedScenarios.Count < rubric.MinimumSeedScenarios)
        {
            alerts.Add($"Seed scenario count {seedScenarios.Count} is below rubric minimum {rubric.MinimumSeedScenarios}.");
        }

        if (prepared.Scenarios.Count < rubric.MinimumPreparedRuns)
        {
            alerts.Add($"Prepared run count {prepared.Scenarios.Count} is below rubric minimum {rubric.MinimumPreparedRuns}.");
        }

        if (providerFixtureCount < rubric.MinimumProviderFixtures)
        {
            alerts.Add($"Provider fixture count {providerFixtureCount} is below rubric minimum {rubric.MinimumProviderFixtures}.");
        }

        if (pageFixtureCount < rubric.MinimumPageFixtures)
        {
            alerts.Add($"Page fixture count {pageFixtureCount} is below rubric minimum {rubric.MinimumPageFixtures}.");
        }

        if (missingKinds.Length > 0)
        {
            alerts.Add($"Missing required scenario classes: {string.Join(", ", missingKinds)}.");
        }

        if (missingLabels.Length > 0)
        {
            alerts.Add($"Missing required labels: {string.Join(", ", missingLabels)}.");
        }

        if (missingMetrics.Length > 0)
        {
            alerts.Add($"Missing required metrics: {string.Join(", ", missingMetrics)}.");
        }

        return new WebResearchParityEvalSummary(
            SeedScenarioCount: seedScenarios.Count,
            PreparedScenarioCount: prepared.Scenarios.Count,
            EndToEndScenarioCount: prepared.Summary.EndToEndScenarios,
            EndToEndRatio: prepared.Summary.EndToEndRatio,
            ProviderFixtureCount: providerFixtureCount,
            PageFixtureCount: pageFixtureCount,
            GateStatus: alerts.Count == 0 ? "pass" : "fail",
            LanguageDistribution: languageDistribution,
            KindDistribution: kindDistribution,
            LabelDistribution: labelDistribution,
            MissingKinds: missingKinds,
            MissingLabels: missingLabels,
            MissingMetrics: missingMetrics,
            Alerts: alerts);
    }

    private static WebResearchParityEvalPackagePaths ResolvePaths(string packageRoot)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            throw new ArgumentException("Package root must not be empty.", nameof(packageRoot));
        }

        var resolvedRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(resolvedRoot))
        {
            throw new DirectoryNotFoundException($"Web-research parity eval package directory was not found: {resolvedRoot}");
        }

        return new WebResearchParityEvalPackagePaths(
            resolvedRoot,
            Path.Combine(resolvedRoot, "corpus.jsonl"),
            Path.Combine(resolvedRoot, "rubric.json"),
            Path.Combine(resolvedRoot, "fixtures", "providers"),
            Path.Combine(resolvedRoot, "fixtures", "pages"));
    }

    private static int CountFixtures(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Count();
    }

    private static IReadOnlyList<string> GetMetricCatalog()
    {
        return new[]
        {
            "helper_web_research_turns_total",
            "helper_web_research_live_turns_total",
            "helper_web_research_cached_turns_total",
            "helper_web_research_avg_queries_per_turn",
            "helper_web_research_avg_fetched_pages_per_turn",
            "helper_web_research_avg_passages_per_turn",
            "helper_web_research_blocked_fetch_total",
            "helper_web_research_blocked_fetch_rate",
            "helper_web_research_stale_disclosure_total",
            "helper_web_research_stale_disclosure_rate"
        };
    }
}

