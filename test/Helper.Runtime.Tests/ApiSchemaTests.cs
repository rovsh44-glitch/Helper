using Helper.Api.Hosting;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public class ApiSchemaTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [Fact]
    [Trait("Category", "Contract")]
    public void OpenApiSchema_MatchesCommittedSnapshot()
    {
        var snapshotPath = ResolveWorkspaceFile("doc", "openapi_contract_snapshot.json");
        var currentDocument = OpenApiDocumentFactory.Create();
        var currentJson = JsonSerializer.Serialize(currentDocument, JsonOptions);

        var updateSnapshot = bool.TryParse(Environment.GetEnvironmentVariable("HELPER_UPDATE_OPENAPI_SNAPSHOT"), out var parsed) && parsed;
        if (updateSnapshot || !File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, currentJson);
        }

        var expectedJson = File.ReadAllText(snapshotPath);
        var expectedCanonical = Canonicalize(expectedJson);
        var currentCanonical = Canonicalize(currentJson);

        Assert.Equal(expectedCanonical, currentCanonical);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void OpenApiSchema_DeclaresBearerSecurityScheme()
    {
        var doc = OpenApiDocumentFactory.Create();
        var json = JsonSerializer.Serialize(doc);

        Assert.Contains("bearer", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/auth/session", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string Canonicalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc);
    }

    private static string ResolveWorkspaceFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "Helper.sln");
            if (File.Exists(marker))
            {
                return Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }
}

