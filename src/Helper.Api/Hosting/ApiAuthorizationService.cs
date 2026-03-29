namespace Helper.Api.Hosting;

public interface IApiAuthorizationService
{
    bool TryAuthorize(string? apiKey, out ApiPrincipal principal);
    bool HasScope(HttpContext context, string scope);
    IResult? EnsureScope(HttpContext context, string scope);
    string? ResolveRequiredScope(string path, string method);
}

public sealed record ApiPrincipal(string KeyId, string Role, IReadOnlySet<string> Scopes, string PrincipalType = "m2m");

public sealed class ApiAuthorizationService : IApiAuthorizationService
{
    public const string PrincipalContextKey = "ApiPrincipal";

    private readonly IAuthKeysStore _keysStore;
    private readonly IApiSessionTokenService _sessionTokens;
    private readonly bool _authV2Enabled;
    private static readonly string[] AdminScopes =
    {
        "chat:read",
        "chat:write",
        "tools:execute",
        "fs:write",
        "build:run",
        "evolution:control",
        "metrics:read",
        "feedback:write",
        "auth:manage"
    };

    public ApiAuthorizationService(
        ApiRuntimeConfig runtimeConfig,
        IApiSessionTokenService sessionTokens,
        IAuthKeysStore keysStore,
        IFeatureFlags? featureFlags = null)
    {
        _sessionTokens = sessionTokens;
        _keysStore = keysStore;
        _authV2Enabled = featureFlags?.AuthV2Enabled ?? ReadFlag("HELPER_FF_AUTH_V2", true);
        _keysStore.EnsurePrimaryKey(runtimeConfig.ApiKey, AdminScopes);
    }

    public bool TryAuthorize(string? apiKey, out ApiPrincipal principal)
    {
        principal = default!;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        if (_keysStore.TryAuthorize(apiKey, out principal))
        {
            return true;
        }

        if (_authV2Enabled && _sessionTokens.TryValidate(apiKey, out principal, out _, out _))
        {
            return true;
        }

        return false;
    }

    public bool HasScope(HttpContext context, string scope)
    {
        if (!context.Items.TryGetValue(PrincipalContextKey, out var value) || value is not ApiPrincipal principal)
        {
            return false;
        }

        return principal.Scopes.Contains(scope);
    }

    public IResult? EnsureScope(HttpContext context, string scope)
    {
        if (HasScope(context, scope))
        {
            return null;
        }

        return Results.Json(new { success = false, error = $"Forbidden. Missing scope '{scope}'." }, statusCode: StatusCodes.Status403Forbidden);
    }

    public string? ResolveRequiredScope(string path, string method)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.ToLowerInvariant();
        var verb = method.ToUpperInvariant();

        if (normalized is "/api/health" or "/api/handshake" or "/api/openapi.json")
        {
            return null;
        }

        if (normalized is "/api/auth/session")
        {
            return null;
        }

        if (normalized.StartsWith("/api/auth/keys"))
        {
            return "auth:manage";
        }

        if (normalized.StartsWith("/hubs/helper"))
        {
            return "chat:read";
        }

        if (normalized.StartsWith("/api/control-plane") || normalized.StartsWith("/api/runtime") || normalized.StartsWith("/api/capabilities"))
        {
            return "metrics:read";
        }

        if (normalized.StartsWith("/api/metrics"))
        {
            return "metrics:read";
        }

        if (normalized.StartsWith("/api/fs/write"))
        {
            return "fs:write";
        }

        if (normalized.StartsWith("/api/workspace"))
        {
            return "fs:write";
        }

        if (normalized.StartsWith("/api/build"))
        {
            return "build:run";
        }

        if (normalized.StartsWith("/api/evolution") || normalized.StartsWith("/api/indexing"))
        {
            return "evolution:control";
        }

        if (normalized.StartsWith("/api/goals"))
        {
            return verb == HttpMethods.Get ? "chat:read" : "chat:write";
        }

        if (normalized.Contains("/feedback"))
        {
            return "feedback:write";
        }

        if (normalized.StartsWith("/api/chat"))
        {
            return verb == HttpMethods.Get ? "chat:read" : "chat:write";
        }

        return "tools:execute";
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }
}

