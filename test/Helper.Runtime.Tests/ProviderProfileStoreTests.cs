using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Api.Hosting;
using Helper.Testing;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderProfileStoreTests
{
    [Fact]
    public void SaveAndLoadProfiles_RoundTrips_BackendOwnedStorage()
    {
        using var temp = new TempDirectoryScope();
        var config = new ApiRuntimeConfig(
            temp.Path,
            temp.Path,
            Path.Combine(temp.Path, "projects"),
            Path.Combine(temp.Path, "library"),
            Path.Combine(temp.Path, "logs"),
            Path.Combine(temp.Path, "templates"),
            "test-key");
        var store = new ProviderProfileStore(config);
        var profiles = new[]
        {
            new ProviderProfile(
                "custom_profile",
                "Custom",
                ProviderKind.Ollama,
                ProviderTransportKind.Ollama,
                "http://localhost:11434",
                Enabled: true,
                IsBuiltIn: false,
                IsLocal: true,
                ProviderTrustMode.Local,
                new[] { ProviderWorkloadGoal.LocalFast },
                new[] { new ProviderModelClassBinding(HelperModelClass.Fast, "command-r7b:7b") })
        };

        store.SaveProfiles(profiles);
        store.SaveActiveProfileId("custom_profile");

        var loadedProfiles = store.LoadProfiles();
        var activeId = store.LoadActiveProfileId();

        var loaded = Assert.Single(loadedProfiles);
        Assert.Equal("custom_profile", loaded.Id);
        Assert.Equal("command-r7b:7b", Assert.Single(loaded.ModelBindings).ModelName);
        Assert.Equal("custom_profile", activeId);
    }

    [Fact]
    public void LoadProfiles_WhenFilesMissing_ReturnsEmptyAndNull()
    {
        using var temp = new TempDirectoryScope();
        var config = new ApiRuntimeConfig(
            temp.Path,
            temp.Path,
            Path.Combine(temp.Path, "projects"),
            Path.Combine(temp.Path, "library"),
            Path.Combine(temp.Path, "logs"),
            Path.Combine(temp.Path, "templates"),
            "test-key");
        var store = new ProviderProfileStore(config);

        Assert.Empty(store.LoadProfiles());
        Assert.Null(store.LoadActiveProfileId());
    }
}
