using System.Text.Json;

namespace Helper.Api.Conversation;

public sealed record EvalScenarioDefinition(
    string Id,
    string Language,
    string Kind,
    string Prompt,
    bool EndToEnd,
    string? ExpectedSignal = null,
    string? ExpectedContains = null,
    string[]? Labels = null);

public sealed record EvalRunOptions(int MinScenarioRuns = 1000, bool UseRealModel = false, int Seed = 42);

public sealed record EvalCorpusSummary(
    int TotalScenarios,
    int EndToEndScenarios,
    double EndToEndRatio,
    IReadOnlyDictionary<string, int> LanguageDistribution,
    bool UseRealModel);

public sealed record EvalPreparedRun(IReadOnlyList<EvalScenarioDefinition> Scenarios, EvalCorpusSummary Summary);

public interface IEvalCorpusLoader
{
    Task<IReadOnlyList<EvalScenarioDefinition>> LoadAsync(string datasetPath, CancellationToken ct);
}

public interface IEvalRunnerV2
{
    Task<EvalPreparedRun> PrepareRunAsync(string datasetPath, EvalRunOptions options, CancellationToken ct);
    Task<EvalPreparedRun> PrepareRunAsync(IReadOnlyList<EvalScenarioDefinition> seedScenarios, EvalRunOptions options, CancellationToken ct);
}

public sealed class EvalCorpusLoader : IEvalCorpusLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<EvalScenarioDefinition>> LoadAsync(string datasetPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(datasetPath))
        {
            throw new ArgumentException("Dataset path must not be empty.", nameof(datasetPath));
        }

        if (!File.Exists(datasetPath))
        {
            throw new FileNotFoundException($"Dataset file not found: {datasetPath}", datasetPath);
        }

        if (!datasetPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only .jsonl datasets are supported by EvalCorpusLoader.");
        }

        var scenarios = new List<EvalScenarioDefinition>();
        await using var stream = File.OpenRead(datasetPath);
        using var reader = new StreamReader(stream);
        var lineNo = 0;
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var scenario = JsonSerializer.Deserialize<EvalScenarioDefinition>(trimmed, JsonOptions);
            if (scenario is null)
            {
                throw new InvalidDataException($"Failed to parse eval dataset line {lineNo}.");
            }

            if (string.IsNullOrWhiteSpace(scenario.Id) ||
                string.IsNullOrWhiteSpace(scenario.Language) ||
                string.IsNullOrWhiteSpace(scenario.Kind) ||
                string.IsNullOrWhiteSpace(scenario.Prompt))
            {
                throw new InvalidDataException($"Invalid eval scenario at line {lineNo}: required fields are missing.");
            }

            scenarios.Add(scenario with
            {
                Id = scenario.Id.Trim(),
                Language = scenario.Language.Trim().ToLowerInvariant(),
                Kind = scenario.Kind.Trim().ToLowerInvariant(),
                Prompt = scenario.Prompt.Trim(),
                ExpectedSignal = scenario.ExpectedSignal?.Trim(),
                ExpectedContains = scenario.ExpectedContains?.Trim(),
                Labels = scenario.Labels?
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => label.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        }

        if (scenarios.Count == 0)
        {
            throw new InvalidDataException($"Dataset '{datasetPath}' does not contain any scenarios.");
        }

        return scenarios;
    }
}

public sealed class EvalRunnerV2 : IEvalRunnerV2
{
    private readonly IEvalCorpusLoader _loader;

    public EvalRunnerV2(IEvalCorpusLoader? loader = null)
    {
        _loader = loader ?? new EvalCorpusLoader();
    }

    public async Task<EvalPreparedRun> PrepareRunAsync(string datasetPath, EvalRunOptions options, CancellationToken ct)
    {
        var seedScenarios = await _loader.LoadAsync(datasetPath, ct);
        return await PrepareRunAsync(seedScenarios, options, ct).ConfigureAwait(false);
    }

    public Task<EvalPreparedRun> PrepareRunAsync(IReadOnlyList<EvalScenarioDefinition> seedScenarios, EvalRunOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var expanded = Expand(seedScenarios, Math.Max(1, options.MinScenarioRuns), options.Seed);
        var endToEndScenarios = expanded.Count(x => x.EndToEnd);
        var languageDistribution = expanded
            .GroupBy(x => x.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var summary = new EvalCorpusSummary(
            expanded.Count,
            endToEndScenarios,
            expanded.Count == 0 ? 0 : endToEndScenarios / (double)expanded.Count,
            languageDistribution,
            options.UseRealModel);

        return Task.FromResult(new EvalPreparedRun(expanded, summary));
    }

    private static IReadOnlyList<EvalScenarioDefinition> Expand(
        IReadOnlyList<EvalScenarioDefinition> seedScenarios,
        int minScenarioRuns,
        int seed)
    {
        var rng = new Random(seed);
        var expanded = new List<EvalScenarioDefinition>(Math.Max(minScenarioRuns, seedScenarios.Count));
        var index = 0;

        while (expanded.Count < minScenarioRuns)
        {
            var baseScenario = seedScenarios[index % seedScenarios.Count];
            var variant = expanded.Count / seedScenarios.Count;
            var expandedScenario = baseScenario with
            {
                Id = $"{baseScenario.Id}::run{variant:D3}",
                Prompt = baseScenario.Prompt,
            };
            expanded.Add(expandedScenario);
            index++;
        }

        for (var i = expanded.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (expanded[i], expanded[j]) = (expanded[j], expanded[i]);
        }

        return expanded;
    }
}

