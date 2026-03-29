using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Helper.Api.Hosting;

public interface IAuthKeysStore
{
    void EnsurePrimaryKey(string rawApiKey, IReadOnlyCollection<string> adminScopes);
    bool TryAuthorize(string presentedKey, out ApiPrincipal principal);
    AuthKeyIssueResult RotateMachineKey(AuthKeyRotationRequest request);
    bool RevokeKey(string keyId, string? reason);
    IReadOnlyList<AuthKeyMetadata> ListKeys(bool includeRevoked);
}

public sealed record AuthKeyRotationRequest(
    string? KeyId = null,
    string? Role = null,
    IReadOnlyList<string>? Scopes = null,
    int? TtlDays = null);

public sealed record AuthKeyIssueResult(
    string KeyId,
    string ApiKey,
    string Role,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string PrincipalType);

public sealed record AuthKeyMetadata(
    string KeyId,
    string Role,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsRevoked,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason,
    string PrincipalType,
    bool IsSystemManaged);

public sealed class AuthKeysStore : IAuthKeysStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AuthKeyEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AuthKeyEntry> _byHash = new(StringComparer.Ordinal);
    private readonly string _path;

    public AuthKeysStore(ApiRuntimeConfig runtimeConfig)
    {
        _path = ResolvePath(runtimeConfig);
        LoadFromDisk();
        LoadEnvironmentKeys();
    }

    public void EnsurePrimaryKey(string rawApiKey, IReadOnlyCollection<string> adminScopes)
    {
        if (string.IsNullOrWhiteSpace(rawApiKey))
        {
            return;
        }

        lock (_sync)
        {
            var hash = ComputeHash(rawApiKey);
            UpsertUnsafe(new AuthKeyEntry(
                KeyId: "primary",
                KeyHash: hash,
                Role: "admin",
                Scopes: adminScopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                RevokedAtUtc: null,
                RevokedReason: null,
                PrincipalType: "m2m",
                IsSystemManaged: true));
            SaveUnsafe();
        }
    }

    public bool TryAuthorize(string presentedKey, out ApiPrincipal principal)
    {
        principal = default!;
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            return false;
        }

        var hash = ComputeHash(presentedKey);
        lock (_sync)
        {
            if (!_byHash.TryGetValue(hash, out var entry))
            {
                return false;
            }

            if (!IsEntryActive(entry, DateTimeOffset.UtcNow))
            {
                return false;
            }

            principal = new ApiPrincipal(
                entry.KeyId,
                entry.Role,
                new HashSet<string>(entry.Scopes, StringComparer.OrdinalIgnoreCase),
                entry.PrincipalType);
            return true;
        }
    }

    public AuthKeyIssueResult RotateMachineKey(AuthKeyRotationRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var keyId = string.IsNullOrWhiteSpace(request.KeyId)
            ? $"m2m-{Guid.NewGuid():N}"[..12]
            : request.KeyId!.Trim();
        var role = string.IsNullOrWhiteSpace(request.Role) ? "integration" : request.Role!.Trim().ToLowerInvariant();
        var scopes = NormalizeScopes(request.Scopes);
        var ttlDays = request.TtlDays.HasValue ? Math.Clamp(request.TtlDays.Value, 1, 365) : 90;
        var expiresAt = now.AddDays(ttlDays);
        var rawKey = GenerateApiKey();
        var hash = ComputeHash(rawKey);

        lock (_sync)
        {
            if (_byId.TryGetValue(keyId, out var existing) && !existing.IsSystemManaged)
            {
                if (!string.IsNullOrWhiteSpace(existing.KeyHash))
                {
                    _byHash.Remove(existing.KeyHash);
                }
            }

            var entry = new AuthKeyEntry(
                keyId,
                hash,
                role,
                scopes,
                now,
                expiresAt,
                null,
                null,
                "m2m",
                false);
            UpsertUnsafe(entry);
            SaveUnsafe();
        }

        return new AuthKeyIssueResult(keyId, rawKey, role, scopes, now, expiresAt, "m2m");
    }

    public bool RevokeKey(string keyId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_byId.TryGetValue(keyId, out var entry))
            {
                return false;
            }

            if (entry.RevokedAtUtc.HasValue)
            {
                return true;
            }

            var revoked = entry with
            {
                RevokedAtUtc = DateTimeOffset.UtcNow,
                RevokedReason = string.IsNullOrWhiteSpace(reason) ? "revoked" : reason!.Trim()
            };
            UpsertUnsafe(revoked);
            SaveUnsafe();
            return true;
        }
    }

    public IReadOnlyList<AuthKeyMetadata> ListKeys(bool includeRevoked)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var items = _byId.Values
                .Where(entry => includeRevoked || IsEntryActive(entry, now))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(entry => new AuthKeyMetadata(
                    entry.KeyId,
                    entry.Role,
                    entry.Scopes.ToArray(),
                    entry.CreatedAtUtc,
                    entry.ExpiresAtUtc,
                    entry.RevokedAtUtc.HasValue,
                    entry.RevokedAtUtc,
                    entry.RevokedReason,
                    entry.PrincipalType,
                    entry.IsSystemManaged))
                .ToArray();
            return items;
        }
    }

    private void LoadFromDisk()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var snapshot = JsonSerializer.Deserialize<AuthKeysSnapshot>(json);
                if (snapshot?.Keys is null)
                {
                    return;
                }

                foreach (var dto in snapshot.Keys)
                {
                    if (string.IsNullOrWhiteSpace(dto.KeyId) || string.IsNullOrWhiteSpace(dto.KeyHash))
                    {
                        continue;
                    }

                    var entry = new AuthKeyEntry(
                        dto.KeyId,
                        dto.KeyHash,
                        string.IsNullOrWhiteSpace(dto.Role) ? "integration" : dto.Role!,
                        NormalizeScopes(dto.Scopes),
                        dto.CreatedAtUtc,
                        dto.ExpiresAtUtc,
                        dto.RevokedAtUtc,
                        dto.RevokedReason,
                        string.IsNullOrWhiteSpace(dto.PrincipalType) ? "m2m" : dto.PrincipalType!,
                        dto.IsSystemManaged);
                    UpsertUnsafe(entry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthKeys] Failed to load {_path}: {ex.Message}");
            }
        }
    }

    private void LoadEnvironmentKeys()
    {
        var json = Environment.GetEnvironmentVariable("HELPER_AUTH_KEYS_JSON");
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<ApiKeyScopeConfig>>(json) ?? new List<ApiKeyScopeConfig>();
            lock (_sync)
            {
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Key))
                    {
                        continue;
                    }

                    var keyId = string.IsNullOrWhiteSpace(item.KeyId) ? $"env-{Guid.NewGuid():N}"[..12] : item.KeyId!;
                    var entry = new AuthKeyEntry(
                        keyId,
                        ComputeHash(item.Key),
                        string.IsNullOrWhiteSpace(item.Role) ? "integration" : item.Role!,
                        NormalizeScopes(item.Scopes),
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        null,
                        "m2m",
                        true);
                    UpsertUnsafe(entry);
                }

                SaveUnsafe();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthKeys] Failed to parse HELPER_AUTH_KEYS_JSON: {ex.Message}");
        }
    }

    private void UpsertUnsafe(AuthKeyEntry entry)
    {
        if (_byId.TryGetValue(entry.KeyId, out var existing) && !string.IsNullOrWhiteSpace(existing.KeyHash))
        {
            _byHash.Remove(existing.KeyHash);
        }

        _byId[entry.KeyId] = entry;
        if (IsEntryActive(entry, DateTimeOffset.UtcNow))
        {
            _byHash[entry.KeyHash] = entry;
        }
    }

    private void SaveUnsafe()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var snapshot = new AuthKeysSnapshot
            {
                SchemaVersion = 1,
                SavedAtUtc = DateTimeOffset.UtcNow,
                Keys = _byId.Values
                    .OrderBy(x => x.KeyId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new AuthKeyEntryDto
                    {
                        KeyId = x.KeyId,
                        KeyHash = x.KeyHash,
                        Role = x.Role,
                        Scopes = x.Scopes.ToList(),
                        CreatedAtUtc = x.CreatedAtUtc,
                        ExpiresAtUtc = x.ExpiresAtUtc,
                        RevokedAtUtc = x.RevokedAtUtc,
                        RevokedReason = x.RevokedReason,
                        PrincipalType = x.PrincipalType,
                        IsSystemManaged = x.IsSystemManaged
                    })
                    .ToList()
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthKeys] Failed to save {_path}: {ex.Message}");
        }
    }

    private static string ResolvePath(ApiRuntimeConfig runtimeConfig)
    {
        var path = Environment.GetEnvironmentVariable("HELPER_AUTH_KEYS_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.Combine(runtimeConfig.DataRoot, "auth_keys.json");
    }

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string>? scopes)
    {
        var normalized = (scopes ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length > 0)
        {
            return normalized;
        }

        return new[] { "chat:read", "chat:write" };
    }

    private static bool IsEntryActive(AuthKeyEntry entry, DateTimeOffset now)
    {
        if (entry.RevokedAtUtc.HasValue)
        {
            return false;
        }

        if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= now)
        {
            return false;
        }

        return true;
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var suffix = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"hk_{suffix}";
    }

    private sealed record AuthKeyEntry(
        string KeyId,
        string KeyHash,
        string Role,
        IReadOnlyList<string> Scopes,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset? RevokedAtUtc,
        string? RevokedReason,
        string PrincipalType,
        bool IsSystemManaged);

    private sealed class AuthKeysSnapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<AuthKeyEntryDto> Keys { get; set; } = new();
    }

    private sealed class AuthKeyEntryDto
    {
        public string KeyId { get; set; } = string.Empty;
        public string KeyHash { get; set; } = string.Empty;
        public string? Role { get; set; }
        public List<string>? Scopes { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public DateTimeOffset? RevokedAtUtc { get; set; }
        public string? RevokedReason { get; set; }
        public string? PrincipalType { get; set; }
        public bool IsSystemManaged { get; set; }
    }

    private sealed class ApiKeyScopeConfig
    {
        public string Key { get; set; } = string.Empty;
        public string? KeyId { get; set; }
        public string? Role { get; set; }
        public List<string>? Scopes { get; set; }
    }
}

