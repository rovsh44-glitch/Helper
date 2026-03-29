using System.Text.Json;
using Helper.Runtime.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

public static class ApiProgramHelpers
{
    public static void LoadEnvironmentFileIfPresent(string helperRoot)
    {
        if (string.IsNullOrWhiteSpace(helperRoot))
        {
            return;
        }

        try
        {
            var envPath = Path.Combine(helperRoot, ".env.local");
            if (!File.Exists(envPath))
            {
                return;
            }

            foreach (var rawLine in File.ReadLines(envPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var name = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var existing = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(name, value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiProgramHelpers] Failed to load .env.local: {ex.Message}");
        }
    }

    public static string ResolvePath(string? primary, string? secondary, string fallback)
    {
        var raw = string.IsNullOrWhiteSpace(primary) ? secondary : primary;
        return Path.GetFullPath(string.IsNullOrWhiteSpace(raw) ? fallback : raw);
    }

    public static string DiscoverHelperRoot(string startPath)
        => HelperWorkspacePathResolver.DiscoverHelperRoot(startPath);

    public static string ResolveDataRoot(string? configuredDataRoot, string helperRoot)
        => HelperWorkspacePathResolver.ResolveDataRoot(configuredDataRoot, helperRoot);

    public static string? ExtractApiKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-API-KEY", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
        {
            return headerKey.ToString();
        }

        if (context.Request.Query.TryGetValue("access_token", out var accessToken) && !string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken.ToString();
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader.Substring("Bearer ".Length).Trim();
        }

        return null;
    }

    public static List<LibraryItemDto> LoadLibraryQueue(string queuePath, string? libraryRoot = null, string? helperRoot = null)
    {
        if (!File.Exists(queuePath))
        {
            return new List<LibraryItemDto>();
        }

        try
        {
            var json = File.ReadAllText(queuePath);
            var queue = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            var normalizedQueue = HelperWorkspacePathResolver.NormalizeLibraryQueue(
                queue,
                out var changed,
                libraryRoot,
                helperRoot,
                pruneMissing: true);

            if (changed)
            {
                var directory = Path.GetDirectoryName(queuePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(queuePath, JsonSerializer.Serialize(normalizedQueue, new JsonSerializerOptions { WriteIndented = true }));
            }

            return normalizedQueue.Select(x => new LibraryItemDto(
                Path: x.Key,
                Name: Path.GetFileName(x.Key),
                Folder: Path.GetFileName(Path.GetDirectoryName(x.Key) ?? string.Empty),
                Status: x.Value))
                .OrderBy(x => x.Status == "Done" ? 1 : 0)
                .ThenBy(x => x.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiProgramHelpers] Failed to load library queue: {ex.Message}");
            return new List<LibraryItemDto>();
        }
    }
}

