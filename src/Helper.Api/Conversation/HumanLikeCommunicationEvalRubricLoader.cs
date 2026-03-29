using System.Text.Json;

namespace Helper.Api.Conversation;

public interface IHumanLikeCommunicationEvalRubricLoader
{
    Task<HumanLikeCommunicationEvalRubric> LoadAsync(string rubricPath, CancellationToken ct);
}

public sealed class HumanLikeCommunicationEvalRubricLoader : IHumanLikeCommunicationEvalRubricLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<HumanLikeCommunicationEvalRubric> LoadAsync(string rubricPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rubricPath))
        {
            throw new ArgumentException("Rubric path must not be empty.", nameof(rubricPath));
        }

        if (!File.Exists(rubricPath))
        {
            throw new FileNotFoundException($"Rubric file not found: {rubricPath}", rubricPath);
        }

        await using var stream = File.OpenRead(rubricPath);
        var rubric = await JsonSerializer.DeserializeAsync<HumanLikeCommunicationEvalRubric>(stream, JsonOptions, ct).ConfigureAwait(false);
        if (rubric is null)
        {
            throw new InvalidDataException($"Failed to parse human-like communication rubric: {rubricPath}");
        }

        if (string.IsNullOrWhiteSpace(rubric.Version))
        {
            throw new InvalidDataException("Human-like communication rubric version is missing.");
        }

        if (rubric.MinimumSeedScenarios <= 0 || rubric.MinimumPreparedRuns <= 0)
        {
            throw new InvalidDataException("Human-like communication rubric minimum scenario counts must be positive.");
        }

        if (rubric.Dimensions.Count == 0)
        {
            throw new InvalidDataException("Human-like communication rubric must define at least one scoring dimension.");
        }

        return rubric with
        {
            RequiredKinds = NormalizeValues(rubric.RequiredKinds),
            RequiredLabels = NormalizeValues(rubric.RequiredLabels),
            Dimensions = rubric.Dimensions
                .Select(static dimension => new HumanLikeCommunicationRubricDimension(
                    dimension.Key.Trim(),
                    dimension.DisplayName.Trim(),
                    dimension.Description.Trim(),
                    dimension.ScoringGuidance.Trim()))
                .ToArray()
        };
    }

    private static IReadOnlyList<string> NormalizeValues(IReadOnlyList<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

