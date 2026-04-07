using System.Text.Json;
using Helper.Api.Hosting;

namespace Helper.Api.Backend.Providers;

public sealed class ProviderProfileStore : IProviderProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _profilesPath;
    private readonly string _activeProfilePath;

    public ProviderProfileStore(ApiRuntimeConfig runtimeConfig)
    {
        var providerRoot = Path.Combine(runtimeConfig.DataRoot, "provider_profiles");
        Directory.CreateDirectory(providerRoot);
        _profilesPath = Path.Combine(providerRoot, "profiles.json");
        _activeProfilePath = Path.Combine(providerRoot, "active_profile.json");
    }

    public IReadOnlyList<ProviderProfile> LoadProfiles()
    {
        if (!File.Exists(_profilesPath))
        {
            return Array.Empty<ProviderProfile>();
        }

        try
        {
            var json = File.ReadAllText(_profilesPath);
            var profiles = JsonSerializer.Deserialize<List<ProviderProfile>>(json, SerializerOptions);
            return profiles is { Count: > 0 } ? profiles : Array.Empty<ProviderProfile>();
        }
        catch
        {
            return Array.Empty<ProviderProfile>();
        }
    }

    public void SaveProfiles(IReadOnlyList<ProviderProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_profilesPath)!);
        File.WriteAllText(_profilesPath, JsonSerializer.Serialize(profiles, SerializerOptions));
    }

    public string? LoadActiveProfileId()
    {
        if (!File.Exists(_activeProfilePath))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ActiveProfileRecord>(File.ReadAllText(_activeProfilePath), SerializerOptions);
            return string.IsNullOrWhiteSpace(payload?.ProfileId) ? null : payload.ProfileId.Trim();
        }
        catch
        {
            return null;
        }
    }

    public void SaveActiveProfileId(string? profileId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_activeProfilePath)!);
        File.WriteAllText(
            _activeProfilePath,
            JsonSerializer.Serialize(new ActiveProfileRecord(string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim()), SerializerOptions));
    }

    private sealed record ActiveProfileRecord(string? ProfileId);
}
