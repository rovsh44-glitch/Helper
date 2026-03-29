using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Hosting;

public interface IApiSessionTokenService
{
    SessionTokenIssueResult Issue(ApiPrincipal principal, TimeSpan ttl);
    bool TryValidate(string token, out ApiPrincipal principal, out DateTimeOffset expiresAtUtc, out string tokenId);
}

public sealed record SessionTokenIssueResult(string AccessToken, DateTimeOffset ExpiresAtUtc, string TokenId);

internal sealed record SessionTokenPayload(
    string Sid,
    string Kid,
    string Role,
    string PrincipalType,
    string[] Scopes,
    long IatUnix,
    long ExpUnix);

public sealed class ApiSessionTokenService : IApiSessionTokenService
{
    private readonly byte[] _signingKey;

    private readonly IBackendOptionsCatalog? _options;

    public ApiSessionTokenService(ApiRuntimeConfig runtimeConfig, IBackendOptionsCatalog? options = null)
    {
        _options = options;
        var rawSecret = Environment.GetEnvironmentVariable(BackendOptionsCatalog.SessionSigningKeyEnvVar);
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            rawSecret = $"{runtimeConfig.ApiKey}:helper-session-signing";
        }

        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret));
    }

    public SessionTokenIssueResult Issue(ApiPrincipal principal, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        var boundedTtl = NormalizeTtl(ttl, _options);
        var expiresAt = now.Add(boundedTtl);
        var payload = new SessionTokenPayload(
            Sid: Guid.NewGuid().ToString("N"),
            Kid: principal.KeyId,
            Role: principal.Role,
            PrincipalType: principal.PrincipalType,
            Scopes: principal.Scopes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            IatUnix: now.ToUnixTimeSeconds(),
            ExpUnix: expiresAt.ToUnixTimeSeconds());

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var signature = Sign(payloadBytes);

        var token = $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
        return new SessionTokenIssueResult(token, expiresAt, payload.Sid);
    }

    public bool TryValidate(string token, out ApiPrincipal principal, out DateTimeOffset expiresAtUtc, out string tokenId)
    {
        principal = default!;
        expiresAtUtc = default;
        tokenId = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!TryBase64UrlDecode(parts[0], out var payloadBytes) || !TryBase64UrlDecode(parts[1], out var providedSignature))
        {
            return false;
        }

        var expectedSignature = Sign(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return false;
        }

        SessionTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SessionTokenPayload>(payloadBytes);
        }
        catch
        {
            return false;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Kid) || payload.Scopes.Length == 0)
        {
            return false;
        }

        expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(payload.ExpUnix);
        if (expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        tokenId = payload.Sid;
        principal = new ApiPrincipal(
            payload.Kid,
            string.IsNullOrWhiteSpace(payload.Role) ? "session" : payload.Role,
            new HashSet<string>(payload.Scopes, StringComparer.OrdinalIgnoreCase),
            string.IsNullOrWhiteSpace(payload.PrincipalType) ? "session" : payload.PrincipalType);
        return true;
    }

    private byte[] Sign(byte[] payloadBytes)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(payloadBytes);
    }

    private static TimeSpan NormalizeTtl(TimeSpan ttl, IBackendOptionsCatalog? options)
    {
        var min = TimeSpan.FromMinutes(options?.Auth.MinSessionTtlMinutes ?? 2);
        var max = TimeSpan.FromMinutes(options?.Auth.MaxSessionTtlMinutes ?? 480);
        if (ttl < min) return min;
        if (ttl > max) return max;
        return ttl;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding != 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

