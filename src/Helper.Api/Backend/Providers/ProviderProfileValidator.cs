namespace Helper.Api.Backend.Providers;

public sealed class ProviderProfileValidator : IProviderProfileValidator
{
    public ProviderProfileValidationResult Validate(ProviderProfile profile, IReadOnlyCollection<string>? knownIds = null)
    {
        var alerts = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            alerts.Add("Profile id is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            alerts.Add($"Profile '{profile.Id}' must have a display name.");
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl) ||
            !Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            alerts.Add($"Profile '{profile.Id}' must define an absolute http/https base URL.");
        }
        else
        {
            if (profile.IsLocal && !IsLocalHost(baseUri))
            {
                warnings.Add($"Profile '{profile.Id}' is marked local but base URL host '{baseUri.Host}' is not loopback.");
            }

            if (!profile.IsLocal && IsLocalHost(baseUri))
            {
                warnings.Add($"Profile '{profile.Id}' is marked remote but base URL host '{baseUri.Host}' is loopback.");
            }

            if (profile.TransportKind == ProviderTransportKind.OpenAiCompatible &&
                !baseUri.AbsolutePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(baseUri.AbsolutePath, "/v1/", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"OpenAI-compatible profile '{profile.Id}' usually expects a '/v1' base path.");
            }
        }

        if (profile.ModelBindings.Count == 0)
        {
            alerts.Add($"Profile '{profile.Id}' must define at least one model binding.");
        }

        if (profile.ModelBindings
            .GroupBy(binding => binding.ModelClass)
            .Any(group => group.Count() > 1))
        {
            alerts.Add($"Profile '{profile.Id}' defines duplicate model-class bindings.");
        }

        if (knownIds is not null &&
            !string.IsNullOrWhiteSpace(profile.Id) &&
            knownIds.Count(id => string.Equals(id, profile.Id, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            alerts.Add($"Profile '{profile.Id}' is duplicated.");
        }

        if (profile.TransportKind == ProviderTransportKind.OpenAiCompatible &&
            (profile.Credential is null || string.IsNullOrWhiteSpace(profile.Credential.ApiKeyEnvVar)))
        {
            alerts.Add($"OpenAI-compatible profile '{profile.Id}' must declare an API key environment variable.");
        }

        if (profile.Credential?.Required == true &&
            !string.IsNullOrWhiteSpace(profile.Credential.ApiKeyEnvVar))
        {
            var rawSecret = Environment.GetEnvironmentVariable(profile.Credential.ApiKeyEnvVar);
            if (string.IsNullOrWhiteSpace(rawSecret))
            {
                warnings.Add($"Credential env '{profile.Credential.ApiKeyEnvVar}' is not configured.");
            }
            else if (LooksLikePlaceholder(rawSecret))
            {
                warnings.Add($"Credential env '{profile.Credential.ApiKeyEnvVar}' looks like a placeholder.");
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.PreferredReasoningEffort) &&
            profile.PreferredReasoningEffort is not ("fast" or "balanced" or "deep"))
        {
            alerts.Add($"Profile '{profile.Id}' uses unsupported preferred reasoning effort '{profile.PreferredReasoningEffort}'.");
        }

        return new ProviderProfileValidationResult(
            alerts.Count == 0,
            alerts,
            warnings);
    }

    private static bool LooksLikePlaceholder(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("INSERT_", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("<", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalHost(Uri uri)
    {
        return uri.IsLoopback ||
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }
}
