using System.Text.Json;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public class FileRoleJsonConverterTests
{
    [Fact]
    public void Deserialize_SwarmFileDefinition_MapsRoleAlias()
    {
        const string json = """
{
  "Path":"ViewModels/MainViewModel.cs",
  "Purpose":"vm",
  "Role":"ViewModels",
  "Dependencies":[],
  "Methods":[]
}
""";

        var file = JsonSerializer.Deserialize<SwarmFileDefinition>(json, JsonDefaults.Options);

        Assert.NotNull(file);
        Assert.Equal(FileRole.ViewModel, file!.Role);
    }

    [Fact]
    public void Deserialize_SwarmFileDefinition_UnknownRoleFallsBackToLogic()
    {
        const string json = """
{
  "Path":"Core/Any.cs",
  "Purpose":"logic",
  "Role":"UnknownRole",
  "Dependencies":[],
  "Methods":[]
}
""";

        var file = JsonSerializer.Deserialize<SwarmFileDefinition>(json, JsonDefaults.Options);

        Assert.NotNull(file);
        Assert.Equal(FileRole.Logic, file!.Role);
    }
}

