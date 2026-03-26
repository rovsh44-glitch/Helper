using System.Text.Json;
using System.Text.Json.Serialization;
using Helper.GeneratedArtifactValidation.Contracts;

namespace Helper.GeneratedArtifactValidation.Core;

public static class ValidationJson
{
    public static JsonSerializerOptions Default { get; } = BuildDefaultOptions();

    public static async Task<ArtifactFixtureManifest?> LoadArtifactFixtureManifestAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ArtifactFixtureManifest>(stream, Default, ct);
    }

    public static async Task<SwarmBlueprint?> LoadBlueprintAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SwarmBlueprint>(stream, Default, ct);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Default);

    private static JsonSerializerOptions BuildDefaultOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(new FileRoleJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

