using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Backend.Providers;

public sealed class ProviderProfileActivationService : IProviderProfileActivationService
{
    private static readonly TimeSpan CatalogRefreshTimeout = TimeSpan.FromSeconds(12);
    private readonly IProviderProfileCatalog _catalog;
    private readonly IProviderProfileStore _store;
    private readonly IProviderProfileResolver _resolver;
    private readonly IModelGateway _modelGateway;

    public ProviderProfileActivationService(
        IProviderProfileCatalog catalog,
        IProviderProfileStore store,
        IProviderProfileResolver resolver,
        IModelGateway modelGateway)
    {
        _catalog = catalog;
        _store = store;
        _resolver = resolver;
        _modelGateway = modelGateway;
    }

    public async Task<ProviderActivationResult> ActivateAsync(string profileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return new ProviderActivationResult(false, null, Array.Empty<string>(), new[] { "Profile id is required." });
        }

        var profile = _catalog.GetById(profileId);
        if (profile is null)
        {
            return new ProviderActivationResult(false, null, Array.Empty<string>(), new[] { $"Provider profile '{profileId}' was not found." });
        }

        if (!profile.Profile.Enabled)
        {
            return new ProviderActivationResult(false, profile.Profile.Id, Array.Empty<string>(), new[] { $"Provider profile '{profile.Profile.Id}' is disabled." });
        }

        if (!profile.Validation.IsValid)
        {
            return new ProviderActivationResult(false, profile.Profile.Id, Array.Empty<string>(), profile.Validation.Alerts);
        }

        _store.SaveActiveProfileId(profile.Profile.Id);
        var appliedProfileId = _resolver.ApplyToRuntime();
        var warnings = profile.Validation.Warnings.ToList();
        try
        {
            using var refreshCts = CreateBoundedRefreshScope(ct);
            await _modelGateway.DiscoverAsync(refreshCts.Token);
        }
        catch (Exception ex)
        {
            warnings.Add($"Model catalog refresh failed after activation: {ex.Message}");
        }

        return new ProviderActivationResult(
            Success: true,
            ActiveProfileId: appliedProfileId ?? profile.Profile.Id,
            ReasonCodes: new[] { "profile_activated", $"profile:{profile.Profile.Id}" },
            Warnings: warnings);
    }

    public async Task<ProviderActivationResult> EnsureRuntimeSynchronizedAsync(CancellationToken ct)
    {
        var appliedProfileId = _resolver.ApplyToRuntime();
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(appliedProfileId))
        {
            try
            {
                using var refreshCts = CreateBoundedRefreshScope(ct);
                await _modelGateway.DiscoverAsync(refreshCts.Token);
            }
            catch (Exception ex)
            {
                warnings.Add($"Model catalog refresh failed during runtime synchronization: {ex.Message}");
            }
        }

        return new ProviderActivationResult(
            Success: !string.IsNullOrWhiteSpace(appliedProfileId),
            ActiveProfileId: appliedProfileId,
            ReasonCodes: string.IsNullOrWhiteSpace(appliedProfileId)
                ? Array.Empty<string>()
                : new[] { "runtime_synchronized", $"profile:{appliedProfileId}" },
            Warnings: warnings);
    }

    private static CancellationTokenSource CreateBoundedRefreshScope(CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(CatalogRefreshTimeout);
        return linked;
    }
}
