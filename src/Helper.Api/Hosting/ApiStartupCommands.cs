using System.Text.Json;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Hosting;

internal static class ApiStartupCommands
{
    public static bool TryHandleConfigInventoryCommand(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "--dump-config-inventory", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var format = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "markdown";
        var generatedAtUtc = DateTimeOffset.UtcNow;

        var output = format switch
        {
            "markdown" => BackendEnvironmentInventory.RenderMarkdown(generatedAtUtc),
            "template" => BackendEnvironmentInventory.RenderLocalEnvironmentTemplate(),
            "json" => JsonSerializer.Serialize(
                new
                {
                    generatedAtUtc = generatedAtUtc.ToString("O"),
                    source = "src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs",
                    definitions = BackendEnvironmentInventory.GetDefinitions()
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
            _ => throw new InvalidOperationException("Unknown config inventory format. Use markdown, json, or template.")
        };

        Console.Write(output);
        return true;
    }
}

