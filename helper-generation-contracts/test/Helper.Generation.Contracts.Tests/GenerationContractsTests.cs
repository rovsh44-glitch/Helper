using System.Text.Json;
using Helper.Generation.Contracts;

namespace Helper.Generation.Contracts.Tests;

public sealed class GenerationContractsTests
{
    [Fact]
    public void FileRoleJsonConverter_ParsesKnownAliases()
    {
        Assert.True(FileRoleJsonConverter.TryParse("services", out var role));
        Assert.Equal(FileRole.Service, role);

        Assert.True(FileRoleJsonConverter.TryParse("contractinterface", out role));
        Assert.Equal(FileRole.Interface, role);
    }

    [Fact]
    public void FileRoleJsonConverter_FallsBackToLogicForUnknownText()
    {
        Assert.False(FileRoleJsonConverter.TryParse("mystery-role", out var role));
        Assert.Equal(FileRole.Logic, role);
    }

    [Fact]
    public void SwarmBlueprint_WebJsonShape_RemainsDeterministic()
    {
        var blueprint = new SwarmBlueprint(
            "Demo.Project",
            "Demo.Project.Root",
            [
                new SwarmFileDefinition(
                    "Services/Runner.cs",
                    "Runs the workflow",
                    FileRole.Service,
                    ["Helper.Generation.Contracts"],
                    [new ArbanMethodTask("Execute", "public void Execute()", "Entry point", string.Empty)])
            ],
            ["Newtonsoft.Json"]);

        var json = JsonSerializer.Serialize(blueprint, CreateWebOptions());

        Assert.Contains("\"projectName\":\"Demo.Project\"", json, StringComparison.Ordinal);
        Assert.Contains("\"rootNamespace\":\"Demo.Project.Root\"", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"Services/Runner.cs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"Service\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedFile_DefaultLanguage_RemainsCSharp()
    {
        var file = new GeneratedFile("Services/Runner.cs", "namespace Demo;");
        Assert.Equal("csharp", file.Language);
    }

    [Fact]
    public void BuildError_RetainsPublishedShape()
    {
        var error = new BuildError("Runner.cs", 14, "CS1002", "; expected");

        Assert.Equal("Runner.cs", error.File);
        Assert.Equal(14, error.Line);
        Assert.Equal("CS1002", error.Code);
        Assert.Equal("; expected", error.Message);
    }

    private static JsonSerializerOptions CreateWebOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FileRoleJsonConverter());
        return options;
    }
}
