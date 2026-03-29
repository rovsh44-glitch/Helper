using System.Text.Json;

namespace Helper.Api.Conversation;

public interface IWebResearchParityEvalRubricLoader
{
    Task<WebResearchParityEvalRubric> LoadAsync(string rubricPath, CancellationToken ct);
}

public sealed class WebResearchParityEvalRubricLoader : IWebResearchParityEvalRubricLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<WebResearchParityEvalRubric> LoadAsync(string rubricPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rubricPath))
        {
            throw new ArgumentException("Rubric path must not be empty.", nameof(rubricPath));
        }

        if (!File.Exists(rubricPath))
        {
            throw new FileNotFoundException($"Web-research parity rubric file was not found: {rubricPath}", rubricPath);
        }

        await using var stream = File.OpenRead(rubricPath);
        var rubric = await JsonSerializer.DeserializeAsync<WebResearchParityEvalRubric>(stream, JsonOptions, ct).ConfigureAwait(false);
        if (rubric is null)
        {
            throw new InvalidDataException($"Failed to deserialize web-research parity rubric: {rubricPath}");
        }

        return rubric with
        {
            RequiredKinds = NormalizeValues(rubric.RequiredKinds),
            RequiredLabels = NormalizeValues(rubric.RequiredLabels),
            RequiredMetrics = NormalizeValues(rubric.RequiredMetrics)
        };
    }

    private static IReadOnlyList<string> NormalizeValues(IReadOnlyList<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

